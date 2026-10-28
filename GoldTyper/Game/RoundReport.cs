using System;
using System.IO;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GoldTyper.Game;

public static unsafe class RoundReport
{
    private static DateTimeOffset? started;
    private static string observedSession = string.Empty;
    private static bool resultSeen;
    public static string DirectoryPath => Path.Combine(Plugin.P.PluginInterface.GetPluginConfigDirectory(), "reports");
    public static string Status { get; private set; } = "尚无本轮简报";

    public static void Observe(GameSnapshot snapshot)
    {
        if (!snapshot.ResultVisible)
        {
            resultSeen = false;
            if (snapshot.InputVisible && snapshot.InputFlag == true &&
                snapshot.Session.Length > 0 && (started == null || observedSession != snapshot.Session))
            {
                started = DateTimeOffset.Now;
                observedSession = snapshot.Session;
                Status = $"本轮开始：{started:yyyy-MM-dd HH:mm:ss}";
            }
            return;
        }
        if (resultSeen)
            return;
        var score = ReadOwnScore();
        if (score == null)
        {
            Status = "等待结算中的本人总分，暂未保存简报";
            return;
        }
        var ended = DateTimeOffset.Now;
        var player = Svc.Objects.LocalPlayer;
        var character = player == null ? "未知角色" : $"{player.Name.TextValue}@{player.HomeWorld.Value.Name}";
        var duration = started.HasValue ? ended - started.Value : (TimeSpan?)null;
        var text = string.Join(Environment.NewLine,
            $"角色：{character}",
            $"开始：{(started.HasValue ? started.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "未知（未采集到开始）")}",
            $"结束：{ended:yyyy-MM-dd HH:mm:ss zzz}",
            $"总分：{score}",
            $"用时：{(duration.HasValue ? $"{(int)duration.Value.TotalHours:00}:{duration.Value.Minutes:00}:{duration.Value.Seconds:00}" : "未知")}",
            "计时口径：首次观察到本轮可输入至结算窗口可见；中途启动时仅包含采集时段。",
            string.Empty);
        Directory.CreateDirectory(DirectoryPath);
        var safeName = character;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalid, '_');
        var path = Path.Combine(DirectoryPath, $"{safeName}-{ended:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        resultSeen = true;
        started = null;
        Status = $"简报已保存：{path}";
        AutomationLog.Write(Status);
    }

    private static int? ReadOwnScore()
    {
        var reference = Svc.GameGui.GetAddonByName("KTGResult", 1);
        if (reference.IsNull || !reference.IsReady || !reference.IsVisible)
            return null;
        var addon = (AtkUnitBase*)reference.Address;
        if (addon->AtkValues == null || addon->AtkValuesCount <= 64)
            return null;
        var player = Svc.Objects.LocalPlayer;
        if (player == null)
            return null;
        for (var row = 0; row < 4; row++)
        {
            var nameValue = addon->AtkValues + 61 + row;
            if (((int)nameValue->Type & 0xF) is not (6 or 8) || (byte*)nameValue->String == null)
                continue;
            var namePointer = (byte*)nameValue->String;
            var length = 0;
            while (length < 256 && namePointer[length] != 0)
                length++;
            if (length == 256)
                continue;
            var name = SeString.Parse(new ReadOnlySpan<byte>(nameValue->String, length).ToArray()).TextValue;
            if (name != player.Name.TextValue)
                continue;
            var value = addon->AtkValues + 10 + row * 8;
            return (int)value->Type == 5 ? unchecked((int)value->UInt) : null;
        }
        return null;
    }
}
