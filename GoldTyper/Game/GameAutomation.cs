using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace GoldTyper.Game;

public enum GameState
{
    Idle,
    Detecting,
    Typing,
    Submitting,
    Done,
    Error,
    Reentering
}

public sealed class GameAutomation : IDisposable
{
    public static GameAutomation Instance { get; } = new();

    public GameState State { get; private set; } = GameState.Idle;
    public string CurrentPrompt { get; private set; } = string.Empty;
    private string lastMessage = "未启动";
    public string LastMessage
    {
        get => lastMessage;
        private set
        {
            if (lastMessage == value)
                return;
            lastMessage = value;
            AutomationLog.Write($"state={State} paused={Paused} prompt={CurrentPrompt} message={value}", State == GameState.Error);
        }
    }
    public bool Paused { get; private set; }

    private bool initialized;
    private long deadline;
    private string pendingPath = string.Empty;
    private string session = string.Empty;
    private readonly Dictionary<string, int> attempts = new();
    private string candidate = string.Empty;
    private long stableSince;
    private string ownedText = string.Empty;
    private string ownedSession = string.Empty;
    private bool clearRequested;
    private const int MaxAttemptsPerTarget = 5;
    private int failures;
    public bool IsRunning => State is GameState.Detecting or GameState.Typing or GameState.Submitting or GameState.Reentering;

    public void Tick(GameSnapshot snapshot)
    {
        ProcessCleanup();
        if (!Plugin.Configuration.启用防Afk活动输入)
        {
            ActivityInput.Reset();
            ActivityInput.ProcessCleanup();
        }
        if (!IsRunning || Paused)
            return;
        if (State == GameState.Reentering)
        {
            if (!Plugin.Configuration.自动再次参加)
            {
                State = GameState.Done;
                LastMessage = "已关闭自动再次参加";
                return;
            }
            if (snapshot.InputVisible && snapshot.InputFlag == true && !snapshot.ResultVisible)
            {
                Start();
                return;
            }
            LastMessage = ReentryDriver.Tick();
            return;
        }
        if (snapshot.Error.Length > 0)
        {
            Abort(snapshot.Error);
            return;
        }
        if (snapshot.ResultVisible)
        {
            ActivityInput.Reset();
            State = Plugin.Configuration.自动再次参加 ? GameState.Reentering : GameState.Done;
            ReentryDriver.Reset();
            LastMessage = "已进入结算界面";
            return;
        }
        if (!snapshot.InputVisible || snapshot.InputFlag != true)
        {
            ActivityInput.Reset();
            if (ownedText.Length > 0)
            {
                InputDriver.ClearOwnedInput(ownedSession, ownedText);
                ownedText = string.Empty;
            }
            State = GameState.Detecting;
            CurrentPrompt = string.Empty;
            deadline = Environment.TickCount64 + NextDelay();
            candidate = string.Empty;
            LastMessage = "等待可输入阶段";
            return;
        }
        if (session.Length == 0)
            session = snapshot.Session;
        if (snapshot.Session.Length == 0 || snapshot.Session != session)
        {
            Abort("游戏输入界面已重建，请重新点击开始");
            return;
        }
        var now = Environment.TickCount64;
        if (Plugin.Configuration.启用防Afk活动输入 && ActivityInput.Tick(snapshot, false))
        {
            LastMessage = ActivityInput.Status;
            return;
        }
        var live = snapshot.Prompts.Where(IsValid).ToArray();
        var keys = live.Select(Key).ToHashSet();
        foreach (var key in attempts.Keys.Where(key => !keys.Contains(key)).ToArray())
            attempts.Remove(key);
        if (State == GameState.Submitting)
        {
            if (snapshot.InputText.Length == 0)
            {
                ownedText = string.Empty;
                State = GameState.Detecting;
                failures = 0;
                deadline = now + NextDelay();
                candidate = string.Empty;
                LastMessage = "提交回调已处理，等待下一次输入（不代表已加分）";
            }
            else if (snapshot.InputText != CurrentPrompt)
                Abort("输入被手动更改，已停止");
            else if (now >= deadline)
                Abort("提交后输入未清空，请检查提交机制");
            return;
        }
        if (State == GameState.Typing)
        {
            if (snapshot.InputText != CurrentPrompt)
            {
                Abort("填入内容已变化，已停止自动提交");
                return;
            }
            if (!keys.Contains(pendingPath))
            {
                InputDriver.ClearOwnedInput(ownedSession, ownedText);
                ownedText = string.Empty;
                State = GameState.Detecting;
                deadline = now + NextDelay();
                candidate = string.Empty;
                LastMessage = "词条已变化，取消待提交操作";
                return;
            }
            if (now < deadline)
                return;
            var message = InputDriver.Send(CurrentPrompt);
            LastMessage = message;
            if (message.StartsWith("发送失败", StringComparison.Ordinal))
            {
                Abort(message);
                return;
            }
            attempts[pendingPath] = attempts.GetValueOrDefault(pendingPath) + 1;
            State = GameState.Submitting;
            deadline = now + 5000;
            return;
        }
        if (now < deadline)
            return;
        if (snapshot.InputText.Length != 0)
        {
            Abort("输入框已有文字，已停止，避免覆盖手动输入");
            return;
        }
        var prompt = live.Where(p => attempts.GetValueOrDefault(Key(p)) < MaxAttemptsPerTarget)
            .OrderByDescending(p => Plugin.Configuration.优先处理读条目标 && IsCastingTarget(snapshot, p))
            .ThenByDescending(p => Plugin.Configuration.优先处理近距离目标 ? p.Depth : 0f)
            .ThenBy(p => attempts.GetValueOrDefault(Key(p)))
            .ThenBy(p => p.Text.EnumerateRunes().Count())
            .ThenBy(p => p.Path, StringComparer.Ordinal).FirstOrDefault();
        if (prompt == null)
        {
            candidate = string.Empty;
            if (live.Length > 0)
                Abort("可见词条均已提交 5 次，请检查游戏反馈后重新开始");
            else
                LastMessage = "等待新的可见词条";
            return;
        }
        if (candidate != Key(prompt))
        {
            candidate = Key(prompt);
            stableSince = now;
            LastMessage = $"等待词条稳定：{prompt.Text}";
            return;
        }
        if (now - stableSince < 200)
            return;
        if (Plugin.Configuration.启用防Afk活动输入 && ActivityInput.Tick(snapshot, true))
        {
            LastMessage = ActivityInput.Status;
            return;
        }
        CurrentPrompt = prompt.Text;
        pendingPath = Key(prompt);
        var filled = InputDriver.Fill(CurrentPrompt, true);
        if (filled.StartsWith("填入失败", StringComparison.Ordinal))
        {
            Fail(filled);
            return;
        }
        ownedText = CurrentPrompt;
        ownedSession = session;
        ActivityInput.Consume();
        State = GameState.Typing;
        var sent = InputDriver.Send(CurrentPrompt);
        LastMessage = sent;
        if (sent.StartsWith("发送失败", StringComparison.Ordinal))
        {
            Abort(sent);
            return;
        }
        attempts[pendingPath] = attempts.GetValueOrDefault(pendingPath) + 1;
        State = GameState.Submitting;
        deadline = Environment.TickCount64 + 5000;
    }

    private static string Key(DiagnosticText prompt) => prompt.Identity + "\n" + prompt.Text;

    private static bool IsCastingTarget(GameSnapshot snapshot, DiagnosticText prompt)
    {
        var separator = prompt.Path.IndexOf('/');
        if (separator < 0)
            return false;
        var castPath = prompt.Path[..separator] + "/4";
        return snapshot.CastNodes.Any(node => node.Path == castPath && node.Visible &&
            !string.IsNullOrWhiteSpace(node.Text));
    }

    private static bool IsValid(DiagnosticText prompt) => prompt.Visible &&
        !string.IsNullOrWhiteSpace(prompt.Text) && prompt.Text.Length <= 128 &&
        Encoding.UTF8.GetByteCount(prompt.Text) <= 512 && !prompt.Text.Any(char.IsControl);

    private void Abort(string message)
    {
        ActivityInput.Reset();
        State = GameState.Error;
        LastMessage = message;
        var frame = GameDiagnostics.Instance.Snapshot;
        AutomationLog.Write($"停止现场：inputVisible={frame.InputVisible} inputFlag={frame.InputFlag} resultVisible={frame.ResultVisible} score={frame.Score} hp={frame.Durability} session={frame.Session} expectedSession={session} input={frame.InputText} target={pendingPath} activity={ActivityInput.Status}", true);
        ECommons.DalamudServices.Svc.Chat.PrintError($"金蝶打字通已停止：{message}");
    }

    private static int NextDelay()
    {
        var min = Math.Clamp(Plugin.Configuration.输入间隔最小毫秒, 500, 30000);
        var max = Math.Clamp(Plugin.Configuration.输入间隔最大毫秒, min, 30000);
        return Random.Shared.Next(min, max + 1);
    }

    private void Fail(string message)
    {
        failures++;
        LastMessage = message;
        State = failures >= Math.Clamp(Plugin.Configuration.失败重试次数 + 1, 1, 11) ? GameState.Error : GameState.Detecting;
        deadline = Environment.TickCount64 + NextDelay();
    }

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        Paused = false;
        State = GameState.Idle;
        CurrentPrompt = string.Empty;
        LastMessage = "点击开始，等待小游戏";
    }

    public void Start()
    {
        ActivityInput.Reset();
        ReentryDriver.RememberTarget();
        ReentryDriver.Reset();
        Paused = false;
        State = GameState.Detecting;
        CurrentPrompt = string.Empty;
        LastMessage = "等待小游戏开始";
        deadline = Environment.TickCount64 + NextDelay();
        failures = 0;
        session = string.Empty;
        candidate = string.Empty;
        attempts.Clear();
        if (Plugin.Configuration.自动再次参加 && ECommons.DalamudServices.Svc.ClientState.TerritoryType != 1359)
            State = GameState.Reentering;
    }

    public void Pause()
    {
        ActivityInput.Reset();
        Paused = true;
        clearRequested = ownedText.Length > 0;
        if (IsRunning && State != GameState.Reentering)
            State = GameState.Detecting;
        CurrentPrompt = string.Empty;
        LastMessage = "已暂停";
    }

    public void Resume()
    {
        if (State == GameState.Idle)
        {
            Start();
            return;
        }

        Paused = false;
        LastMessage = "已恢复";
        deadline = Environment.TickCount64 + NextDelay();
    }

    public void Stop(string reason = "已停止")
    {
        ActivityInput.Reset();
        clearRequested = ownedText.Length > 0;
        Paused = false;
        State = GameState.Idle;
        CurrentPrompt = string.Empty;
        LastMessage = reason;
    }

    public void ProcessCleanup()
    {
        ActivityInput.ProcessCleanup();
        if (!clearRequested)
            return;
        clearRequested = false;
        InputDriver.ClearOwnedInput(ownedSession, ownedText);
        ownedText = string.Empty;
    }

    public void Dispose()
    {
        Stop();
        initialized = false;
    }
}
