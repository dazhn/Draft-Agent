using Avalonia.Controls;
using DraftAgent.App.ViewModels;
using DraftAgent.App.Views;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchObjectNameEditorHostTests
{
    [Fact]
    public void EnablingHostCreatesEditorForAlreadySelectedRowOnStartup()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.IsSelected = true;
        var host = new Grid
        {
            DataContext = row
        };

        WorkbenchObjectNameEditorHost.SetCharacterNameOptions(host, new[] { "旁白", "Chen" });
        WorkbenchObjectNameEditorHost.SetIsEnabled(host, true);

        var editor = Assert.Single(host.Children.OfType<ComboBox>());
        Assert.Equal("Chen", editor.SelectedItem);
        Assert.Equal(30, editor.Height);
        Assert.Equal(30, editor.MinHeight);
        Assert.Equal(30, editor.MaxHeight);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, editor.VerticalAlignment);
    }

    [Fact]
    public void RefreshingOptionsKeepsSelectedRowNameEvenWhenCurrentNameIsNotInOptions()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.IsSelected = true;
        var host = new Grid
        {
            DataContext = row
        };

        WorkbenchObjectNameEditorHost.SetCharacterNameOptions(host, Array.Empty<string>());
        WorkbenchObjectNameEditorHost.SetIsEnabled(host, true);
        WorkbenchObjectNameEditorHost.SetCharacterNameOptions(host, new[] { "旁白", "Lin" });

        var editor = Assert.Single(host.Children.OfType<ComboBox>());
        var options = Assert.IsAssignableFrom<IEnumerable<string>>(editor.ItemsSource);
        Assert.Equal("Chen", editor.SelectedItem);
        Assert.Contains("Chen", options);
        Assert.Equal("Chen", row.SubjectName);
    }

    [Fact]
    public void CollectionChangedOptionsRefreshSelectedEditorItems()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.IsSelected = true;
        var options = new System.Collections.ObjectModel.ObservableCollection<string>(["旁白", "Chen"]);
        var host = new Grid
        {
            DataContext = row
        };

        WorkbenchObjectNameEditorHost.SetCharacterNameOptions(host, options);
        WorkbenchObjectNameEditorHost.SetIsEnabled(host, true);
        var editor = Assert.Single(host.Children.OfType<ComboBox>());

        row.SubjectName = "Lin";
        options.Remove("Chen");
        options.Add("Lin");

        var editorOptions = Assert.IsAssignableFrom<IEnumerable<string>>(editor.ItemsSource);
        Assert.Equal("Lin", editor.SelectedItem);
        Assert.DoesNotContain("Chen", editorOptions);
        Assert.Contains("Lin", editorOptions);
    }
}

