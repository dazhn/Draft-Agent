# Draft Agent VEGAS Scripts

## import-draft-agent-fcpxml.cs

在 VEGAS Pro 中运行：

1. 先在 Draft Agent 中导出 `FCPXML`。
2. 打开 VEGAS Pro。
3. 选择 `Tools > Scripting > Run Script...`。
4. 选择 `tools/vegas/import-draft-agent-fcpxml.cs`。
5. 在弹出的文件选择框中选择 Draft Agent 导出的 `.fcpxml`。

脚本会从当前 VEGAS 光标位置开始插入：

- `Draft Agent - Text`：Titles & Text 字幕事件。
- `Draft Agent - Character Images`：角色图/行级 PSD rendered PNG。
- `Draft Agent - Scene Images`：场景图。
- `Draft Agent - Audio`：TTS 音频。

脚本只读取 FCPXML 中的 `asset-clip`、`caption` 和 `resources/asset/media-rep`，不依赖外部 JSON 库。所有写入放在一个 VEGAS `UndoBlock` 中；失败时会弹窗提示，不清空现有工程。
