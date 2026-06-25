# Draft Agent Architecture

本文是 L1 架构与功能合同。只在需要理解项目工作流、模块职责或实施状态时读取；L0 入口见 [README.md](../README.md)。

## 当前方向

- 桌面端：Avalonia 12 + .NET 9 + MVVM。
- 领域层：`DraftAgent.Core`，负责文稿分析、工作台模型、项目资产、TTS、时间线投影、问题检查和导出。
- UI 层：`DraftAgent.App`，负责 Avalonia 窗口、ViewModel、绑定面板、本地试听和文件选择。
- 旧 Web/Tauri 原型不再作为主线；当前实现以 Avalonia 工程为准。
- 调试窗口、提示词调试入口、工作台 Debug 探针和性能基准入口已移除。

## 端到端工作流

```text
1. 文稿输入 / 解说优化
   -> 用户粘贴原文，或调用 AI 生成“改编1”文稿 tab
   -> 解说优化只保存 script drafts，不替换 Project，也不刷新工作台

2. AI 结构化分析
   -> LLM 输出 scene_interaction_flow_v1 JSON
   -> JSON 根对象为 scenes；每个 scene 只保留 description 和 items
   -> item 统一使用 type、role、text、emotion、intensity
   -> description 只写主要地点、环境和氛围；role 仅 dialogue/action/thought 写角色名
   -> 原始 type/role 只决定工作台属性，不直接决定最终配音
   -> 默认 dialogue/effects/action/thought/narration 都走默认配音；角色行参考音总开关开启后，只有已绑定有效角色参考音频的角色才启用角色参考音，dialogue 锁定角色参考音，thought/action 可单独切换角色参考音或默认配音
   -> 不输出镜头语言、图片、音频或 ComfyUI 任务

3. 项目归一化
   -> InteractionProjectNormalizer 生成 InteractionProject
   -> 补齐场景顺序、人物、旁白情绪、TTS 任务和绑定默认值

4. 工作台编辑
   -> Avalonia 工作台按“场景 + 顺序交互行”展示
   -> 用户编辑人物、旁白、台词、情绪、图片、声音状态
   -> WorkbenchDocument / WorkbenchSession 作为编辑期业务权威

5. 场景图片与角色管理
   -> 工作台场景行绑定普通场景图片
   -> 角色管理弹窗绑定角色普通图片或 PSD/PSB
   -> 角色管理弹窗先绑定默认配音参考音频和参考文本
   -> 默认隐藏角色参考音频/参考文本编辑，开启角色行参考音总开关后才显示并启用角色参考音绑定；台词按钮文案固定为“台词使用角色参考音”

6. PSD 图层选择
   -> 内置 PSD Loader 读取 source PSD
   -> 用户选择图层或分组
   -> 角色 PSD 可在“PSD图层模板设置”中用左侧 PSD 图层组 tree 绑定右侧语义 tree，不展示 PSD 文件名和叶子图层；任一角色有可读取 PSD 时按钮即启用，当前未选中 PSD 角色时回退到第一个 PSD 角色
   -> 表情上层与眉/眼/嘴互斥；动作支持动作整体、左手、右手语义槽位
   -> 工作台 PSD 批量匹配入口按同角色、同 PSD asset/path、同模板把这个角色的角色视觉行生成结构化 AI 请求，`dialogue/action/thought` 都参与，角色归属同时识别 `SubjectCharacterId/SubjectName` 与 `SourceRoleCharacterId/SourceRole`
   -> 当前 AI system prompt 每批按该 PSD 模板重新构造，先给固定分类约束和 JSON 返回形态，再追加固定词汇表；user prompt 只提交文本行或 rows JSON，不提交场景、类型、情绪、动作、layer id、坐标或 PSD 图层位置；AI 只返回眉/眼/嘴/左手/右手状态词汇
   -> AI 返回词汇后由本地当前 PSD 图层结构映射为 layer id；空值或非候选词优先使用该槽位默认可见 renderable 层，无法可靠解析当前 PSD 选中/默认层和受控层时跳过该槽位，避免清掉默认手或其它默认可见层；selection override 只控制眉/眼/嘴/左手/右手候选层，其余图层保持原显隐状态
   -> 角色默认绑定保存 PSD source / 默认预览
   -> 从工作台行发起的图层选择只把 rendered PNG 写回当前行 visual binding，不刷新同角色其他行或角色聚合缩略图

7. TTS / ComfyUI 语音生成
   -> 合成/继续队列/全量提交/单行重生成前必须已选择或保存工作台项目目录
   -> 按窗口队列提交 IndexTTS-2 / ComfyUI 语音任务，默认最多 2 个 prompt 在飞
   -> 轮询 /history 和 /queue
   -> 参考音频下拉、试听和生成都只走 ComfyUI HTTP 边界；`voice-preview` 只返回音频，不能用于下载 models 文件或读取参考文本 sidecar
   -> 用户手填参考文本会随 workflow `reference_text` 提交；若 `tts_audio_suite` 在 voices 目录中找到同名 `.reference.txt` / `.txt`，以插件内部优先级为准，App 不读取也不写入该目录文本
   -> 完成一个再追加一个；停止合成后停止追加，并删除本项目仍在 ComfyUI pending 队列中的 prompt
   -> 已提交 prompt 连续从 history/queue 消失时释放本地窗口占用并等待重新提交，避免队列永久卡住
   -> 完成一条就同步一条回工作台，可立即试听
   -> 合成中工作台结构/素材写操作全局锁定；已完成 TTS 行仅允许文本/情绪继续编辑，并按当前行数据插入重生成队列

8. 项目资产闭环
   -> 普通图片、PSD、参考音频、生成音频导入项目 assets
   -> 工作台保存 manifest assetId
   -> UI 预览、试听、导出统一通过 ProjectAssetResolver 解析

9. 导出预检
   -> 检查 PSD 未渲染、缺失视觉素材、缺失音频、不支持音频格式等问题
   -> FCPXML 允许视觉缺失并以 gap 导出；音频缺失或无法读取真实时长会阻断导出
   -> 弹窗只显示用户可读短问题，不暴露 asset id、task id、内部长 message

10. 时间线投影与导出
    -> WorkbenchTimelineAssembler 投影为 EditingTimeline
    -> EditingTimeline 统一管理八轨：字幕、角色图、角色台词配音、角色内心配音、角色动作配音、旁白叙述和环境场效图、旁白叙述和环境场效配音、场景图；长文本字幕按 20-30 字窗口优先标点切割
    -> exporter 只把 EditingTimeline 转换为 FCPXML / 剪映草稿 / Animate 项目结构
    -> OTIO 保留为内部中间层和自动化测试兼容路径
    -> FCPXML 固定 lane：1 字幕、2 角色图、3 角色台词配音、4 角色内心配音、5 角色动作配音、6 旁白叙述和环境场效图、7 旁白叙述和环境场效配音、8 场景图；caption duration 来自 subtitle clips
```

## 关键边界

- `InteractionProject` 是工作台真实数据源。
- `InteractionProject -> WorkbenchTimelineAssembler -> EditingTimeline -> exporter` 是唯一导出链路。
- `source asset` 不等于 `export media`：PSD/PSB 和 ComfyUI 临时音频引用不能直接进入剪辑软件导出。
- 角色管理的角色列表来自当前工作台仍引用的角色，并保留手动新增但暂未用于行的 `char_manual_*` 角色；保存工作台时同步裁剪非手动、已无工作台角色视觉行引用的旧 `Project.Characters`。工作台行归属来自角色视觉索引；该索引必须同时收录 `SubjectCharacterId/SubjectName` 与 `SourceRoleCharacterId/SourceRole`。`dialogue/action/thought` 都是角色视觉行，不能只用 `WorkbenchRowKind.Narration` 判断为旁白/场景行；`effects/narration` 不进入角色管理，行缩略图可绑定普通图片且不支持 PSD。
- 工作台拖动和语音合成互斥，避免行顺序、任务 targetId 和 UI 状态错位。
- 修改拖拽前必须遵守 [engineering-rules.md](engineering-rules.md) 的“工作台拖拽硬规则”。

## 当前已知跨模块风险

2026-06-23 复查确认：2026-06-21 专项审查中的保存到 workspace draft、角色默认视觉污染、行级 PSD 未渲染预检和重新分析 dirty/revision 覆盖问题已在代码与回归测试中关闭。当前仍需关注的交互风险为：

- App 已有外部操作 gate 和 AI revision 防护，TTS 合成期间也锁定普通工作台写操作；但打开项目、导出、TTS 实时发布等跨外部操作还没有统一 coordinator / cancellation 合同。
- 项目 JSON、workspace draft 和 manifest 仍主要是直接覆盖写，缺少原子提交和最近成功版本恢复。
- FCPXML / 剪映草稿仍需要真实剪辑软件和目标环境端到端导入验证。
- `VegasFcpxmlImporter` 是独立 VEGAS Pro Application Extension，源码维护在独立仓库 `https://github.com/dazhn/VegasFcpxmlImporter`：它只在 VEGAS 内部导入 FCPXML / 计划中的剪映草稿，不读取 Draft Agent 工作台运行时、项目 JSON 或 Avalonia UI 状态。
- `MainWindowViewModel` 仍是维护热点，后续应继续拆 feature coordinator / child view model。

这些问题不改变当前模块划分；整改优先级是统一外部操作协调、原子持久化和真实环境验证。

## 模块划分

| 模块 | 主要位置 | 职责边界 |
|---|---|---|
| Core 领域层 | `src/DraftAgent.Core` | 模型、文稿分析、归一化、项目资产、TTS/ComfyUI 语音、时间线、导出 |
| Avalonia App | `src/DraftAgent.App` | UI、ViewModel、窗口、绑定面板、本地播放、文件选择 |
| PSD Loader Core | `src/PsdLayerLoader.Core` | PSD 协议 DTO、图层模型、selection、preset/match 纯逻辑 |
| PSD Native Bridge | `src/PsdLayerLoader.Native` | 第三方 psd_sdk/native 读取能力，不承载 Draft Agent 业务 |
| App Tests | `tests/DraftAgent.App.Tests` | ViewModel、绑定、工作台、导出 UI 编排测试 |
| Core Tests | `tests/DraftAgent.Core.Tests` | 文稿、归一化、TTS、资产、时间线、导出测试 |
| PSD Tests | `tests/PsdLayerLoader.Core.Tests` | PSD 协议和图层选择纯逻辑测试 |
| VEGAS FCPXML Importer | 独立仓库 `https://github.com/dazhn/VegasFcpxmlImporter` | 单独的 VEGAS Pro 扩展；参考 `reuelk/pipeline` 解析 FCPXML，参考 `pyJianYingDraft` 基础解析剪映草稿，并在 VEGAS dock/UI 路径内创建文本、图片和音频事件；不再保留独立单元测试项目，发布验证走 Release 构建和真实 VEGAS 手动导入 |
| 文档 | `README.md`、`docs/*.md` | README 为 L0 入口；架构、硬规则、审查仪表盘和模块证据渐进式拆分；`docs/review-history/` 只存 L3 追溯归档 |
| 工具与样例资产 | `tools`、根目录样例文件 | 验证脚本、历史备份、真实素材样例；不等同于主应用交付面 |

## 接口落点

- 场景交互流模型：[InteractionModels.cs](../src/DraftAgent.Core/InteractionModels.cs)
- LLM / ComfyUI 接口：[IntegrationInterfaces.cs](../src/DraftAgent.Core/IntegrationInterfaces.cs)
- 真实 HTTP 集成：[HttpAiModelClient.cs](../src/DraftAgent.Core/HttpAiModelClient.cs)、[HttpLlmAnalysisClient.cs](../src/DraftAgent.Core/HttpLlmAnalysisClient.cs)、[ComfyUiHttpClient.cs](../src/DraftAgent.Core/ComfyUiHttpClient.cs)
- 工作台编排服务：[DraftWorkspaceService.cs](../src/DraftAgent.Core/DraftWorkspaceService.cs)
- Avalonia 主工作台：[MainWindowViewModel.cs](../src/DraftAgent.App/ViewModels/MainWindowViewModel.cs)
- 主工作台命令分组：[MainWindowViewModel.Ai.cs](../src/DraftAgent.App/ViewModels/MainWindowViewModel.Ai.cs)、[MainWindowViewModel.Tts.cs](../src/DraftAgent.App/ViewModels/MainWindowViewModel.Tts.cs)、[MainWindowViewModel.Project.cs](../src/DraftAgent.App/ViewModels/MainWindowViewModel.Project.cs)、[MainWindowViewModel.Binding.cs](../src/DraftAgent.App/ViewModels/MainWindowViewModel.Binding.cs)、[MainWindowViewModel.Workbench.cs](../src/DraftAgent.App/ViewModels/MainWindowViewModel.Workbench.cs)

## 模块功能、工作要求与实施状态

| 模块 | 功能 | 工作要求 | 当前状态 |
|---|---|---|---|
| 文稿输入 | 原文输入、清洗、段落识别、示例/清空 | 文稿区保持简单；示例/清空只作用原文 tab，不能覆盖当前改编稿 | 已实现 |
| 解说优化 | 调用 AI 改写当前文稿，生成 `改编1` tab | 输出为独立 tab；保存/恢复所有 `scriptDrafts`；只保存脚本草稿，不替换 `Project` 或刷新工作台；只有“分析”会用当前选中 tab 生成新工作台 | 已实现 |
| LLM 分析 | 把文稿转成 `scene_interaction_flow_v1` JSON | 只输出 JSON；根对象为 `scenes`；scene 只使用 `description/items`，description 只写主要地点、环境和氛围；item 统一使用 `type/role/text/emotion/intensity`；role 仅 `dialogue/action/thought` 写角色名，`effects/narration` 留空；不拆镜头；不生成图片/音频任务细节；角色名必须过滤动作、状语、物件、地点和代词碎片；解析得到的 `SourceItemType/SourceRole/SourceRoleCharacterId` 必须保留为原始属性，不被配音选择覆盖 | 已接入 DeepSeek / OpenAI / Gemini / LM Studio |
| 旁白情绪与配音路由 | 非台词行支持 `emotion` / `intensity`；配音选择和原始属性隔离 | 默认 `dialogue/effects/action/thought/narration` 均走默认配音；角色管理提供角色行参考音总开关，开启后台词 `dialogue` 锁定使用角色参考音，按钮显示“台词使用角色参考音”；角色绑定空时继续使用默认配音，只有结构化 `VoiceBinding` 有有效参考音频且路由开启时才启用角色参考音；内心 `thought` 和动作 `action` 才允许在角色参考音与默认配音之间切换；`effects/narration` 始终使用默认配音；关闭路由时必须回填当前默认配音，避免残留角色音 | 已实现 |
| 工作台模型 | 场景 + 顺序交互行 | `InteractionProject` 是主模型；编辑期以 `WorkbenchDocument` / `WorkbenchSession` 为权威 | 已实现 |
| 工作台编辑 | 编辑场景、人物、旁白、台词、情绪、图片、语音状态 | UI 投影不能反向污染 Core document；高频编辑走局部命令和可测试状态 | 已实现并多轮优化 |
| 拖拽交互 | 行拖动、场景拖动、preview/complete/cancel | 不用 pointer capture；不按像素距离计算；row drag 和 scene drag 严格分离 | 已稳定，硬契约单独保留 |
| 绑定面板 / 角色管理 | 场景、角色、默认配音分区绑定 | 紧凑对齐；场景只普通图；角色支持普通图和 PSD/PSB；默认配音绑定放在角色管理第一块；角色默认禁用参考音频和参考文本编辑，不能展示灰色默认音频；开启角色配音路由后角色参考音绑定才可编辑，空绑定表示继续使用默认配音；TTS 合成中禁用增加角色和角色配音路由按钮，合成完成或停止后恢复 | 已实现 |
| 视觉绑定 | 普通图片、PSD source、rendered PNG、selection | source 不等于 export media；导出只用 rendered PNG/JPG 等稳定媒体 | 已实现结构化 `VisualAssetBinding` |
| PSD Loader | 内置 PSD 图层选择、AI 结构化图层匹配和 PNG 渲染 | 轻量 PSD Core 是 `VisibleLayerIds` 唯一权威，核心操作只保留 `Load -> SetVisibleLayers -> RenderVisible/ExportVisiblePng -> BuildSelectionMetadata`；GUI 只做缩略图、breadcrumb、选中高亮和异步 Bitmap/预览缓存，所有显隐变更先由外层 mapper 算出新的 layer id 集合再提交 Core；Headless、AI selection override 和交互式确认共用同一 Core，Core 容错忽略不存在、group、不可渲染和重复 layer id，空 `SelectionHint` 和无效 id 进入 Core 后由 Core 过滤，最终无可导出像素时返回明确失败且不回退 intent/default；过滤透明/不可渲染层和 PSD section divider/0 尺寸层；只排除整层不透明纯黑/纯白占位层；联合边界防裁切；校验 source fingerprint；支持固定方形缩略图；任一角色有可读取 PSD 时 `PSD图层模板设置` 启用并指向当前 PSD 角色或首个 PSD 角色；角色 PSD 通过左右树显式保存整体表情、眉、眼、嘴、动作、左手、右手图层组候选；当前 AI 批量匹配暂不使用整体表情或整体动作，按同角色、同 PSD asset/path、同模板分批构造 system prompt，不发送 layer id/坐标/图层位置，user prompt 只提交文本行/rows；AI 词汇由本地当前 PSD 结构解析为 layer id，只有选中/默认层和受控层都能可靠解析的槽位才进入 selection override，`ControlledLayerIds` 优先于同名 `ControlledLayerNames`，空值或非法词使用可解析默认可见层，无法解析则跳过该槽位以保留默认手和其它既有显隐 | 已实现 |
| 声音绑定 | 参考音频、参考文本、试听、自定义音频提示 | 下拉只通过 ComfyUI `object_info/CharacterVoicesNode` 读取节点真实 `voice_name` key，过滤 `voices_examples/**`、`custom_nodes/**` 示例音频；ComfyUI 不可用时不扫描、不读取、不推断本机 voices 目录，只保留已有绑定/默认项并提示用户先连接 ComfyUI。选项、试听和 workflow `voice_name` 保持节点可接受的安全相对 key，可包含 `TTS/voices/...` 前缀，但不得是绝对路径、URL 或路径逃逸；`voice-preview` 只作为试听音频接口，不返回参考文本，改 `.txt` 或 `.reference.txt` URL 不属于当前 API 合同；自定义音频不再自动导入，只提示用户手动放入音频和同名 `.reference.txt` / `.txt` 到 `models/voices` 后选择；`CharacterVoicesNode` 在存在同名文本时以文件为准，插件内部优先级为 `.reference.txt` 高于 `.txt`；App 不直接读取 ComfyUI voices 目录下的 sidecar，也不锁定参考文本框，只在用户手填参考文本时随 workflow `reference_text` 提交，没有手填则传空并交给节点读取目录文本；App 不写 ComfyUI voices 目录，旧 `voiceSourceAssetId` / `voiceSyncedPath` 仅作历史字段读取，不参与本地兜底 | 已实现 `VoiceAssetBinding` |
| TTS 队列 | 提交、轮询、继续、重生成、同步结果 | 合成完成一条同步一条并立即刷新行级试听/重生成按钮；默认最多 2 个 ComfyUI prompt 在飞，完成一个再补一个；停止合成后不再补交并删除 pending prompt；已提交 prompt 连续从 history/queue 消失时释放窗口占用；合成/继续队列/单行重生成前必须已有工作台项目目录；TTS/PSD AI 匹配/导出长事务期间禁用角色、类型、拖拽、增删行/场景、素材绑定和 PSD 批量匹配；保存按钮提示等待或停止合成，不隐式取消；数据同步层拒绝或恢复非法 ViewModel 漂移；试听不禁用；TTS 运行中已完成行仅允许文本/情绪继续编辑，并按当前行文本/情绪插入当前队列；未完成、队列中、合成中的行仍锁定文本和情绪；顶部语音合成在部分完成时默认继续未完成/失败队列，只有全部完成才确认是否全量重生成；TTS 中行缩略图 PSD 单行重选保持可用，PSD AI 匹配或导出中锁住；旧全量 ComfyUI 提交链路已移除，统一耦合到窗口队列；IndexTTS workflow 默认关闭 `low_vram` | 已实现 |
| ComfyUI 语音资产导入 | 下载 `/view` 语音输出并导入项目 | 生成语音不能依赖 ComfyUI 缓存目录；项目内保存 assetId | TTS 已闭环；AI 出图已移除 |
| 项目管理 | 创建/打开项目、manifest、workspace JSON、启动恢复 | 项目可搬迁；manifest 路径按当前 root 重建；未打开项目只允许普通内存编辑，不允许进入语音合成链路 | 已实现 |
| 资产解析 | `assetId`、项目相对路径、显式绝对路径 | UI 预览、试听、导出必须共用 `ProjectAssetResolver` | 已实现 |
| 时间线投影 | 工作台转 `EditingTimeline` | 导出器不直接读取工作台模型；固定八轨为字幕、角色图、角色台词配音、角色内心配音、角色动作配音、旁白叙述和环境场效图、旁白叙述和环境场效配音、场景图；`dialogue/action/thought` 都按角色视觉行处理，角色图和 PSD 默认/行级 rendered PNG 解析同时识别 `SourceRoleCharacterId/SourceRole`，避免内心/动作缺角色图片或音频落入旁白轨；轨道顺序、行时长和长文本字幕切割由统一投影决定；长字幕按 20-30 字窗口优先在标点后切分，无合适标点时按 24 字目标切分；subtitle track 是 FCPXML caption、剪映 text segment、Animate text item 和 SRT 的唯一字幕来源 | 已实现 |
| FCPXML 导出 | 剪辑软件项目导出 | 按工作台顺序导出；spine 顶层使用一个全长 primary gap；固定 lane 1 字幕、2 角色图、3 角色台词配音、4 角色内心配音、5 角色动作配音、6 旁白叙述和环境场效图、7 旁白叙述和环境场效配音、8 场景图，全部作为 anchored items；音频必须存在且可读取真实时长；场景图持续场景内全部音频总时长；角色图和旁白/场效图逐行与当前音频对齐；PSD 行视觉必须使用 rendered PNG；caption 从 `EditingTimeline` subtitle clips 转换；resources 生成 timeline/image format、`fieldOrder="progressive"`、asset uid/duration/media 类型和 media-rep 绝对文件 URI；sequence 生成 format/duration/tc/audio 参数；时间值使用 FCP 帧对齐 rational time | 已实现 |
| 剪映草稿导出 | 生成剪映草稿目录、`draft_content.json`、`draft_meta_info.json` 和逐段 SRT | 参考 `pyJianYingDraft` 草稿结构生成本地素材、音频、字幕、speed 和 track/segment JSON；字幕 segment 从 `EditingTimeline` subtitle clips 转换；素材路径写入真实绝对路径，SRT 路径作为 Draft Agent 追踪字段；仍需在目标剪映版本做真实导入验证 | 已实现 |
| VEGAS FCPXML/剪映导入扩展 | 在 VEGAS Pro 内部导入外部项目描述 | `VegasFcpxmlImporter` 是独立 `net48` WinForms / ScriptPortal.Vegas 工具扩展；加载只读，应用导入时在 dock/UI 线程和一个 `UndoBlock` 内创建轨道、媒体、事件和 Titles & Text；FCPXML 与剪映 `draft_content.json` 基础解析/导入已实现，复杂剪映特效/转场/曲线变速/加密草稿仍不支持；解析、文件和宿主错误写入 UI 日志和本地 log | 基础版已实现 |
| OTIO 导出 | 内部中间层和测试路径 | 不作为普通用户主入口；保留 `EditingTimeline` 八轨顺序，subtitle track 输出为 Text kind | 已稳定 |
| 导出预检 | 检查缺失视觉素材、PSD 未渲染、缺失音频和不支持音频格式 | 弹窗显示短问题；FCPXML 缺图不阻断并以 gap 处理；缺音频、项目外 Comfy 缓存或无法读取真实时长会阻断导出 | 已实现 |
| 本地播放 | 音频试听 | Windows 用 NAudio；非 Windows 用系统默认程序打开音频目标 | 已实现 |
| 测试 | Core/App/PSD 单元测试 | Core parser/export 必测；ViewModel 状态可无窗口测试；拖拽/绑定/导出关键路径有回归测试 | 已覆盖，运行 `dotnet test` |
| 工具与样例资产 | ComfyUI/IndexTTS 辅助脚本、验证工具、根目录样例素材、历史镜像备份 | 主工程以 `DraftAgent.sln` 为准；未纳入 sln 的工具需单独验证；历史镜像备份不作为项目文档或 App 分发内容 | 部分工具可用；过期 benchmark 工具已移除 |

ComfyUI 边界硬约束：运行时所有 ComfyUI 交互都必须走配置端口/HTTP API；端口不可用时不得 fallback 到本机绝对路径或 ComfyUI 安装目录做任何扫描、读取、写入、复制、试听、导入或目录推断。

## 后续重点

- 长文小说分析：补 `chunk -> analyze -> merge -> normalize`。
- 剪映草稿：继续按目标剪映版本补齐真实导入验证；`pyJianYingDraft` 文档提示模板草稿在剪映 6+ 存在 `draft_content.json` 加密限制，本项目当前走直接生成 JSON 路径。
- FCPXML：继续用真实剪辑软件做最终导入验证，尤其验证 lane 1/2/3/4/5/6/7/8 在 FCP 中的显示层级是否符合预期。
- VEGAS FCPXML Importer：补真实 VEGAS 导入验证，并按真实剪映草稿继续扩展复杂特效、转场、曲线变速和加密草稿诊断；保持解析层输出中立导入计划，VEGAS API 只在写入层调用。
- 音频格式：若新增 MP3 / M4A / OGG 输出，需要补真实时长读取器。
- ViewModel：继续拆 feature coordinator / child view model。
- 工具清理：`tools/hf-mirror-backup/**` 当前按历史镜像备份处理，不进入项目文档和 App 内容；是否继续留仓仍属于仓库体积/资产策略决策。





