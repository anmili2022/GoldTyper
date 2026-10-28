using System;
using System.Text;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GoldTyper.Game;

public static unsafe class InputDriver
{
    private static AtkComponentTextInput* ResolveInput()
    {
        var reference = Svc.GameGui.GetAddonByName("KTGInput", 1);
        if (reference.IsNull || !reference.IsReady || !reference.IsVisible)
            return null;
        var addon = (AtkUnitBase*)reference.Address;
        var node = addon->GetNodeById(7);
        var input = node == null ? null : node->GetAsAtkComponentTextInput();
        return input == null || !input->Enabled || input->OwnerAddon != addon || input->Callback == null ? null : input;
    }

    public static string GetSession()
    {
        var input = ResolveInput();
        return input == null ? string.Empty : $"{(nint)input->OwnerAddon:X}/{(nint)input:X}";
    }

    public static string GetInputText()
    {
        var input = ResolveInput();
        return input == null ? string.Empty : ReadString(&input->RawString);
    }

    private static string ReadString(FFXIVClientStructs.FFXIV.Client.System.String.Utf8String* str)
    {
        if ((byte*)str->StringPtr == null || str->BufUsed <= 0 || str->BufUsed > 4096 || str->BufUsed > str->BufSize)
            throw new InvalidOperationException("无法校验当前输入字符串");
        var bytes = new ReadOnlySpan<byte>(str->StringPtr, (int)str->BufUsed);
        var end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes);
    }

    public static void ClearOwnedInput(string session, string text)
    {
        if (GetSession() != session)
            return;
        var input = ResolveInput();
        if (input != null && ReadString(&input->RawString) == text)
            input->SetText(string.Empty);
    }

    public static string Send(string? expectedText = null)
    {
        try
        {
            var result = Svc.GameGui.GetAddonByName("KTGResult", 1);
            if (!result.IsNull && result.IsVisible)
                return "发送失败：当前处于结算界面";
            var reference = Svc.GameGui.GetAddonByName("KTGInput", 1);
            if (reference.IsNull || !reference.IsReady || !reference.IsVisible)
                return "发送失败：小游戏输入窗口未就绪";
            var addon = (AtkUnitBase*)reference.Address;
            if (addon->AtkValues == null || addon->AtkValuesCount <= 2 || !addon->AtkValues[2].Bool)
                return "发送失败：小游戏当前不接受输入";
            var node = addon->GetNodeById(7);
            if (node == null || !node->IsVisible())
                return "发送失败：输入节点不可见";
            var input = node->GetAsAtkComponentTextInput();
            if (input == null || !input->Enabled || input->Callback == null || input->OwnerAddon != addon)
                return "发送失败：原生输入提交接口不可用";
            if (!input->HasEvaluatedText())
                return "发送失败：请先填入文字";
            if (expectedText != null)
            {
                var str = &input->EvaluatedString;
                if ((byte*)str->StringPtr == null || str->BufUsed <= 0 || str->BufUsed > 4096 || str->BufUsed > str->BufSize)
                    return "发送失败：无法校验当前输入";
                var bytes = new ReadOnlySpan<byte>(str->StringPtr, (int)str->BufUsed);
                var end = bytes.IndexOf((byte)0);
                if (end >= 0)
                    bytes = bytes[..end];
                if (Encoding.UTF8.GetString(bytes) != expectedText)
                    return "发送失败：输入内容已变化，已取消自动提交";
            }
            var session = GetSession();
            var rawText = ReadString(&input->RawString);
            var evaluatedText = ReadString(&input->EvaluatedString);
            var raw = Encoding.UTF8.GetBytes(rawText + '\0');
            var evaluated = Encoding.UTF8.GetBytes(evaluatedText + '\0');
            InputCallbackResult response;
            fixed (byte* rawPtr = raw)
            fixed (byte* evaluatedPtr = evaluated)
                response = input->Callback(addon, InputCallbackType.Enter,
                    rawPtr, evaluatedPtr, input->CallbackEventKind);
            if ((int)response == 1)
                ClearOwnedInput(session, rawText);
            else if ((int)response != 0)
                return $"发送失败：不支持的回调结果 {response}";
            return "已调用原生 Enter 提交，请检查词条和得分变化";
        }
        catch (Exception ex)
        {
            Svc.Chat.PrintError($"金蝶打字通发送失败：{ex.Message}");
            return $"发送失败：{ex.Message}";
        }
    }

    public static string Fill(string text, bool requireEmpty = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 128 || text.IndexOfAny(['\r', '\n', '\0']) >= 0)
                return "填入失败：词条为空、过长或包含控制字符";
            var result = Svc.GameGui.GetAddonByName("KTGResult", 1);
            if (!result.IsNull && result.IsVisible)
                return "填入失败：当前处于结算界面";
            var reference = Svc.GameGui.GetAddonByName("KTGInput", 1);
            if (reference.IsNull || !reference.IsReady || !reference.IsVisible)
                return "填入失败：小游戏输入窗口未就绪";
            var addon = (AtkUnitBase*)reference.Address;
            if (addon->AtkValues == null || addon->AtkValuesCount <= 2 || !addon->AtkValues[2].Bool)
                return "填入失败：小游戏当前不接受输入";
            var node = addon->GetNodeById(7);
            if (node == null || !node->IsVisible())
                return "填入失败：输入节点 7 不可见";
            var input = node->GetAsAtkComponentTextInput();
            if (input == null || !input->Enabled || input->OwnerAddon != addon || input->Callback == null)
                return "填入失败：节点 7 不是原生文本输入控件";
            if (requireEmpty && ReadString(&input->RawString).Length != 0)
                return "填入失败：输入框已有文字，未覆盖手动输入";
            var session = GetSession();
            input->SetText(text);
            input = ResolveInput();
            if (input == null || GetSession() != session || ReadString(&input->RawString) != text ||
                ReadString(&input->EvaluatedString) != text)
                return "填入失败：输入内容被截断、改变或界面重建，未提交";
            return $"已调用原生输入接口填入：{text}；请检查游戏输入框并按 Enter 提交";
        }
        catch (Exception ex)
        {
            Svc.Chat.PrintError($"金蝶打字通填入失败：{ex.Message}");
            return $"填入失败：{ex.Message}";
        }
    }
}
