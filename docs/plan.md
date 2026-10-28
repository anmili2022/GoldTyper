# 项目规划

## 当前进度

- P1 骨架、测试工具与本轮 UI 重构已完成。
- “开始”目前只将状态机切换到 `Detecting`；addon 监听、题目提取、输入、提交及自动终态判定尚未接入。
- P0 实机调研进行中，已确认“魔光键影”准备窗口 `KTGEnterDialogue` 与报名按钮节点；题目、输入与提交方式仍待采集。实证记录见 [UI 结构调研](ui-structure.md)，识别与输入实现须等待关键调研结论。
- 本轮 Release 构建通过，0 个警告、0 个错误；离线布局及操作按钮验证已完成，重构后的游戏内回归待验证。
- 插件 UI 当前行为与验证记录见 [UI 说明](ui.md)。

## 阶段

### P0 预研（游戏上线后 1-2 天）

- 用 Dalamud addon inspector / `/xldata` 找到小游戏 addon 名称与文本节点
- 确认：题目来源、输入控件、提交判定
- 产出：`docs/ui-structure.md`
- 验收：稳定 dump 当前题目文本

### P1 骨架（已完成）

- 解决方案、插件入口、配置、调试窗口
- 命令 `/gt` 与 `/打字通`
- 加载后默认空闲，点击“开始”直接进入检测状态，提供暂停 / 恢复与停止
- UI：移除原生标题栏和启用开关，顶部操作区常驻，自动 / 选项 / 测试工具 / 状态四页独立滚动
- 卡片随内容增高，长文本换行，适配窄窗口及全局缩放；聊天发送集中在测试工具页
- 测试工具：聊天发送（`ChatSender`）、addon 节点点击（`AddonClicker`）
- 聊天发送走 `ECommons.Automation.Chat.SendMessage`（`UIModule.ProcessChatBoxEntry`），非 `Svc.Commands.ProcessCommand`（后者仅支持插件注册命令）
- 原有骨架已完成构建与游戏内加载验证；本轮 UI 重构已通过 Release 构建和离线检查，游戏内回归待完成，详见 `docs/ui.md`

### P2 识别层

- `AddonWatcher`：监听 addon 生命周期
- `TextExtractor`：提取题目，失败降级到文本扫描
- 状态机接入
- 验收：开始 200ms 内识别，准确率 >99%

### P3 输入层

- `InputDriver`：优先控件写入，备选 `SendInput`
- 输入法处理、随机间隔（配置项已有）
- 验收：单局成功率 >95%

### P4 稳定化

- 失败重试与止损（配置项已有）
- 连续 10 局压测
- 将已有暂停 / 停止状态控制接入实际输入与提交流程，补齐白名单校验
- 验收：连续 10 局无失败

## 架构

```
Plugin.cs
 ├─ Configuration.cs
 ├─ Windows/DebugWindow.cs
 └─ Game/
     ├─ AddonClicker.cs    已有（原生 ButtonClick 派发）
     ├─ ChatSender.cs      已有（聊天发送）
     ├─ AddonWatcher.cs    待建
     ├─ TextExtractor.cs   待建
     ├─ InputDriver.cs     待建
     └─ GameAutomation.cs  已有状态机骨架
```

`TextExtractor` 与 `InputDriver` 采用接口 + 单一实现，P0 结论出来后只补实现，不改主流程。
