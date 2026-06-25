# 工作流与模块交互审查（2026-06-21）

本文记录对当前文档、代码结构、端到端工作流和跨模块交互的专项审查。审查重点不是重复确认“类和测试是否存在”，而是检查模块之间的数据权威、保存语义、异步并发、默认值继承和导出优先级是否一致。

## 2026-06-25 状态更新

本报告保留 2026-06-21 当时发现的问题和整改建议，用于追溯。2026-06-25 复查确认，以下问题已在代码和回归测试中关闭：

- IA-01：顶部/角色管理保存已写入 workspace draft，保存失败会保留 dirty 状态。
- IA-04：Core 保存层不再从行级普通图或行级 PSD rendered PNG 推导角色默认视觉。
- IA-05：显式行 PSD 未渲染时，即使角色默认图存在，也会报告行级预检问题。
- IA-06：重新分析会处理 dirty 工作台，并用 revision / 文稿快照防止晚到响应覆盖新编辑。
- TTS 运行中普通工作台写操作锁已收敛；保存按钮只提示等待或停止合成，不再隐式取消；角色管理顶层写按钮在合成中禁用。行缩略图触发的 PSD 单行重选按图片操作保留可用。
- 导出投影已修复角色内心/角色动作/旁白场效音频混入同一轨的问题；`EditingTimeline` 八轨为当前权威，剪映草稿按八轨拆分输出。
- 角色内心/角色动作行已通过 `SourceRoleCharacterId/SourceRole` 解析角色图或角色 PSD rendered PNG，不再只有角色台词存在时才有角色图片。
- PSD AI 匹配已改为只控制眉/眼/嘴/左手/右手候选层，空值或非法候选回退到对应组默认可见层，其它图层显隐保持不动。

当前仍需跟进的是跨外部操作统一协调、项目 JSON 原子写和真实剪辑软件/真实 ComfyUI 端到端验证。

## 审查结论

当前解决方案的工程分层总体合理：

- `DraftAgent.Core` 没有引用 Avalonia，领域模型、TTS、资产、时间线和导出逻辑具备独立测试能力。
- `DraftAgent.App` 通过 ViewModel 和适配层连接 Core，PSD 纯逻辑单独放在 `PsdLayerLoader.Core`。
- LLM 输出经过转换和归一化后进入 `InteractionProject`，导出统一经过 `WorkbenchTimelineAssembler`，主链路没有重新接回旧 shot-list。
- 全量构建与现有自动化测试均通过。

2026-06-21 当时的跨模块交互缺口如下；其中原 IA-02 已在后续 TTS 修复中收敛，保存语义、长事务编辑锁、角色默认视觉污染、行级 PSD 预检和重新分析 dirty/revision 已在后续修复中关闭：

1. “保存”只提交到本机 last-project 存储，没有同步写入当前项目目录的 `draft-agent-workspace.json`。
2. TTS 等长事务虽然已有串行化、轮询上限/间隔和取消入口，但如果运行期间继续允许普通文本/绑定编辑，后续状态发布仍可能覆盖未合并的新编辑。
3. 行级角色视觉会反向覆盖角色默认视觉，与“行级 PSD 不污染角色聚合默认值”的产品规则冲突。
4. 导出预检在行级 PSD 未渲染时可能回退到角色默认图片，从而隐藏应提示的行级问题。
5. 重新分析会直接替换当前项目，没有处理未保存工作台编辑。

因此，当前状态应表述为“核心模块主路径通过；跨外部操作统一协调、项目 JSON 原子写和真实端到端验证仍部分通过”，不应把真实软件/真实 ComfyUI 环境验证说成已完成。

## 审查范围与证据

### 文档

- [README.md](../README.md)
- [architecture.md](architecture.md)
- [engineering-rules.md](engineering-rules.md)
- [module-review.md](module-review.md)
- [review-log.md](review-log.md)
- `docs/review-history/2026-06-19.md` 至 `2026-06-21.md`

### 关键代码

- 应用编排：`MainWindowViewModel*.cs`
- 工作台权威模型：`WorkbenchDocument`、`WorkbenchSession`、`WorkbenchCommandService`
- 项目投影：`WorkbenchEditingService`、`InteractionProjectSceneUpdater`、`InteractionProjectCharacterUpdater`
- 持久化：`DraftWorkspaceService`、`FileInteractionProjectStore`、`WorkspaceProjectManager`
- TTS：`TtsSynthesisQueueService`、`GeneratedAssetImportService`
- 视觉与导出：`VisualAssetBinding`、`WorkbenchTimelineAssembler`、`VisualExportAssetResolver`
- PSD：`PsdLayerComponentClient`、`PsdLayerLoader.Core`

### 验证结果

2026-06-21 在当前工作区执行：

```powershell
dotnet build .\DraftAgent.sln --no-restore
dotnet test .\DraftAgent.sln --no-restore
```

结果：

- solution build：0 警告、0 错误。
- PSD Loader：28/28。
- Core：308/308。
- App：579/579。

这些结果证明现有回归集稳定，但下文问题多数缺少对应的跨模块合同测试；绿测不能证明这些交互已经正确。

### 审查限制

- 当前工作区的 `.git` 目录不能被 Git 识别为有效仓库，因此本次只能审查当前文件状态，不能可靠比较提交历史或未提交差异。
- 未连接真实 ComfyUI，也未在真实剪辑软件中导入 FCPXML；相关结论来自代码、自动化测试和现有文档合同。

## 当前端到端工作流

```text
文稿输入/解说优化
  -> AI 连接检查
  -> scene_interaction_flow_v1
  -> InteractionProjectNormalizer
  -> InteractionProject
  -> WorkbenchDocument / WorkbenchSession
  -> 工作台编辑、角色/场景/声音绑定
  -> PSD source / rendered PNG、TTS task / generated audio
  -> 项目 manifest + workspace draft
  -> WorkbenchTimelineAssembler
  -> 导出预检
  -> FCPXML / 剪映草稿
```

当前真正存在多个“数据权威”：

| 状态 | 当前权威 | 主要消费者 |
|---|---|---|
| 工作台编辑中 | `WorkbenchSession.Document` | UI、撤销/重做、提交 |
| 已提交业务状态 | `MainWindowViewModel.Project` / `InteractionProject` | TTS、资产、导出 |
| 本机恢复 | `%LocalAppData%/DraftAgent/last-project.json` | 启动恢复 |
| 可搬迁项目 | `draft-agent-workspace.json` | 打开项目、跨机器复制 |
| 项目资产索引 | `draft-agent-project.json` | 预览、试听、导出 |

主要风险来自这些权威之间没有统一提交协议：部分操作只更新其中一个或两个副本。

## 模块划分与依赖判断

| 模块 | 职责 | 依赖方向 | 结论 |
|---|---|---|---|
| `DraftAgent.Core` | 领域模型、工作台命令、TTS、资产、时间线、导出 | 不依赖 App/Avalonia | 通过 |
| `PsdLayerLoader.Core` | PSD 协议、图层模型、匹配与 selection | 独立纯逻辑 | 通过 |
| `DraftAgent.App` | UI、ViewModel、文件选择、预览、外部操作编排 | 依赖 Core 和 PSD Core | 部分通过 |
| `WorkbenchDocument/Session` | 编辑期权威、撤销/重做 | Core 内部 | 通过 |
| `WorkspaceProjectManager` | 项目目录、manifest、workspace draft | Core 内部文件边界 | 单体功能通过，App 集成部分通过 |
| `TtsSynthesisQueueService` | 提交、轮询、状态迁移 | 外部 ComfyUI 接口 | Core 功能通过，App 调度部分通过 |
| `WorkbenchTimelineAssembler` | 项目到统一时间线 | 项目资产解析 | 部分通过 |
| `MainWindowViewModel` | 所有功能协调 | 连接几乎全部模块 | 维护风险高 |

`MainWindowViewModel` 的 6 个 partial 文件合计约 7201 行，其中 Binding 2820 行、Workbench 2354 行。partial 只拆分了文件，没有形成独立生命周期、并发和持久化边界；它仍是单一高耦合协调器。

## 跨模块交互矩阵

| 来源 | 目标 | 应有合同 | 当前判断 |
|---|---|---|---|
| LLM | Normalizer | 丢弃模型夹带任务/绑定，只保留 scene flow | 通过 |
| Normalizer | `InteractionProject` | 建立人物、场景、TTS 任务和默认绑定 | 通过 |
| `InteractionProject` | `WorkbenchDocument` | 恢复行、任务链接、默认绑定 | 通过 |
| 工作台 UI | `WorkbenchSession` | 局部命令更新，支持撤销/重做 | 通过 |
| `WorkbenchSession` | `InteractionProject` | 提交后结构、任务和问题一致 | 通过：行级普通图/PSD rendered PNG 不再反写角色默认视觉 |
| App 保存 | 本机 store | 可恢复最近状态 | 通过 |
| App 保存 | workspace draft | 用户点击保存后项目目录立即可搬迁 | 已关闭：保存会写 workspace draft，失败保留 dirty |
| TTS App 调度 | Core 队列 | 有节流、上限、取消、窗口队列和运行中编辑锁策略 | 通过：delay/max-pass/cancel、窗口队列、prompt 缺失释放和工作台写操作锁定已实现 |
| TTS 完成 | manifest/project | 音频导入后任务引用与项目草稿一致 | 通过：完成一条导入一条并刷新工作台行状态 |
| 行级 PSD | 角色默认绑定 | 行 override 不污染角色默认值 | 已关闭：行级 PSD rendered PNG 不反向污染角色默认值或聚合缩略图 |
| 视觉绑定 | 导出预检 | 显式行绑定失效时必须报告该行 | 已关闭：显式行 PSD 未渲染时不静默回退角色默认图 |
| 外部异步操作 | `Project` | 串行或使用版本令牌，旧结果不能覆盖新状态 | 部分通过：已有运行锁和 revision 防护，仍缺统一 coordinator / cancellation 合同 |

## 缺陷与风险

### IA-01 高：保存按钮没有保存到项目目录

证据：

- `MainWindowViewModel.Workbench.cs:54-60` 的 `SaveWorkbenchEditsAsync` 只调用 `_externalActionTransaction.RunAsync(Task.FromResult)`。
- `MainWindowViewModel.Workbench.cs:1418-1425` 的 `CommitWorkbenchAsync` 最终调用 `DraftWorkspaceService.ApplyWorkbenchDocumentAsync`。
- `DraftWorkspaceService.ApplyWorkbenchDocumentAsync` 只通过 `_store.SaveAsync` 保存本机 last-project。
- 只有 `SaveCurrentProjectSnapshotAsync`、导出等少数路径显式调用 `SaveWorkspaceProjectDraftAsync`。

影响：

- 顶部“保存”和角色管理中的“保存”会显示成功，并把工作台标记为非脏，但当前项目目录的 `draft-agent-workspace.json` 仍可能是旧内容。
- 把项目文件夹复制到另一台机器，或手动重新打开该项目目录时，可能看不到最近保存的编辑。
- 打开另一个项目之前，旧项目工作台变更只提交到本机恢复文件，不会可靠写回旧项目目录。

建议：

- 建立唯一的 `ProjectPersistenceCoordinator.SaveCommittedProjectAsync`。
- 保存顺序至少应为：提交工作台 -> 更新脚本 tab -> 写本机恢复 -> 写 workspace draft -> 更新 manifest 状态。
- `SaveWorkbenchEditsCommand` 在存在 workspace 时必须等待 workspace draft 成功后才能清除 dirty 状态并显示“已保存”。

应补测试：

- `SaveWorkbenchEditsPersistsWorkspaceDraftWhenProjectIsOpen`
- `SwitchingWorkspacePersistsPreviousWorkspaceBeforeLoad`
- `WorkspaceSaveFailureKeepsWorkbenchDirty`

### IA-02 已处理：TTS 实时循环补齐节流、上限和取消

原证据：

- `MainWindowViewModel.Tts.cs:560-585` 的 `ProcessTtsQueueWithLiveImportAsync` 使用 `while (HasProcessableTtsTask(...))`。
- 循环内连续调用 `ProcessNextTtsQueueStepAsync`，没有 `Task.Delay`、最大轮次、超时或用户取消令牌。
- Core 已经提供带 `MaxPasses` 和 `DelayMilliseconds` 的 `TtsQueueDrainOptions`，但 App 这条主路径没有使用。

当前修正：

- App 实时 TTS 循环已有 `LiveTtsQueueDelayMilliseconds`、`LiveTtsQueueMaxPasses` 和 cancellation token。
- 顶部“语音合成”按钮在 `IsTtsSynthesisRunning` 时切换为“停止合成”，再次点击会停止当前本地提交/轮询并恢复工作台拖拽。
- `TtsSynthesisQueueService` 对 `OperationCanceledException` 继续抛出，不再把用户取消写成 Failed；App 统一显示“语音合成已取消”。
- 已提交 prompt 连续从 ComfyUI history/queue 查询不到时，会清除 promptId 并释放本地窗口队列占用，避免两个失踪 prompt 永久阻塞后续行提交。
- App 默认最多保留 2 个本项目 ComfyUI prompt 在飞，完成或释放一个窗口后才提交下一个。
- 完成音频导入项目 assets 后会同步工作台行状态，行内试听/重生成按钮立即刷新。

剩余注意：

- 只在任务状态真正变化时发布；优先增量更新 task/row，而不是每次轮询替换整个项目。
- TTS 运行期间普通工作台写操作由 IA-03 的编辑锁定策略处理。

已补测试：

- `CancelTtsSynthesisCommandStopsRunningSubmissionAndRestoresWorkbench`
- `HeaderProjectActionsSeparateOpenAndSaveCommands`

### IA-03 中高：外部异步操作可能互相覆盖；TTS 普通编辑覆盖风险已收敛

证据：

- `WorkbenchExternalActionTransaction` 已有 `SemaphoreSlim` 操作门和 revision 检查能力，AI 分析路径会用 revision 防止旧结果覆盖新编辑。
- 但 `RunAsync` 内部的 TTS 多轮 `PublishAsync` 仍会发布整份 `Project`，其它外部操作还没有统一 revision/取消策略。
- TTS 运行时已禁用工作台结构/素材写操作，包括拖拽、角色、类型、增删行/场景、绑定和 PSD 匹配；未完成、队列中、合成中的行继续锁定文本和情绪。试听不禁用；已完成行仅解锁文本/情绪编辑，并可按当前行文本/情绪排队重生成。
- 每次 `Project` 变化都会执行 `RefreshCollections`，重建 `WorkbenchDocument` 并 reset session。
- TTS 现在有停止入口、窗口队列、缺失 prompt 释放和编辑锁定策略；仍缺少跨外部操作的统一事务/取消合同。

影响：

- TTS 启动后，普通工作台结构/素材写操作不再允许；文本/情绪写入例外只适用于已完成 TTS 行，重生成走显式队列请求，并使用当前行文本/情绪。
- 跨外部操作的串行化已部分收敛，但打开项目、导出、AI 分析和其它可能写 `Project` 的操作仍需统一协调。

建议：

- 继续保持 TTS 期间工作台结构/素材写操作全局锁定；命令和同步层必须恢复或拒绝非法运行中数据漂移。TTS 启动前先提交当前工作台，运行中已完成行可编辑文本/情绪，重生成必须通过显式队列请求并使用当前行状态。
- 对打开项目、导出等剩余外部操作继续统一 operation coordinator 和取消/等待策略。

应补测试：

- `ConcurrentExternalActionsDoNotOverwriteNewerProject`
- `OpeningProjectCancelsOrWaitsForActiveProjectMutation`

### IA-04 中高：行级视觉会覆盖角色默认视觉

证据：

- `InteractionProjectCharacterUpdater.ResolvePortrait` 和 `ResolvePortraitVisualBinding` 先读取角色交互行绑定，再读取显式 `WorkbenchCharacterBindingEdit` 默认绑定。
- `InteractionProjectCharacterUpdaterTests` 明确固定了“row visual before explicit default”的行为。
- 当前产品文档又要求行级 PSD rendered PNG 只属于目标行，不污染角色聚合缩略图或角色默认视觉。

影响：

- 保存工作台后，某个角色第一条有行级 rendered PNG 的台词可能被写成 `Character.AssetBinding.PortraitVisualBinding`。
- 重新打开项目、创建新台词行或读取角色默认素材时，行级姿态可能被误当成角色默认图。
- UI 当前通过聚合投影规则隐藏了部分症状，但 Core 项目模型已经发生污染。

建议：

- 明确区分 `CharacterDefaultVisualBinding` 与 `InteractionVisualOverride`。
- `InteractionProjectCharacterUpdater` 应优先保留显式角色默认绑定；只有没有默认绑定且兼容旧数据时，才允许从普通图片行推导一次。
- PSD 行级 rendered binding 不得用于推导角色默认值。

应补测试：

- `ApplyingWorkbenchKeepsExplicitCharacterDefaultBeforeRowOverride`
- `RowPsdRenderDoesNotRewriteCharacterDefaultVisual`
- `ReopenProjectKeepsRoleDefaultIndependentFromFirstRow`

### IA-05 中：导出预检可能掩盖行级 PSD 未渲染

证据：

- `WorkbenchTimelineAssembler.ResolveCharacterVisualAsset` 把行级视觉和角色默认视觉拼成同一个候选列表。
- `FirstExportableResolution` 遇到 PSD 时只记录 `blockedPsd`，随后继续寻找角色默认图片。
- 只有完全找不到可导出图片时才报告 `PsdReference`。

影响：

- 行明确绑定了一个尚未渲染的 PSD，但角色默认图片存在时，预检可直接选择默认图片并不报告问题。
- 导出结果看似成功，实际丢失了该行预期的表情/动作图层组合。

建议：

- 将优先级改为分层决策：先判断是否存在显式行绑定。
- 显式行绑定存在但不可导出时，必须报告该行问题；只有行绑定为 `None` 时才能回退角色默认视觉。
- 场景视觉使用同样的“显式绑定失败不可静默 fallback”原则。

应补测试：

- `PreflightReportsUnrenderedRowPsdEvenWhenCharacterDefaultImageExists`
- `MissingExplicitRowImageDoesNotSilentlyUseCharacterDefault`

### IA-06 中：重新分析可能静默丢弃未保存工作台编辑

证据：

- `AnalyzeScriptAsync` 完成后直接执行 `Project = AttachCurrentScriptDrafts(response.Project)`。
- 操作前没有检查 `IsWorkbenchDirty`，也没有提交、确认丢弃或创建新版本。
- `Project` setter 会重建工作台 session。

影响：

- 用户修改工作台后点击“分析”，未保存编辑会被新分析结果覆盖。
- 由于分析可能耗时，等待期间发生的新编辑也可能在响应返回时被覆盖。

建议：

- 分析前检测 dirty：提供“保存并分析 / 丢弃并分析 / 取消”。
- 分析请求绑定输入文本版本和项目 revision；响应返回时若版本变化，应提示而不是直接发布。
- 更稳妥的产品路径是把重新分析结果作为新草稿版本，而不是覆盖当前工作台。

应补测试：

- `AnalyzeDirtyWorkbenchRequiresDecision`
- `LateAnalysisResponseDoesNotOverwriteNewerWorkbenchRevision`

### IA-07 中：项目 JSON 使用直接覆盖写，缺少原子提交和恢复

证据：

- `FileInteractionProjectStore`、`WorkspaceProjectManager.SaveDraftAsync` 和 manifest 写入都直接调用 `File.WriteAllTextAsync` 覆盖目标文件。
- 没有临时文件、flush、原子 replace 或 `.bak` 恢复路径。

影响：

- 进程退出、磁盘写入失败或设备异常可能留下截断 JSON。
- last-project、workspace draft 和 manifest 任一损坏都会影响恢复或项目打开。

建议：

- 使用“同目录临时文件 -> flush -> replace/move”的原子写入策略。
- workspace draft 和 manifest 至少保留一个最近成功版本。
- 所有生产文本读写显式使用 UTF-8，避免依赖运行时默认值。

## 已确认良好的交互

- Core/App/PSD Core 项目引用方向正确，Core 不依赖 Avalonia。
- LLM 转换器和 normalizer 会丢弃模型夹带的任务与绑定。
- `WorkbenchSession` 的局部命令、撤销/重做和拖拽 checkpoint 结构清晰。
- 资产解析对项目根目录逃逸有防护。
- generated TTS 资产会导入项目目录并改写为 manifest assetId。
- 导出统一走 `InteractionProject -> WorkbenchTimelineAssembler -> EditingTimeline -> exporter`。
- PSD source 与 rendered PNG 已在数据结构上分离。
- FCPXML、剪映草稿和 OTIO 内部路径有明确边界；剪映草稿当前按 `EditingTimeline` 八轨输出 `draft_content.json` / `draft_meta_info.json` / SRT。

## 建议的整改顺序

### P0：当前仍需保证数据不丢

1. 给项目写操作补统一 operation coordinator / cancellation 合同。
2. 所有项目 JSON 改为原子写并保留最近成功版本。
3. 做真实 ComfyUI 和真实剪辑软件端到端导入验证。

### P1：继续巩固长任务和视觉合同

1. 继续保持 TTS、PSD AI 和导出期间的工作台写锁与命令层漂移恢复。
2. 继续验证角色默认视觉、行级 PSD override 和导出预检在真实素材上的表现。
3. 给剪映草稿、FCPXML 和 Animate 的真实导入建立可重复验证记录。

### P2：降低长期维护成本

1. 将 `MainWindowViewModel` 拆成：
   - `ProjectCoordinator`
   - `WorkbenchCoordinator`
   - `BindingCoordinator`
   - `TtsCoordinator`
   - `ExportCoordinator`
2. 建立统一 `ProjectPersistenceCoordinator`。
3. 所有项目 JSON 改为原子写并保留备份。
4. 为跨模块合同建立专门的 integration test fixture，不再只验证单模块方法。

## 建议验收门槛

- 点击“保存”后，直接读取 `draft-agent-workspace.json` 能看到刚刚的工作台修改。
- TTS Pending 状态持续 30 秒时，轮询频率受控、UI 可取消；长事务期间的新编辑需要通过编辑锁定策略保护。
- 行级 PSD rendered PNG 不改变 `Character.AssetBinding.PortraitVisualBinding`。
- 行级 PSD 未渲染且角色有默认图时，预检仍定位到该行并提示修复。
- AI 分析响应晚于用户新编辑时，不会自动覆盖新 revision。
- 角色内心/角色动作行导出时有角色图或角色 PSD rendered PNG，并分别进入角色内心/角色动作音频轨。
- 项目 JSON 写入中断后，可从最近成功版本恢复。

## 文档同步计划（已执行）

| Source file | Classification | Unique facts | Target section | Action |
|---|---|---|---|---|
| `README.md` | main / L0 | 文档入口、最新验证 | README 文档入口与验证摘要 | 增加本报告入口并更新测试数 |
| `docs/architecture.md` | architecture / L1 | 当前工作流与模块合同 | 已知跨模块风险 | 增加风险摘要和报告链接 |
| `docs/module-review.md` | review evidence / L2 | 模块结论 | 受影响模块状态 | 从“通过”调整为“部分通过” |
| `docs/review-log.md` | review dashboard / L2 | 最新验证和当前结论 | 最新验证、模块摘要 | 记录本次专项审查 |

没有删除或合并任何项目文档；历史归档和第三方 PSD SDK README 保持不变。
