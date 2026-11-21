# Adobe Animate 导出踩坑笔记

本文是 Draft Agent 导出到 Adobe Animate 的 L2 操作笔记。目标是把音频、字幕、角色图、场景图按时间线导入 Animate 继续编辑；不是转换成 FLA/XFL，也不是生成不可编辑的视频。

## 当前目标

- 每次导入都新建 Animate 文档，不复用当前打开项目。
- Animate 舞台固定为 `1920 x 1080`，帧率固定为 `30 fps`。
- 时间线固定 4 条轨道，UI 从上到下为：`01 文本`、`02 角色`、`03 场景`、`04 音频`。
- 按 `EditingTimeline` 中的开始秒数和持续秒数计算帧区间，再把文本、图片、音频放到对应轨道的对应帧。
- 导出包必须保留可编辑素材：文本是 Animate 文本对象，图片是导入的位图/素材，音频是帧上的 sound。

## 导出包

`AnimateTimelineExporter` 当前生成：

- `draft-agent-animate-subtitles.json`：导入清单，包含舞台尺寸、帧率、轨道、素材、`startFrame`、`endFrame`、`durationFrames`。
- `import-draft-agent-audio-subtitles.jsfl`：Animate 里执行的 JSFL 导入脚本。
- `run-import-in-animate.ps1` / `run-import-in-animate.cmd`：一键启动 Animate 并执行 JSFL。
- `draft-agent-animate-import-report.txt`：JSFL 导入完成后写出的验证报告；必须用它核对每个 item 的 `expected` / `actual` frame duration。
- `audio/`、`images/`、`srt/`：复制后的音频、图片和调试/旁路字幕。

已验证 Animate 支持从命令行直接执行 JSFL，例如：

```powershell
& 'C:\Program Files\Adobe\Adobe Animate 2022\Animate.exe' '.\import-draft-agent-audio-subtitles.jsfl' -AlwaysRunJSFL
```

用户环境里 FLAC 在 Animate 工作正常，不要因为格式假设强制转 WAV。导出预检可以继续检查真实缺失和不可读，但不能把 FLAC 当成必然不支持。

## 帧计算

- Animate JSFL 的帧索引是 0-based。
- Draft Agent 导出按固定 `30 fps` 算帧：`frame = round(seconds * 30)`。
- 每个 item 必须有半开区间语义：`startFrame` inclusive，`endFrame` exclusive。
- `durationFrames = max(1, endFrame - startFrame)`。
- 不要用另一个关键帧的位置判断当前素材持续时间；要看该关键帧自身的 `frame.duration`，或在 UI 里确认同一个 span 是否真的被拉长。

## 图层顺序

Animate 新增图层会改变显示位置。为了最终 UI 从上到下保持 `文本 -> 角色 -> 场景 -> 音频`，JSFL 创建导入层时需要按轨道数组反向创建，再记录真实 layer index。

经验规则：

- manifest 里的逻辑顺序仍保持 `01 文本`、`02 角色`、`03 场景`、`04 音频`。
- JSFL 创建层时从 `tracks.length - 1` 倒序到 `0`。
- 后续导入时不要假设 `tracks[i]` 等于 `timeline.layers[i]`，必须用 layer id/name 映射回真实 index。
- `timeline.addNewLayer(name, ...)` 在 Animate 2022 里不能完全信任传入的中文层名；更稳的是先新增普通层，再立刻设置 `timeline.layers[timeline.currentLayer].name = layerName`。
- 处理每个轨道前都按 layer name 重新找一次 index，不要长期缓存 layer index。之前缓存 index 导致 `timeline.layers[layer] is undefined`，报错停在导入末尾的 span trace。

## 持续时间坑

这是目前最重要的坑。

`frame.duration` 是只读属性，不能用 `frame.duration = 100` 设置素材跨度。官方 JSAPI 也把它描述为只读的帧序列长度。

`timeline.insertBlankKeyframe(frame)` 也不适合作为“制造持续时间”的主手段。它在目标是普通帧、关键帧、相邻关键帧时行为不同：有时转换，有时插到下一帧，有时只移动播放头。用它批量切 span 很容易看起来多了关键帧，但素材本身仍然只有 1 帧。

当前已验证的最小顺序是：

```javascript
fl.createDocument();
var dom = fl.getDocumentDOM();
dom.width = 1920;
dom.height = 1080;
dom.frameRate = 30;

var timeline = dom.getTimeline();
timeline.currentLayer = 0;
timeline.layers[0].name = "01 文本";
timeline.currentFrame = 0;

timeline.convertToBlankKeyframes(0, 1);
timeline.insertFrames(99, false, 0);
timeline.currentFrame = 0;

dom.addNewText(bounds, text);
var frame = timeline.layers[0].frames[0];
var textElement = frame.elements[frame.elements.length - 1];
textElement.setTextString(text);
textElement.setTextAttr("face", "Microsoft YaHei", 0, text.length);
textElement.setTextAttr("size", 48, 0, text.length);
textElement.setTextAttr("alignment", "center", 0, text.length);
```

这套顺序在 Animate 中已被用户确认：单条字幕持续 `100` 帧。

关键点：

- 先把起点转成 blank keyframe：`convertToBlankKeyframes(startFrame, startFrame + 1)`。
- 再扩当前层当前 span：`insertFrames(durationFrames - 1, false, startFrame)`。
- 最后才在 `startFrame` 添加文本/图片/音频。
- 添加对象后，优先从 `timeline.layers[layer].frames[startFrame].elements` 取刚创建的元素，再设置文字属性。

单条素材的 100 帧验证可以用 `insertFrames(durationFrames - 1, false, startFrame)`。批量导入不能对每个 item 逐条 `insertFrames`，否则会把同层后续帧整体往后挤，导致只有第一条文本看起来连续，后面的 start/end 全错。批量导入应先按该层所有 item 的 `startFrame` / `endFrame` 收集边界，从后往前 `convertToBlankKeyframes(boundary, boundary + 1)` 切出固定 spans，再在对应起始帧放文本、图片或音频。注意不要对 `0` 帧执行这一步；Animate 会把第 0 帧切成 1 帧 span，导致第一条文本/音频只有 1 帧。

2026-06-23 真实验证过的批量规则：

- 先 `ensureFrameCount(totalFrameCount)` 扩整条时间线。
- 每条导入层再 `ensureCurrentLayerFrameCount(maxEndFrame + 1)`，确保该层长度够。
- 对该层所有 item 收集 `startFrame` 和 `endFrame` 边界。
- 边界从大到小排序后切 blank keyframe。
- 切边界时只处理 `frameIndex > 0`，绝对不要切 `0`。
- 切完 spans 后，再循环导入 text/audio/image；此时 `prepareContentSpan` 只能定位和返回起始帧，不能再 `insertFrames`。
- 导入完成后写 `draft-agent-animate-import-report.txt`，逐项确认 `actual == expected`。

## 文本规则

字幕导入为 static text，默认规则：

- 矩形位置：`left = width * 0.08`，`top = height * 0.78`，`right = width * 0.92`，`bottom = height * 0.92`。
- 字体：`Microsoft YaHei`。
- 字号：`48`。
- 对齐：居中。
- 颜色：当前主导入脚本为黑色；单条 100 帧验证脚本用蓝色仅用于醒目测试。
- Animate 路径消费 `EditingTimeline` subtitle track；长文本切割由 `WorkbenchTimelineAssembler` 统一完成，导入包只把 subtitle clips 转成文本 item。

踩坑：

- 不要在没有选中文本对象时调用依赖 selection 的 `document.setTextSelection(...)`；之前会报错。
- `dom.addNewText(bounds, text)` 后要从当前 frame 的 `elements` 里拿文本对象，直接用 `textElement.setTextString(...)` 和 `textElement.setTextAttr(...)` 更稳定。
- 文本必须在 span 扩好之后添加；反过来做容易出现对象只存在 1 帧或被后续帧操作影响。

## 音频规则

音频导入当前使用：

```javascript
dom.importFile(sourceUri, true, false, false);
frame.soundLibraryItem = libraryItem;
frame.soundName = item.libraryName;
frame.soundSync = "stream";
frame.soundLoopMode = "repeat";
frame.soundLoop = 1;
```

经验：

- `dom.importFile(sourceUri, true, false, false)` 用于把音频导入库。
- 需要把库里的 `SoundItem` 绑定到目标起始帧的 `frame.soundLibraryItem`。
- `soundSync = "stream"` 便于按时间线编辑/预览。
- 音频帧本身也需要先按持续时间扩 span，否则会表现为只占 1 帧。
- FLAC 在用户当前 Animate 2022 环境里已导入成功，不要在 Animate 路径强制转 WAV。

## 图片规则

图片导入当前使用：

```javascript
prepareContentSpan(timeline, item);
dom.importFile(fileUri(item.path), false, false, false);
```

导入后通过当前 selection 调整 bounds：

- 角色图：放在画面中间偏大区域，约 `25%~75%` 宽、`8%~96%` 高，使用 contain，避免裁切角色。
- 场景图：铺满舞台，使用 cover，允许裁切边缘。

踩坑：

- 不能用设置 selected element 的 `width/height` 硬猜比例；应读取 selection rect 后按目标 bounds 算 scale，再 `setSelectionBounds(...)`。
- 图片也要先扩 span 再导入，否则同样只占 1 帧。
- 2026-06-23 这轮真实验证项目里角色图和场景图 item 数为 0，音频/文本目标已经闭环；图片导入逻辑仍需要后续用带图片素材的项目单独验，不要把本轮结果说成图片也已端到端通过。

## 验证流程

真实导入后不要只看截图，必须看导入报告。

示例命令：

```powershell
dotnet run --project .\tools\export-animate-timeline\export-animate-timeline.csproj -- "D:\Documents\Adobe\test1" "D:\Documents\Adobe\test1\exports\Animate音频字幕_yyyyMMdd-HHmmss"
& "D:\Documents\Adobe\test1\exports\Animate音频字幕_yyyyMMdd-HHmmss\run-import-in-animate.ps1"
Get-Content -LiteralPath "D:\Documents\Adobe\test1\exports\Animate音频字幕_yyyyMMdd-HHmmss\draft-agent-animate-import-report.txt" -Encoding UTF8
```

报告检查规则：

- `frameRate=30,width=1920,height=1080`。
- `track=01 文本` 和 `track=04 音频` 的 item 数应与 manifest 一致。
- 每条文本和音频都必须 `actual == expected`。
- `start=0` 的第一条也必须匹配；如果第一条 `actual=1`，通常是错误地对 0 帧执行了 `convertToBlankKeyframes`。
- 长文本应在 report 中显示为多个按时间排列的文本 item；每个 item 的 `actual` 必须等于 `expected`。
- AN 很卡，截图要等导入完成后再截；截图只用于确认无弹窗、轨道可见、文本在舞台上，不能替代 report。

## 已知坏路

- 把目标说成“转换 FLA/XFL”。本功能是导入素材到 Animate 供编辑。
- 在同一个已打开项目里反复追加。当前约定是每次 `fl.createDocument()` 新建项目。
- 只创建 keyframe，不扩 frame span。
- 用 `frame.duration = ...` 设置持续时间。
- 用 `insertBlankKeyframe(start)` / `insertBlankKeyframe(end)` 当作可靠持续时间方案。
- 批量导入时对每个 item 单独 `insertFrames(duration - 1, false, startFrame)`；它会移动同层后续时间点。
- 对第 0 帧执行 `convertToBlankKeyframes(0, 1)`；批量导入里它会把第一条 span 变成 1 帧。
- 长期缓存 layer index；Animate 新增/命名层后 index 可能失效，使用前按 layer name 重新找。
- 只看 AN 截图或波形判断持续时间；必须读 `draft-agent-animate-import-report.txt` 的 `actual`。
- 看到后面某一帧有文字，就误判前一个文字持续到了那里；那可能是另一条字幕的关键帧。
- 一次性导入很多素材后再肉眼猜问题。AN 卡时应先做单条字幕、100 帧、单轨道最小验证。

## 当前状态

已确认：

- Animate 2022 可以通过命令行执行 JSFL。
- 每次新建文档、设置 `1920 x 1080 @ 30fps` 的方向正确。
- 轨道固定为文本、角色、场景、音频。
- 单条字幕用 `convertToBlankKeyframes` + `insertFrames(99, false, 0)` 可以形成 `100` 帧 span。
- FLAC 在用户当前 Animate 环境工作正常。
- 2026-06-23 用 `D:\Documents\Adobe\test1` 真实导入成功：`33` 条文本、`33` 条音频，`draft-agent-animate-import-report.txt` 中所有文本和音频 item 均 `actual == expected`。
- 2026-06-23 同一轮验证中的长文本行为是旧实现：按 24 个中文字符预算截断，不拆分。当前实现已改为消费 `EditingTimeline` subtitle track，长文本会拆成多个文本 item，需重新做真实 Animate 导入验证。
- 同一轮验证中，角色图/场景图轨道为空，图片不作为已通过项。

仍需复验：

- 带角色图、场景图的真实项目：图片导入、bounds、contain/cover、持续时间都还要用 report 和截图单独验。
- 多字幕重叠场景：当前工作流是音频顺序追加，尚未验证同层重叠文本会如何表现。
- 长文本拆分后的真实 Animate 导入：需确认多个文本 item 的 frame span、层内 keyframe 边界和 report 结果。
- AN 卡顿时截图和自动化验证要放慢，优先看 `draft-agent-animate-import-report.txt`，`fl.trace` 只作为辅助。

## 参考资料

- Adobe Animate SDK JSAPI：`Timeline` 对象、0-based frame index、`insertFrames`、`convertToBlankKeyframes`、`insertBlankKeyframe`。
- Adobe Animate SDK JSAPI：`Frame.duration` 是只读属性，`Frame.soundLibraryItem`、`soundName`、`soundSync` 用于帧音频。
- Adobe Animate SDK JSAPI：`Document.importFile` 导入文件，`Document.addNewText` 创建文本。
- Adobe Animate SDK JSAPI：`Text.setTextAttr`、`Text.setTextString` 设置文本内容和样式。
- Adobe Help：Animate 时间线和帧/关键帧文档说明了静态 frame span、插入帧和扩展 span 的 UI 语义。
