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
    private int candidateDelay;
    private string waitingPrompt = string.Empty;
    private string delayBreakdown = string.Empty;
    private long submittedAt;
    public string DelayBreakdown => delayBreakdown;
    public string WaitingPrompt => State == GameState.Detecting && candidate.Length > 0 ? waitingPrompt : string.Empty;
    public int RemainingDelay => IsRunning && !Paused && State == GameState.Detecting && candidate.Length > 0
        ? (int)Math.Clamp(candidateDelay - (Environment.TickCount64 - stableSince), 0, candidateDelay) : 0;
    public long LastAnswerMilliseconds { get; private set; }
    public long LastSubmitMilliseconds { get; private set; }
    private string ownedText = string.Empty;
    private string ownedSession = string.Empty;
    private bool clearRequested;
    private const int MaxAttemptsPerTarget = 5;
    private int failures;
    public bool IsRunning => State is GameState.Detecting or GameState.Typing or GameState.Submitting or GameState.Reentering;

    public void Tick(GameSnapshot snapshot)
    {
        ProcessCleanup();
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
            State = Plugin.Configuration.自动再次参加 ? GameState.Reentering : GameState.Done;
            ReentryDriver.Reset();
            LastMessage = "已进入结算界面";
            return;
        }
        if (!snapshot.InputVisible || snapshot.InputFlag != true)
        {
            if (ownedText.Length > 0)
            {
                InputDriver.ClearOwnedInput(ownedSession, ownedText);
                ownedText = string.Empty;
            }
            State = GameState.Detecting;
            CurrentPrompt = string.Empty;
            deadline = Environment.TickCount64;
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
                LastAnswerMilliseconds = now - stableSince;
                LastSubmitMilliseconds = submittedAt > 0 ? now - submittedAt : 0;
                deadline = now;
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
                deadline = now;
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
            submittedAt = now;
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
            candidateDelay = NextDelay(prompt.Text);
            waitingPrompt = prompt.Text;
            var characters = Plugin.Configuration.按词条长度动态延迟 ? prompt.Text.EnumerateRunes().Count() : 0;
            var characterDelay = (long)characters * Math.Clamp(Plugin.Configuration.每个字符追加毫秒, 0, 30000);
            delayBreakdown = $"基础随机 {lastBaseDelay} ms + {characters} 字 × {Math.Clamp(Plugin.Configuration.每个字符追加毫秒, 0, 30000)} ms = {lastBaseDelay + characterDelay} ms，实际等待 {candidateDelay} ms";
            LastMessage = $"等待词条：{prompt.Text}（延迟 {candidateDelay} ms）";
            return;
        }
        if (now - stableSince < candidateDelay)
            return;
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
        submittedAt = Environment.TickCount64;
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
        State = GameState.Error;
        LastMessage = message;
        var frame = GameDiagnostics.Instance.Snapshot;
        AutomationLog.Write($"停止现场：inputVisible={frame.InputVisible} inputFlag={frame.InputFlag} resultVisible={frame.ResultVisible} score={frame.Score} hp={frame.Durability} session={frame.Session} expectedSession={session} input={frame.InputText} target={pendingPath}", true);
        ECommons.DalamudServices.Svc.Chat.PrintError($"金蝶打字通已停止：{message}");
    }

    private static int lastBaseDelay;

    private static int NextDelay(string text = "")
    {
        var config = Plugin.Configuration;
        var min = Math.Clamp(config.输入间隔最小毫秒, 500, 30000);
        var max = Math.Clamp(config.输入间隔最大毫秒, min, 30000);
        var perCharacter = Math.Clamp(config.每个字符追加毫秒, 0, 30000);
        var characters = config.按词条长度动态延迟 ? text.EnumerateRunes().Count() : 0;
        lastBaseDelay = Random.Shared.Next(min, max + 1);
        return (int)Math.Clamp((long)lastBaseDelay + (long)characters * perCharacter, 500, 30000);
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
        ReentryDriver.RememberTarget();
        ReentryDriver.Reset();
        Paused = false;
        State = GameState.Detecting;
        CurrentPrompt = string.Empty;
        LastMessage = "等待小游戏开始";
        LastAnswerMilliseconds = 0;
        LastSubmitMilliseconds = 0;
        submittedAt = 0;
        waitingPrompt = string.Empty;
        delayBreakdown = string.Empty;
        deadline = Environment.TickCount64;
        failures = 0;
        session = string.Empty;
        candidate = string.Empty;
        attempts.Clear();
        if (Plugin.Configuration.自动再次参加 && ECommons.DalamudServices.Svc.ClientState.TerritoryType != 1359)
            State = GameState.Reentering;
    }

    public void Pause()
    {
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
        candidate = string.Empty;
        deadline = Environment.TickCount64;
    }

    public void Stop(string reason = "已停止")
    {
        clearRequested = ownedText.Length > 0;
        Paused = false;
        State = GameState.Idle;
        CurrentPrompt = string.Empty;
        LastMessage = reason;
    }

    public void ProcessCleanup()
    {
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
