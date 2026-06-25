using System.Xml.Linq;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchViewStructureTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MainWindowUserVisibleTextDoesNotContainCommonMojibake()
    {
        var files = new[]
        {
            FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml"),
            FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"),
            FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.cs"),
            FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.Binding.cs"),
            FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.Tts.cs"),
            FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.Workbench.cs")
        };

        var mojibakeMarkers = new[]
        {
            "\u93c3",
            "\u6924",
            "\u7481",
            "\u9397",
            "\u7487",
            "\u7f01",
            "\u7025",
            "\u95c1",
            "\u5a62",
            "\u9413"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(mojibakeMarkers, marker => text.Contains(marker, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void WorkbenchSceneListUsesVirtualizingListBox()
    {
        var (document, ns) = LoadMainWindow();
        var workbenchSceneList = FindListBoxByClass(document, ns, "workbenchSceneList");

        Assert.Equal("{Binding WorkbenchDisplayItems}", (string?)workbenchSceneList.Attribute("ItemsSource"));
        Assert.Equal("WorkbenchSceneList", AttributeValue(workbenchSceneList, "Name"));
        Assert.Equal("True", AttributeValue(workbenchSceneList, "ScrollViewer.IsScrollChainingEnabled"));
        AssertUsesVirtualizingStackPanel(ns, workbenchSceneList);
        Assert.DoesNotContain(workbenchSceneList.Descendants(ns + "ScrollViewer"), _ => true);

        Assert.Empty(workbenchSceneList.Elements(ns + "ListBox.ItemTemplate"));
        var displayTemplates = workbenchSceneList
            .Elements(ns + "ListBox.DataTemplates")
            .Single()
            .Elements(ns + "DataTemplate")
            .ToArray();
        Assert.Equal(
            [
                "vm:WorkbenchSceneHeaderDisplayItem",
                "vm:WorkbenchEmptySceneDisplayItem",
                "vm:WorkbenchRowDisplayItem"
            ],
            displayTemplates.Select(template => (string)template.Attribute("DataType")!).ToArray());
        Assert.All(displayTemplates, template =>
            Assert.Equal((string?)template.Attribute("DataType"), (string?)template.Attribute(XamlNamespace + "DataType")));
        Assert.Empty(FindListBoxesByClass(document, ns, "workbenchRowList"));
    }

    [Fact]
    public void WorkbenchFlatListContainsSceneRowAndEmptySceneTargets()
    {
        var (document, ns) = LoadMainWindow();
        var workbenchSceneList = FindListBoxByClass(document, ns, "workbenchSceneList");
        var sceneTemplate = DataTemplateByDataType(workbenchSceneList, ns, "vm:WorkbenchSceneHeaderDisplayItem");
        var emptySceneTemplate = DataTemplateByDataType(workbenchSceneList, ns, "vm:WorkbenchEmptySceneDisplayItem");
        var rowTemplate = DataTemplateByDataType(workbenchSceneList, ns, "vm:WorkbenchRowDisplayItem");
        var sceneHeader = sceneTemplate
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "referenceScene"));
        var emptySceneTarget = emptySceneTemplate
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "emptySceneDropTarget"));
        var eventRow = rowTemplate
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "eventRow"));
        var rowRoot = rowTemplate.Elements(ns + "Grid").Single();
        var rowEndTarget = rowTemplate
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "rowSceneEndDropTarget"));
        var nonRowTemplateBindings = sceneTemplate
            .DescendantsAndSelf()
            .Concat(emptySceneTemplate.DescendantsAndSelf())
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Null((string?)sceneHeader.Attribute("IsVisible"));
        Assert.Equal("{Binding Scene}", (string?)sceneHeader.Attribute("Tag"));
        Assert.Equal("{Binding Scene}", AttributeValue(sceneHeader, "WorkbenchDragTarget.Target"));
        Assert.Null((string?)emptySceneTarget.Attribute("IsVisible"));
        Assert.Equal("{Binding Scene}", (string?)emptySceneTarget.Attribute("Tag"));
        Assert.Equal("{Binding Scene}", AttributeValue(emptySceneTarget, "WorkbenchDragTarget.Target"));
        Assert.Null((string?)emptySceneTarget.Attribute("PointerPressed"));
        Assert.Null((string?)rowRoot.Attribute("IsVisible"));
        Assert.Equal("{Binding Row}", AttributeValue(eventRow, "WorkbenchDragTarget.Target"));
        Assert.Equal("{Binding !Row.IsDragSourceActive}", (string?)eventRow.Attribute("IsHitTestVisible"));
        Assert.Equal("{Binding IsLastRowInScene}", (string?)rowEndTarget.Attribute("IsVisible"));
        Assert.Equal("{Binding Scene}", AttributeValue(rowEndTarget, "WorkbenchDragTarget.Target"));
        Assert.Null((string?)rowEndTarget.Attribute("PointerPressed"));
        Assert.DoesNotContain(nonRowTemplateBindings, value => value.Contains("Row.", StringComparison.Ordinal));
    }

    [Fact]
    public void BindingTargetDialogUsesWrappingCardList()
    {
        var (document, ns) = LoadMainWindow();
        var bindingTargetLists = FindListBoxesByClass(document, ns, "bindingTargetList").ToArray();
        var bindingTargetListSources = bindingTargetLists
            .Select(element => (string)element.Attribute("ItemsSource")!)
            .ToArray();
        var sectionLabels = document
            .Descendants(ns + "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        Assert.DoesNotContain("场景绑定", sectionLabels);
        Assert.Contains("角色素材", sectionLabels);
        Assert.Contains("默认配音", sectionLabels);
        Assert.Equal(
            [
                "{Binding NarrationBindingTargets}",
                "{Binding CharacterBindingTargets}"
            ],
            bindingTargetListSources);
        Assert.Equal(
            [
                "{Binding SelectedNarrationBindingTarget, Mode=TwoWay}",
                "{Binding SelectedCharacterBindingTarget, Mode=TwoWay}"
            ],
            bindingTargetLists.Select(element => (string)element.Attribute("SelectedItem")!).ToArray());
        Assert.Equal("{StaticResource NarrationBindingTargetRowTemplate}", (string?)bindingTargetLists[0].Attribute("ItemTemplate"));
        Assert.Equal("{StaticResource BindingTargetCardTemplate}", (string?)bindingTargetLists[1].Attribute("ItemTemplate"));
        var narrationItemsPanel = bindingTargetLists[0]
            .Elements(ns + "ListBox.ItemsPanel")
            .Single()
            .Element(ns + "ItemsPanelTemplate");
        var characterItemsPanel = bindingTargetLists[1]
            .Elements(ns + "ListBox.ItemsPanel")
            .Single()
            .Element(ns + "ItemsPanelTemplate");

        Assert.NotNull(narrationItemsPanel);
        Assert.NotNull(characterItemsPanel);
        Assert.Contains(narrationItemsPanel.Elements(), element => element.Name == ns + "StackPanel");
        Assert.Contains(characterItemsPanel.Elements(), element => element.Name == ns + "WrapPanel");
        var dialogGrid = bindingTargetLists[0]
            .Ancestors(ns + "Grid")
            .First(element => (string?)element.Attribute("MaxHeight") == "760");
        Assert.Equal("1300", (string?)dialogGrid.Attribute("Width"));
    }

    [Fact]
    public void BindingTargetDialogDisplaysCompactBindingControls()
    {
        var (document, ns) = LoadMainWindow();
        var bindings = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var buttonTexts = document
            .Descendants(ns + "Button")
            .Select(element => (string?)element.Attribute("Content"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        Assert.Contains("{Binding BindingPreviewShowsImage}", bindings);
        Assert.Contains("{Binding BindingPreviewShowsText}", bindings);
        Assert.Contains("{Binding BindingPreviewTitle}", bindings);
        Assert.Contains("{Binding BindingPreviewSubtitle}", bindings);
        Assert.Contains("{Binding UsesLargeBindingPreview}", bindings);
        Assert.Contains("{Binding VoiceReferenceText, Mode=TwoWay}", bindings);
        Assert.Contains("{Binding CanEditVoiceBinding}", bindings);
        Assert.Contains("{Binding CanEditVoiceReferenceText}", bindings);
        Assert.Contains("{Binding CanPreviewVoiceBinding}", bindings);
        Assert.Contains("{Binding EditableDisplayName, Mode=TwoWay}", bindings);
        Assert.Contains("{Binding CanEditDisplayName}", bindings);
        Assert.Contains("{Binding IsEditingDisplayName}", bindings);
        Assert.Contains("{Binding RenameButtonText}", bindings);
        Assert.Contains("{Binding !IsEditingDisplayName}", bindings);
        Assert.Contains("选择", buttonTexts);
        Assert.Contains("图层", buttonTexts);
        Assert.Contains("试听", buttonTexts);
        Assert.Contains("保存", buttonTexts);
        Assert.DoesNotContain("Apply", buttonTexts);
        Assert.DoesNotContain("同步", buttonTexts);
        Assert.DoesNotContain("PSD预览", buttonTexts);
        Assert.DoesNotContain("AI生成", buttonTexts);
        Assert.DoesNotContain("智能生成", buttonTexts);
        Assert.DoesNotContain(document.Descendants(ns + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "音频");

        var bindingTargetCardTemplate = DataTemplateByKey(document, ns, "BindingTargetCardTemplate");
        Assert.Contains(bindingTargetCardTemplate.Descendants(ns + "Grid"), element =>
            (string?)element.Attribute("ColumnDefinitions") == "*,60");
        var chooseImageButton = bindingTargetCardTemplate
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "选择");
        Assert.Equal(
            "{Binding $parent[Window].DataContext.CanEditWorkbenchStructure}",
            (string?)chooseImageButton.Attribute("IsEnabled"));
        var psdPreviewButton = bindingTargetCardTemplate
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "图层");
        Assert.Equal(
            "{Binding $parent[Window].DataContext.CanEditWorkbenchStructure}",
            (string?)psdPreviewButton.Attribute("IsEnabled"));
        var nameEditor = bindingTargetCardTemplate
            .Descendants(ns + "TextBox")
            .Single(element => (string?)element.Attribute("Text") == "{Binding EditableDisplayName, Mode=TwoWay}");
        Assert.Equal("{Binding IsEditingDisplayName}", (string?)nameEditor.Attribute("IsVisible"));
        Assert.Equal(
            "{Binding $parent[Window].DataContext.CanEditWorkbenchStructure}",
            (string?)nameEditor.Attribute("IsEnabled"));
        Assert.Equal("{Binding IsEditingDisplayName}", AttributeValue(nameEditor, "BindingTargetNameEditFocus.IsEditing"));
        Assert.Null((string?)nameEditor.Attribute("LostFocus"));
        Assert.Equal("BindingTargetNameKeyDown", (string?)nameEditor.Attribute("KeyDown"));
        Assert.Equal("{Binding}", (string?)nameEditor.Attribute("Tag"));
        var renameButton = bindingTargetCardTemplate
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "{Binding RenameButtonText}");
        Assert.Equal("{Binding CanEditDisplayName}", (string?)renameButton.Attribute("IsVisible"));
        Assert.Equal(
            "{Binding $parent[Window].DataContext.CanEditWorkbenchStructure}",
            (string?)renameButton.Attribute("IsEnabled"));
        Assert.Equal("ToggleBindingTargetNameEdit", (string?)renameButton.Attribute("Click"));
        Assert.Equal("{Binding}", (string?)renameButton.Attribute("Tag"));
        var nameText = bindingTargetCardTemplate
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding DisplayName}");
        Assert.Equal("{Binding !IsEditingDisplayName}", (string?)nameText.Attribute("IsVisible"));
        Assert.Equal("17", (string?)nameText.Attribute("FontSize"));
        Assert.Equal("Center", (string?)nameText.Attribute("VerticalAlignment"));
        Assert.Equal("BeginBindingTargetNameEdit", (string?)nameText.Attribute("PointerPressed"));
        Assert.Equal("{Binding}", (string?)nameText.Attribute("Tag"));
        Assert.DoesNotContain(document.Descendants(ns + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding Category}");
        var visualActionStack = Assert.IsType<XElement>(chooseImageButton.Parent);
        Assert.Equal("StackPanel", visualActionStack.Name.LocalName);
        Assert.Equal("1", (string?)visualActionStack.Attribute("Grid.Column"));
        Assert.Equal("6", (string?)visualActionStack.Attribute("Spacing"));
        Assert.Same(visualActionStack, psdPreviewButton.Parent);
        var visualActionGrid = Assert.IsType<XElement>(visualActionStack.Parent);
        Assert.Equal("*,60", (string?)visualActionGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("6", (string?)visualActionGrid.Attribute("ColumnSpacing"));
        Assert.DoesNotContain(visualActionGrid.Descendants().Attributes(), attribute =>
            attribute.Name.LocalName.Contains("WorkbenchPreviewToolTip", StringComparison.Ordinal));
        Assert.DoesNotContain(bindingTargetCardTemplate.Descendants(ns + "Button"), element =>
            (string?)element.Attribute("Content") == "PSD图层模板设置");
        var psdLayerMatchButton = document
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "PSD图层模板设置");
        Assert.Equal("{Binding HasCharacterBindingTargets}", (string?)psdLayerMatchButton.Attribute("IsVisible"));
        Assert.Equal("{Binding CanConfigureSelectedCharacterPsdLayerMatch}", (string?)psdLayerMatchButton.Attribute("IsEnabled"));
        Assert.Equal("{Binding CharacterPsdLayerMatchTarget}", (string?)psdLayerMatchButton.Attribute("Tag"));
        Assert.Equal("{Binding CharacterPsdLayerMatchSettingsText}", (string?)psdLayerMatchButton.Attribute("ToolTip.Tip"));
        Assert.Equal("ConfigurePsdLayerMatch", (string?)psdLayerMatchButton.Attribute("Click"));
        var psdLayerMatchHeader = Assert.IsType<XElement>(psdLayerMatchButton.Parent);
        Assert.Equal("StackPanel", psdLayerMatchHeader.Name.LocalName);
        Assert.Equal("Horizontal", (string?)psdLayerMatchHeader.Attribute("Orientation"));
        Assert.Contains(psdLayerMatchHeader.Descendants(ns + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "角色素材");

        var voiceReferenceCombo = bindingTargetCardTemplate
            .Descendants(ns + "ComboBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding $parent[Window].DataContext.VoiceReferenceOptions}");
        Assert.Equal("VoiceReferenceSelectionChanged", (string?)voiceReferenceCombo.Attribute("SelectionChanged"));
        Assert.Equal("Stretch", (string?)voiceReferenceCombo.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding CanEditVoiceBinding}", (string?)voiceReferenceCombo.Attribute("IsEnabled"));

        var voiceGrid = Assert.IsType<XElement>(voiceReferenceCombo.Parent);
        var previewButton = voiceGrid
            .Elements(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "试听");
        Assert.Equal("{Binding CanPreviewVoiceBinding}", (string?)previewButton.Attribute("IsEnabled"));
        Assert.Equal("*,56", (string?)voiceGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("Stretch", (string?)voiceGrid.Attribute("HorizontalAlignment"));
        var voiceSectionGrid = Assert.IsType<XElement>(voiceGrid.Parent);
        Assert.Equal("{Binding ShowVoiceBindingControls}", (string?)voiceSectionGrid.Attribute("IsVisible"));

        var referenceTextBox = bindingTargetCardTemplate
            .Descendants(ns + "TextBox")
            .Single(element => (string?)element.Attribute("Text") == "{Binding VoiceReferenceText, Mode=TwoWay}");
        Assert.Equal("Stretch", (string?)referenceTextBox.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding CanEditVoiceReferenceText}", (string?)referenceTextBox.Attribute("IsEnabled"));
        Assert.Equal("没有同名文本文件时输入临时参考文本", (string?)referenceTextBox.Attribute("PlaceholderText"));
        Assert.Contains("临时输入", AttributeValue(referenceTextBox, "ToolTip.Tip"), StringComparison.Ordinal);
        var referenceGrid = Assert.IsType<XElement>(referenceTextBox.Parent);
        Assert.Equal("*,56", (string?)referenceGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("Stretch", (string?)referenceGrid.Attribute("HorizontalAlignment"));
        var referenceSaveButton = referenceGrid
            .Elements(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "保存");
        Assert.Equal("False", (string?)referenceSaveButton.Attribute("IsEnabled"));
        Assert.Contains(
            "要持久保存请在音频旁边建立同名 .reference.txt 或 .txt",
            AttributeValue(referenceSaveButton, "ToolTip.Tip"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NarrationBindingTargetUsesVoiceOnlyRowTemplate()
    {
        var (document, ns) = LoadMainWindow();
        var template = DataTemplateByKey(document, ns, "NarrationBindingTargetRowTemplate");

        Assert.Contains(template.Descendants(ns + "ComboBox"), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding $parent[Window].DataContext.VoiceReferenceOptions}");
        Assert.Contains(template.Descendants(ns + "TextBox"), element =>
            (string?)element.Attribute("Text") == "{Binding VoiceReferenceText, Mode=TwoWay}");
        Assert.Contains(template.Descendants(ns + "ComboBox"), element =>
            (string?)element.Attribute("IsEnabled") == "{Binding CanEditVoiceBinding}");
        Assert.Contains(template.Descendants(ns + "TextBox"), element =>
            (string?)element.Attribute("IsEnabled") == "{Binding CanEditVoiceReferenceText}");
        Assert.Contains(template.Descendants(ns + "Button"), element =>
            (string?)element.Attribute("Content") == "试听" &&
            (string?)element.Attribute("IsEnabled") == "{Binding CanPreviewVoiceBinding}");
        Assert.Contains(template.Descendants(ns + "Button"), element =>
            (string?)element.Attribute("Content") == "保存");
        var narrationSaveButton = template
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "保存");
        Assert.Equal("False", (string?)narrationSaveButton.Attribute("IsEnabled"));
        Assert.Contains("要持久保存请在音频旁边建立同名 .reference.txt 或 .txt", AttributeValue(narrationSaveButton, "ToolTip.Tip"), StringComparison.Ordinal);
        Assert.DoesNotContain(template.Descendants(ns + "Border"), element =>
            HasClass(element, "bindingPreviewTile"));
        Assert.DoesNotContain(template.Descendants(ns + "Button"), element =>
            (string?)element.Attribute("Content") is "选择" or "图层");
    }

    [Fact]
    public void WorkbenchObjectThumbnailUsesCompactSquarePreview()
    {
        var (document, ns) = LoadMainWindow();
        var objectThumb = document
            .Descendants(ns + "Grid")
            .Single(element => (string?)element.Attribute("PointerPressed") == "ToggleWorkbenchPsdLayersFromThumbnail");
        var objectCellGrid = Assert.IsType<XElement>(objectThumb.Parent);
        var rowGrid = objectCellGrid.Ancestors(ns + "Grid")
            .First(element => (string?)element.Attribute("ColumnDefinitions") == "46,Auto,36,66,*,84,38");

        Assert.Equal("Auto,*", (string?)objectCellGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("46,Auto,36,66,*,84,38", (string?)rowGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("{Binding Row.InlineObjectPreviewWidth}", (string?)objectThumb.Attribute("Width"));
        Assert.Equal("{Binding Row.InlineObjectPreviewHeight}", (string?)objectThumb.Attribute("Height"));
        Assert.Equal("ToggleWorkbenchPsdLayersFromThumbnail", (string?)objectThumb.Attribute("PointerPressed"));
        Assert.DoesNotContain(
            objectCellGrid.Attributes(),
            attribute => attribute.Name.LocalName == "WorkbenchObjectNameEditorHost.IsEnabled");
        var image = objectThumb
            .Descendants(ns + "Image")
            .Single(element => HasClass(element, "inlineObjectPreviewImage"));
        Assert.Equal("Uniform", (string?)image.Attribute("Stretch"));
        Assert.Equal("{Binding Row.HasInlineObjectPreviewImage}", (string?)image.Attribute("IsVisible"));
        var objectPreviewTip = rowGrid
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "rowObjectBox"));
        Assert.Equal("{Binding Row.InlineObjectPreviewSource}", AttributeValue(objectPreviewTip, "WorkbenchPreviewToolTip.ImagePath"));
        Assert.Equal("{Binding Row.InlineObjectPreviewLabel}", AttributeValue(objectPreviewTip, "WorkbenchPreviewToolTip.Label"));
        Assert.Equal("{Binding Row.HasInlineObjectPreviewImage}", AttributeValue(objectPreviewTip, "WorkbenchPreviewToolTip.HasImage"));
        Assert.Equal("260", AttributeValue(objectPreviewTip, "WorkbenchPreviewToolTip.PreviewWidth"));
        Assert.Equal("260", AttributeValue(objectPreviewTip, "WorkbenchPreviewToolTip.PreviewHeight"));

        var objectNameRegion = objectCellGrid
            .Descendants(ns + "Grid")
            .Single(element => HasClass(element, "objectNameRegion"));
        var objectNameText = objectNameRegion
            .Descendants(ns + "TextBlock")
            .Single(element => HasClass(element, "objectNameText"));
        Assert.Equal("{Binding $parent[Window].DataContext.CanEditWorkbenchStructure}", objectNameRegion.Attributes().Single(attribute =>
            attribute.Name.LocalName == "WorkbenchObjectNameEditorHost.IsEnabled").Value);
        Assert.Equal("88", (string?)objectNameRegion.Attribute("MinWidth"));
        var appStyles = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "App.axaml"));
        Assert.Contains("Style Selector=\"Grid.objectNameRegion\"", appStyles, StringComparison.Ordinal);
        Assert.Contains("Height\" Value=\"30\"", appStyles, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"ComboBox.objectNameSelect\"", appStyles, StringComparison.Ordinal);
        Assert.Contains("MaxHeight\" Value=\"30\"", appStyles, StringComparison.Ordinal);
        Assert.Equal("{Binding Row.SubjectName}", (string?)objectNameText.Attribute("Text"));
        Assert.Null((string?)objectNameText.Attribute("IsVisible"));
        Assert.Equal(
            "{Binding $parent[Window].DataContext.CharacterNameOptions}",
            objectNameRegion.Attributes().Single(attribute =>
                attribute.Name.LocalName == "WorkbenchObjectNameEditorHost.CharacterNameOptions").Value);
        Assert.DoesNotContain(
            objectNameRegion.DescendantsAndSelf(),
            element => (string?)element.Attribute("PointerPressed") == "ToggleWorkbenchPsdLayersFromThumbnail");
    }

    [Fact]
    public void WorkbenchRowShowsSourceTypeBadgeBetweenObjectAndEmotion()
    {
        var (document, ns) = LoadMainWindow();
        var typeCell = document
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "rowTypeBox"));
        var rowGrid = Assert.IsType<XElement>(typeCell.Parent);
        var typeText = typeCell
            .Descendants(ns + "TextBlock")
            .Single(element => HasClass(element, "sourceTypeGlyph"));
        var appStyles = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "App.axaml"));

        Assert.Equal("46,Auto,36,66,*,84,38", (string?)rowGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("2", (string?)typeCell.Attribute("Grid.Column"));
        Assert.Equal("{Binding Row.SourceTypeText}", (string?)typeCell.Attribute("ToolTip.Tip"));
        Assert.Equal("{Binding Row}", (string?)typeCell.Attribute("Tag"));
        Assert.Equal("ToggleWorkbenchRowSourceType", (string?)typeCell.Attribute("PointerPressed"));
        Assert.Equal("{Binding Row.SourceTypeGlyph}", (string?)typeText.Attribute("Text"));
        Assert.Contains("Style Selector=\"Border.rowTypeBox\"", appStyles, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"TextBlock.sourceTypeGlyph\"", appStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkbenchEmotionCellUsesCompactTwoLineDisplay()
    {
        var (document, ns) = LoadMainWindow();
        var emotionCell = document
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "rowEmotionBox"));
        var rowGrid = Assert.IsType<XElement>(emotionCell.Parent);
        Assert.Equal(
            "{Binding Row.CanEditTextAndEmotion}",
            (string?)emotionCell.Attribute("IsEnabled"));
        var displayText = emotionCell
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding Row.EffectiveEmotionDisplayText}");
        var lineEditorHost = rowGrid
            .Descendants(ns + "Grid")
            .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "WorkbenchLineEditorHost.IsEnabled"));

        Assert.Equal("46,Auto,36,66,*,84,38", (string?)rowGrid.Attribute("ColumnDefinitions"));
        Assert.Equal(
            "{Binding Row.CanEditTextAndEmotion}",
            lineEditorHost.Attributes().Single(attribute => attribute.Name.LocalName == "WorkbenchLineEditorHost.IsEnabled").Value);
        Assert.True(HasClass(displayText, "emotionDisplayText"));
        Assert.Equal("Wrap", (string?)displayText.Attribute("TextWrapping"));
        Assert.Equal("2", (string?)displayText.Attribute("MaxLines"));
        Assert.Equal("CharacterEllipsis", (string?)displayText.Attribute("TextTrimming"));
    }

    [Fact]
    public void SelectingWorkbenchRowDoesNotOpenPsdLayerPicker()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));
        var methodStart = source.IndexOf("private void SelectWorkbenchRow", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async void ToggleWorkbenchPsdLayersFromThumbnail", StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var selectRowMethod = source[methodStart..methodEnd];
        Assert.Contains("SelectRowCommand.Execute(row)", selectRowMethod);
        Assert.DoesNotContain("GenerateSelectedRowPsdImageFromCharacterSourceAsync", selectRowMethod);
    }

    [Fact]
    public void ImagePreviewsUseUniformStretchToAvoidCropping()
    {
        var (document, ns) = LoadMainWindow();
        var previewImages = document
            .Descendants(ns + "Image")
            .Where(element =>
                (string?)element.Attribute("Source") is { } source &&
                source.Contains("ImageAssetPreviewConverter", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(previewImages);
        Assert.All(previewImages, image => Assert.NotEqual("UniformToFill", (string?)image.Attribute("Stretch")));
        Assert.Contains(previewImages, image => (string?)image.Attribute("Stretch") == "Uniform");

        var tooltipSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "WorkbenchPreviewToolTip.cs"));
        Assert.Contains("Stretch = Stretch.Uniform", tooltipSource);
        Assert.Contains("OnPreviewPropertyChanged", tooltipSource);
        Assert.Contains("ToolTip.SetTip(control, CreatePreview(control))", tooltipSource);
        Assert.DoesNotContain("Stretch.UniformToFill", tooltipSource);
    }

    [Fact]
    public void WorkbenchInlineThumbnailsDecodeSmallImagesForSmoothScrolling()
    {
        var (document, ns) = LoadMainWindow();
        var images = document.Descendants(ns + "Image").ToArray();
        var rowThumbnail = images.Single(element => (string?)element.Attribute("Classes") == "inlineObjectPreviewImage");
        var sceneThumbnail = images.Single(element =>
            (string?)element.Attribute("Source") is { } source &&
            source.Contains("SelectedScenePreviewAsset", StringComparison.Ordinal));
        var bindingPreviewImages = images
            .Where(element =>
                (string?)element.Attribute("Source") is { } source &&
                source.Contains("ImagePreviewSource", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains("ConverterParameter=32", (string?)rowThumbnail.Attribute("Source"));
        Assert.Contains("ConverterParameter=48", (string?)sceneThumbnail.Attribute("Source"));
        Assert.NotEmpty(bindingPreviewImages);
        Assert.All(bindingPreviewImages, image => Assert.DoesNotContain("ConverterParameter", (string?)image.Attribute("Source")));
    }

    [Fact]
    public void HeaderStatusSupportsMultilinePreflightMessages()
    {
        var (document, ns) = LoadMainWindow();
        var statusText = document
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding StatusText}");

        Assert.Equal("Wrap", (string?)statusText.Attribute("TextWrapping"));
        Assert.Equal("5", (string?)statusText.Attribute("MaxLines"));
    }

    [Fact]
    public void HeaderPlacesStatusBesideTitleAndCentersContent()
    {
        var (document, ns) = LoadMainWindow();
        var title = document
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "Draft Agent");
        var titleGrid = Assert.IsType<XElement>(title.Parent);
        var statusText = titleGrid
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding StatusText}");
        var projectText = titleGrid
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding WorkspaceProjectMeta}");

        Assert.Equal("Auto,260,*", (string?)titleGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("Center", (string?)titleGrid.Attribute("VerticalAlignment"));
        Assert.Equal("2", (string?)Assert.IsType<XElement>(statusText.Parent).Attribute("Grid.Column"));
        Assert.Same(statusText.Parent, projectText.Parent);
        Assert.True(projectText.NodesBeforeSelf().OfType<XElement>().All(element => element != statusText));
    }

    [Fact]
    public void MainWindowKeepsExportPreflightIssuesOutOfTopChrome()
    {
        var (document, ns) = LoadMainWindow();
        var exportPreflightPanels = document
            .Descendants(ns + "Border")
            .Where(element => HasClass(element, "exportPreflightPanel"))
            .ToArray();

        Assert.Empty(exportPreflightPanels);
        Assert.DoesNotContain(document.Descendants(ns + "ItemsControl"), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding ExportPreflightIssues}");
    }

    [Fact]
    public void ExportMissingVisualDialogDoesNotRenderInternalIds()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));
        var methodStart = source.IndexOf("private async Task<bool> ShowExportMissingVisualsDialogAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<ExportDestinationSelection?> SelectExportDestinationAsync", StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("issue.CompactLocationText", method);
        Assert.Contains("issue.SummaryText", method);
        Assert.Contains("new WrapPanel", method);
        Assert.Contains("ItemWidth = 304", method);
        Assert.DoesNotContain("issue.ReferenceText", method);
        Assert.DoesNotContain("issue.SceneId", method);
        Assert.DoesNotContain("issue.InteractionId", method);
        Assert.DoesNotContain("issue.TaskId", method);
        Assert.DoesNotContain("issue.Message", method);
        Assert.DoesNotContain("issue.InternalMessage", method);
        Assert.DoesNotContain("issue.InternalSuggestedAction", method);
    }

    [Fact]
    public void HeaderExportButtonUsesInteractiveProjectExportCommand()
    {
        var (document, ns) = LoadMainWindow();
        var exportButton = document
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "导出");

        Assert.Equal("{Binding ExportProjectCommand}", (string?)exportButton.Attribute("Command"));
        Assert.NotEqual("{Binding ExportJsonCommand}", (string?)exportButton.Attribute("Command"));
    }

    [Fact]
    public void HeaderProjectActionsSeparateOpenAndSaveCommands()
    {
        var (document, ns) = LoadMainWindow();
        var buttons = document.Descendants(ns + "Button").ToArray();
        var openButton = buttons.Single(element => (string?)element.Attribute("Content") == "打开");
        var saveButton = buttons.Single(element =>
            (string?)element.Attribute("Content") == "保存" &&
            (string?)element.Attribute("Click") == "SaveWorkbenchEdits" &&
            element.Ancestors(ns + "Border").Any(border => (string?)border.Attribute("Classes") == "appHeader"));

        Assert.Equal("ChooseWorkspaceProjectFolder", (string?)openButton.Attribute("Click"));
        Assert.Equal("primary", (string?)saveButton.Attribute("Classes"));
        Assert.Contains(buttons, element =>
            (string?)element.Attribute("Content") == "角色管理" &&
            (string?)element.Attribute("Command") == "{Binding ToggleBindingPanelCommand}");
        var ttsActionButton = buttons.Single(element => (string?)element.Attribute("Content") == "{Binding TtsSynthesisActionText}");
        Assert.Equal("SynthesizeAllVoices", (string?)ttsActionButton.Attribute("Click"));
        Assert.DoesNotContain(buttons, element => (string?)element.Attribute("Content") == "取消语音");
        Assert.DoesNotContain(buttons, element => (string?)element.Attribute("Content") == "{Binding BindingPanelToggleText}");
        Assert.DoesNotContain(buttons, element => (string?)element.Attribute("Content") == "项目");
        Assert.DoesNotContain(buttons, element =>
            (string?)element.Attribute("Content") == "保存" &&
            (string?)element.Attribute("Click") == "SaveWorkbenchEdits" &&
            element.Ancestors(ns + "Border").Any(border => (string?)border.Attribute("Classes") == "sectionHeader"));
    }

    [Fact]
    public void SaveWorkbenchButtonPromptsForProjectFolderBeforeSavingWhenNeeded()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));
        var methodStart = source.IndexOf("private async void SaveWorkbenchEdits", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async void SynthesizeAllVoices", StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("!viewModel.HasWorkspaceProject", method, StringComparison.Ordinal);
        Assert.Contains("PickWorkspaceProjectFolderAsync()", method, StringComparison.Ordinal);
        Assert.Contains("OpenWorkspaceProjectAsync(path)", method, StringComparison.Ordinal);
        Assert.Contains("SaveWorkbenchEditsCommand.ExecuteAsync(null)", method, StringComparison.Ordinal);
        Assert.Contains("请等待合成完成后再保存", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelTtsSynthesisCommand.Execute(null)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AppViewModelDoesNotExposeOtioExportCommand()
    {
        var files = Directory.EnumerateFiles(
            Path.Combine(FindWorkspaceRoot(), "src", "DraftAgent.App", "ViewModels"),
            "*.cs");
        var source = string.Join(
            Environment.NewLine,
            files.Select(file => File.ReadAllText(file)));

        Assert.DoesNotContain("ExportOtioProject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EditingProjectFormat.Otio", source, StringComparison.Ordinal);
        Assert.Contains("WorkbenchExportFormat.Fcpxml", source, StringComparison.Ordinal);
        Assert.Contains("WorkbenchExportFormat.JianyingDraft", source, StringComparison.Ordinal);
        Assert.Contains("WorkbenchExportFormat.AnimateTimeline", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppAnalysisCommandUsesSceneInteractionFlow()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.Ai.cs"));
        var methodStart = source.IndexOf("private async Task AnalyzeScriptAsync()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task OptimizeScriptNarrationAsync()", StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("var sourceScript = ScriptText", method, StringComparison.Ordinal);
        Assert.Contains("AnalyzeWithLlmAsync(sourceScript)", method, StringComparison.Ordinal);
        Assert.Contains("IsCurrentRevision", method, StringComparison.Ordinal);
        Assert.DoesNotContain(".Analyze(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("DraftAnalyzer", method, StringComparison.Ordinal);
        Assert.DoesNotContain("DraftProject", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AiSettingsWindowHighlightsApiKeyFieldWhenKeyValidationFails()
    {
        var (document, ns) = LoadAiSettingsWindow();
        var apiKeyTextBox = document
            .Descendants(ns + "TextBox")
            .Single(element => (string?)element.Attribute("Text") == "{Binding AiApiKey}");

        Assert.Equal("{Binding AiApiKeyBackground}", (string?)apiKeyTextBox.Attribute("Background"));
        Assert.Equal("{Binding AiApiKeyBorderBrush}", (string?)apiKeyTextBox.Attribute("BorderBrush"));
    }

    [Fact]
    public void EmotionEditorWindowUsesSingleToolLayoutWithTwoRowsAndFourColumns()
    {
        var (document, ns) = LoadEmotionEditorWindow();
        var window = document.Root!;

        Assert.Equal("None", (string?)window.Attribute("WindowDecorations"));
        Assert.Equal("False", (string?)window.Attribute("ShowInTaskbar"));
        Assert.Equal("False", (string?)window.Attribute("CanResize"));
        Assert.Equal("400", (string?)window.Attribute("Width"));
        Assert.Equal("248", (string?)window.Attribute("Height"));
        Assert.Equal("390", (string?)window.Attribute("MinWidth"));
        Assert.Equal("240", (string?)window.Attribute("MinHeight"));
        Assert.Equal("#0F1724", (string?)window.Attribute("Background"));
        Assert.Equal("vm:EmotionEditorSessionViewModel", (string?)window.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}DataType"));
        var headerGrid = document.Descendants(ns + "Grid")
            .First(element => (string?)element.Attribute("ColumnDefinitions") == "Auto,*,Auto,Auto");
        Assert.Contains(headerGrid.Descendants(ns + "Button"), button =>
            (string?)button.Attribute("Grid.Column") == "2" &&
            (string?)button.Attribute("Content") == "取消" &&
            (string?)button.Attribute("Click") == "CancelWindow");
        Assert.Contains(headerGrid.Descendants(ns + "Button"), button =>
            (string?)button.Attribute("Grid.Column") == "3" &&
            (string?)button.Attribute("Content") == "保存" &&
            (string?)button.Attribute("Click") == "SaveWindow");

        var uniformGrid = document.Descendants(ns + "UniformGrid").Single();
        Assert.Equal("4", (string?)uniformGrid.Attribute("Rows"));
        Assert.Equal("2", (string?)uniformGrid.Attribute("Columns"));
        Assert.Empty(document.Descendants(ns + "NumericUpDown"));
        Assert.Single(document.Descendants(ns + "Slider"));
        var emotionCell = document.Descendants(ns + "Border")
            .Single(element => (string?)element.Attribute("MinHeight") == "34");
        var cellGrid = emotionCell.Descendants(ns + "Grid")
            .Single(element => (string?)element.Attribute("ColumnDefinitions") == "Auto,Auto,*");
        Assert.Equal("4", (string?)cellGrid.Attribute("ColumnSpacing"));
        var nameText = cellGrid.Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding DisplayName}");
        var valueText = cellGrid.Descendants(ns + "TextBlock")
            .Single(element => ((string?)element.Attribute("Text"))?.Contains("Value", StringComparison.Ordinal) == true);
        Assert.Equal("14", (string?)nameText.Attribute("FontSize"));
        Assert.Equal("13", (string?)valueText.Attribute("FontSize"));
        Assert.Equal("22", (string?)valueText.Attribute("MinWidth"));
        Assert.Equal("88", (string?)document.Descendants(ns + "Slider").Single().Attribute("MinWidth"));

        var sessionSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "ViewModels", "EmotionEditorSessionViewModel.cs"));
        var displayOrderStart = sessionSource.IndexOf("private static readonly IReadOnlyList<string> DisplayOrder", StringComparison.Ordinal);
        Assert.True(displayOrderStart >= 0);
        var displayOrder = sessionSource[displayOrderStart..sessionSource.IndexOf("];", displayOrderStart, StringComparison.Ordinal)];
        Assert.True(displayOrder.IndexOf("\"Happy\"", StringComparison.Ordinal) < displayOrder.IndexOf("\"Angry\"", StringComparison.Ordinal));
        Assert.True(displayOrder.IndexOf("\"Sad\"", StringComparison.Ordinal) < displayOrder.IndexOf("\"Angry\"", StringComparison.Ordinal));
        Assert.True(displayOrder.IndexOf("\"Angry\"", StringComparison.Ordinal) < displayOrder.IndexOf("\"Surprised\"", StringComparison.Ordinal));
        Assert.True(displayOrder.IndexOf("\"Calm\"", StringComparison.Ordinal) < displayOrder.IndexOf("\"Surprised\"", StringComparison.Ordinal));
        Assert.True(displayOrder.IndexOf("\"Surprised\"", StringComparison.Ordinal) < displayOrder.IndexOf("\"Afraid\"", StringComparison.Ordinal));
        Assert.True(displayOrder.IndexOf("\"Melancholic\"", StringComparison.Ordinal) < displayOrder.IndexOf("\"Afraid\"", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenEmotionEditorRetargetsSingleWindowInsteadOfIgnoringRepeatedClicks()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));
        var methodStart = source.IndexOf("private void OpenEmotionEditor", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private (EmotionEditorWindow Window, bool ShouldShow) GetOrCreateEmotionEditorWindow", StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("GetOrCreateEmotionEditorWindow()", method, StringComparison.Ordinal);
        Assert.Contains("new EmotionEditorSessionViewModel(row)", method, StringComparison.Ordinal);
        Assert.Contains("WorkbenchRowContext.Resolve(border.Tag)", method, StringComparison.Ordinal);
        Assert.Contains("PositionToolWindowNear(window, (Control)sender)", method, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateOwnedSingleInstanceWindow<EmotionEditorWindow>()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AppProjectCopiesOnlyTtsWorkflowContent()
    {
        var projectFile = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "DraftAgent.App.csproj"));

        Assert.Contains("IndexTTS-2.json", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("anima.json", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"..\..\tools\", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hf-mirror", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("comfy-hf-mirror", projectFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolProjectsStayTtsOnlyAndReferenceCore()
    {
        var refreshProject = File.ReadAllText(FindSourceFile("tools", "refresh-saved-tts-queue", "refresh-saved-tts-queue.csproj"));
        var verifyProject = File.ReadAllText(FindSourceFile("tools", "verify-real-tts-queue", "verify-real-tts-queue.csproj"));
        var verifySource = File.ReadAllText(FindSourceFile("tools", "verify-real-tts-queue", "Program.cs"));

        Assert.Contains(@"..\..\src\DraftAgent.Core\DraftAgent.Core.csproj", refreshProject, StringComparison.Ordinal);
        Assert.Contains(@"..\..\src\DraftAgent.Core\DraftAgent.Core.csproj", verifyProject, StringComparison.Ordinal);
        Assert.Contains("new ComfyUiOptions(endpoint, workflowPath)", verifySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ComfyUiOptions(endpoint, workflowPath, workflowPath)", verifySource, StringComparison.Ordinal);
        Assert.DoesNotContain("anima.json", verifySource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppProjectPublishesPsdNativeLoader()
    {
        var root = FindWorkspaceRoot();
        var projectFile = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "DraftAgent.App.csproj"));
        var nativeDll = Path.Combine(root, "src", "PsdLayerLoader.Native", "build", "PsdLayerLoader.Native.dll");

        Assert.True(File.Exists(nativeDll), "PSD native loader build output must exist for App packaging.");
        Assert.Contains(@"..\PsdLayerLoader.Native\build\PsdLayerLoader.Native.dll", projectFile, StringComparison.Ordinal);
        Assert.Contains("Link=\"PsdLayerLoader.Native.dll\"", projectFile, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", projectFile, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveSolutionKeepsAvaloniaAppAndCoreBoundaries()
    {
        var root = FindWorkspaceRoot();
        var solution = File.ReadAllText(Path.Combine(root, "DraftAgent.sln"));
        var coreProject = File.ReadAllText(FindSourceFile("src", "DraftAgent.Core", "DraftAgent.Core.csproj"));
        var appProject = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "DraftAgent.App.csproj"));
        var webRuntimeFiles = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file =>
                string.Equals(Path.GetFileName(file), "package.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(file), "tauri.conf.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(file), "tauri.conf.toml", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).StartsWith("vite.config.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Contains(@"src\DraftAgent.App\DraftAgent.App.csproj", solution, StringComparison.Ordinal);
        Assert.Contains(@"src\DraftAgent.Core\DraftAgent.Core.csproj", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Tauri", solution, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("package.json", solution, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Avalonia", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("DraftAgent.App", coreProject, StringComparison.Ordinal);
        Assert.Contains(@"..\DraftAgent.Core\DraftAgent.Core.csproj", appProject, StringComparison.Ordinal);
        Assert.Empty(webRuntimeFiles);
    }

    [Fact]
    public void WorkbenchDeleteButtonsUseConfirmationAndDedicatedRowColumn()
    {
        var (document, ns) = LoadMainWindow();
        var attributes = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        var sceneDeleteButton = document
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "删" &&
                               (string?)element.Attribute("DataContext") == "{Binding Scene}");
        Assert.Equal("ConfirmRemoveScene", (string?)sceneDeleteButton.Attribute("Click"));
        Assert.Null((string?)sceneDeleteButton.Attribute("Command"));

        var rowDeleteButton = document
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "删" &&
                               (string?)element.Attribute("Tag") == "{Binding}" &&
                               (string?)element.Attribute("Click") == "ConfirmRemoveRow");
        Assert.Equal("6", (string?)rowDeleteButton.Attribute("Grid.Column"));
        Assert.Equal("{Binding}", (string?)rowDeleteButton.Attribute("Tag"));
        Assert.Equal("ConfirmRemoveRow", (string?)rowDeleteButton.Attribute("Click"));
        Assert.Null((string?)rowDeleteButton.Attribute("Command"));

        var rowGrid = rowDeleteButton.Ancestors(ns + "Grid").First();
        Assert.Equal("46,Auto,36,66,*,84,38", (string?)rowGrid.Attribute("ColumnDefinitions"));

        Assert.DoesNotContain("WorkbenchRowActionsHost.RemoveCommand", attributes);
    }

    [Fact]
    public void SceneHeaderThumbnailSelectsImageBinding()
    {
        var (document, ns) = LoadMainWindow();

        var sceneThumb = document
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "sceneThumb"));
        var sceneButtons = document
            .Descendants(ns + "Button")
            .Where(element => (string?)element.Attribute("Tag") == "{Binding Scene}")
            .ToArray();

        Assert.Equal("{Binding Scene}", (string?)sceneThumb.Attribute("Tag"));
        Assert.Equal("ChooseSceneImageFileFromThumbnail", (string?)sceneThumb.Attribute("PointerPressed"));
        Assert.Equal("{Binding $parent[Window].DataContext.CanEditWorkbenchStructure}", (string?)sceneThumb.Attribute("IsHitTestVisible"));
        Assert.DoesNotContain(sceneButtons, button => (string?)button.Attribute("Content") == "图");
    }

    [Fact]
    public void SceneHeaderButtonsUseAlignedSquareControls()
    {
        var (document, ns) = LoadMainWindow();
        var appXaml = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "App.axaml"));
        var sceneHeaderGrid = document
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "referenceSceneHeader"))
            .Descendants(ns + "Grid")
            .First(element => ((string?)element.Attribute("ColumnDefinitions"))?.Contains("*,Auto,Auto", StringComparison.Ordinal) == true);
        var sceneHeader = sceneHeaderGrid.Ancestors(ns + "Border")
            .Single(element => HasClass(element, "referenceSceneHeader"));
        var sceneThumb = sceneHeaderGrid
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "sceneThumb"));
        var sceneTitleInput = sceneHeaderGrid
            .Descendants(ns + "TextBox")
            .Single(element => HasClass(element, "sceneTitleInput"));
        var squareButtons = sceneHeaderGrid
            .Descendants(ns + "Button")
            .Where(element => HasClass(element, "sceneSquareButton"))
            .Select(element => (string?)element.Attribute("Content") ?? string.Empty)
            .ToArray();

        Assert.Equal("8,4", (string?)sceneHeader.Attribute("Padding"));
        Assert.Equal("38,38,*,Auto,Auto,38", (string?)sceneHeaderGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("Center", (string?)sceneHeaderGrid.Attribute("VerticalAlignment"));
        Assert.Equal("38", (string?)sceneThumb.Attribute("Width"));
        Assert.Equal("38", (string?)sceneThumb.Attribute("Height"));
        Assert.Equal("NoWrap", (string?)sceneTitleInput.Attribute("TextWrapping"));
        Assert.Equal("1", (string?)sceneTitleInput.Attribute("MaxLines"));
        Assert.Equal("Center", (string?)sceneTitleInput.Attribute("VerticalAlignment"));
        Assert.Equal(["删", "+"], squareButtons);
        Assert.Contains("Style Selector=\"Button.sceneSquareButton\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Width\" Value=\"38\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Height\" Value=\"38\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Button.sceneDragHandle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"TextBox.sceneTitleInput\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Height\" Value=\"38\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"VerticalContentAlignment\" Value=\"Center\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleManagementDialogHeaderExposesSaveAddAndCloseActions()
    {
        var (document, ns) = LoadMainWindow();
        var roleTitle = document
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "角色管理");
        var headerGrid = roleTitle.Ancestors(ns + "Grid").First(element =>
            (string?)element.Attribute("RowDefinitions") == "Auto,Auto" &&
            (string?)element.Attribute("RowSpacing") == "4");
        var roleHint = headerGrid
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "先绑定默认配音，再按需把角色台词、内心或动作路由到角色参考音。");
        Assert.Equal("1", (string?)roleHint.Attribute("Grid.Row"));
        Assert.Equal("Wrap", (string?)roleHint.Attribute("TextWrapping"));
        var titleRow = Assert.IsType<XElement>(roleTitle.Parent);
        Assert.Equal("Auto,*,Auto", (string?)titleRow.Attribute("ColumnDefinitions"));
        Assert.Equal("12", (string?)titleRow.Attribute("ColumnSpacing"));
        var actionButtons = headerGrid
            .Descendants(ns + "Button")
            .Select(element => new
            {
                Content = (string?)element.Attribute("Content"),
                Command = (string?)element.Attribute("Command"),
                Click = (string?)element.Attribute("Click"),
                IsVisible = (string?)element.Attribute("IsVisible"),
                IsEnabled = (string?)element.Attribute("IsEnabled")
            })
            .ToArray();
        var actionRows = headerGrid
            .Descendants(ns + "StackPanel")
            .Where(element => (string?)element.Attribute("Orientation") == "Horizontal" &&
                (string?)element.Attribute("Spacing") == "8")
            .ToArray();
        Assert.Equal(2, actionRows.Length);
        Assert.Equal("1", (string?)actionRows[0].Attribute("Grid.Column"));
        Assert.Equal("Left", (string?)actionRows[0].Attribute("HorizontalAlignment"));
        Assert.Equal("2", (string?)actionRows[1].Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)actionRows[1].Attribute("HorizontalAlignment"));

        Assert.Equal(
            [
                "增加角色",
                "{Binding RoleAllVoiceRouteText}",
                "{Binding RoleDialogueVoiceRouteText}",
                "{Binding RoleSceneNarrationVoiceRouteText}",
                "{Binding RoleThoughtVoiceRouteText}",
                "{Binding RoleActionVoiceRouteText}",
                "保存",
                "关闭"
            ],
            actionButtons.Select(button => button.Content ?? string.Empty).ToArray());
        Assert.Equal("{Binding AddManagedCharacterCommand}", actionButtons[0].Command);
        Assert.Equal("{Binding ToggleRoleAllVoiceRouteCommand}", actionButtons[1].Command);
        Assert.Equal("{Binding ToggleRoleDialogueVoiceRouteCommand}", actionButtons[2].Command);
        Assert.Null(actionButtons[3].Command);
        Assert.Equal("{Binding ToggleRoleThoughtVoiceRouteCommand}", actionButtons[4].Command);
        Assert.Equal("{Binding ToggleRoleActionVoiceRouteCommand}", actionButtons[5].Command);
        Assert.Equal("SaveWorkbenchEdits", actionButtons[6].Click);
        Assert.Equal("{Binding CloseBindingDialogCommand}", actionButtons[7].Command);
        Assert.Equal("{Binding CanEditWorkbenchStructure}", actionButtons[0].IsEnabled);
        Assert.Equal("{Binding CanEditWorkbenchStructure}", actionButtons[1].IsEnabled);
        Assert.Equal("False", actionButtons[2].IsEnabled);
        Assert.Equal("False", actionButtons[3].IsEnabled);
        Assert.Equal("{Binding CanEditWorkbenchStructure}", actionButtons[4].IsEnabled);
        Assert.Equal("{Binding CanEditWorkbenchStructure}", actionButtons[5].IsEnabled);
        Assert.Null(actionButtons[0].IsVisible);
        Assert.Null(actionButtons[1].IsVisible);
        Assert.Equal("{Binding ShowRoleVoiceDetailRouteButtons}", actionButtons[2].IsVisible);
        Assert.Equal("{Binding ShowRoleVoiceDetailRouteButtons}", actionButtons[3].IsVisible);
        Assert.Equal("{Binding ShowRoleVoiceDetailRouteButtons}", actionButtons[4].IsVisible);
        Assert.Equal("{Binding ShowRoleVoiceDetailRouteButtons}", actionButtons[5].IsVisible);
        Assert.Equal([
            "增加角色",
            "{Binding RoleAllVoiceRouteText}",
            "{Binding RoleDialogueVoiceRouteText}",
            "{Binding RoleSceneNarrationVoiceRouteText}",
            "{Binding RoleThoughtVoiceRouteText}",
            "{Binding RoleActionVoiceRouteText}"
        ], actionRows[0]
            .Descendants(ns + "Button")
            .Select(button => (string?)button.Attribute("Content"))
            .Where(content => content is not null)
            .Select(content => content!)
            .ToArray());
        Assert.Equal(["保存", "关闭"], actionRows[1]
            .Descendants(ns + "Button")
            .Select(button => (string?)button.Attribute("Content"))
            .Where(content => content is not null)
            .Select(content => content!)
            .ToArray());
    }

    [Fact]
    public void RoleManagementCardsUseFiveColumnWidth()
    {
        var appXaml = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "App.axaml"));

        Assert.Contains("Style Selector=\"Border.compactBindingCard\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Width\" Value=\"236\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowButtonsUseChineseLabelsAndShowResetAsIconOnly()
    {
        var (document, ns) = LoadMainWindow();
        var buttonTexts = document
            .Descendants(ns + "Button")
            .Select(element => (string?)element.Attribute("Content"))
            .Where(text => !string.IsNullOrWhiteSpace(text) && !text.StartsWith("{Binding", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain("新增人物", buttonTexts);
        Assert.Contains("新增场景", buttonTexts);
        Assert.Contains("PSD图层AI匹配", buttonTexts);
        Assert.Contains("PSD图层模板设置", buttonTexts);
        Assert.Contains("↶", buttonTexts);
        var addSceneIndex = Array.IndexOf(buttonTexts, "新增场景");
        var matchIndex = Array.IndexOf(buttonTexts, "PSD图层AI匹配");
        var expandIndex = Array.IndexOf(buttonTexts, "展开");
        Assert.True(addSceneIndex >= 0 && matchIndex == addSceneIndex + 1);
        Assert.True(expandIndex >= 0 && matchIndex < expandIndex);
        Assert.DoesNotContain("Add Character", buttonTexts);
        Assert.DoesNotContain("Add Scene", buttonTexts);
        Assert.DoesNotContain("Revert", buttonTexts);
        Assert.DoesNotContain(buttonTexts, text => text is not null && text.Any(char.IsAsciiLetter) && !text.Contains("PSD", StringComparison.Ordinal));

        var resetButton = Assert.Single(
            document.Descendants(ns + "Button"),
            element => string.Equals((string?)element.Attribute("Classes"), "roundResetButton", StringComparison.Ordinal));
        Assert.Equal("↶", (string?)resetButton.Attribute("Content"));
        Assert.Equal("{Binding RevertWorkbenchCommand}", (string?)resetButton.Attribute("Command"));
    }

    [Fact]
    public void PsdLayerAiMatchingUsesStructuredSettingsDialog()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));
        var viewModelSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.cs"));
        var bindingViewModelSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "ViewModels", "MainWindowViewModel.Binding.cs"));
        var settingsViewModelSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "ViewModels", "PsdLayerMatchSettingsViewModel.cs"));
        var selectionServiceSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "ViewModels", "PsdLayerAiSelectionService.cs"));
        var loaderWindowSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Psd", "Views", "PsdLayerLoaderWindow.axaml"));

        Assert.Contains("ConfirmPsdLayerMatchSettingsAsync", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ShowPsdLayerMatchSettingsDialogAsync", source, StringComparison.Ordinal);
        Assert.Contains("PSD 图层模板设置", source, StringComparison.Ordinal);
        Assert.Contains("PSD 图层组", source, StringComparison.Ordinal);
        Assert.Contains("模板结构", source, StringComparison.Ordinal);
        Assert.Contains("ApplySelectedGroupToTemplate", source, StringComparison.Ordinal);
        Assert.Contains("FaceModeText", source, StringComparison.Ordinal);
        Assert.Contains("左手", settingsViewModelSource, StringComparison.Ordinal);
        Assert.Contains("右手", settingsViewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FootGroup", settingsViewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PsdLayerTemplateRole.Foot", settingsViewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePsdLayerGroupPicker", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateUnused", source, StringComparison.Ordinal);
        Assert.Contains("修改模板", loaderWindowSource, StringComparison.Ordinal);
        Assert.Contains("MatchRoleText", loaderWindowSource, StringComparison.Ordinal);
        Assert.Contains("LayerMatchTemplateSummary", loaderWindowSource, StringComparison.Ordinal);
        Assert.Contains("psd_layer_selection_v1", selectionServiceSource, StringComparison.Ordinal);
        Assert.Contains("固定词汇分类器", selectionServiceSource, StringComparison.Ordinal);
        Assert.Contains("只根据输入 text", selectionServiceSource, StringComparison.Ordinal);
        Assert.Contains("不得新增字段", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("不得使用角色名、场景、情绪、上一行、下一行或常识脑补", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("text 没有明确证据时填空字符串", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("否则填空字符串", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"expression\"", selectionServiceSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"action\"", selectionServiceSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"reason\"", selectionServiceSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("判断依据", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("理由", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"scene\"]", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"sourceItemType\"]", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"emotion\"]", selectionServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("整体表情", selectionServiceSource, StringComparison.Ordinal);
        Assert.Contains("PsdLayerAiRowsPerRequest = 12", bindingViewModelSource, StringComparison.Ordinal);
        Assert.Contains("Chunk(PsdLayerAiRowsPerRequest)", bindingViewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmPsdExpressionMappingAsync", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowPsdExpressionMappingDialogAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("添加规则", source, StringComparison.Ordinal);
        Assert.DoesNotContain("图层/图层组关键词", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkbenchVoiceCellShowsPendingTextUntilSynthesizedThenOnlyActions()
    {
        var (document, ns) = LoadMainWindow();
        var voiceState = document
            .Descendants(ns + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding Row.TtsStatusText}");

        Assert.Equal("{Binding !Row.HasSynthesizedVoice}", (string?)voiceState.Attribute("IsVisible"));

        var actionsHost = voiceState.Ancestors(ns + "Grid").First();
        Assert.Equal("True", AttributeValue(actionsHost, "WorkbenchRowActionsHost.IsEnabled"));
        Assert.Equal(
            "{Binding $parent[Window].DataContext.PreviewRowVoiceCommand}",
            AttributeValue(actionsHost, "WorkbenchRowActionsHost.PreviewCommand"));
        Assert.Equal(
            "{Binding $parent[Window].DataContext.RegenerateRowVoiceCommand}",
            AttributeValue(actionsHost, "WorkbenchRowActionsHost.RegenerateCommand"));

        var actionsHostSource = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "WorkbenchRowActionsHost.cs"));
        Assert.Contains("VoiceDurationText", actionsHostSource, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 24", actionsHostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneHeaderAddLineButtonDefaultsToNarration()
    {
        var (document, ns) = LoadMainWindow();
        var addLineButton = document
            .Descendants(ns + "Button")
            .Single(element => (string?)element.Attribute("Content") == "+" &&
                               (string?)element.Attribute("Command") == "{Binding $parent[Window].DataContext.AddNarrationRowCommand}");

        Assert.Equal(
            "{Binding $parent[Window].DataContext.AddNarrationRowCommand}",
            (string?)addLineButton.Attribute("Command"));
        Assert.DoesNotContain(
            document.Descendants(ns + "Button"),
            element => (string?)element.Attribute("Content") == "+ 旁白");
    }

    [Fact]
    public void WorkbenchDragPreviewUsesEnteredTargetsAndThrottledPointerRefresh()
    {
        var (document, ns) = LoadMainWindow();
        var window = document.Root!;
        var eventRow = document
            .Descendants(ns + "Border")
            .Single(element => HasClass(element, "eventRow"));
        var eventTables = document
            .Descendants(ns + "Border")
            .Where(element => HasClass(element, "eventTable"))
            .ToArray();
        var referenceScene = document
            .Descendants(ns + "Border")
            .First(element => HasClass(element, "referenceScene"));

        Assert.Equal("MoveWorkbenchDragGhost", (string?)window.Attribute("PointerMoved"));
        Assert.Null((string?)window.Attribute("PointerCaptureLost"));
        Assert.Equal("FinishWorkbenchDrag", (string?)window.Attribute("PointerReleased"));
        Assert.Equal("PreviewWorkbenchDragTarget", (string?)eventRow.Attribute("PointerEntered"));
        Assert.All(eventTables, eventTable =>
            Assert.Equal("PreviewWorkbenchDragTarget", (string?)eventTable.Attribute("PointerEntered")));
        Assert.Equal("PreviewWorkbenchDragTarget", (string?)referenceScene.Attribute("PointerEntered"));
    }

    [Fact]
    public void WorkbenchDragCodeBehindDoesNotCaptureOrDirectlyHitTestPointerMovement()
    {
        var text = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));

        Assert.DoesNotContain("Pointer.Capture", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InputHitTest", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetVisualsAt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewWorkbenchDrag(object?", text, StringComparison.Ordinal);
        Assert.Contains("MoveWorkbenchDragGhost", text, StringComparison.Ordinal);
        Assert.Contains("_workbenchDragVisualOverlay.Move", text, StringComparison.Ordinal);
        Assert.Contains("RefreshTargetFromPointer", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkbenchFlatListDoesNotUseNestedWheelForwarding()
    {
        var text = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));

        Assert.DoesNotContain("InputElement.PointerWheelChangedEvent", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollWorkbenchSceneListFromRows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFromWorkbenchRowList", text, StringComparison.Ordinal);
        Assert.DoesNotContain("control.Classes.Contains(\"workbenchRowList\")", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[WorkbenchScroll]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("drift=", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkbenchDragPreviewUsesMarkerOnlySortableListStyles()
    {
        var text = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "App.axaml"));

        Assert.Contains("Border.eventRow.dragSourceActive", text, StringComparison.Ordinal);
        Assert.Contains("Border.referenceScene.dragSourceActive", text, StringComparison.Ordinal);
        var rowSourceStyleStart = text.IndexOf("Border.eventRow.dragSourceActive", StringComparison.Ordinal);
        var rowSourceStyleEnd = text.IndexOf("</Style>", rowSourceStyleStart, StringComparison.Ordinal);
        var rowSourceStyle = text[rowSourceStyleStart..rowSourceStyleEnd];
        Assert.Contains("Opacity\" Value=\"0\"", rowSourceStyle, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"Transparent\"", rowSourceStyle, StringComparison.Ordinal);
        var sceneSourceStyleStart = text.IndexOf("Border.referenceScene.dragSourceActive", StringComparison.Ordinal);
        var sceneSourceStyleEnd = text.IndexOf("</Style>", sceneSourceStyleStart, StringComparison.Ordinal);
        var sceneSourceStyle = text[sceneSourceStyleStart..sceneSourceStyleEnd];
        Assert.Contains("Opacity\" Value=\"0\"", sceneSourceStyle, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"Transparent\"", sceneSourceStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.eventRow.dropBefore", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.eventRow.dropAfter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.referenceScene.dropBefore", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.referenceScene.dropAfter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.eventTable.dropSceneEnd", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dragSourceHidden", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dragDisplaceUp", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dragDisplaceDown", text, StringComparison.Ordinal);
        Assert.DoesNotContain("workbenchRowList", text, StringComparison.Ordinal);
        Assert.DoesNotContain("translateY(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TransformOperationsTransition", text, StringComparison.Ordinal);
        var mainWindow = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml"));
        Assert.Contains("Classes.dragSourceActive=\"{Binding Row.IsDragSourceActive}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Classes.dragSourceActive=\"{Binding Scene.IsDragSourceActive}\"", mainWindow, StringComparison.Ordinal);
        var codeBehind = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml.cs"));
        var coordinator = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "WorkbenchDragInteractionCoordinator.cs"));
        var dropIndicator = File.ReadAllText(FindSourceFile("src", "DraftAgent.App", "Views", "WorkbenchDropIndicatorState.cs"));
        Assert.Contains("AttachDropIndicatorOverlay(WorkbenchDragOverlay)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetLeft(line, origin.X)", dropIndicator, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetTop(line, y)", dropIndicator, StringComparison.Ordinal);
        Assert.Contains("Height = IndicatorThickness", dropIndicator, StringComparison.Ordinal);
        Assert.Contains("Width = target.Bounds.Width", dropIndicator, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkbenchLayoutAnimation", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkbenchLayoutAnimation", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewWorkbenchDragWithLayoutAnimation", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewWorkbenchDragFromPointerWithLayoutAnimation", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("LayoutAnimationScope", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture(this", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateFromAsync", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshActiveDragSource", codeBehind, StringComparison.Ordinal);
    }

    private static (XDocument Document, XNamespace Namespace) LoadMainWindow()
    {
        return (
            XDocument.Load(FindSourceFile("src", "DraftAgent.App", "Views", "MainWindow.axaml")),
            "https://github.com/avaloniaui");
    }

    private static (XDocument Document, XNamespace Namespace) LoadAiSettingsWindow()
    {
        return (
            XDocument.Load(FindSourceFile("src", "DraftAgent.App", "Views", "AiSettingsWindow.axaml")),
            "https://github.com/avaloniaui");
    }

    private static (XDocument Document, XNamespace Namespace) LoadEmotionEditorWindow()
    {
        return (
            XDocument.Load(FindSourceFile("src", "DraftAgent.App", "Views", "EmotionEditorWindow.axaml")),
            "https://github.com/avaloniaui");
    }

    private static XElement FindListBoxByClass(XDocument document, XNamespace ns, string className)
    {
        return FindListBoxesByClass(document, ns, className).Single();
    }

    private static IEnumerable<XElement> FindListBoxesByClass(XDocument document, XNamespace ns, string className)
    {
        return document
            .Descendants(ns + "ListBox")
            .Where(element => HasClass(element, className));
    }

    private static XElement DataTemplateByKey(XDocument document, XNamespace ns, string key)
    {
        return document
            .Descendants(ns + "DataTemplate")
            .Single(element => AttributeValue(element, "Key") == key);
    }

    private static XElement DataTemplateByDataType(XElement owner, XNamespace ns, string dataType)
    {
        return owner
            .Descendants(ns + "DataTemplate")
            .Single(element => (string?)element.Attribute("DataType") == dataType);
    }

    private static void AssertUsesVirtualizingStackPanel(XNamespace ns, XElement listBox)
    {
        var itemsPanel = listBox
            .Elements(ns + "ListBox.ItemsPanel")
            .Single()
            .Element(ns + "ItemsPanelTemplate");

        Assert.NotNull(itemsPanel);
        Assert.Contains(itemsPanel.Elements(), element => element.Name == ns + "VirtualizingStackPanel");
    }

    private static bool HasClass(XElement element, string className)
    {
        var classes = ((string?)element.Attribute("Classes"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return classes?.Contains(className, StringComparer.Ordinal) == true;
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;
    }

    private static string FindSourceFile(params string[] segments)
    {
        return Path.Combine(FindWorkspaceRoot(), Path.Combine(segments));
    }

    private static string FindWorkspaceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DraftAgent.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate DraftAgent.sln.");
    }
}

