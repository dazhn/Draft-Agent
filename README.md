# Draft Agent

视频草稿生成 Agent 的 Avalonia 桌面工作台。它把小说、短视频文稿或解说稿转换成可编辑、可绑定素材、可合成语音、可导出到剪辑软件的结构化项目。

当前主模型是 `InteractionProject`：不是旧 shot list，也不是 OTIO。

## 文档入口

README 是 L0 入口，只保留项目目标、文档索引、硬边界摘要、运行命令和最新验证状态。深入内容按需加载：

| 文档 | 内容 | 何时读取 |
|---|---|---|
| [docs/architecture.md](docs/architecture.md) | 当前方向、端到端工作流、模块划分、接口落点、功能要求和实施状态 | 理解项目结构、模块职责、功能合同 |
| [docs/engineering-rules.md](docs/engineering-rules.md) | 工作台拖拽硬规则、项目资产规则、PSD Loader 规则、工具/样例资产规则 | 修改拖拽、绑定、PSD、资产、工具时 |
| [docs/review-log.md](docs/review-log.md) | 最新验证、最近变更、模块审查入口、历史归档入口 | 做代码审查、同步修改记录、确认测试证据 |
| [docs/module-review.md](docs/module-review.md) | 逐模块审查结论、测试证据和风险表 | 需要按模块核对实现是否满足文档要求时 |
| [docs/workflow-module-interaction-review-2026-06-21.md](docs/workflow-module-interaction-review-2026-06-21.md) | 工作流、模块边界、跨模块数据权威和交互缺陷专项审查 | 处理保存、并发、TTS、PSD 默认值或导出优先级时 |
| [docs/vegas-pro-api-and-call-flow.md](docs/vegas-pro-api-and-call-flow.md) | VEGAS Pro 扩展 API、调用流、宿主限制和 `VegasFcpxmlImporter` 独立边界 | 修改独立 VEGAS 扩展仓库、FCPXML/剪映草稿导入或 ScriptPortal.Vegas 写入时 |
| [docs/adobe-animate-export-notes.md](docs/adobe-animate-export-notes.md) | Adobe Animate 音频字幕导入的固定参数、JSFL 顺序、持续时间坑和真实验证状态 | 修改 Animate 音频字幕导出、JSFL 导入脚本、轨道/帧持续时间时 |

`docs/review-history/` 是 L3 历史归档，只在追溯具体修复流水或测试筛选器时读取，不属于默认加载路径。

第三方文档 [src/PsdLayerLoader.Native/third_party/psd_sdk/README.md](src/PsdLayerLoader.Native/third_party/psd_sdk/README.md) 是 vendor 文档，不合并进项目主文档。

## 主工作流

```text
文稿/解说优化 -> LLM scene flow JSON -> InteractionProject -> 工作台编辑/素材绑定
-> PSD rendered PNG / IndexTTS-2 TTS -> 项目资产闭环 -> 导出预检 -> FCPXML / 剪映草稿
```

## 硬边界摘要

- LLM 分析只接收/输出 scene flow JSON；当前 AI JSON 形态为 `scenes[].description` + `scenes[].items[]`，item 统一使用 `type/role/text/emotion/intensity`。原始 JSON 类型/角色字段必须和工作台配音选择隔离：默认所有行走默认配音；角色管理启用角色行参考音主开关后，台词按钮固定显示“台词使用角色参考音”并锁定角色参考音，内心/动作才允许在角色参考音和默认配音之间切换；`effects/narration` 始终走默认配音；不得信任模型夹带的 `generationTasks`、素材绑定、图片、音频、ComfyUI 或镜头字段。
- AI 出图已移除；场景只保存普通 rendered 图片；角色支持普通图片或 PSD/PSB。
- PSD 批量切层以角色 PSD 图层匹配设置为前提：只要任一角色绑定了可读取 PSD，角色素材区的 `PSD图层模板设置` 就可进入该角色模板；角色 PSD 默认显示图层保存为默认 rendered PNG；工作台“PSD图层AI匹配”按同角色、同 PSD asset/path、同模板分组，每批重新构造 system prompt，只发送 `brow/eye/mouth/leftHand/rightHand` 固定候选词和行 `interactionId/text`，不发送 layer id、坐标或 PSD 图层位置；AI 返回词汇后由本地当前 PSD 图层结构映射为 layer id，只有能可靠解析当前 PSD 的槽位才加入 selection override，空值、非法词或无法解析的槽位不得清掉默认手/默认可见层；没做行级切层时通常继续使用角色默认 PNG。
- `InteractionProject -> WorkbenchTimelineAssembler -> EditingTimeline -> exporter` 是唯一导出链路。
- `EditingTimeline` 是剪辑导出的权威轨道结构：字幕、角色图、角色台词配音、角色内心配音、角色动作配音、旁白叙述和环境场效图、旁白叙述和环境场效配音、场景图八轨固定排列；长文本字幕切割在时间线投影阶段完成，按 20-30 字窗口优先在标点后切分，无合适标点时按 24 字目标切分。
- `VegasFcpxmlImporter` 是单独的 VEGAS Pro Application Extension，源码维护在独立仓库 `https://github.com/dazhn/VegasFcpxmlImporter`，用于在 VEGAS 内部导入 FCPXML / 计划中的剪映草稿；不要把它和 Draft Agent Avalonia 工作台运行时混在一起。
- `source asset` 不等于 `export media`：PSD/PSB 和 ComfyUI 临时音频引用不能直接进入剪辑软件导出。
- 与 ComfyUI 交互必须只走配置端口/HTTP API；ComfyUI 不可用时只能报错或提示连接，不能绕过端口 fallback 到本机绝对路径、安装目录、`models/**`、`output/**` 或 `temp/**` 做扫描、读取、写入、复制、试听、导入或目录推断。`tts_audio_suite` 的 `voice-preview` 接口只作为参考音频试听音频流使用，Draft Agent 不通过它下载 models 文件或读取 `.txt` / `.reference.txt` 参考文本。
- FCPXML 导出以工作台当前顺序为准：音频必须存在且可读取真实时长，图片允许缺失并以 gap 输出；场景图持续整个场景音频总时长，角色图逐行跟随对应音频；PSD source 不进入导出，导出只消费时间线中每行已经生成好的 PNG。
- 语音合成、继续队列和单行重生成前必须先选择/保存工作台项目目录，生成音频必须同步到项目 assets，不能只依赖内存项目或 ComfyUI 缓存。
- 角色管理卡片以当前工作台仍引用的角色为来源，并保留“增加角色”手动创建但暂未用于行的角色；保存工作台时会裁剪非手动、已无工作台角色视觉行引用的旧 `Project.Characters`，避免工作台删行后角色管理和角色下拉残留旧角色。工作台角色行归属必须同时识别 `SubjectCharacterId/SubjectName` 和 `SourceRoleCharacterId/SourceRole`。`dialogue/action/thought` 都属于角色视觉行，即使内心/动作在 TTS 任务上仍是旁白语义，也要同步到对应角色卡、角色素材绑定和 PSD 行处理；`effects/narration` 不进入角色管理，行缩略图可绑定普通图片且不支持 PSD。
- 工作台“重置”必须读取项目目录中的原始 AI JSON `draft-agent-original-ai.json`；保存工作台只更新当前草稿 `draft-agent-workspace.json`，不能刷新或污染重置基线。旧项目首次打开若没有原始 AI JSON 文件，会用当时的项目草稿建立一次兼容基线。
- 工作台编辑和长事务互斥；TTS 合成、PSD 图层 AI 匹配和导出运行中禁用角色、类型、拖拽、增删行/场景、素材绑定和 PSD 批量匹配等写操作，数据层也必须拒绝或恢复非法 ViewModel 漂移，不能同步进 `WorkbenchDocument` 或 `Project`。长事务启动前先提交当前合法工作台状态；TTS 保存按钮只提示等待或停止合成，不会隐式取消合成。试听不禁用；TTS 运行中已完成合成的行仅允许继续编辑文本和情绪，并可按当前行文本/情绪插入当前 TTS 重生成队列；未完成、队列中、合成中的行仍锁定文本和情绪。语音完成后工作台行语音格以紧凑 `听 / 重 / 02s` 显示试听、重生成和两位秒数，秒数读取复用导出侧 WAV/FLAC 真实时长逻辑。顶部语音合成在部分完成时默认继续未完成/失败队列，不弹全量重生成确认；只有全部完成时才询问是否重新生成已完成语音。TTS 运行中行缩略图触发的 PSD 单行重选属于图片操作，仍保持可用，但 PSD AI 匹配或导出运行中也会锁住该入口。ComfyUI prompt 若连续从 history/queue 消失，必须释放本地窗口队列占用，避免“队列中 2”卡死。
- 修改拖拽前必须读取 [docs/engineering-rules.md](docs/engineering-rules.md) 的“工作台拖拽硬规则”。

## 本地运行

```powershell
dotnet run --project .\src\DraftAgent.App\DraftAgent.App.csproj
```

## 构建与测试

```powershell
dotnet build .\DraftAgent.sln
dotnet test .\DraftAgent.sln
```

最新验证见 [docs/review-log.md](docs/review-log.md)。当前本轮验证：`dotnet build .\DraftAgent.sln` 0 警告 0 错误；`dotnet test .\DraftAgent.sln` 通过，PSD Loader 30/30、Core 349/349、App 690/690。绿测之外仍有项目 JSON 原子写、统一 operation coordinator / cancellation 合同、真实 ComfyUI 端到端和真实剪辑软件导入验证缺口，详见专项审查。

`DraftAgent.sln` 不覆盖所有 `tools/**` 项目；工具目录修改后需单独构建对应 `.csproj` 或运行对应脚本。

`VegasFcpxmlImporter` 是独立 VEGAS Pro 扩展项目，不属于本仓库同步范围。修改它时请在独立仓库中构建和发布；该扩展不再保留单元测试项目，发布验证以 Release 构建、安装到 VEGAS Application Extensions 目录和真实 VEGAS 手动导入验证为准。

## 后续重点

- 先补项目 JSON 原子写、统一 operation coordinator / cancellation 合同和真实 FCP 导入验证。
- 再继续拆分 `MainWindowViewModel` 中的 feature coordinator / child view model。
- 长文分块分析、原生剪映工程和更多音频格式属于后续增强。

