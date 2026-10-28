using System;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GoldTyper.Game;

public static unsafe class ActivityInput
{
    private static long deadline;
    private static int phase;
    private static ushort heldScan;
    private static bool prepared;
    private static string observedText = string.Empty;
    private static bool cleanupRequested;
    private static string session = string.Empty;
    private static char activityCharacter;
    private static readonly ushort[] letterScans =
        [0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
         0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C];
    public static string Status { get; private set; } = "等待启动";

    public static void Reset()
    {
        ReleaseKey();
        cleanupRequested |= observedText.Length > 0;
        prepared = false;
        phase = 0;
        Status = "等待下一次活动输入";
    }

    public static void ProcessCleanup()
    {
        if (!cleanupRequested)
            return;
        InputDriver.ClearOwnedInput(session, observedText);
        cleanupRequested = false;
        observedText = string.Empty;
    }

    public static void Consume() => prepared = false;

    public static bool Tick(GameSnapshot snapshot, bool canStart)
    {
        var now = Environment.TickCount64;
        if (phase == 0 && (!canStart || prepared))
            return false;
        if (!snapshot.InputVisible || snapshot.InputFlag != true || snapshot.ResultVisible)
        {
            Reset();
            return false;
        }
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        if (process != Environment.ProcessId)
        {
            Reset();
            throw new InvalidOperationException("游戏不在前台，已停止活动输入");
        }
        if (phase == 0)
        {
            if (snapshot.InputText.Length != 0)
                return false;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
                (GetAsyncKeyState(0x12) & 0x8000) != 0 || IsLetterHeld() ||
                (GetAsyncKeyState(0x0D) & 0x8000) != 0)
            {
                Status = "等待释放修饰键、字母键或回车键";
                return true;
            }
            var reference = Svc.GameGui.GetAddonByName("KTGInput", 1);
            if (reference.IsNull || !reference.IsReady || !reference.IsVisible)
                return true;
            var addon = (AtkUnitBase*)reference.Address;
            var node = addon->GetNodeById(7);
            var input = node == null ? null : node->GetAsAtkComponentTextInput();
            if (input == null || !input->Enabled || input->OwnerAddon != addon)
                throw new InvalidOperationException("活动输入控件不可用");
            addon->SetFocusNode(node);
            session = snapshot.Session;
            observedText = string.Empty;
            var index = Random.Shared.Next(25);
            var previousIndex = activityCharacter - 'a';
            if (previousIndex >= 0 && previousIndex < 26 && index >= previousIndex)
                index++;
            else if (previousIndex < 0 || previousIndex >= 26)
                index = Random.Shared.Next(26);
            activityCharacter = (char)('a' + index);
            PressKey(letterScans[index]);
            phase = 1;
            deadline = now + 100;
            Status = $"已按下 {char.ToUpperInvariant(activityCharacter)} 键扫描码，等待跨帧释放";
            AutomationLog.Write(Status);
            return true;
        }
        if (snapshot.Session != session)
            throw new InvalidOperationException("活动输入期间界面已重建");
        var visibleText = SeString.Parse(Encoding.UTF8.GetBytes(snapshot.InputText)).TextValue;
        if (visibleText.Length != 0 &&
            (visibleText.Length != 1 || char.ToLowerInvariant(visibleText[0]) != activityCharacter))
            throw new InvalidOperationException($"活动输入期间文字被修改，已停止：预期 {activityCharacter}，实际 {visibleText}");
        if (visibleText.Length > 0)
            observedText = snapshot.InputText;
        if (phase == 2 && visibleText.Length > 0)
        {
            AutomationLog.Write($"已实际读到活动字符：{visibleText}，按下回车键");
            PressKey(0x1C);
            phase = 3;
            deadline = now + 100;
            Status = "已确认活动字符，等待释放回车键";
            return true;
        }
        if (phase == 4 && snapshot.InputText.Length == 0)
            deadline = now;
        if (now < deadline)
            return true;
        if (phase == 1)
        {
            ReleaseKey();
            phase = 2;
            deadline = now + 1500;
            Status = $"已释放 {char.ToUpperInvariant(activityCharacter)} 键，等待实际输入字符";
            AutomationLog.Write(Status);
            return true;
        }
        if (phase == 2)
        {
            throw new InvalidOperationException($"未观察到 {char.ToUpperInvariant(activityCharacter)} 键字符进入输入框；活动输入未验证，请检查输入法和游戏焦点");
        }
        if (phase == 3)
        {
            ReleaseKey();
            phase = 4;
            deadline = now + 1500;
            Status = "已释放回车键，等待确认提交后清空";
            AutomationLog.Write(Status);
            return true;
        }
        if (snapshot.InputText.Length != 0)
            throw new InvalidOperationException("活动字符回车提交后未清空，已停止，请检查输入框");
        phase = 0;
        observedText = string.Empty;
        prepared = true;
        Status = "已确认活动字符回车提交并清空，准备立即答题（AFK 刷新待验证）";
        AutomationLog.Write(Status);
        return false;
    }

    private static bool IsLetterHeld()
    {
        for (var key = 0x41; key <= 0x5A; key++)
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
                return true;
        return false;
    }

    private static void PressKey(ushort scan)
    {
        heldScan = scan;
        SendKey(scan, 8);
    }

    private static void ReleaseKey()
    {
        if (heldScan == 0)
            return;
        SendKey(heldScan, 8 | 2);
        heldScan = 0;
    }

    private static void SendKey(ushort scan, uint flags)
    {
        var events = new[] { new NativeInput { Type = 1, Key = new Keyboard { Scan = scan, Flags = flags } } };
        if (SendInput(1, events, Marshal.SizeOf<NativeInput>()) != 1)
            throw new InvalidOperationException($"系统键盘输入发送失败：{Marshal.GetLastWin32Error()}");
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public Keyboard Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Keyboard
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
