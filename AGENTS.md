# GoldTyper 开发指引

本文件面向所有参与本项目的 AI/开发者，开始工作前请先读完。

## 1. 项目定位

- 载体：Dalamud 插件（FFXIV 国服，API Level 15）
- 目标：金蝶打字小游戏全自动完成：检测开始 → 读题 → 输入 → 提交
- 当前阶段：已实机确认词条来源、原生填入和结算窗口；自动提交与状态机已接入，完整自动流程实机回归待验证。

## 2. 技术栈

- C# / `net10.0-windows` / `Nullable enable` / 允许 `unsafe`
- SDK：`Dalamud.CN.NET.Sdk/14.0.1`
- 依赖：`ECommons 3.2.0.6`
- UI：`Dalamud.Bindings.ImGui`
- 构建：`dotnet build GoldTyper.sln -c Release`

## 3. 目录结构

```
GoldTyper.sln
GoldTyper/
  Plugin.cs                插件入口、命令 /gt、/打字通
  Configuration.cs         持久化配置
  GoldTyper.json           插件清单
  Windows/DebugWindow.cs   无原生标题栏窗口、常驻操作区、四个滚动页面
  Game/
    GameAutomation.cs      自动读题、延迟提交、会话与输入保护
    InputDriver.cs         原生填入、Enter 回调与输入一致性校验
    GameDiagnostics.cs     游戏线程只读采集、词条与输入节点诊断（实机待验证）
    ChatSender.cs          聊天频道发送（走 ECommons Chat.SendMessage）
    AddonClicker.cs        addon 节点原生点击（ButtonClick 派发）
docs/
  plan.md                  阶段规划
  ui.md                    插件 UI 行为、布局约定与验证记录
  ui-structure.md          P0 实机调研记录（进行中）
  preview/                 早期 HTML 视觉草稿（与当前实现有差异）
```

## 4. 编码约定

- 保持现有代码风格：优先局部改动，避免无关重构。
- **在 `Plugin` 以外的文件里引用配置必须写 `Plugin.Configuration`**，直接写 `Configuration` 会绑定到类型名导致编译错误。
- Hook / SigScanner / 指针访问必须有异常保护与空值保护；detour 必须 `try/catch` 且始终调用 `Original`。
- 重要错误使用 `Svc.Chat.PrintError`。
- 除非用户要求，不添加注释。
- Dalamud 接口文档：<https://dalamud.dev/api/>。
- 本机卫月 MCP 调试端点：`http://127.0.0.1:39217/mcp/`，连接说明见 `docs/ui-structure.md` 的“本机卫月 MCP 连接”。调研前先确认连接与实际工具能力，不能将记录的地址视为已连接。
- UI 保持顶部操作区常驻、页面内容独立滚动；优先使用内容自然撑高的布局，避免固定高度卡片裁切文本、绝对坐标右对齐导致窄窗口溢出，尺寸与间距适配 Dalamud 全局缩放。
- 运行状态由 `GameAutomation` 管理；点击“开始”直接启动，不恢复“启用自动完成”配置字段或启动前置开关。加载后默认 `Idle`，重新打开窗口不自动开始。
- UI 当前行为与已验证范围见 `docs/ui.md`；离线验证不能记为游戏内回归通过。

## 5. 待实现（按顺序）

1. `Game/AddonWatcher.cs`：用 `Svc.AddonLifecycle` 监听小游戏 addon 出现/关闭，产出 `Detected/Closed` 事件
2. `Game/TextExtractor.cs`：从 addon 节点/AtkValues 提取题目文本，接口化便于替换策略
3. `Game/InputDriver.cs`：优先写输入控件，备选 `SendInput`；处理输入法状态
4. `Game/GameAutomation.cs`：把上述模块串成状态机 `Idle → Detecting → Typing → Submitting → Done/Error`
5. 安全机制：白名单 addon、连续失败止损、全局暂停

## 6. P0 调研清单（游戏上线后第一步）

见 `docs/ui-structure.md`。必须先得到以下结论再写实现：

- 小游戏 addon 名称与出现/关闭时机
- 题目文本所在节点或数据源（AtkValues / SeString）
- 输入方式：是否有文本输入控件、是否需要激活
- 提交方式：回车 / 专用按钮 / 自动判定
- 是否存在错误惩罚、时间限制、固定词表

## 7. 风险

自动输入属于自动化操作，可能违反游戏条款并导致封号。任何实现都必须：

- 加载后默认空闲，由用户点击“开始”显式启动
- 只在目标 addon 处于激活状态时动作
- 提供随时可用的暂停/停止
- 不把 key/账号信息写入日志
