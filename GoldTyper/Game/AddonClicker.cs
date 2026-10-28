using System;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GoldTyper.Game;

public static unsafe class AddonClicker
{
    public static bool ClickNode(string addonName, uint nodeId, out string message)
    {
        try
        {
            var name = (addonName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                message = "addon 名称为空";
                return false;
            }

            var addonRef = Svc.GameGui.GetAddonByName(name, 1);
            if (addonRef.IsNull)
            {
                message = $"未找到 addon: {name}";
                return false;
            }

            if (!addonRef.IsVisible)
            {
                message = $"addon 未打开: {name}";
                return false;
            }

            if (!addonRef.IsReady)
            {
                message = $"addon 未就绪: {name}";
                return false;
            }

            var addon = (AtkUnitBase*)addonRef.Address;
            var evt = FindButtonClickEvent(&addon->UldManager, nodeId, 0);
            if (evt == null)
            {
                message = $"未找到可点击节点: {nodeId}";
                return false;
            }

            var param = (int)evt->Param;
            evt->Listener->ReceiveEvent(AtkEventType.ButtonClick, param, evt, null);
            message = $"已点击 {name} 节点 {nodeId} (param {param})";
            return true;
        }
        catch (Exception ex)
        {
            message = $"点击失败: {ex.Message}";
            return false;
        }
    }

    private static AtkEvent* FindButtonClickEvent(AtkUldManager* uld, uint nodeId, int depth)
    {
        if (uld == null || depth > 8)
            return null;

        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;

            if (node->NodeId == nodeId)
            {
                var evt = FindButtonClickEvent(node);
                if (evt != null)
                    return evt;
            }

            if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                {
                    var found = FindButtonClickEvent(&component->UldManager, nodeId, depth + 1);
                    if (found != null)
                        return found;
                }
            }
        }

        return null;
    }

    private static AtkEvent* FindButtonClickEvent(AtkResNode* node)
    {
        var evt = node->AtkEventManager.Event;
        var guard = 0;
        while (evt != null && guard++ < 64)
        {
            if (evt->State.EventType == AtkEventType.ButtonClick && evt->Listener != null)
                return evt;

            evt = evt->NextEvent;
        }

        return null;
    }
}
