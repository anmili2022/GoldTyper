using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GoldTyper.Game;

public sealed record DiagnosticText(string Addon, string Path, ushort Type, bool Visible, string Text, string RawHex)
{
    public string Identity { get; init; } = string.Empty;
    public float ScreenX { get; init; }
    public float ScreenY { get; init; }
    public float LocalX { get; init; }
    public float LocalY { get; init; }
    public float Depth { get; init; }
}

public sealed record GameSnapshot(DateTime Time, bool InputVisible, bool ResultVisible, int? Score,
    int? Durability, int? MaxDurability, bool? InputFlag, IReadOnlyList<DiagnosticText> Prompts,
    IReadOnlyList<DiagnosticText> InputNodes, double ReadMilliseconds, string Error)
{
    public string Session { get; init; } = string.Empty;
    public string InputText { get; init; } = string.Empty;
    public IReadOnlyList<DiagnosticText> CastNodes { get; init; } = Array.Empty<DiagnosticText>();
}

public sealed unsafe class GameDiagnostics : IDisposable
{
    public static GameDiagnostics Instance { get; } = new();
    public GameSnapshot Snapshot { get; private set; } = Empty();
    public bool Enabled { get; private set; }
    public int HistoryCount => history.Count;

    private readonly Queue<GameSnapshot> history = new();
    private long nextRead;
    private bool initialized;
    private string lastSignature = string.Empty;
    private string? pendingInput;
    private bool pendingSend;
    private bool pendingSnapshot;
    public static string SnapshotDirectory => Path.Combine(Plugin.P.PluginInterface.GetPluginConfigDirectory(), "snapshots");
    public string SnapshotMessage { get; private set; } = string.Empty;

    public void RequestSnapshot()
    {
        pendingSnapshot = true;
        SnapshotMessage = "等待游戏线程保存快照…";
    }
    public string InputMessage { get; private set; } = string.Empty;

    public void RequestInput(string text)
    {
        GameAutomation.Instance.Stop();
        pendingInput = text;
        InputMessage = "等待游戏线程填入…";
    }

    public void RequestSend()
    {
        GameAutomation.Instance.Stop();
        pendingSend = true;
        InputMessage = "等待游戏线程发送…";
    }

    public void Initialize()
    {
        if (initialized)
            return;
        initialized = true;
        Svc.Framework.Update += OnUpdate;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        nextRead = 0;
        if (!enabled)
            Snapshot = Empty();
    }

    public string Export() => JsonSerializer.Serialize(new { Snapshot, History = history.ToArray() },
        new JsonSerializerOptions { WriteIndented = true });

    public void ClearHistory()
    {
        history.Clear();
        lastSignature = string.Empty;
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            GameAutomation.Instance.ProcessCleanup();
        }
        catch (Exception ex)
        {
            GameAutomation.Instance.Stop($"清理输入失败：{ex.Message}");
            AutomationLog.Write($"清理输入异常：{ex}", true);
            Svc.Chat.PrintError($"金蝶打字通清理输入失败：{ex.Message}");
        }
        if (pendingInput is { } text)
        {
            pendingInput = null;
            InputMessage = InputDriver.Fill(text);
        }
        if (pendingSend)
        {
            pendingSend = false;
            InputMessage = InputDriver.Send();
        }
        if (!pendingSnapshot && Environment.TickCount64 < nextRead)
            return;
        nextRead = Environment.TickCount64 + 100;
        var timer = Stopwatch.StartNew();
        try
        {
            var prompts = new List<DiagnosticText>();
            var inputNodes = new List<DiagnosticText>();
            var castNodes = new List<DiagnosticText>();
            var word = FindAddon("KTGEWord");
            var wordNodes = new List<DiagnosticText>();
            var input = FindAddon("KTGInput");
            var result = FindAddon("KTGResult");
            var cast = FindAddon("KTGEnemyCastBar");
            if (cast != null)
                ReadTree("KTGEnemyCastBar", &cast->UldManager, "", cast->IsVisible, 0,
                    new HashSet<nint>(), castNodes, false);
            if (word != null)
            {
                ReadTree("KTGEWord", &word->UldManager, "", word->IsVisible, 0,
                    new HashSet<nint>(), wordNodes, false);
                var slotDepths = new Dictionary<string, float>();
                foreach (var node in wordNodes)
                    if (!node.Path.Contains('/') && node.Visible && float.IsFinite(node.Depth) && node.Depth > 0)
                        slotDepths[node.Path] = node.Depth;
                foreach (var node in wordNodes)
                {
                    var separator = node.Path.IndexOf('/');
                    if (node.Type != 3 || separator < 0 || node.Path[(separator + 1)..] != "20")
                        continue;
                    prompts.Add(node with { Depth = slotDepths.GetValueOrDefault(node.Path[..separator]) });
                }
            }
            if (input != null)
                ReadTree("KTGInput", &input->UldManager, "", input->IsVisible, 0,
                    new HashSet<nint>(), inputNodes, false);
            var score = FindAddon("KTGHudScore");
            var hp = FindAddon("KTGHudGaugeHp");
            var flag = ReadNumber(input, 2);
            Snapshot = new GameSnapshot(DateTime.UtcNow, input != null && input->IsVisible,
                result != null && result->IsVisible, ReadNumber(score, 1), ReadNumber(hp, 1),
                ReadNumber(hp, 2), flag.HasValue ? flag.Value != 0 : null,
                prompts, inputNodes, timer.Elapsed.TotalMilliseconds, string.Empty)
            {
                Session = InputDriver.GetSession(),
                InputText = InputDriver.GetInputText(),
                CastNodes = castNodes,
            };
            var signature = JsonSerializer.Serialize(new
            {
                Snapshot.InputVisible, Snapshot.ResultVisible, Snapshot.Score, Snapshot.Durability,
                Snapshot.MaxDurability, Snapshot.InputFlag, Snapshot.Prompts, Snapshot.InputNodes,
                Snapshot.Session, Snapshot.InputText,
                Snapshot.CastNodes,
            });
            if (Enabled && signature != lastSignature)
            {
                lastSignature = signature;
                history.Enqueue(Snapshot);
                while (history.Count > 300)
                    history.Dequeue();
            }
        }
        catch (Exception ex)
        {
            Snapshot = Empty() with { Error = ex.Message, ReadMilliseconds = timer.Elapsed.TotalMilliseconds };
            AutomationLog.Write($"读取界面异常：{ex}", true);
            nextRead = Environment.TickCount64 + 1000;
        }
        if (pendingSnapshot)
        {
            pendingSnapshot = false;
            SaveSnapshot();
        }
        try
        {
            try
            {
                RoundReport.Observe(Snapshot);
            }
            catch (Exception reportError)
            {
                AutomationLog.Write($"简报保存失败：{reportError.Message}", true);
            }
            GameAutomation.Instance.Tick(Snapshot);
        }
        catch (Exception ex)
        {
            GameAutomation.Instance.Stop($"自动流程异常：{ex.Message}");
            AutomationLog.Write($"自动流程异常：{ex}", true);
            Svc.Chat.PrintError($"金蝶打字通已停止：{ex.Message}");
        }
    }

    private void SaveSnapshot()
    {
        try
        {
            if (Snapshot.Error.Length > 0)
                throw new InvalidOperationException(Snapshot.Error);
            var player = Svc.Objects.LocalPlayer;
            var enemies = Svc.Objects.OfType<IBattleNpc>().Select(npc => new
            {
                npc.EntityId, npc.ObjectIndex, npc.BaseId,
                Name = npc.Name.TextValue,
                Position = new { npc.Position.X, npc.Position.Y, npc.Position.Z },
                Distance = player == null ? (float?)null : System.Numerics.Vector3.Distance(npc.Position, player.Position),
                npc.IsTargetable, npc.IsDead, npc.CurrentHp, npc.MaxHp,
                npc.TargetObjectId, npc.IsCasting, npc.IsCastInterruptible,
                npc.CastActionId, npc.CurrentCastTime, npc.TotalCastTime,
            }).ToArray();
            var wordNodes = new List<DiagnosticText>();
            var word = FindAddon("KTGEWord");
            if (word != null)
                ReadTree("KTGEWord", &word->UldManager, "", word->IsVisible, 0,
                    new HashSet<nint>(), wordNodes, false);
            var json = JsonSerializer.Serialize(new
            {
                CapturedAt = DateTimeOffset.Now,
                Territory = Svc.ClientState.TerritoryType,
                PlayerPosition = player == null ? null : new { player.Position.X, player.Position.Y, player.Position.Z },
                Snapshot, WordNodes = wordNodes, Enemies = enemies,
            }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
            Directory.CreateDirectory(SnapshotDirectory);
            var name = $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json";
            File.WriteAllText(Path.Combine(SnapshotDirectory, name), json, new UTF8Encoding(false));
            SnapshotMessage = $"快照已保存：{name}";
            AutomationLog.Write(SnapshotMessage);
        }
        catch (Exception ex)
        {
            SnapshotMessage = $"快照保存失败：{ex.Message}";
            AutomationLog.Write(SnapshotMessage, true);
            Svc.Chat.PrintError($"金蝶打字通{SnapshotMessage}");
        }
    }

    private static AtkUnitBase* FindAddon(string name)
    {
        var addon = Svc.GameGui.GetAddonByName(name, 1);
        return addon.IsNull || !addon.IsReady ? null : (AtkUnitBase*)addon.Address;
    }

    private static int? ReadNumber(AtkUnitBase* addon, int index)
    {
        if (addon == null || addon->AtkValues == null || index >= addon->AtkValuesCount)
            return null;
        var value = addon->AtkValues + index;
        return (int)value->Type switch
        {
            2 => value->Bool ? 1 : 0,
            3 => value->Int,
            5 => unchecked((int)value->UInt),
            _ => null,
        };
    }

    private static void ReadTree(string addon, AtkUldManager* manager, string prefix, bool parentVisible,
        int depth, HashSet<nint> visited, List<DiagnosticText> output, bool promptsOnly)
    {
        if (manager == null || manager->NodeList == null || manager->NodeListCount > 512 || depth > 8)
            return;
        for (var i = 0; i < manager->NodeListCount && visited.Count < 1024; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !visited.Add((nint)node))
                continue;
            var visible = parentVisible;
            var ancestor = node;
            var guard = 0;
            while (ancestor != null && guard++ < 64)
            {
                visible &= ancestor->IsVisible() && !ancestor->IsDrawDisabled && ancestor->Color.A != 0;
                ancestor = ancestor->ParentNode;
            }
            if (ancestor != null)
                visible = false;
            var path = prefix.Length == 0 ? node->NodeId.ToString() : $"{prefix}/{node->NodeId}";
            var type = (ushort)node->Type;
            if (type == 3 && (!promptsOnly || node->NodeId == 20))
            {
                var text = &((AtkTextNode*)node)->NodeText;
                if ((byte*)text->StringPtr != null && text->BufUsed > 0 && text->BufUsed <= text->BufSize && text->BufUsed <= 4096)
                {
                    var bytes = new ReadOnlySpan<byte>(text->StringPtr, (int)text->BufUsed);
                    var end = bytes.IndexOf((byte)0);
                    if (end >= 0)
                        bytes = bytes[..end];
                    if (bytes.Length > 0)
                    {
                        var copy = bytes.ToArray();
                        output.Add(new DiagnosticText(addon, path, type, visible,
                            SeString.Parse(copy).TextValue, Convert.ToHexString(copy))
                        {
                            Identity = $"{path}@{(nint)node:X}",
                            ScreenX = node->ScreenX, ScreenY = node->ScreenY,
                            LocalX = node->X, LocalY = node->Y, Depth = node->Depth,
                        });
                    }
                }
            }
            else if (type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                {
                    if (!promptsOnly)
                        output.Add(new DiagnosticText(addon, path, type, visible, string.Empty, string.Empty)
                        {
                            Identity = $"{path}@{(nint)node:X}",
                            ScreenX = node->ScreenX, ScreenY = node->ScreenY,
                            LocalX = node->X, LocalY = node->Y, Depth = node->Depth,
                        });
                    ReadTree(addon, &component->UldManager, path, visible, depth + 1, visited, output, promptsOnly);
                }
            }
        }
    }

    private static GameSnapshot Empty() => new(DateTime.UtcNow, false, false, null, null, null, null,
        Array.Empty<DiagnosticText>(), Array.Empty<DiagnosticText>(), 0, string.Empty);

    public void Dispose()
    {
        if (initialized)
            Svc.Framework.Update -= OnUpdate;
        initialized = false;
        pendingInput = null;
        pendingSend = false;
        SetEnabled(false);
        ClearHistory();
    }
}
