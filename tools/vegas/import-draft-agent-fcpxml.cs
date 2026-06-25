// Run inside VEGAS Pro: Tools > Scripting > Run Script.
// Select a Draft Agent FCPXML export. The script inserts subtitles, character
// images, scene images, and audio into named VEGAS tracks at the current cursor.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using ScriptPortal.Vegas;

public class EntryPoint
{
    private const int SubtitleLane = 1;
    private const int CharacterLane = 2;
    private const int SceneLane = 3;
    private const int AudioLane = 4;

    private static readonly string[] TitlesAndTextGeneratorIds =
    {
        "{Svfx:com.vegascreativesoftware:titlesandtext}",
        "{Svfx:com.sonycreativesoftware:titlesandtext}"
    };

    public void FromVegas(Vegas vegas)
    {
        string fcpxmlPath = PickFcpxmlPath();
        if (string.IsNullOrWhiteSpace(fcpxmlPath))
        {
            return;
        }

        try
        {
            DraftAgentImportPlan plan = DraftAgentFcpxmlReader.Read(fcpxmlPath);
            if (plan.Items.Count == 0)
            {
                MessageBox.Show("没有在 FCPXML 中找到可导入的文本、音频或图片。", "Draft Agent VEGAS Import");
                return;
            }

            Timecode insertOffset = ReadCursorPosition(vegas);
            using (new UndoBlock(vegas.Project, "Import Draft Agent Timeline"))
            {
                ImportPlan(vegas, plan, insertOffset);
            }

            TryUpdateUi(vegas);
            MessageBox.Show(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "导入完成：文本 {0}，图片 {1}，音频 {2}。",
                    plan.Items.Count(item => item.Kind == DraftAgentImportKind.Text),
                    plan.Items.Count(item => item.Kind == DraftAgentImportKind.Video),
                    plan.Items.Count(item => item.Kind == DraftAgentImportKind.Audio)),
                "Draft Agent VEGAS Import");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Draft Agent VEGAS Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string PickFcpxmlPath()
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Title = "选择 Draft Agent FCPXML";
            dialog.Filter = "FCPXML (*.fcpxml)|*.fcpxml|XML (*.xml)|*.xml|All files (*.*)|*.*";
            dialog.CheckFileExists = true;
            dialog.Multiselect = false;

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : string.Empty;
        }
    }

    private static void ImportPlan(Vegas vegas, DraftAgentImportPlan plan, Timecode insertOffset)
    {
        Track subtitleTrack = FindOrCreateVideoTrack(vegas, "Draft Agent - Text", 0);
        Track characterTrack = FindOrCreateVideoTrack(vegas, "Draft Agent - Character Images", 1);
        Track sceneTrack = FindOrCreateVideoTrack(vegas, "Draft Agent - Scene Images", 2);
        Track audioTrack = FindOrCreateAudioTrack(vegas, "Draft Agent - Audio", 0);

        foreach (DraftAgentImportItem item in plan.Items.OrderBy(entry => entry.StartSeconds).ThenBy(entry => entry.SortOrder))
        {
            Timecode start = insertOffset + Timecode.FromSeconds(item.StartSeconds);
            Timecode length = Timecode.FromSeconds(item.DurationSeconds);
            if (item.Kind == DraftAgentImportKind.Text)
            {
                AddTextEvent(vegas, subtitleTrack, item, start, length);
            }
            else if (item.Kind == DraftAgentImportKind.Video)
            {
                Track targetTrack = item.Lane == SceneLane ? sceneTrack : characterTrack;
                AddVideoFileEvent(vegas, targetTrack, item, start, length);
            }
            else if (item.Kind == DraftAgentImportKind.Audio)
            {
                AddAudioFileEvent(vegas, audioTrack, item, start, length);
            }
        }
    }

    private static void AddAudioFileEvent(Vegas vegas, Track track, DraftAgentImportItem item, Timecode start, Timecode length)
    {
        ValidateMediaFile(item.Path, "音频");
        Media media = Media.CreateInstance(vegas.Project, item.Path);
        MediaStream stream = media.GetAudioStreamByIndex(0);
        if (stream == null)
        {
            throw new InvalidOperationException("音频没有可用音频流：" + item.Path);
        }

        AudioEvent audioEvent = new AudioEvent(vegas.Project, start, length, item.Name);
        track.Events.Add(audioEvent);
        try
        {
            audioEvent.AddTake(stream, true, Path.GetFileName(item.Path));
            TrySetLoop(audioEvent, false);
        }
        catch
        {
            track.Events.Remove(audioEvent);
            throw;
        }
    }

    private static void AddVideoFileEvent(Vegas vegas, Track track, DraftAgentImportItem item, Timecode start, Timecode length)
    {
        ValidateMediaFile(item.Path, "图片");
        Media media = Media.CreateInstance(vegas.Project, item.Path);
        MediaStream stream = media.GetVideoStreamByIndex(0);
        if (stream == null)
        {
            throw new InvalidOperationException("图片没有可用视频流：" + item.Path);
        }

        VideoEvent videoEvent = new VideoEvent(vegas.Project, start, length, item.Name);
        track.Events.Add(videoEvent);
        try
        {
            videoEvent.AddTake(stream, true, Path.GetFileName(item.Path));
            TrySetLoop(videoEvent, false);
            TrySetVideoEventFlags(videoEvent);
        }
        catch
        {
            track.Events.Remove(videoEvent);
            throw;
        }
    }

    private static void AddTextEvent(Vegas vegas, Track track, DraftAgentImportItem item, Timecode start, Timecode length)
    {
        PlugInNode generator = FindTitlesAndTextGenerator(vegas);
        Media media = Media.CreateInstance(vegas.Project, generator);
        MediaStream stream = media.GetVideoStreamByIndex(0);
        if (stream == null)
        {
            throw new InvalidOperationException("Titles & Text 生成器没有可用视频流。");
        }

        VideoEvent textEvent = new VideoEvent(vegas.Project, start, length, item.Name);
        track.Events.Add(textEvent);
        try
        {
            textEvent.AddTake(stream, true, item.Text);
            WriteGeneratedText(media, item.Text);
            TrySetLoop(textEvent, false);
        }
        catch
        {
            track.Events.Remove(textEvent);
            throw;
        }
    }

    private static PlugInNode FindTitlesAndTextGenerator(Vegas vegas)
    {
        foreach (string id in TitlesAndTextGeneratorIds)
        {
            PlugInNode generator = vegas.Generators.FindChildByUniqueID(id);
            if (generator != null)
            {
                return generator;
            }
        }

        throw new InvalidOperationException("找不到 VEGAS Titles & Text 生成器。");
    }

    private static void WriteGeneratedText(Media media, string text)
    {
        Effect generator = media.Generator;
        if (generator == null || generator.OFXEffect == null)
        {
            throw new InvalidOperationException("无法访问 Titles & Text 的 OFX 参数。");
        }

        OFXStringParameter parameter = FindTextParameter(generator.OFXEffect);
        if (parameter == null)
        {
            throw new InvalidOperationException("找不到 Titles & Text 的 Text 参数。");
        }

        using (RichTextBox box = new RichTextBox())
        {
            string currentRtf = parameter.Value;
            if (!string.IsNullOrWhiteSpace(currentRtf))
            {
                try
                {
                    box.Rtf = currentRtf;
                }
                catch
                {
                    box.Text = string.Empty;
                }
            }

            box.Text = text ?? string.Empty;
            parameter.Value = box.Rtf;
        }
    }

    private static OFXStringParameter FindTextParameter(OFXEffect effect)
    {
        string[] names = { "Text", "text", "Texts", "texts" };
        foreach (string name in names)
        {
            OFXStringParameter parameter = null;
            try
            {
                parameter = effect[name] as OFXStringParameter;
            }
            catch
            {
                try
                {
                    parameter = effect.FindParameterByName(name) as OFXStringParameter;
                }
                catch
                {
                    parameter = null;
                }
            }

            if (parameter != null)
            {
                return parameter;
            }
        }

        return null;
    }

    private static Track FindOrCreateVideoTrack(Vegas vegas, string name, int preferredIndex)
    {
        Track existing = FindTrackByName(vegas, name, true);
        if (existing != null)
        {
            return existing;
        }

        VideoTrack track = null;
        try
        {
            track = new VideoTrack(vegas.Project, Math.Min(preferredIndex, vegas.Project.Tracks.Count), null);
            vegas.Project.Tracks.Add(track);
        }
        catch
        {
            track = vegas.Project.AddVideoTrack();
        }

        TrySetTrackName(track, name);
        return track;
    }

    private static Track FindOrCreateAudioTrack(Vegas vegas, string name, int preferredIndex)
    {
        Track existing = FindTrackByName(vegas, name, false);
        if (existing != null)
        {
            return existing;
        }

        AudioTrack track = null;
        try
        {
            track = new AudioTrack(vegas.Project, Math.Min(preferredIndex, vegas.Project.Tracks.Count), null);
            vegas.Project.Tracks.Add(track);
        }
        catch
        {
            track = vegas.Project.AddAudioTrack();
        }

        TrySetTrackName(track, name);
        return track;
    }

    private static Track FindTrackByName(Vegas vegas, string name, bool video)
    {
        foreach (Track track in vegas.Project.Tracks)
        {
            try
            {
                if (string.Equals(track.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    ((video && track is VideoTrack) || (!video && track is AudioTrack)))
                {
                    return track;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static Timecode ReadCursorPosition(Vegas vegas)
    {
        try
        {
            return vegas.Transport.CursorPosition;
        }
        catch
        {
            try
            {
                return vegas.Cursor;
            }
            catch
            {
                return Timecode.FromSeconds(0);
            }
        }
    }

    private static void ValidateMediaFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(label + "路径为空。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(label + "文件不存在：" + path, path);
        }

        FileInfo file = new FileInfo(path);
        if (file.Length == 0)
        {
            throw new InvalidOperationException(label + "文件为空：" + path);
        }
    }

    private static void TrySetTrackName(Track track, string name)
    {
        try
        {
            track.Name = name;
        }
        catch
        {
        }
    }

    private static void TrySetLoop(TrackEvent trackEvent, bool loop)
    {
        try
        {
            trackEvent.Loop = loop;
        }
        catch
        {
        }
    }

    private static void TrySetVideoEventFlags(VideoEvent videoEvent)
    {
        try
        {
            videoEvent.MaintainAspectRatio = true;
        }
        catch
        {
        }

        try
        {
            videoEvent.ReduceInterlace = false;
        }
        catch
        {
        }
    }

    private static void TryUpdateUi(Vegas vegas)
    {
        try
        {
            vegas.UpdateUI();
        }
        catch
        {
        }
    }
}

internal sealed class DraftAgentFcpxmlReader
{
    private const int SubtitleLane = 1;

    public static DraftAgentImportPlan Read(string path)
    {
        XDocument document = XDocument.Load(path);
        Dictionary<string, DraftAgentAsset> assets = ReadAssets(document);
        DraftAgentImportPlan plan = new DraftAgentImportPlan();
        int order = 0;

        foreach (XElement assetClip in Descendants(document, "asset-clip"))
        {
            string refId = Attr(assetClip, "ref");
            if (string.IsNullOrWhiteSpace(refId) || !assets.ContainsKey(refId))
            {
                continue;
            }

            DraftAgentAsset asset = assets[refId];
            DraftAgentImportItem item = new DraftAgentImportItem();
            item.Name = Attr(assetClip, "name");
            item.Path = asset.Path;
            item.Lane = ParseInt(Attr(assetClip, "lane"));
            item.StartSeconds = ParseTime(Attr(assetClip, "offset"));
            item.DurationSeconds = ParseTime(Attr(assetClip, "duration"));
            item.Kind = asset.HasAudio ? DraftAgentImportKind.Audio : DraftAgentImportKind.Video;
            item.SortOrder = order++;

            if (item.DurationSeconds > 0)
            {
                plan.Items.Add(item);
            }
        }

        foreach (XElement caption in Descendants(document, "caption"))
        {
            DraftAgentImportItem item = new DraftAgentImportItem();
            item.Kind = DraftAgentImportKind.Text;
            item.Lane = SubtitleLane;
            item.Name = Attr(caption, "name");
            item.Text = ReadCaptionText(caption);
            item.StartSeconds = ParseTime(Attr(caption, "offset"));
            item.DurationSeconds = ParseTime(Attr(caption, "duration"));
            item.SortOrder = order++;

            if (!string.IsNullOrWhiteSpace(item.Text) && item.DurationSeconds > 0)
            {
                plan.Items.Add(item);
            }
        }

        return plan;
    }

    private static Dictionary<string, DraftAgentAsset> ReadAssets(XDocument document)
    {
        Dictionary<string, DraftAgentAsset> assets = new Dictionary<string, DraftAgentAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement assetElement in Descendants(document, "asset"))
        {
            string id = Attr(assetElement, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            XElement mediaRep = Children(assetElement, "media-rep").FirstOrDefault();
            string src = mediaRep == null ? string.Empty : Attr(mediaRep, "src");
            string localPath = FileUriToPath(src);
            if (string.IsNullOrWhiteSpace(localPath))
            {
                continue;
            }

            DraftAgentAsset asset = new DraftAgentAsset();
            asset.Id = id;
            asset.Path = localPath;
            asset.HasAudio = Attr(assetElement, "hasAudio") == "1";
            asset.HasVideo = Attr(assetElement, "hasVideo") == "1";
            assets[id] = asset;
        }

        return assets;
    }

    private static string ReadCaptionText(XElement caption)
    {
        IEnumerable<XElement> textStyleElements = caption
            .Descendants()
            .Where(element => element.Name.LocalName == "text-style" && element.Parent != null && element.Parent.Name.LocalName == "text");
        string text = string.Join(string.Empty, textStyleElements.Select(element => element.Value));
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        XElement textElement = caption.Descendants().FirstOrDefault(element => element.Name.LocalName == "text");
        return textElement == null ? Attr(caption, "name") : textElement.Value;
    }

    private static IEnumerable<XElement> Descendants(XDocument document, string localName)
    {
        return document.Descendants().Where(element => element.Name.LocalName == localName);
    }

    private static IEnumerable<XElement> Children(XElement element, string localName)
    {
        return element.Elements().Where(child => child.Name.LocalName == localName);
    }

    private static string Attr(XElement element, string name)
    {
        XAttribute attribute = element.Attribute(name);
        return attribute == null ? string.Empty : attribute.Value;
    }

    private static string FileUriToPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Uri uri;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return value;
    }

    private static int ParseInt(string value)
    {
        int parsed;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
    }

    private static double ParseTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        string text = value.Trim();
        if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 1);
        }

        int slash = text.IndexOf('/');
        if (slash > 0)
        {
            double numerator;
            double denominator;
            if (double.TryParse(text.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out numerator) &&
                double.TryParse(text.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out denominator) &&
                Math.Abs(denominator) > double.Epsilon)
            {
                return numerator / denominator;
            }
        }

        double seconds;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ? seconds : 0;
    }
}

internal sealed class DraftAgentImportPlan
{
    public DraftAgentImportPlan()
    {
        Items = new List<DraftAgentImportItem>();
    }

    public List<DraftAgentImportItem> Items { get; private set; }
}

internal sealed class DraftAgentImportItem
{
    public DraftAgentImportKind Kind;
    public int Lane;
    public int SortOrder;
    public string Name;
    public string Path;
    public string Text;
    public double StartSeconds;
    public double DurationSeconds;
}

internal sealed class DraftAgentAsset
{
    public string Id;
    public string Path;
    public bool HasAudio;
    public bool HasVideo;
}

internal enum DraftAgentImportKind
{
    Text,
    Video,
    Audio
}
