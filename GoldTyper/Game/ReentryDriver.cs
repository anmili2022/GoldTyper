using System;
using System.Linq;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Game.Text.SeStringHandling;

namespace GoldTyper.Game;

public static unsafe class ReentryDriver
{
    private static long nextAction;
    private static int attempts;

    public static void Reset()
    {
        nextAction = Environment.TickCount64 + 3000;
        attempts = 0;
    }

    public static void RememberTarget()
    {
        var target = Svc.Targets.Target;
        if (target == null || target.ObjectKind != ObjectKind.EventNpc ||
            Svc.ClientState.TerritoryType == 1359)
            return;
        Plugin.Configuration.入口Npc名称 = target.Name.TextValue;
        Plugin.Configuration.Save();
    }

    public static string Tick()
    {
        if (Environment.TickCount64 < nextAction)
            return "等待再次参加";
        nextAction = Environment.TickCount64 + 5000;
        if (!Svc.ClientState.IsLoggedIn)
            return "等待角色登录";
        if (Visible("ContentsFinderConfirm"))
            return ConfirmDeparture();
        if (Visible("KTGResult"))
            return Click("KTGResult", 48, "退出结算");
        if (Visible("KTGEnterDialogue"))
            return Click("KTGEnterDialogue", 9, "申请参加");
        if (Svc.ClientState.TerritoryType == 1359)
            return "等待离开小游戏";
        if (attempts >= 6)
            throw new InvalidOperationException("再次参加连续尝试达到上限，请检查报名惩罚、NPC 和出发确认窗口");
        var name = Plugin.Configuration.入口Npc名称;
        if (name.Length == 0)
        {
            RememberTarget();
            return "请选中入口 NPC，以记录自动再次参加的交互目标";
        }
        var player = Svc.Objects.LocalPlayer;
        var npc = Svc.Objects.FirstOrDefault(o => o.ObjectKind == ObjectKind.EventNpc &&
            o.Name.TextValue == name && player != null &&
            System.Numerics.Vector3.Distance(o.Position, player.Position) < 5);
        if (npc == null)
            return $"等待入口 NPC：{name}（需在 5 米内）";
        var system = TargetSystem.Instance();
        if (system == null)
            return "等待交互系统就绪";
        attempts++;
        Svc.Targets.Target = npc;
        system->InteractWithObject((GameObject*)npc.Address, false);
        return $"已交互入口 NPC：{name}，等待报名窗口";
    }

    private static bool Visible(string name)
    {
        var addon = Svc.GameGui.GetAddonByName(name, 1);
        return !addon.IsNull && addon.IsReady && addon.IsVisible;
    }

    private static string ConfirmDeparture()
    {
        var reference = Svc.GameGui.GetAddonByName("ContentsFinderConfirm", 1);
        if (reference.IsNull || !reference.IsReady || !reference.IsVisible)
            return "等待出发确认窗口";
        var confirm = (AddonContentsFinderConfirm*)reference.Address;
        var title = confirm->AtkTextNode230;
        if (title == null || (byte*)title->NodeText.StringPtr == null || title->NodeText.BufUsed <= 0 ||
            title->NodeText.BufUsed > 4096 || title->NodeText.BufUsed > title->NodeText.BufSize)
            return "等待出发任务名称";
        var bytes = new ReadOnlySpan<byte>(title->NodeText.StringPtr, (int)title->NodeText.BufUsed);
        var end = bytes.IndexOf((byte)0);
        var name = SeString.Parse((end >= 0 ? bytes[..end] : bytes).ToArray()).TextValue.Trim();
        if (name != "魔光键影")
            return $"未自动确认其他任务：{name}";
        var button = confirm->CommenceButton;
        if (button == null || button->OwnerNode == null || !button->IsEnabled)
            return "等待出发按钮可用";
        return Click("ContentsFinderConfirm", button->OwnerNode->AtkResNode.NodeId, "确认魔光键影出发");
    }

    private static string Click(string addon, uint id, string action)
    {
        attempts++;
        if (attempts > 6)
            throw new InvalidOperationException("再次参加操作超过重试上限，请检查游戏提示");
        if (!AddonClicker.ClickNode(addon, id, out var message))
            return message;
        return $"已{action}，等待界面响应";
    }
}
