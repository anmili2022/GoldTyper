using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using GoldTyper.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoldTyper.Windows;

public class DebugWindow : Window, IDisposable
{
    private static readonly Vector4 TextColor = new(0.91f, 0.92f, 0.95f, 1f);
    private static readonly Vector4 MutedColor = new(0.56f, 0.59f, 0.63f, 1f);
    private static readonly Vector4 AccentColor = new(0.54f, 0.71f, 0.97f, 1f);
    private static readonly Vector4 SuccessColor = new(0.51f, 0.84f, 0.65f, 1f);
    private static readonly Vector4 WarningColor = new(0.85f, 0.68f, 0.45f, 1f);
    private static readonly Vector4 FailureColor = new(1f, 0.56f, 0.56f, 1f);
    private static readonly Vector4 CardBackground = new(0.07f, 0.083f, 0.105f, 0.92f);
    private static readonly Vector4 BorderColor = new(1f, 1f, 1f, 0.07f);

    private readonly List<string> uiLog = new();
    private readonly List<string> runHistory = new();

    private string chatResult = string.Empty;
    private bool chatSuccess;
    private string clickResult = string.Empty;
    private bool clickSuccess;
    private int selectedTab;
    private bool minimized;
    private Vector2 restoreSize;
    private GameState previousState = GameState.Idle;
    private Task<string>? clipboardTask;
    private string clipboardResult = string.Empty;
    private string clipboardTest = string.Empty;

    public DebugWindow() : base("金蝶打字通###GoldTyperMain",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(520, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = Vector2.Zero,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        BgAlpha = 0.98f;

        uiLog.Add("SYS  插件已初始化");
        uiLog.Add("CFG  配置加载完成");
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Text, TextColor);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, MutedColor);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.027f, 0.035f, 0.047f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, BorderColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.035f, 0.043f, 0.055f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.07f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.11f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.54f, 0.71f, 0.97f, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.54f, 0.71f, 0.97f, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.54f, 0.71f, 0.97f, 0.3f));
        ImGui.PushStyleColor(ImGuiCol.Tab, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.54f, 0.71f, 0.97f, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.54f, 0.71f, 0.97f, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, AccentColor);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, AccentColor);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, new Vector4(0.65f, 0.8f, 0.97f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Separator, BorderColor);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(9);
        ImGui.PopStyleColor(21);
    }

    public override void Draw()
    {
        TrackRunHistory();

        var automation = GameAutomation.Instance;
        DrawHeader();
        DrawControls(automation);
        if (minimized)
            return;
        DrawTabs();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 8) * ImGuiHelpers.GlobalScale);
        var visible = ImGui.BeginChild($"##page{selectedTab}", Vector2.Zero, false,
            ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();

        if (visible)
        {
            switch (selectedTab)
            {
                case 0:
                    DrawAutoTab(automation);
                    break;
                case 1:
                    DrawOptionsTab();
                    break;
                case 2:
                    DrawToolsTab();
                    break;
                case 3:
                    DrawStatusTab(automation);
                    break;
            }
        }

        ImGui.EndChild();
    }

    private void DrawHeader()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetFrameHeight();
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - height * 2 - gap * 2);
        var position = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton("##dragWindow", new Vector2(width, height));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);

        var textPosition = position + new Vector2(0, (height - ImGui.GetFontSize()) * 0.5f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(textPosition, ImGui.GetColorU32(TextColor), "金蝶打字通");

        var titleWidth = ImGui.CalcTextSize("金蝶打字通").X;
        if (width >= titleWidth + ImGui.CalcTextSize("v0.1.0").X + 12f * scale)
            drawList.AddText(textPosition + new Vector2(titleWidth + 12f * scale, 0),
                ImGui.GetColorU32(MutedColor), "v0.1.0");

        ImGui.SameLine(0, gap);
        if (ImGui.Button(minimized ? "□##minimize" : "−##minimize", new Vector2(height)))
        {
            if (minimized)
            {
                minimized = false;
                ImGui.SetWindowSize(restoreSize);
            }
            else
            {
                restoreSize = ImGui.GetWindowSize();
                minimized = true;
                ImGui.SetWindowSize(new Vector2(280, 180) * scale);
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(minimized ? "恢复窗口" : "最小化为 280 × 180");
        ImGui.SameLine(0, gap);
        if (ImGui.Button("×##close", new Vector2(height)))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("关闭窗口");
    }

    private void DrawControls(GameAutomation automation)
    {
        BeginCard("##controls");
        DrawWrapped(CurrentStateColor(automation), $"● {StateText(automation)}");
        DrawControlMessage(automation.LastMessage);

        var gap = ImGui.GetStyle().ItemSpacing.X;
        var buttonWidth = (ImGui.GetContentRegionAvail().X - gap * 2f) / 3f;
        var buttonSize = new Vector2(buttonWidth, ImGui.GetFrameHeight() + 4f * ImGuiHelpers.GlobalScale);

        var canStart = automation.State is GameState.Idle or GameState.Done or GameState.Error;
        ImGui.BeginDisabled(!canStart);
        if (PrimaryButton("开始", buttonSize))
        {
            automation.Start();
            AddLog("SYS", "开始检测小游戏");
        }
        ImGui.EndDisabled();

        ImGui.SameLine(0, gap);

        var canPause = automation.IsRunning;
        ImGui.BeginDisabled(!canPause);
        if (ImGui.Button(automation.Paused ? "恢复" : "暂停", buttonSize))
        {
            if (automation.Paused)
            {
                automation.Resume();
                AddLog("SYS", "已恢复运行");
            }
            else
            {
                automation.Pause();
                AddLog("SYS", "已暂停运行");
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine(0, gap);

        ImGui.PushStyleColor(ImGuiCol.Text, FailureColor);
        if (ImGui.Button("停止", buttonSize))
        {
            automation.Stop();
            AddLog("SYS", "已停止运行");
        }
        ImGui.PopStyleColor();
        EndCard();
    }

    private static void DrawControlMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ');
        var width = ImGui.GetContentRegionAvail().X;
        var truncated = ImGui.CalcTextSize(text).X > width;
        if (truncated)
        {
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (ImGui.CalcTextSize(text[..middle] + "…").X <= width)
                    low = middle;
                else
                    high = middle - 1;
            }
            if (low > 0 && char.IsHighSurrogate(text[low - 1]))
                low--;
            text = text[..low] + "…";
        }

        ImGui.TextColored(MutedColor, text);
        if (truncated && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            ImGui.TextUnformatted(message);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private void DrawTabs()
    {
        if (!ImGui.BeginTabBar("##GoldTyperTabs", ImGuiTabBarFlags.FittingPolicyResizeDown))
            return;

        DrawTab("自动", 0);
        DrawTab("选项", 1);
        DrawTab("测试工具", 2);
        DrawTab("状态", 3);
        ImGui.EndTabBar();
    }

    private void DrawTab(string label, int index)
    {
        if (!ImGui.BeginTabItem(label))
            return;

        selectedTab = index;
        ImGui.EndTabItem();
    }

    private void DrawAutoTab(GameAutomation automation)
    {
        DrawPageHeading("自动完成", "点击开始，等待金蝶小游戏出现。仅在目标窗口激活时运行。\n自动输入属于自动化操作，存在违规风险，请自行评估。");

        BeginCard("##prompt");
        DrawWrapped(MutedColor, "当前题目");
        DrawWrapped(string.IsNullOrEmpty(automation.CurrentPrompt) ? MutedColor : AccentColor,
            string.IsNullOrEmpty(automation.CurrentPrompt) ? "暂无题目" : automation.CurrentPrompt);
        EndCard();

        DrawStats();
        DrawWrapped(MutedColor, Plugin.Configuration.启用防Afk活动输入 ? $"活动输入：{ActivityInput.Status}" : "活动输入：已关闭");

        BeginCard("##modules");
        ImGui.TextUnformatted("模块状态");
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(0, 4) * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginTable("##moduleRows", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("模块", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthStretch, 1f);
            DrawModuleRow("Addon 检测", true);
            DrawModuleRow("题目识别", true);
            DrawModuleRow("输入与提交", true);
            HoverTip("完整输入与提交流程仍需实机验证。");
            DrawModuleRow("测试工具", true);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        EndCard();

    }

    private void DrawStats()
    {
        var completed = 0;
        foreach (var entry in runHistory)
        {
            if (entry.StartsWith("已完成", StringComparison.Ordinal))
                completed++;
        }

        var success = runHistory.Count == 0
            ? "--"
            : $"{(int)Math.Round(completed * 100.0 / runHistory.Count)}%";

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 0) * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginTable("##stats", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.PopStyleVar();
            return;
        }

        ImGui.TableNextColumn();
        BeginCard("##recentRuns");
        DrawWrapped(MutedColor, "最近运行");
        ImGui.TextUnformatted(runHistory.Count.ToString("00"));
        EndCard();

        ImGui.TableNextColumn();
        BeginCard("##successRate");
        DrawWrapped(MutedColor, "成功率");
        ImGui.TextUnformatted(success);
        EndCard();
        ImGui.EndTable();
        ImGui.PopStyleVar();
    }

    private static void DrawModuleRow(string name, bool ready)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawWrapped(TextColor, name);
        ImGui.TableNextColumn();
        DrawWrapped(ready ? SuccessColor : MutedColor, ready ? "已就绪" : "待接入");
    }

    private void DrawOptionsTab()
    {
        DrawPageHeading("选项", "调整输入节奏与调试输出，修改后自动保存。");
        if (ImGui.Button("打开简报文件夹", new Vector2(-1, 0)))
            OpenFolder(RoundReport.DirectoryPath);
        var reenter = Plugin.Configuration.自动再次参加;
        if (ImGui.Checkbox("结算后退出并再次参加", ref reenter))
        {
            Plugin.Configuration.自动再次参加 = reenter;
            Plugin.Configuration.Save();
        }
        HoverTip("结算后自动退出、交互已记录的入口 NPC、申请参加并确认魔光键影出发。");
        if (ImGui.Button("记录当前选中的入口 NPC"))
            ReentryDriver.RememberTarget();
        HoverTip($"入口 NPC：{(Plugin.Configuration.入口Npc名称.Length == 0 ? "未记录；在入口选中 NPC 后点击开始，或点击此按钮记录" : Plugin.Configuration.入口Npc名称)}");

        BeginCard("##inputOptions");
        ImGui.TextUnformatted("输入参数");
        ImGui.Separator();

        var activityInput = Plugin.Configuration.启用防Afk活动输入;
        if (ImGui.Checkbox("启用防 AFK 活动输入", ref activityInput))
        {
            Plugin.Configuration.启用防Afk活动输入 = activityInput;
            Plugin.Configuration.Save();
        }
        HoverTip("每次自动答题前发送随机英文字母和实体回车，确认输入清空后再填写答案。关闭后跳过活动输入；正在执行的按键在下一次游戏线程更新中释放并清理已确认属于插件的字符。需要游戏在前台，实际防 AFK 效果仍待验证。");

        var prioritizeCasting = Plugin.Configuration.优先处理读条目标;
        if (ImGui.Checkbox("优先处理读条目标", ref prioritizeCasting))
        {
            Plugin.Configuration.优先处理读条目标 = prioritizeCasting;
            Plugin.Configuration.Save();
        }
        HoverTip("开启后优先填写正在读条目标的词条；多个目标同时读条时按其他已启用策略排序。当前槽位关联可能不准确，仍需修正与实机验证。");

        var prioritizeNearby = Plugin.Configuration.优先处理近距离目标;
        if (ImGui.Checkbox("优先处理近距离目标", ref prioritizeNearby))
        {
            Plugin.Configuration.优先处理近距离目标 = prioritizeNearby;
            Plugin.Configuration.Save();
        }
        HoverTip("按词条组件的画面深度优先处理靠前目标，不代表距玩家的精确米数。同时开启读条优先时，先选读条目标，再按深度排序。");

        var min = Plugin.Configuration.输入间隔最小毫秒;
        if (DrawSliderField("##minInterval", "输入间隔最小", ref min, 500, 30000, "ms"))
        {
            Plugin.Configuration.输入间隔最小毫秒 = Math.Clamp(min, 500, 30000);
            if (Plugin.Configuration.输入间隔最大毫秒 < Plugin.Configuration.输入间隔最小毫秒)
                Plugin.Configuration.输入间隔最大毫秒 = Plugin.Configuration.输入间隔最小毫秒;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            Plugin.Configuration.Save();

        var max = Plugin.Configuration.输入间隔最大毫秒;
        if (DrawSliderField("##maxInterval", "输入间隔最大", ref max,
            Plugin.Configuration.输入间隔最小毫秒, 30000, "ms"))
            Plugin.Configuration.输入间隔最大毫秒 = Math.Clamp(max, Plugin.Configuration.输入间隔最小毫秒, 30000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Plugin.Configuration.Save();

        var retry = Plugin.Configuration.失败重试次数;
        if (DrawSliderField("##retryCount", "失败重试次数", ref retry, 0, 10, "次"))
            Plugin.Configuration.失败重试次数 = Math.Clamp(retry, 0, 10);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Plugin.Configuration.Save();

        if (ImGui.Button("恢复默认参数", new Vector2(-1, 0)))
        {
            Plugin.Configuration.ResetAutomationDefaults();
            Plugin.Configuration.Save();
            AddLog("CFG", "已恢复默认参数");
        }
        EndCard();

        BeginCard("##debugOptions");
        var debug = Plugin.Configuration.调试日志;
        if (ImGui.Checkbox("启用调试日志", ref debug))
        {
            Plugin.Configuration.调试日志 = debug;
            Plugin.Configuration.Save();
            AutomationLog.Write(debug ? "调试日志已开启" : "调试日志已关闭", !debug);
            AddLog("CFG", debug ? "调试日志已开启" : "调试日志已关闭");
        }
        HoverTip("记录自动流程状态、输入和提交结果。错误始终记录；日志超过 2 MiB 后轮换。");
        if (ImGui.Button("打开调试日志文件夹", new Vector2(-1, 0)))
            OpenFolder(Path.GetDirectoryName(AutomationLog.FilePath)!);
        if (AutomationLog.WriteError.Length > 0)
            DrawWrapped(FailureColor, $"日志写入失败：{AutomationLog.WriteError}");
        EndCard();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"打开文件夹失败：{ex.Message}");
            Svc.Chat.PrintError($"金蝶打字通打开文件夹失败：{ex.Message}");
        }
    }

    private static bool DrawSliderField(string id, string label, ref int value, int min, int max, string unit)
    {
        DrawWrapped(MutedColor, label);
        ImGui.SetNextItemWidth(-1);
        var changed = ImGui.SliderInt(id, ref value, min, max, $"%d {unit}");
        HoverTip(unit == "ms"
            ? "默认间隔 500–5000 ms（0.5–5 秒）。填入并校验后立即发送，提交处理后随机等待再处理下一词条。"
            : "填入失败后的重试次数；输入一致性或会话异常会立即停止。");
        return changed;
    }

    private void DrawToolsTab()
    {
        DrawPageHeading("测试工具", "查看小游戏实时数据与输入诊断。");
        if (ImGui.Button("保存手动快照", new Vector2(-1, 0)))
            GameDiagnostics.Instance.RequestSnapshot();
        HoverTip("在下一次游戏线程更新中采集词条、读条节点和敌人数据，保存为 JSON 文件。无需开启只读采集。");
        if (ImGui.Button("打开快照文件夹", new Vector2(-1, 0)))
            OpenFolder(GameDiagnostics.SnapshotDirectory);
        if (GameDiagnostics.Instance.SnapshotMessage.Length > 0)
            DrawWrapped(MutedColor, GameDiagnostics.Instance.SnapshotMessage);
        DrawDiagnostics();

        if (!ImGui.CollapsingHeader("备用工具（聊天发送 / 节点点击）"))
            return;

        BeginCard("##chatTool");
        ImGui.TextUnformatted("聊天发送");
        ImGui.Separator();

        DrawWrapped(MutedColor, "发送频道");
        ImGui.SetNextItemWidth(-1);
        var channel = Plugin.Configuration.发送频道索引;
        if (ImGui.Combo("##toolChannel", ref channel, ChatSender.ChannelLabels, ChatSender.ChannelLabels.Length))
        {
            Plugin.Configuration.发送频道索引 = Math.Clamp(channel, 0, ChatSender.ChannelLabels.Length - 1);
            Plugin.Configuration.Save();
        }

        DrawWrapped(MutedColor, "发送内容");
        ImGui.SetNextItemWidth(-1);
        var text = Plugin.Configuration.发送内容;
        var send = ImGui.InputTextWithHint("##toolText", "输入消息，Enter 发送", ref text, 512,
            ImGuiInputTextFlags.EnterReturnsTrue);
        Plugin.Configuration.发送内容 = text;
        if (ImGui.IsItemDeactivatedAfterEdit())
            Plugin.Configuration.Save();

        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(text));
        var clickedSend = PrimaryButton("发送", new Vector2(-1, 0));
        ImGui.EndDisabled();
        if (send || clickedSend)
        {
            chatSuccess = ChatSender.Send(Plugin.Configuration.发送频道索引, text, out chatResult);
            AddLog(chatSuccess ? "CHAT" : "ERR", chatResult);
            if (!chatSuccess)
                Svc.Chat.PrintError(chatResult);
        }

        DrawResult(chatResult, chatSuccess);
        EndCard();

        BeginCard("##addonTool");
        ImGui.TextUnformatted("Addon 节点点击");
        ImGui.Separator();

        DrawWrapped(MutedColor, "Addon 名称");
        ImGui.SetNextItemWidth(-1);
        var addonName = Plugin.Configuration.点击Addon名称;
        if (ImGui.InputText("##addonName", ref addonName, 128))
            Plugin.Configuration.点击Addon名称 = addonName;
        if (ImGui.IsItemDeactivatedAfterEdit())
            Plugin.Configuration.Save();

        DrawWrapped(MutedColor, "节点 ID");
        ImGui.SetNextItemWidth(-1);
        var nodeId = (int)Math.Min(Plugin.Configuration.点击节点ID, int.MaxValue);
        if (ImGui.InputInt("##nodeId", ref nodeId))
            Plugin.Configuration.点击节点ID = (uint)Math.Max(0, nodeId);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Plugin.Configuration.Save();

        if (PrimaryButton("点击节点", new Vector2(-1, 0)))
        {
            clickSuccess = AddonClicker.ClickNode(Plugin.Configuration.点击Addon名称,
                Plugin.Configuration.点击节点ID, out clickResult);
            AddLog(clickSuccess ? "ADDON" : "ERR", clickResult);
            if (!clickSuccess)
                Svc.Chat.PrintError(clickResult);
        }

        DrawResult(clickResult, clickSuccess);
        EndCard();

        if ((!string.IsNullOrEmpty(chatResult) || !string.IsNullOrEmpty(clickResult)) &&
            ImGui.Button("清空结果", new Vector2(-1, 0)))
        {
            chatResult = string.Empty;
            clickResult = string.Empty;
            AddLog("SYS", "已清空结果");
        }
    }

    private void DrawDiagnostics()
    {
        var diagnostics = GameDiagnostics.Instance;
        BeginCard("##gameDiagnostics");
        ImGui.TextUnformatted("小游戏实时诊断");
        if (diagnostics.InputMessage.Length > 0)
            DrawWrapped(WarningColor, diagnostics.InputMessage);
        if (ImGui.Button("发送当前已填入文字", new Vector2(-1, 0)))
            diagnostics.RequestSend();
        if (clipboardTask is { IsCompletedSuccessfully: true })
        {
            clipboardResult = clipboardTask.Result;
            clipboardTask = null;
        }
        if (clipboardResult.Length > 0)
            DrawWrapped(MutedColor, clipboardResult);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##clipboardTest", "点击这里，再按 Ctrl+V", ref clipboardTest, 4096);
        HoverTip("粘贴验证：复制词条后，可在此框按 Ctrl+V。小游戏输入框是否支持粘贴需单独确认。");
        var enabled = diagnostics.Enabled;
        if (ImGui.Checkbox("开启只读采集（100 ms）", ref enabled))
            diagnostics.SetEnabled(enabled);
        HoverTip("独立记录最近 300 次数据变化；关闭窗口后继续记录，取消勾选后停止记录。自动流程和简报所需的读取仍会继续。");
        if (enabled)
        {
            var snapshot = diagnostics.Snapshot;
            DrawWrapped(TextColor, $"输入窗口：{(snapshot.InputVisible ? "可见" : "隐藏/未加载")}　结算：{(snapshot.ResultVisible ? "可见" : "隐藏/未加载")}");
            DrawWrapped(TextColor, $"得分：{snapshot.Score?.ToString() ?? "--"}　耐久：{snapshot.Durability?.ToString() ?? "--"}/{snapshot.MaxDurability?.ToString() ?? "--"}");
            DrawWrapped(MutedColor, $"输入状态字段：{snapshot.InputFlag?.ToString() ?? "--"}");
            HoverTip("该字段仅表示当前是否可输入，不代表整局结束。");
            DrawWrapped(MutedColor, $"更新时间：{snapshot.Time.ToLocalTime():HH:mm:ss.fff}　读取：{snapshot.ReadMilliseconds:F2} ms　记录：{diagnostics.HistoryCount}");
            if (snapshot.Error.Length > 0)
                DrawWrapped(FailureColor, snapshot.Error);
            ImGui.Separator();
            ImGui.TextUnformatted("可见词条");
            var count = 0;
            foreach (var prompt in snapshot.Prompts)
            {
                if (!prompt.Visible)
                    continue;
                ImGui.PushID(prompt.Path);
                if (ImGui.BeginTable("##promptRow", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("词条", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed,
                        ImGui.CalcTextSize("复制填入").X + ImGui.GetStyle().FramePadding.X * 4 + ImGui.GetStyle().ItemSpacing.X);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    DrawWrapped(AccentColor, $"{prompt.Path}　{prompt.Text}");
                    ImGui.TableNextColumn();
                    if (ImGui.Button("复制"))
                        CopyText(prompt.Text);
                    ImGui.SameLine();
                    if (ImGui.Button("填入"))
                        diagnostics.RequestInput(prompt.Text);
                    ImGui.EndTable();
                }
                ImGui.PopID();
                count++;
            }
            if (count == 0)
                DrawWrapped(MutedColor, "暂无可见词条");
            if (ImGui.TreeNode("输入节点与候选文字"))
            {
                foreach (var node in snapshot.InputNodes)
                    DrawWrapped(node.Visible ? TextColor : MutedColor,
                        $"{node.Path} [{node.Type}] {(node.Visible ? "可见" : "隐藏")}　{node.Text}");
                ImGui.TreePop();
            }
        }
        if (ImGui.Button("复制诊断记录", new Vector2(-1, 0)))
            CopyText(diagnostics.Export());
        if (ImGui.Button("清空诊断记录", new Vector2(-1, 0)))
            diagnostics.ClearHistory();
        EndCard();
    }

    private void CopyText(string text)
    {
        if (clipboardTask is { IsCompleted: false })
            return;
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboardTask = completion.Task;
        clipboardResult = "正在复制…";
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetDataObject(text, true, 5, 20);
                completion.SetResult(Clipboard.GetText(TextDataFormat.UnicodeText) == text
                    ? $"已复制 {text.Length} 个字符到系统剪贴板"
                    : "复制后校验失败，请重试");
            }
            catch (Exception ex)
            {
                completion.SetResult($"复制失败：{ex.Message}");
            }
        }) { IsBackground = true, Name = "GoldTyper Clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void DrawStatusTab(GameAutomation automation)
    {
        DrawPageHeading("运行状态", "查看当前题目、执行信息与最近记录。\n使用 /gt 或 /打字通 打开窗口；拖动顶部名称区域移动窗口。");

        BeginCard("##currentData");
        DrawWrapped(MutedColor, "当前题目");
        DrawWrapped(AccentColor, string.IsNullOrEmpty(automation.CurrentPrompt) ? "暂无题目" : automation.CurrentPrompt);
        ImGui.Separator();
        DrawWrapped(MutedColor, "最近信息");
        DrawWrapped(TextColor, automation.LastMessage);

        if (ImGui.Button("复制状态", new Vector2(-1, 0)))
        {
            var text = string.Join(Environment.NewLine,
                $"状态: {StateLabel(automation.State)}",
                $"暂停: {(automation.Paused ? "是" : "否")}",
                $"题目: {(string.IsNullOrEmpty(automation.CurrentPrompt) ? "-" : automation.CurrentPrompt)}",
                $"信息: {automation.LastMessage}");
            ImGui.SetClipboardText(text);
            AddLog("SYS", "状态已复制到剪贴板");
        }
        EndCard();

        BeginCard("##runHistory");
        ImGui.TextUnformatted("最近运行");
        ImGui.Separator();
        if (runHistory.Count == 0)
            DrawWrapped(MutedColor, "暂无运行记录");
        foreach (var entry in runHistory)
            DrawWrapped(MutedColor, entry);
        EndCard();

        BeginCard("##sessionLog");
        ImGui.TextUnformatted("会话日志");
        ImGui.Separator();
        foreach (var entry in uiLog)
            DrawWrapped(MutedColor, entry);
        EndCard();

    }

    private void TrackRunHistory()
    {
        var state = GameAutomation.Instance.State;
        if (state == previousState)
            return;

        if (state is GameState.Done or GameState.Error)
        {
            runHistory.Insert(0, $"{StateLabel(state)} · {DateTime.Now:HH:mm}");
            if (runHistory.Count > 6)
                runHistory.RemoveAt(runHistory.Count - 1);
            AddLog("RUN", StateLabel(state));
        }

        previousState = state;
    }

    private void AddLog(string tag, string message)
    {
        uiLog.Insert(0, $"{DateTime.Now:HH:mm}  {tag}  {message}");
        if (uiLog.Count > 8)
            uiLog.RemoveAt(uiLog.Count - 1);
    }

    private static void DrawPageHeading(string title, string description)
    {
        ImGui.TextUnformatted(title);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        HoverTip(description);
    }

    private static void HoverTip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static void DrawWrapped(Vector4 color, string text)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    private static void BeginCard(string id)
    {
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, CardBackground);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, BorderColor);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, BorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(12, 12) * ImGuiHelpers.GlobalScale);
        ImGui.BeginTable(id, 1,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.NoSavedSettings);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
    }

    private static void EndCard()
    {
        ImGui.EndTable();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private static bool PrimaryButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.89f, 0.9f, 0.92f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.97f, 0.98f, 0.99f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.76f, 0.81f, 0.88f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.07f, 0.08f, 0.1f, 1f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static void DrawResult(string result, bool success)
    {
        if (!string.IsNullOrEmpty(result))
            DrawWrapped(success ? SuccessColor : FailureColor, result);
    }

    private static string StateLabel(GameState state) => state switch
    {
        GameState.Idle => "空闲",
        GameState.Detecting => "检测中",
        GameState.Typing => "输入中",
        GameState.Submitting => "提交中",
        GameState.Done => "已完成",
        GameState.Error => "出错",
        GameState.Reentering => "再次参加",
        _ => state.ToString(),
    };

    private static string StateText(GameAutomation automation)
        => automation.Paused ? $"{StateLabel(automation.State)} · 已暂停" : StateLabel(automation.State);

    private static Vector4 CurrentStateColor(GameAutomation automation)
        => automation.Paused ? WarningColor : automation.State switch
        {
            GameState.Detecting => AccentColor,
            GameState.Typing or GameState.Submitting => WarningColor,
            GameState.Done => SuccessColor,
            GameState.Error => FailureColor,
            _ => MutedColor,
        };
}
