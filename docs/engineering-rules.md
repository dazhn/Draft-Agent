# Engineering Rules

本文是 L1/L2 硬规则集合。修改拖拽、绑定、PSD、资产、工具或样例资产时读取；项目入口见 [README.md](../README.md)。

## 文档渐进式加载规则

- `README.md` 只能作为 L0 入口，保留项目目标、主工作流、硬边界摘要、运行命令、最新验证状态和深层文档索引。
- 不把逐模块长表、完整测试流水、历史修复记录或详细审查日志写入 `README.md`。
- 当前审查仪表盘写入 [review-log.md](review-log.md)，保持短小，只记录最新验证、最近变更和追溯入口。
- 逐模块实现/测试证据写入 [module-review.md](module-review.md)。
- 完整历史流水写入 `docs/review-history/`；常规审查不默认读取历史归档。
- Vendor README、历史镜像备份和第三方文档不合并进项目主文档，除非用户明确要求。
- 每次代码修改仍必须同步文档，但优先同步到最小足够层级；只有 L0 摘要或全量验证状态变化时才更新 `README.md`。

## 工作台拖拽硬规则

拖拽实现不是像素拖动算法。工作台采用虚拟列表实时排序方案：preview 只计算数据目标，目标变化时调用 ViewModel preview 临时重排数据，row 和 scene 都直接切到新顺序。

成熟方案取舍：

- Avalonia 官方 `DragDrop.DoDragDropAsync` 适合控件间、应用间或系统级数据拖放；它会进入平台拖放流程并捕获指针，不适合作为工作台虚拟列表内部排序的主交互。
- SortableJS / dnd-kit / Framer Motion Reorder 的共同点是：拖动中目标变化会更新排序状态或 sibling transform，不来自鼠标像素 delta。
- 工作台内部排序使用自有目标解析和 ViewModel preview/complete 链路，预览视觉保留 `dropBefore`、`dropAfter`、`dropSceneEnd` 和 `dragSourceActive`。
- `dropBefore` / `dropAfter` / `dropSceneEnd` 的视觉提示必须由 drag overlay 绘制为独立单边插入线；不能把提示 class 直接画在 row / scene visual 上，避免列表重排或控件复用后往上拖时提示线丢失。
- 工作台 row drag 和 scene drag 都不做 layout animation，不捕获旧布局，不播放 FLIP，不给兄弟项写位移 class。
- 不使用第三方拖拽 service 接管目标解析，避免绕开当前 row/scene/TTS 互斥防呆和单次提交契约。

必须遵守：

- 不调用 `Pointer.Capture(...)`。
- 不用鼠标距离、像素 delta、timer tick 或逐帧 pointer movement 驱动 preview。
- 不在 window event handler 里直接用 `InputHitTest(pointerPosition)` 决定业务目标。
- `PointerMoved` 只能移动半透明 ghost，不能计算方向、位移、动画值或提交位置。
- 指针坐标只允许被 drag service 在 `PointerEntered` 不可靠时，用于低频扫描当前可见 realized visual。
- 方向来自数据 index，不来自像素。
- row drag 和 scene drag 是独立模式。
- row drag 不能移动 scene card；scene drag 不能移动 row。
- 拖拽源状态必须保存在 row / scene ViewModel 上，并由 XAML `Classes.dragSourceActive` 绑定生成样式；row 源透明隐藏但必须保留布局空间，scene 源透明隐藏但必须保留布局空间。
- row drop position 必须按当前 `OwnerScene.Rows` index 判断，不能按延迟重编号的 `Order` 字段判断。
- scene drop position 必须按当前 `SceneTables` index 判断，不能按延迟重编号的 `Order` 字段判断。
- preview 不能给非源控件添加位移 class，不能使用固定 `translateY` 避让值。
- layout animation 禁用；row drag 和 scene drag 都只做数据 preview 直跳；不能捕获或动画 `eventRow`、`referenceScene` 或 `eventTable`。
- drop 只在 `PointerReleased` commit 一次。
- `Escape` 必须取消并恢复 source、drop、ghost 状态。

目标解析：

- 快路径：`PointerEntered` on `eventRow`、`eventTable`、`referenceScene`。
- 兜底路径：节流后的 pointer-position target refresh，只扫描可见 realized visuals，并对相同目标去重。
- row reliability refresh 只能解析 realized `eventRow` 和显式 `eventTable`。
- 裸 `referenceScene` 不能成为 row target。
- `eventTable` scene-end target 必须来自显式 table visual，不能从 table child 向上提升。
- scene reliability refresh 只能解析 `referenceScene`，不能解析 row 或 table。

row drag：

- begin：记录 dragged row、source scene、source row index；显示 row ghost；不 capture mouse。
- preview：更新当前 row 或 scene-end drop indicator；pending target 变化时调用 `PreviewMoveInteractionRow*` 临时重排数据；不做 layout animation。
- complete：确认当前 preview checkpoint 一次；若没有成功 preview，才提交最后一个 pending row intent 一次；清理 ghost/source/drop classes。

scene drag：

- begin：记录 dragged scene 和 source scene index；显示 scene ghost；不 capture mouse。
- preview：更新当前 scene drop indicator；pending target 变化时调用 `PreviewMoveScene` 临时重排数据；不做 layout animation。
- complete：确认当前 preview checkpoint 一次；若没有成功 preview，才提交最后一个 pending scene intent 一次；清理 ghost/source/drop classes。

相关实现：

- `MainWindow.axaml.cs`
- `WorkbenchDragInteractionCoordinator`
- `WorkbenchDragTargetRefresh`
- `WorkbenchDropTargetResolver`
- `WorkbenchDragDisplacementState`

## 项目目录与资产规则

```text
ProjectRoot/
  draft-agent-project.json
  draft-agent-workspace.json
  assets/
    images/
      imported/
      psd/
    audio/
      voice-references/
      generated/
  timeline/
  exports/
  logs/
```

- 普通图片导入 `assets/images/imported`。
- PSD/PSB 导入 `assets/images/psd`。
- 参考音频导入 `assets/audio/voice-references`。
- TTS / 生成音频导入 `assets/audio/generated`。
- 项目模式下，场景图、人物图和音频绑定写入 manifest `assetId`。
- UI 预览、试听、导出必须共用 `ProjectAssetResolver`。
- 资产导入服务读取 manifest 后必须按当前 workspace root rebase，并使用 rebased folders 作为目标目录；项目搬迁后不能因调用方残留旧 `Folders` 而把新资产写回旧目录。
- manifest 中的项目资产 `AbsolutePath` 和 `ProjectRelativePath` 解析后都必须仍在项目根内；项目外文件只能在用户显式绑定绝对路径时被解析，不能通过 manifest assetId 混入导出链路。
- Core 资产导入服务必须校验扩展名：图片只接受 PNG/JPG/WEBP/BMP，PSD 只接受 PSD/PSB，参考音频只接受 WAV/FLAC/MP3/M4A/OGG。
- Core 资产导入服务必须先校验 `MediaAssetKind` / `MediaAssetSource` 组合：`ImportedPng` 只能配 Image，`ImportedPsd` 只能配 Psd，`VoiceReference` / `TtsGenerated` 只能配 Audio，避免错配 source 把文件写进错误资产目录。
- `TtsGenerated` 项目音频当前只接受 WAV/FLAC；未补真实时长读取器前，MP3/M4A/OGG 不能作为 completed TTS 项目资产导入。
- Comfy history / 本地试听可以识别 WAV/FLAC/MP3/M4A/OGG 音频输出，但生成 TTS 项目资产导入必须在下载前筛掉非 WAV/FLAC 输出，不能先下载再依赖资产服务失败。
- completed TTS 只读取 `GenerationTask.ResultAssetId`；若它是缺失的 `asset_*` 引用，或 manifest 记录存在但文件丢失，导入服务不做旧字段回退，用户必须重新同步或重新生成该语音。
- 所有 ComfyUI 交互必须通过配置的 HTTP 端口/API 完成，包括连接检测、`object_info` 目录读取、prompt 提交/轮询/删除、`/view` 输出下载和参考音频试听；ComfyUI 不可用或 HTTP 调用失败时只能向用户报错/提示连接，严禁绕过端口 fallback 到本机 ComfyUI 绝对路径、安装目录、`models/**`、`output/**` 或 `temp/**` 做扫描、读取、写入、复制、试听、导入或目录推断。
- ComfyUI `/view` 输出引用只接受 `output` / `temp` 类型；filename 必须是单个文件名，subfolder 必须是不能逃逸的相对 ComfyUI 输出目录。
- 参考音频下拉只能通过 ComfyUI HTTP 边界读取 `object_info/CharacterVoicesNode` 暴露的 `voice_name` 下拉 key，并过滤 `voices_examples/**`、`custom_nodes/**` 等插件示例音频；ComfyUI 不可用时不得扫描、读取或推断本机 voices 目录，只保留已有绑定/默认项并提示用户先连接 ComfyUI。UI 选项、试听和 workflow `voice_name` 必须保持 `CharacterVoicesNode` 可接受的安全相对 key，可包含 `TTS/voices/...` 等节点下拉前缀，但不得是绝对路径、URL 或路径逃逸。Draft Agent 不得向 ComfyUI `models/voices` / `models/TTS/voices` 写入、复制、缓存或生成参考音频/同名 `.txt`，不得用内部 `assetId`、row/target id 或旧 `voiceSyncedPath` 重建 voices 文件。自定义音频入口保留但取消自动导入；点击只提示用户手动把音频和同名 `.reference.txt` 或 `.txt` 放到 ComfyUI `models/voices` 后再从下拉选择。项目内 `VoiceReference` 资产只作为历史/显式项目资产读取，不得被自动同步成 voices 副本，也不得被加入参考音频下拉。
- `CharacterVoicesNode` 源码确认：`reference_text` 输入存在；但当前 App 通过 `voice_name` 下拉选择声音时，节点只从固定 voices 目录读取音频，并调用 `load_voice_reference()`，若音频旁边存在同名 `.reference.txt` / `.txt`，节点会优先使用文件文本，其中 `.reference.txt` 优先于 `.txt`。因此 Draft Agent 不直接读取 ComfyUI voices 目录下的 sidecar，也不锁定参考文本框；用户手填参考文本时随 workflow JSON 的 `reference_text` 提交，没有手填则传空并交给节点读取目录文本。`/api/tts-audio-suite/voice-preview` 只用于返回试听音频，不能通过把音频后缀改成 `.txt` 或 `.reference.txt` 获取参考文本，也不能用于下载 ComfyUI models 文件。参考文本“保存”不得写 ComfyUI voices，只提示用户要持久保存请在音频目录创建同名 `.reference.txt` 或 `.txt` 后刷新/重选。
- 语音合成、继续 TTS 队列、角色/单行重生成前必须已有工作台项目目录；UI 主按钮应先弹出项目文件夹选择器，命令层也必须硬阻断无项目目录的调用。
- 工作台语音合成必须采用窗口队列，默认最多同时向 ComfyUI 保留 2 个本项目 prompt；完成或失败一个才允许追加下一个，禁止一次性把所有语音行塞进 ComfyUI 队列。已提交 prompt 若连续从 ComfyUI history/queue 查询不到，必须释放本地窗口占用并等待重新提交，不能永久占着“队列中 2”。
- 停止语音合成必须同时停止本地继续提交/轮询，并对本项目已知的 ComfyUI pending prompt 调用 `/queue` delete；正在运行的 prompt 不应由普通停止按钮强制 interrupt，避免误伤 ComfyUI 当前执行流。
- TTS 合成、PSD 图层 AI 匹配和导出运行中必须禁用工作台结构/素材写操作，包括修改角色、修改行类型、拖拽行/场景、增删行/场景、素材绑定、PSD 图层匹配和参考音频/文本绑定；按钮禁用之外，命令和同步层也必须拒绝或恢复非法 ViewModel 漂移，不能把未允许的运行中改动写入 `WorkbenchDocument` 或 `Project`。这些长事务启动前必须先提交当前合法工作台状态。试听是只读操作，不得因合成中被禁用；TTS 运行中已完成合成的行仅允许继续编辑台词和情绪，点击重新生成必须按当前行文本/情绪加入当前重生成队列，未完成、队列中、合成中的行仍锁定台词和情绪。行内试听/重生成按钮必须在音频导入后立即刷新。TTS 运行中行缩略图触发的 PSD 单行重选属于图片操作，仍保持可用；PSD AI 匹配或导出运行中也必须锁住该入口。顶部语音合成在部分完成时继续剩余队列，不弹全量重生成确认；只有全部完成时才确认是否重新生成已完成语音。
- IndexTTS-2 workflow patch 默认必须关闭 `low_vram`；只有任务参数显式设置 `lowVram=true` 时才启用低显存模式，避免普通合成反复加载/卸载分析模型导致队列吞吐下降。
- `voiceSourceAssetId`、`voiceSyncedPath` 仅作为旧项目读取/保留字段；当前 App 不得自动新增或更新这两个字段来表示 voices 同步，也不得因旧 `voiceSyncedPath` 缺失而写文件。
- 参考音频目录读取必须只走 ComfyUI HTTP 边界的 `object_info/CharacterVoicesNode`，并只接受节点下拉返回的安全相对 `voice_name` key；必须过滤插件目录示例音频（例如 `voices_examples/**`、`custom_nodes/**`），不得保留 `models/vocal` 兼容路径。`localhost:8000` 只是 ComfyUI HTTP 边界，Draft Agent 对 ComfyUI voices 目录的合同是：不扫描、不读取 sidecar、不写入、不用绝对路径兜底。
- 远端试听缓存必须使用安全原始文件名和稳定缓存 key；同一个 URL 不得每试听一次生成一个新的 GUID 文件。
- 语音合成、继续队列、单行重生成在提交到 ComfyUI 前，必须先提交/同步当前工作台文档，并以这一刻的工作台 `WorkbenchDocument` / TTS task 参数作为唯一基准做参考音频、参考文本和 ComfyUI 预检。角色管理只是写入工作台/项目的编辑入口，合成阶段不得再从角色管理 UI 状态重新推导配音归属。

## 剪辑导出硬规则

- `InteractionProject -> WorkbenchTimelineAssembler -> EditingTimeline -> exporter` 是唯一导出链路；导出器不得直接读取工作台 UI 状态或原始 LLM JSON。
- 导出顺序必须来自当前工作台场景顺序和行顺序，而不是原始 JSON 顺序或角色管理 UI 顺序。
- 轨道和字幕切割都必须由 `EditingTimeline` 管理：`WorkbenchTimelineAssembler` 固定生成字幕、角色图、角色台词配音、角色内心配音、角色动作配音、旁白叙述和环境场效图、旁白叙述和环境场效配音、场景图八轨；导出器只把这些 clips 转换为目标项目格式，不得在 FCPXML、剪映草稿或 Animate exporter 内重新决定轨道语义或长文本拆分/截断策略。
- 场景图、角色图和字幕时长必须以音频为基准：场景视觉持续该场景内所有行音频时长总和；角色视觉每行单独一段，offset / duration 与当前行音频一致；字幕 offset / duration 与对应音频一致。
- FCPXML 音频是硬要求：每条有文本的导出行必须有已完成、项目内、可解析真实时长的 WAV/FLAC 音频；缺音频、只指向 ComfyUI 缓存、项目外 manifest 伪造音频或无法读取真实时长都必须阻断导出。
- FCPXML 图片是软要求：普通 PNG/JPG/JPEG/WEBP/BMP、PSD rendered PNG/JPG 等可导出视觉应进入资源；缺图、PSD 未渲染或 manifest 伪造到项目外图片时，FCPXML 以 gap 输出，不得引用不可导出的 source。
- PSD/PSB source 永远不是剪辑软件 export media。角色默认 PSD 可以有默认 rendered PNG；行级切换显隐图层后会生成自己的 rendered PNG/JPG，并在该行优先导出。没有行级切换时使用角色默认 PNG 即可。导出阶段只消费 `EditingTimeline` 中已经投影好的图片 clip，不重新解释 PSD 图层。
- FCPXML 固定 lane 合同：字幕 `lane="1"`，角色图 `lane="2"`，角色台词配音 `lane="3"`，角色内心配音 `lane="4"`，角色动作配音 `lane="5"`，旁白叙述和环境场效图 `lane="6"`，旁白叙述和环境场效配音 `lane="7"`，场景图 `lane="8"`。`audioRole` 只表达逻辑角色（如 `dialogue` / `voiceover`），不能替代 lane 顺序。
- FCPXML spine 必须使用保守结构：`spine` 顶层只放一个全长 primary gap，字幕、角色图、场景图和音频都作为该 primary gap 的 anchored items 写入并携带固定 lane；不要把带 lane 的 `asset-clip` / `caption` 直接放在 `spine` 顶层。
- FCPXML 固定八轨之外还必须生成可导入资源结构：`resources` 必须包含 timeline `format`（`frameDuration="1001/24000s"`、`fieldOrder="progressive"`、宽高、Rec.709）和图片 `format`（无 `frameDuration`，但必须有 `fieldOrder="progressive"`）；每个 `asset` 必须有稳定 `uid`、`start`、`duration`、媒体类型字段，并通过子节点 `<media-rep kind="original-media" src="file://...">` 引用绝对文件 URI，禁止把 `src` 直接写在 `asset` 上。
- FCPXML `sequence` 必须写 `format`、总 `duration`、`tcStart="0s"`、`tcFormat="NDF"`、`audioLayout="stereo"`、`audioRate="48k"`；所有 clip/caption/gap 的 offset/duration 必须输出为帧对齐 FCP rational time（`0s` 或 `n/24000s` 且 n 为 1001 的倍数），不能回退到小数秒字符串。
- 字幕文本在 `EditingTimeline` subtitle track 中统一切割：20-30 字窗口内优先在中文/英文标点后切分；没有合适标点时按 24 字目标切分。FCPXML caption、剪映 text segment、Animate text item 和 SRT 都必须消费同一批 subtitle clips。
- 剪映草稿、FCPXML 和 Animate 可保留各自项目文件结构，但不得拥有各自独立的字幕切割规则。

## 工作台配音路由硬规则

- 原始 JSON 属性和工作台配音选择必须隔离。`SourceItemType`、`SourceRole`、`SourceRoleCharacterId` 记录模型返回的原始类型/角色归属，不能因为用户选择旁白或角色参考音而被覆盖。
- 默认情况下，工作台所有可合成语音行都走默认配音，包括 `dialogue`、`action`、`thought`、`effects` 和 `narration`。
- 角色管理中的默认配音绑定必须放在第一块，作为默认配音总线；角色卡默认禁用参考音频、试听、参考文本和保存参考文本控件，不能把内部默认配音展示成每个角色的灰色声音绑定。
- 角色管理提供一个角色行参考音主开关。主开关关闭时，台词/内心/动作三个细分按钮必须隐藏且不可用；角色参考音编辑不能写入普通台词行。
- 主开关开启后，`dialogue` 台词必须锁定使用角色参考音，不再提供“台词使用默认配音”路径；台词按钮文案固定为“台词使用角色参考音”，可以显示为只读/禁用状态。
- 角色参考音绑定只有两个状态：空绑定表示该角色继续使用默认配音；选择了有效参考音频且对应角色路由开启时，才启用角色参考音配音。旧 `AssetBinding.Voice` 字段不得绕过结构化 `VoiceBinding` 触发角色配音。
- 只有 `thought` 内心和 `action` 动作允许在角色参考音与默认配音之间切换。`effects` 和 `narration` 始终使用默认配音，不允许路由到角色参考音。
- 关闭某类角色配音路由时，必须清空该类行的 `VoiceRole` / `VoiceRoleCharacterId`，并用当前默认配音绑定回填该行 `VoiceBinding`，避免 UI 显示默认配音但实际残留角色音。
- 角色参考音绑定只更新已路由到角色音的行；角色图片/PSD 默认视觉仍按角色原始行管理，不能因为声音路由关闭而失效。
- 默认配音目标表示“当前所有走默认配音的行”，不等同于仅 `WorkbenchRowKind.Narration`。删除最后一条 narration 行时，如果仍有普通台词走默认配音，不得移除默认配音目标。
- 新 scene-flow JSON 的 `dialogue` 行优先用 `SourceRole` 解析角色；旧/手动 dialogue 行缺少 `SourceRole` 时，角色配音路由可回退使用 `SubjectCharacterId` / `SubjectName`，但不能补写或伪造原始 JSON 字段。
- 角色管理完成设定后，用户继续编辑工作台文本、参考文本、行级配音或角色归属时，后续语音合成必须以工作台最新行状态为准。角色管理中“内心/动作/角色参考音”按钮只决定之前写入工作台的路由，不得在合成阶段覆盖工作台后续编辑。

## PSD Loader 规则

- source PSD：用于后续重新选层，不进入剪辑导出。
- rendered PNG：用于工作台预览和剪辑导出。
- selection：记录当前图层选择。
- fingerprint：检测 source PSD 是否变化。
- PSD 加载控制核心必须保持轻量：`Load -> SetVisibleLayers -> RenderVisible/ExportVisiblePng -> BuildSelectionMetadata` 是唯一核心操作集合。
- `VisibleLayerIds` 必须由 PSD Core 作为唯一权威维护；GUI 缩略图 `IsVisible`、`IsSelected` 只是从 Core 状态投影出来的显示状态，不得作为渲染或导出的权威状态。
- GUI 可以维护异步体验缓存，例如缩略图 Bitmap、最近预览 Bitmap、当前分组预热队列；这些缓存只能依赖 PSD Core 的图层像素读取和当前 visible key，不得绕过 Core 修改 PSD 显隐状态。
- PSD Core 必须缓存图层 RGBA 像素，缓存 key 至少包含 source fingerprint 和 layer id；GUI/headless/导出共享同一套像素缓存，避免重复 native 读取。
- 所有外部操作（GUI 单选/多选/眼睛按钮/隐藏组、AI selection override、selection hint、headless 请求）都必须先映射为新的 visible layer id 集合，再调用 PSD Core 的 `SetVisibleLayers`。`Show`、`Hide`、`Toggle`、`HideGroup`、`ReplaceControlledLayers` 等只能作为外层集合运算 helper，不能成为并列核心写入口。
- PSD Core 的 `SetVisibleLayers` 必须容错过滤不存在的 layer id、group layer id、不可渲染 layer id 和重复 layer id；过滤后没有可渲染图层时，预览可以返回透明画布或空预览，导出/headless 必须返回明确失败，不得抛出未处理异常。
- 默认预览、缩略图、交互式导出和 headless 导出共用过滤规则；只排除整张图层所有有效像素都完全不透明且全为纯黑/纯白的图层，透明背景上的纯黑/纯白内容、半透明层或局部图形不能被当作占位底色过滤。
- 导出边界使用所有有效图层的非透明像素联合矩形，避免切换图层后裁切变化。
- PSD Loader 图层缩略图使用固定方形预览框；名称显示在方框外下方，只显示图层名，不显示分辨率或坐标。
- 角色素材区 `PSD图层模板设置` 的启用条件是任一角色目标存在可读取 PSD source；当前选中角色不是 PSD 时，入口必须回退到第一个可配置 PSD 角色，不能因为当前卡片仍显示 PNG 而禁用。
- 工作台 `PSD图层AI匹配` 必须以角色 PSD 图层模板为前提，按同角色、同 PSD assetId/path、同模板分组，每批最多 12 行；每批 system prompt 必须重新按该分组模板构造，固定词汇表放在约束和 JSON 返回形态之后；user prompt 只能提交当前批次行的 `interactionId/text`，不得包含固定词汇表、角色、场景、类型、情绪、动作说明、layer id、坐标或 PSD 图层位置。
- AI 只返回候选内的 `brow/eye/mouth/leftHand/rightHand` 结构化词汇或空字符串；词汇到 layer id 的映射必须在本地读取当前 PSD 图层结构后完成。当前 PSD 可读时，只有该槽位的选中层/默认层和受控层都能可靠解析出当前 PSD 的 renderable layer id，才允许把该槽位加入 selection override；解析不到的槽位必须跳过，不能凭旧模板候选名或同名叶子层清掉默认手、默认可见层或其它角色 PSD 的图层。
- AI 返回空字符串或不在候选内的词时，该槽位优先使用对应 PSD 组的默认可见 renderable 层；无法读取默认层时才退回该组第一个可解析候选。非法候选不得抛出未处理异常，也不得让受控集合只删不补。
- AI selection override 只能控制眉、眼、嘴、左手、右手五类候选层；整体表情、整体动作和其它非受控图层的显隐必须保持 PSD 默认状态或当前请求既有状态，不能被批量 AI 匹配清空。有精确 `ControlledLayerIds` 时解析层必须优先使用 id，不得再用重复 `ControlledLayerNames` 扩大控制范围。
- 空 `SelectionHint`、无效 layer id、group id、不可渲染 id 必须允许进入 headless/Core 边界，由 PSD Core 过滤；过滤后没有可导出图层时返回明确失败，不得回退到 intent/default 渲染，也不得抛出未处理异常。
- 自动匹配不能复用上一轮手动 `SelectionHint`；手动 PSD Loader 重选仍保留上一轮选择作为交互式提示。

## 工作台优化结论

- large 工作台证据支持 viewport-bounded，没有按总行数膨胀。
- 工作台已切换为单一平铺虚拟列表：`WorkbenchDisplayItems` 将场景头、台词行和空场景 drop target 投影为同一个外层 `workbenchSceneList`，不再嵌套 `workbenchRowList`，也不再通过窗口级滚轮转发修正内外层滚动冲突。
- flat list 仍必须保留拖拽硬规则：row target 用 realized `eventRow`，空场景和场景末尾 drop target 用显式 `eventTable`，scene target 用 `referenceScene`。
- 后续优化继续走小切片：command result、affected ranges、binding target change keys、preview asset refresh intent。

## 工具与样例资产

- 主工程边界是 `DraftAgent.sln`：`DraftAgent.Core`、`DraftAgent.App`、`PsdLayerLoader.Core` 及其测试项目。
- `tools/` 包含 ComfyUI / IndexTTS 辅助脚本、验证工具和历史镜像备份；其中 `tools/hf-mirror-backup/**` 是模型来源/引擎备份材料，不作为主项目文档合并来源，也不能作为 App 内容分发。
- 根目录 `IndexTTS-2.json` 会作为 App 内容复制到输出目录；历史 `anima.json` 不再作为 App 内容分发，根目录 PSD、FLAC、TXT、参考 UI 图片是样例/验证素材。
- 未纳入 solution 的工具不由 `dotnet build .\DraftAgent.sln` 覆盖，修改工具时必须单独构建或运行对应脚本。

## Avalonia MCP

- 项目级配置位于 `.vscode/mcp.json`。
- Codex 全局 MCP 示例：`codex mcp add avalonia --url https://api.avaloniaui.net/mcp`。
- 若配置变更，优先使用项目内 `.codex/skills/avalonia-app`；Avalonia DevTools MCP 需要对应许可，不能作为必备验证路径。


