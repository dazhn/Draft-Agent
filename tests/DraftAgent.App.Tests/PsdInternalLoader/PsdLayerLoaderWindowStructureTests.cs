namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdLayerLoaderWindowStructureTests
{
    [Fact]
    public void WindowUsesSimpleLeftGroupAndRightPreviewThumbnailLayout()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DraftAgent.App",
            "Psd",
            "Views",
            "PsdLayerLoaderWindow.axaml"));

        var xaml = File.ReadAllText(xamlPath, System.Text.Encoding.UTF8);

        Assert.Contains("WindowDecorations=\"Full\"", xaml);
        Assert.Contains("Background=\"#0D1117\"", xaml);
        Assert.Contains("ColumnDefinitions=\"260,*\"", xaml);
        Assert.Contains("RowDefinitions=\"*,5,0.38*\"", xaml);
        Assert.Contains("加载 PSD", xaml);
        Assert.Contains("PNG", xaml);
        Assert.Contains("图层组", xaml);
        Assert.Contains("当前图层组", xaml);
        Assert.Contains("图层缩略图", xaml);
        Assert.Contains("拖出保存 PNG", xaml);
        Assert.Contains("IndentWidth", xaml);
        Assert.Contains("OnGroupExpandClicked", xaml);
        Assert.Contains("ExpandGlyph", xaml);
        Assert.Contains("HasChildren", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\" />", xaml);
        Assert.Contains("GridSplitter", xaml);
        Assert.Contains("Width\" Value=\"96", xaml);
        Assert.Contains("Border.thumbnailFrame", xaml);
        Assert.Contains("RowDefinitions=\"Auto,Auto\"", xaml);
        Assert.DoesNotContain("Text=\"{Binding Bounds}\"", xaml);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\" />", xaml);
        Assert.DoesNotContain("MinWidth\" Value=\"128", xaml);
        Assert.DoesNotContain("Width\" Value=\"126", xaml);
        Assert.DoesNotContain("Height\" Value=\"132", xaml);
        Assert.DoesNotContain("NestedGroupTabs", xaml);
        Assert.DoesNotContain("OnNestedGroupTabClicked", xaml);
        Assert.DoesNotContain("VisibleLayerCount", xaml);
        Assert.DoesNotContain("SelectedLayerCount", xaml);
        Assert.DoesNotContain("CurrentGroupItemCount", xaml);
        Assert.DoesNotContain("ColumnDefinitions=\"240,*,240\"", xaml);
    }
}


