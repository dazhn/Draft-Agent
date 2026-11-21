namespace DraftAgent.App.ViewModels;

using DraftAgent.Core;

public sealed record ExportPreflightIssueViewModel(
    int Index,
    EditingTimelinePreflightIssueKind Kind,
    string InternalMessage,
    string SceneId,
    string InteractionId,
    string ReferenceText,
    string TaskId,
    GenerationTaskStatus? TaskStatus,
    string InternalSuggestedAction)
{
    public string IndexText => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? DisplaySceneLabel { get; init; }

    public string? DisplayRowLabel { get; init; }

    public bool CanLocate => !string.IsNullOrWhiteSpace(SceneId) || !string.IsNullOrWhiteSpace(InteractionId);

    public string SummaryText => Kind switch
    {
        EditingTimelinePreflightIssueKind.PsdReference => "PSD 未渲染为图片",
        EditingTimelinePreflightIssueKind.MissingVisual => "图片文件缺失",
        EditingTimelinePreflightIssueKind.MissingAudio => "语音文件缺失",
        EditingTimelinePreflightIssueKind.UnsupportedAudio => "语音格式不支持",
        _ => "图片未绑定"
    };

    public string DetailText => Kind switch
    {
        EditingTimelinePreflightIssueKind.PsdReference when CanRepair => "请先渲染 PSD 图层为图片后再导出。",
        EditingTimelinePreflightIssueKind.PsdReference => "请定位到对应绑定后改用普通图片。",
        EditingTimelinePreflightIssueKind.MissingVisual => "请重新绑定图片，或确认项目 assets 中的图片文件仍然存在。",
        EditingTimelinePreflightIssueKind.MissingAudio => "请重新同步或重新生成该行语音后再导出。",
        EditingTimelinePreflightIssueKind.UnsupportedAudio => "请转换为 WAV/FLAC，或补齐该格式真实时长读取器后再导出。",
        _ => "请定位到对应场景或行后修复绑定。"
    };

    public string CompactLocationText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SceneId) && string.IsNullOrWhiteSpace(InteractionId))
            {
                return "未定位";
            }

            var sceneText = string.IsNullOrWhiteSpace(DisplaySceneLabel) ? "场景" : DisplaySceneLabel;
            if (string.IsNullOrWhiteSpace(InteractionId))
            {
                return sceneText;
            }

            var rowText = string.IsNullOrWhiteSpace(DisplayRowLabel) ? "当前行" : $"第 {DisplayRowLabel} 行";
            return $"{sceneText} / {rowText}";
        }
    }

    public bool CanRepair => Kind == EditingTimelinePreflightIssueKind.PsdReference &&
        !string.IsNullOrWhiteSpace(InteractionId);

    public string RepairText => Kind switch
    {
        EditingTimelinePreflightIssueKind.PsdReference when CanRepair => "渲染",
        EditingTimelinePreflightIssueKind.PsdReference => "定位",
        EditingTimelinePreflightIssueKind.MissingAudio => "定位",
        EditingTimelinePreflightIssueKind.UnsupportedAudio => "定位",
        _ => "定位"
    };

    public string ActionText => Kind switch
    {
        EditingTimelinePreflightIssueKind.PsdReference when CanRepair => "渲染 PSD",
        EditingTimelinePreflightIssueKind.PsdReference => "定位处理",
        EditingTimelinePreflightIssueKind.MissingVisual => "重新绑定图片",
        EditingTimelinePreflightIssueKind.MissingAudio => "重新生成语音",
        EditingTimelinePreflightIssueKind.UnsupportedAudio => "转换音频",
        _ => "定位处理"
    };

    public string LocationText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SceneId) && string.IsNullOrWhiteSpace(InteractionId))
            {
                return "未定位";
            }

            var sceneText = string.IsNullOrWhiteSpace(DisplaySceneLabel) ? "场景" : DisplaySceneLabel;
            if (string.IsNullOrWhiteSpace(InteractionId))
            {
                return sceneText;
            }

            var rowText = string.IsNullOrWhiteSpace(DisplayRowLabel) ? "当前行" : $"第 {DisplayRowLabel} 行";
            return $"{sceneText} / {rowText}";
        }
    }
}
