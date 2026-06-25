using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Styling;
using DraftAgent.App.Psd.ViewModels;
using DraftAgent.App.Psd.Views;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public partial class MainWindow : Window
{
    private readonly WorkbenchDragInteractionCoordinator _workbenchDragInteraction = new();
    private readonly WorkbenchDragVisualOverlay _workbenchDragVisualOverlay = new();
    private readonly Dictionary<Type, Window> _ownedSingleInstanceWindows = new();
    private readonly HashSet<string> _activePickerKeys = new(StringComparer.Ordinal);
    private EmotionEditorWindow? _emotionEditorWindow;
    private bool _isExportMissingVisualsDialogOpen;
    private bool _isExportDestinationDialogOpen;
    private bool _isRegenerateCompletedTtsDialogOpen;
    private bool _isDeleteConfirmationDialogOpen;
    private bool _isRoleMergeConfirmationDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        _workbenchDragVisualOverlay.Attach(WorkbenchDragOverlay);
        _workbenchDragInteraction.AttachDropIndicatorOverlay(WorkbenchDragOverlay);
        DataContextChanged += (_, _) => ConfigureViewModelInteractions();
        AddHandler(
            InputElement.KeyDownEvent,
            CancelWorkbenchDragFromKeyboard,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerPressedEvent,
            StartWorkbenchSceneDragFromHandle,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void ConfigureViewModelInteractions()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ConfirmRegenerateCompletedTtsAsync = ShowRegenerateCompletedTtsDialogAsync;
            viewModel.ConfirmExportMissingVisualsAsync = ShowExportMissingVisualsDialogAsync;
            viewModel.ConfirmRoleMergeAsync = ShowRoleMergeConfirmationAsync;
            viewModel.ConfirmPsdLayerMatchSettingsAsync = ShowPsdLayerMatchSettingsDialogAsync;
            viewModel.ConfirmResetWorkbenchAsync = ShowResetWorkbenchConfirmationAsync;
            viewModel.SelectExportDestinationAsync = SelectExportDestinationAsync;
        }
    }

    private async Task<bool> ShowPsdLayerMatchSettingsDialogAsync(PsdLayerMatchSettingsViewModel session)
    {
        var dialog = new Window
        {
            Title = "PSD 图层模板设置",
            Width = 820,
            Height = 560,
            MinWidth = 720,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Background = new SolidColorBrush(Color.Parse("#111722")),
            DataContext = session
        };

        var cancelButton = CreateDialogButton(dialog, "取消", false, "#1B2230", "#2C3546");
        var saveButton = CreateDialogButton(dialog, "保存", true, "#1D4ED8", "#2563EB");
        dialog.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 14,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "配置 PSD 图层模板",
                                FontSize = 20,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                            },
                            new TextBlock
                            {
                                Text = session.SummaryText,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
                            }
                        }
                    },
                    new Grid
                    {
                        [Grid.RowProperty] = 1,
                        ColumnDefinitions = new ColumnDefinitions("*,*"),
                        ColumnSpacing = 14,
                        Children =
                        {
                            CreatePsdGroupTreePanel(session),
                            CreatePsdTemplateTreePanel(session)
                        }
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 2,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            cancelButton,
                            saveButton
                        }
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool?>(this) == true;
    }

    private static Border CreatePsdGroupTreePanel(PsdLayerMatchSettingsViewModel session)
    {
        return CreateTreePanel(
            "PSD 图层组",
            "只显示 PSD 图层组，不显示 PSD 文件名和具体图层。",
            CreatePsdGroupNodeList(session.PsdGroupTree, session));
    }

    private static Border CreatePsdTemplateTreePanel(PsdLayerMatchSettingsViewModel session)
    {
        var panel = CreateTreePanel(
            "模板结构",
            "点击右侧语义节点，把左侧选中的 PSD 图层组绑定过来。",
            CreateTemplateNodeList(session.TemplateTree, session));
        panel[Grid.ColumnProperty] = 1;
        return panel;
    }

    private static Border CreateTreePanel(
        string title,
        string subtitle,
        Control content)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#151D2A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2C3546")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = subtitle,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
                    },
                    new ScrollViewer
                    {
                        [Grid.RowProperty] = 2,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = content
                    }
                }
            }
        };
    }

    private static StackPanel CreatePsdGroupNodeList(
        IEnumerable<PsdLayerGroupTreeNodeViewModel> nodes,
        PsdLayerMatchSettingsViewModel session)
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (var node in nodes)
        {
            stack.Children.Add(CreatePsdGroupNodeButton(node, session));
            if (node.Children.Count > 0)
            {
                stack.Children.Add(CreatePsdGroupNodeList(node.Children, session));
            }
        }

        return stack;
    }

    private static Button CreatePsdGroupNodeButton(
        PsdLayerGroupTreeNodeViewModel node,
        PsdLayerMatchSettingsViewModel session)
    {
        var button = new Button
        {
            DataContext = node,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6),
            Background = new SolidColorBrush(Color.Parse("#1B2230")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2C3546")),
            Tag = node,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 6,
                Children =
                {
                    new Border { Width = node.IndentWidth },
                    new TextBlock
                    {
                        [Grid.ColumnProperty] = 1,
                        Text = node.DisplayName,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    new TextBlock
                    {
                        [Grid.ColumnProperty] = 2,
                        Text = node.CountSummary,
                        Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
                    }
                }
            }
        };
        button.Bind(InputElement.IsEnabledProperty, new Binding(nameof(node.CanBind)));
        button.Click += (_, _) => session.SelectPsdGroup(node);
        return button;
    }

    private static StackPanel CreateTemplateNodeList(
        IEnumerable<PsdLayerTemplateTreeNodeViewModel> nodes,
        PsdLayerMatchSettingsViewModel session,
        bool includeStatus = true)
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (var node in nodes)
        {
            stack.Children.Add(CreateTemplateNodeButton(node, session));
            if (node.Children.Count > 0)
            {
                stack.Children.Add(CreateTemplateNodeList(node.Children, session, includeStatus: false));
            }
        }

        if (!includeStatus)
        {
            return stack;
        }

        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#58A6FF"))
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(session.StatusText)));
        stack.Children.Add(status);

        var mode = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
        };
        mode.Bind(TextBlock.TextProperty, new Binding(nameof(session.FaceModeText)));
        stack.Children.Add(mode);
        return stack;
    }

    private static Button CreateTemplateNodeButton(
        PsdLayerTemplateTreeNodeViewModel node,
        PsdLayerMatchSettingsViewModel session)
    {
        var bindingText = new TextBlock
        {
            [Grid.ColumnProperty] = 2,
            Foreground = new SolidColorBrush(Color.Parse("#58A6FF")),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        bindingText.Bind(TextBlock.TextProperty, new Binding(nameof(node.BindingText)));

        var button = new Button
        {
            DataContext = node,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6),
            Background = new SolidColorBrush(Color.Parse("#1B2230")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2C3546")),
            Tag = node,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 6,
                Children =
                {
                    new Border { Width = node.IndentWidth },
                    new TextBlock
                    {
                        [Grid.ColumnProperty] = 1,
                        Text = node.DisplayName,
                        FontWeight = node.Depth == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    bindingText
                }
            }
        };
        button.Bind(InputElement.IsEnabledProperty, new Binding(nameof(node.CanApply)));
        button.Click += (_, _) => session.ApplySelectedGroupToTemplate(node);
        return button;
    }

    private async Task<bool> ShowExportMissingVisualsDialogAsync(ExportMissingVisualConfirmation confirmation)
    {
        if (_isExportMissingVisualsDialogOpen)
        {
            return false;
        }

        _isExportMissingVisualsDialogOpen = true;

        var dialog = new Window
        {
            Title = "导出问题",
            Width = 680,
            Height = 430,
            MinWidth = 680,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#111722"))
        };
        var issueList = new ItemsControl
        {
            ItemsSource = confirmation.Issues
        };
        issueList.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 304
        });
        issueList.ItemTemplate = new FuncDataTemplate<ExportPreflightIssueViewModel>((issue, _) =>
        {
            var locateButton = new Button
            {
                [Grid.ColumnProperty] = 2,
                Content = "定位",
                Classes = { "compact" },
                IsEnabled = issue.CanLocate,
                Padding = new Thickness(9, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            locateButton.Click += (_, _) =>
            {
                confirmation.LocateIssue?.Invoke(issue);
                dialog.Close(false);
            };

            return new Border
            {
                Width = 304,
                Padding = new Thickness(0, 0, 12, 8),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("26,*,56"),
                    ColumnSpacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = issue.IndexText,
                            Classes = { "muted", "small" },
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new StackPanel
                        {
                            [Grid.ColumnProperty] = 1,
                            Spacing = 0,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = issue.CompactLocationText,
                                    FontSize = 14,
                                    FontWeight = FontWeight.SemiBold,
                                    Foreground = new SolidColorBrush(Color.Parse("#E7EAF0")),
                                    TextTrimming = TextTrimming.CharacterEllipsis
                                },
                                new TextBlock
                                {
                                    Text = issue.SummaryText,
                                    Classes = { "muted", "small" },
                                    TextTrimming = TextTrimming.CharacterEllipsis
                                }
                            }
                        },
                        locateButton
                    }
                }
            };
        });

        dialog.Content = new Border
        {
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = $"发现 {confirmation.Issues.Count} 个导出问题",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = confirmation.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 12),
                        Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
                    },
                    new ScrollViewer
                    {
                        [Grid.RowProperty] = 2,
                        MaxHeight = 240,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = issueList
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 3,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 16, 0, 0),
                        Spacing = 10,
                        Children =
                        {
                            CreateDialogButton(dialog, "取消", false, "#1B2230", "#2C3546"),
                            CreateDialogButton(dialog, "继续导出", true, "#3B82F6", "#60A5FA")
                        }
                    }
                }
            }
        };

        try
        {
            var result = await dialog.ShowDialog<bool?>(this);
            return result == true;
        }
        finally
        {
            _isExportMissingVisualsDialogOpen = false;
        }
    }

    private async Task<ExportDestinationSelection?> SelectExportDestinationAsync(ExportDestinationRequest request)
    {
        if (_isExportDestinationDialogOpen)
        {
            return null;
        }

        _isExportDestinationDialogOpen = true;
        try
        {
            var format = await ShowExportFormatDialogAsync(request);
            if (format is null)
            {
                return null;
            }

            if (format == WorkbenchExportFormat.JianyingDraft || format == WorkbenchExportFormat.AnimateTimeline)
            {
                var isAnimate = format == WorkbenchExportFormat.AnimateTimeline;
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = isAnimate ? "选择 Animate 音频字幕导出文件夹" : "选择剪映草稿保存文件夹",
                    AllowMultiple = false,
                    SuggestedStartLocation = await TryGetFolderAsync(request.WorkspaceExportDirectory)
                });
                var folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                return string.IsNullOrWhiteSpace(folder)
                    ? null
                    : new ExportDestinationSelection(format.Value, Path.Combine(folder, $"{request.SuggestedName}_{(isAnimate ? "Animate音频字幕" : "剪映草稿")}"));
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存 FCPXML",
                SuggestedFileName = $"{request.SuggestedName}.fcpxml",
                SuggestedStartLocation = await TryGetFolderAsync(request.WorkspaceExportDirectory),
                FileTypeChoices =
                [
                    new FilePickerFileType("FCPXML")
                    {
                        Patterns = ["*.fcpxml"]
                    }
                ]
            });
            var path = file?.TryGetLocalPath();
            return string.IsNullOrWhiteSpace(path)
                ? null
                : new ExportDestinationSelection(format.Value, path);
        }
        finally
        {
            _isExportDestinationDialogOpen = false;
        }
    }

    private async Task<WorkbenchExportFormat?> ShowExportFormatDialogAsync(ExportDestinationRequest request)
    {
        var dialog = new Window
        {
            Title = "选择导出格式",
            Width = 420,
            Height = 246,
            MinWidth = 420,
            MinHeight = 246,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#111722"))
        };

        var selected = request.Format;
        dialog.Content = new Border
        {
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "选择导出格式",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 1,
                        Margin = new Thickness(0, 16, 0, 18),
                        Spacing = 8,
                        Children =
                        {
                            new RadioButton
                            {
                                Content = "FCPXML",
                                IsChecked = selected == WorkbenchExportFormat.Fcpxml,
                                Foreground = Brushes.White
                            },
                            new RadioButton
                            {
                                Content = "剪映草稿",
                                IsChecked = selected == WorkbenchExportFormat.JianyingDraft,
                                Foreground = Brushes.White
                            },
                            new RadioButton
                            {
                                Content = "Animate 音频字幕",
                                IsChecked = selected == WorkbenchExportFormat.AnimateTimeline,
                                Foreground = Brushes.White
                            }
                        }
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 2,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            CreateDialogButton(dialog, "取消", false, "#1B2230", "#2C3546"),
                            CreateDialogButton(dialog, "下一步", true, "#3B82F6", "#60A5FA")
                        }
                    }
                }
            }
        };

        var panel = ((Border)dialog.Content).Child as Grid;
        var options = panel?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<RadioButton>().ToArray() ?? [];
        if (options.Length == 3)
        {
            options[0].Click += (_, _) => selected = WorkbenchExportFormat.Fcpxml;
            options[1].Click += (_, _) => selected = WorkbenchExportFormat.JianyingDraft;
            options[2].Click += (_, _) => selected = WorkbenchExportFormat.AnimateTimeline;
        }

        var result = await dialog.ShowDialog<bool?>(this);
        return result == true ? selected : null;
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : await StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ShowRegenerateCompletedTtsDialogAsync(TtsRegenerationConfirmation confirmation)
    {
        if (_isRegenerateCompletedTtsDialogOpen)
        {
            return false;
        }

        _isRegenerateCompletedTtsDialogOpen = true;
        var dialog = new Window
        {
            Title = "语音合成",
            Width = 420,
            Height = 210,
            MinWidth = 420,
            MinHeight = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#111722"))
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "检测到部分语音已经完成",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = confirmation.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 14, 0, 18),
                        Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 2,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            CreateDialogButton(dialog, "继续未完成", false, "#1B2230", "#2C3546"),
                            CreateDialogButton(dialog, "重新生成", true, "#3B82F6", "#60A5FA")
                        }
                    }
                }
            }
        };

        try
        {
            var result = await dialog.ShowDialog<bool?>(this);
            return result == true;
        }
        finally
        {
            _isRegenerateCompletedTtsDialogOpen = false;
        }
    }

    private static Button CreateDialogButton(Window dialog, string text, bool result, string background, string border)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7),
            Background = new SolidColorBrush(Color.Parse(background)),
            BorderBrush = new SolidColorBrush(Color.Parse(border)),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(6)
        };
        button.Click += (_, _) => dialog.Close(result);
        return button;
    }

    private async Task<bool> ShowDeleteConfirmationAsync(
        string heading,
        string message,
        string title = "确认删除",
        string confirmText = "删除")
    {
        if (_isDeleteConfirmationDialogOpen)
        {
            return false;
        }

        _isDeleteConfirmationDialogOpen = true;
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 190,
            MinWidth = 400,
            MinHeight = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#111722"))
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = heading,
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 14, 0, 18),
                        Foreground = new SolidColorBrush(Color.Parse("#AAB4C3"))
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 2,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            CreateDialogButton(dialog, "取消", false, "#1B2230", "#2C3546"),
                            CreateDialogButton(dialog, confirmText, true, "#B91C1C", "#EF4444")
                        }
                    }
                }
            }
        };

        try
        {
            var result = await dialog.ShowDialog<bool?>(this);
            return result == true;
        }
        finally
        {
            _isDeleteConfirmationDialogOpen = false;
        }
    }

    private Task<bool> ShowResetWorkbenchConfirmationAsync()
    {
        return ShowDeleteConfirmationAsync(
            "重置所有修改？",
            "将丢弃当前工作台中尚未保存到项目状态的编辑，并恢复到当前项目状态。此操作不会清空原始 AI JSON。",
            "重置工作台",
            "重置");
    }

    private async Task<bool> ShowRoleMergeConfirmationAsync(RoleMergeConfirmation confirmation)
    {
        if (_isRoleMergeConfirmationDialogOpen)
        {
            return false;
        }

        _isRoleMergeConfirmationDialogOpen = true;
        var dialog = new Window
        {
            Width = 460,
            Height = 210,
            MinWidth = 460,
            MinHeight = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Background = new SolidColorBrush(Color.Parse("#111722"))
        };
        var cancelButton = CreateDialogButton(dialog, "取消", false, "#1B2230", "#2C3546");
        var mergeButton = CreateDialogButton(dialog, "合并", true, "#B91C1C", "#EF4444");
        dialog.Content = new Border
        {
            Padding = new Thickness(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#263044")),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 18,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "合并角色",
                                FontSize = 20,
                                FontWeight = FontWeight.SemiBold,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = new SolidColorBrush(Color.Parse("#E7EAF0"))
                            },
                            new StackPanel
                            {
                                [Grid.ColumnProperty] = 1,
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Spacing = 10,
                                Children =
                                {
                                    cancelButton,
                                    mergeButton
                                }
                            }
                        }
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 1,
                        Spacing = 10,
                        Children =
                        {
                            new WrapPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Children =
                                {
                                    CreateDialogText("将 "),
                                    CreateMergeChip(confirmation.SourceRoleName, "#7F1D1D", "#FCA5A5"),
                                    CreateDialogText(" 合并到 "),
                                    CreateMergeChip(confirmation.TargetRoleName, "#14532D", "#86EFAC"),
                                    CreateDialogText("。")
                                }
                            },
                            new WrapPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Children =
                                {
                                    CreateMergeChip($"{confirmation.AffectedDialogueCount} 行对话", "#7F1D1D", "#FCA5A5"),
                                    CreateDialogText(" 会改到目标角色下，源角色卡片会移除。")
                                }
                            }
                        }
                    }
                }
            }
        };

        try
        {
            var result = await dialog.ShowDialog<bool?>(this);
            return result == true;
        }
        finally
        {
            _isRoleMergeConfirmationDialogOpen = false;
        }
    }

    private static TextBlock CreateDialogText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#AAB4C3")),
            Margin = new Thickness(0, 2)
        };
    }

    private static Border CreateMergeChip(string text, string border, string foreground)
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse(border)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 2),
            Margin = new Thickness(2, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.Parse(foreground))
            }
        };
    }

    private async void ConfirmRemoveScene(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { DataContext: SceneTableViewModel scene })
        {
            return;
        }

        if (await ShowDeleteConfirmationAsync("删除场景？", $"将删除 {scene.SceneLabel} 以及其中 {scene.Rows.Count} 行。"))
        {
            viewModel.RemoveSceneCommand.Execute(scene);
        }
    }

    private async void ConfirmRemoveRow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button button ||
            WorkbenchRowContext.Resolve(button.Tag) is not { } row)
        {
            return;
        }

        if (await ShowDeleteConfirmationAsync("删除行？", $"将删除第 {row.OrderLabel} 行。"))
        {
            viewModel.RemoveInteractionRowCommand.Execute(row);
        }
    }

    private async void BindingTargetNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await CommitBindingTargetNameEditAsync(sender);
        e.Handled = true;
    }

    private void BeginBindingTargetNameEdit(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { CanEditWorkbenchStructure: false } viewModel)
        {
            viewModel.StatusText = "语音合成中已锁定角色管理。请等待合成完成，或停止合成后再修改角色。";
            e.Handled = true;
            return;
        }

        if (sender is TextBlock { Tag: BindingTargetViewModel target })
        {
            target.BeginDisplayNameEdit();
            e.Handled = true;
        }
    }

    private async void ToggleBindingTargetNameEdit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { Tag: BindingTargetViewModel target })
        {
            return;
        }

        if (!viewModel.CanEditWorkbenchStructure)
        {
            viewModel.StatusText = "语音合成中已锁定角色管理。请等待合成完成，或停止合成后再修改角色。";
            return;
        }

        if (!target.IsEditingDisplayName)
        {
            target.BeginDisplayNameEdit();
            return;
        }

        await viewModel.RenameBindingTargetCommand.ExecuteAsync(target);
    }

    private async Task CommitBindingTargetNameEditAsync(object? sender)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            sender is TextBox { Tag: BindingTargetViewModel target })
        {
            await viewModel.RenameBindingTargetCommand.ExecuteAsync(target);
        }
    }

    private void OpenAiSettingsWindow(object? sender, RoutedEventArgs e)
    {
        if (ActivateOwnedSingleInstanceWindow<AiSettingsWindow>())
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BeginAiSettingsEdit();
        }

        var window = new AiSettingsWindow
        {
            DataContext = DataContext
        };
        ShowOwnedSingleInstanceWindow(window);
    }

    private async void ChooseWorkspaceProjectFolder(object? sender, RoutedEventArgs e)
    {
        const string pickerKey = nameof(ChooseWorkspaceProjectFolder);
        if (!TryBeginPicker(pickerKey))
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            EndPicker(pickerKey);
            return;
        }

        try
        {
            var path = await PickWorkspaceProjectFolderAsync();
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.OpenWorkspaceProjectAsync(path);
            }
        }
        finally
        {
            EndPicker(pickerKey);
        }
    }

    private async void SaveWorkbenchEdits(object? sender, RoutedEventArgs e)
    {
        const string pickerKey = nameof(SaveWorkbenchEdits);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsTtsSynthesisRunning)
        {
            viewModel.StatusText = "语音合成正在运行。请等待合成完成后再保存，或点击“停止合成”取消后再保存。";
            return;
        }

        if (!viewModel.HasWorkspaceProject)
        {
            if (!TryBeginPicker(pickerKey))
            {
                return;
            }

            try
            {
                var path = await PickWorkspaceProjectFolderAsync();
                if (string.IsNullOrWhiteSpace(path))
                {
                    viewModel.StatusText = "保存已取消：未选择项目文件夹。";
                    return;
                }

                await viewModel.OpenWorkspaceProjectAsync(path);
            }
            finally
            {
                EndPicker(pickerKey);
            }
        }

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
    }

    private async void SynthesizeAllVoices(object? sender, RoutedEventArgs e)
    {
        const string pickerKey = nameof(SynthesizeAllVoices);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsTtsSynthesisRunning)
        {
            viewModel.CancelTtsSynthesisCommand.Execute(null);
            return;
        }

        if (!viewModel.HasWorkspaceProject)
        {
            if (!TryBeginPicker(pickerKey))
            {
                return;
            }

            try
            {
                viewModel.StatusText = "语音合成前请先选择或新建项目保存目录。";
                var path = await PickWorkspaceProjectFolderAsync();
                if (string.IsNullOrWhiteSpace(path))
                {
                    viewModel.StatusText = "语音合成已取消：未选择项目保存目录。";
                    viewModel.ComfySubmitStatusText = "未提交：请先选择项目保存目录。";
                    return;
                }

                await viewModel.OpenWorkspaceProjectAsync(path);
            }
            finally
            {
                EndPicker(pickerKey);
            }
        }

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);
    }

    private async Task<string?> PickWorkspaceProjectFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择或新建工作台项目文件夹",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void ChooseVoiceFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { Tag: BindingTargetViewModel target })
        {
            return;
        }

        viewModel.ShowCustomVoiceReferenceInstruction(target);
    }

    private async void VoiceReferenceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not ComboBox { DataContext: BindingTargetViewModel target } comboBox ||
            comboBox.SelectedItem is not string selected)
        {
            return;
        }

        if (!string.Equals(selected, MainWindowViewModel.CustomVoiceReferenceOption, StringComparison.Ordinal))
        {
            await viewModel.SetVoiceReferenceSelectionAsync(target, selected);
            return;
        }

        var previous = e.RemovedItems.Count > 0
            ? e.RemovedItems[0] as string
            : target.VoiceAsset;
        comboBox.SelectedItem = string.IsNullOrWhiteSpace(previous) ||
            string.Equals(previous, MainWindowViewModel.CustomVoiceReferenceOption, StringComparison.Ordinal)
                ? target.VoiceAsset
                : previous;
        viewModel.ShowCustomVoiceReferenceInstruction(target);
    }

    private async void ChooseImageFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { Tag: BindingTargetViewModel target })
        {
            return;
        }

        await ChooseImageFileAsync(viewModel, target);
    }

    private async void ChooseSceneImageFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { Tag: SceneTableViewModel scene })
        {
            return;
        }

        await ChooseSceneImageFileAsync(viewModel, scene);
    }

    private async void ChooseSceneImageFileFromThumbnail(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { Tag: SceneTableViewModel scene })
        {
            return;
        }

        e.Handled = true;
        await ChooseSceneImageFileAsync(viewModel, scene);
    }

    private async Task ChooseSceneImageFileAsync(MainWindowViewModel viewModel, SceneTableViewModel scene)
    {
        var target = viewModel.SceneBindingTargets.FirstOrDefault(candidate =>
            candidate.Kind == BindingTargetKind.Scene &&
            candidate.TargetId == scene.SceneId);
        if (target is null)
        {
            viewModel.RefreshBindingTargetForSceneImageSelection(scene);
            target = viewModel.SceneBindingTargets.FirstOrDefault(candidate =>
                candidate.Kind == BindingTargetKind.Scene &&
                candidate.TargetId == scene.SceneId);
        }

        if (target is null)
        {
            viewModel.StatusText = $"未找到场景图片绑定目标：{scene.SceneName}";
            return;
        }

        await ChooseImageFileAsync(viewModel, target);
    }

    private async void ChooseImageFileFromPreview(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Border { Tag: BindingTargetViewModel target } ||
            !target.SupportsImage)
        {
            return;
        }

        await ChooseImageFileAsync(viewModel, target);
    }

    private async Task ChooseImageFileAsync(MainWindowViewModel viewModel, BindingTargetViewModel target)
    {
        var pickerKey = $"{nameof(ChooseImageFileAsync)}:{target.TargetId}";
        if (!TryBeginPicker(pickerKey))
        {
            return;
        }

        var fileTypes = target.SupportsPsd
            ? new[]
            {
                new FilePickerFileType("图片或 PSD")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.psd", "*.psb"]
                },
                new FilePickerFileType("Photoshop PSD")
                {
                    Patterns = ["*.psd", "*.psb"]
                },
                FilePickerFileTypes.ImageAll
            }
            : new[]
            {
                new FilePickerFileType("图片")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"]
                },
                FilePickerFileTypes.ImageAll
            };
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择图片",
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            });

            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.SetImageBindingAsync(target, path);
            }
        }
        finally
        {
            EndPicker(pickerKey);
        }
    }

    private async void ChoosePsdFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { Tag: BindingTargetViewModel target })
        {
            return;
        }

        var request = viewModel.BuildPsdLayerPreviewRequest(target);
        if (request is null)
        {
            return;
        }

        var previewViewModel = new PsdLayerLoaderViewModel();
        previewViewModel.EnableLayerMatchConfiguration();
        previewViewModel.ApplyLayerMatchTemplate(viewModel.ResolvePsdLayerMatchConfiguration(target));
        var window = new PsdLayerLoaderWindow
        {
            Title = request.Title,
            DataContext = previewViewModel,
            ConfigureLayerMatchAsync = async () =>
            {
                if (await viewModel.ConfigurePsdLayerMatchSettingsAndReportAsync(target))
                {
                    previewViewModel.ApplyLayerMatchTemplate(viewModel.ResolvePsdLayerMatchConfiguration(target));
                }
            }
        };
        window.Show(this);
        await previewViewModel.LoadPsdAsync(request.PsdPath);
        previewViewModel.EnableLayerMatchConfiguration();
        previewViewModel.ApplyLayerMatchTemplate(viewModel.ResolvePsdLayerMatchConfiguration(target));
    }

    private async void ConfigurePsdLayerMatch(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var target = sender is Button { Tag: BindingTargetViewModel taggedTarget }
            ? taggedTarget
            : viewModel.CharacterPsdLayerMatchTarget;

        await viewModel.ConfigurePsdLayerMatchSettingsAsync(target);
    }

    private async void ReselectPsdLayers(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            sender is Button { Tag: BindingTargetViewModel target })
        {
            await viewModel.GeneratePsdImageBindingFromExistingSourceAsync(target);
        }
    }

    private void SelectWorkbenchRow(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Border border ||
            WorkbenchRowContext.Resolve(border.Tag) is not { } row)
        {
            return;
        }

        viewModel.SelectRowCommand.Execute(row);
    }

    private async void ToggleWorkbenchPsdLayersFromThumbnail(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            DataContext is not MainWindowViewModel viewModel ||
            sender is not Control control ||
            WorkbenchRowContext.Resolve(control.Tag) is not { } row)
        {
            return;
        }

        viewModel.SelectRowCommand.Execute(row);
        e.Handled = true;
        if (row.UsesCharacterVisualPreview)
        {
            _ = await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(row);
            return;
        }

        if (!row.SourceItemType.Equals("narration", StringComparison.OrdinalIgnoreCase) &&
            !row.SourceItemType.Equals("effects", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ChooseWorkbenchRowImageFileAsync(viewModel, row);
    }

    private async Task ChooseWorkbenchRowImageFileAsync(MainWindowViewModel viewModel, AnalysisResultRowViewModel row)
    {
        var pickerKey = $"{nameof(ChooseWorkbenchRowImageFileAsync)}:{row.InteractionId}";
        if (!TryBeginPicker(pickerKey))
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择行图片",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"]
                    },
                    FilePickerFileTypes.ImageAll
                ]
            });

            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.SetWorkbenchRowImageBindingAsync(row, path);
            }
        }
        finally
        {
            EndPicker(pickerKey);
        }
    }

    private void ToggleWorkbenchRowSourceType(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { Tag: AnalysisResultRowViewModel row })
        {
            return;
        }

        e.Handled = true;
        viewModel.ToggleSelectedRowSourceTypeCommand.Execute(row);
    }

    private void StartWorkbenchRowDrag(object? sender, PointerPressedEventArgs e)
    {
        var isLeftButtonPressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.CanDragWorkbench &&
            WorkbenchDropTargetResolver.TryResolveRowDragSource(sender, out var row) &&
            isLeftButtonPressed)
        {
            viewModel.SelectRowCommand.Execute(row);
            var sourceVisual = e.Source as Visual ?? sender as Visual;
            _workbenchDragInteraction.BeginRowDrag(viewModel, row, sourceVisual, this);
            _workbenchDragVisualOverlay.BeginRowDrag(row, e.GetPosition(this));
            e.Handled = true;
        }
        else
        {
        }
    }

    private void StartWorkbenchSceneDrag(object? sender, PointerPressedEventArgs e)
    {
        StartWorkbenchSceneDragCore(sender, e, explicitHandle: false);
    }

    private void StartWorkbenchSceneDragCore(object? sender, PointerPressedEventArgs e, bool explicitHandle)
    {
        if (DataContext is MainWindowViewModel { CanDragWorkbench: false })
        {
            e.Handled = true;
            return;
        }

        if (HasActiveWorkbenchDrag())
        {
            return;
        }

        if (WorkbenchDropTargetResolver.IsRowDragTargetSource(e.Source as Visual))
        {
            return;
        }

        var isLeftButtonPressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (DataContext is MainWindowViewModel viewModel &&
            WorkbenchDropTargetResolver.TryResolveSceneDragSource(sender, out var scene) &&
            (explicitHandle || isLeftButtonPressed))
        {
            var sourceVisual = e.Source as Visual ?? sender as Visual;
            _workbenchDragInteraction.BeginSceneDrag(viewModel, scene, sourceVisual, this);
            _workbenchDragVisualOverlay.BeginSceneDrag(scene, e.GetPosition(this));
            e.Handled = true;
        }
        else if (explicitHandle)
        {
        }
    }

    private void StartWorkbenchSceneDragFromHandle(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        if (WorkbenchDropTargetResolver.TryResolveSceneDragHandle(source, out var handle, out var isDedicatedHandle))
        {
            StartWorkbenchSceneDragCore(handle, e, explicitHandle: isDedicatedHandle);
            return;
        }

        if (WorkbenchDropTargetResolver.TryFindSceneDragTargetOutsideRow(source, out var scene, out var sceneVisual))
        {
        }
    }

    private void PreviewWorkbenchDragTarget(object? sender, PointerEventArgs e)
    {
        if (!HasActiveWorkbenchDrag())
        {
            return;
        }

        if (DataContext is MainWindowViewModel { CanDragWorkbench: false })
        {
            EndWorkbenchDrag(false);
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndWorkbenchDrag(true);
            e.Handled = true;
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            EndWorkbenchDrag(true);
            e.Handled = true;
            return;
        }

        var hit = sender as Visual ?? e.Source as Visual;
        PreviewWorkbenchDrag(viewModel, hit);

        e.Handled = true;
    }

    private void MoveWorkbenchDragGhost(object? sender, PointerEventArgs e)
    {
        if (!HasActiveWorkbenchDrag())
        {
            return;
        }

        if (DataContext is MainWindowViewModel { CanDragWorkbench: false })
        {
            EndWorkbenchDrag(false);
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndWorkbenchDrag(true);
            e.Handled = true;
            return;
        }

        var pointerPosition = e.GetPosition(this);
        _workbenchDragVisualOverlay.Move(pointerPosition);
        PreviewWorkbenchDragFromPointer(pointerPosition);
        e.Handled = true;
    }

    private void FinishWorkbenchDrag(object? sender, PointerReleasedEventArgs e)
    {
        if (!HasActiveWorkbenchDrag())
        {
            return;
        }

        EndWorkbenchDrag(true);
        e.Handled = true;
    }

    private void CancelWorkbenchDragFromKeyboard(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !HasActiveWorkbenchDrag())
        {
            return;
        }

        EndWorkbenchDrag(false);
        e.Handled = true;
    }

    private void EndWorkbenchDrag(bool complete)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _workbenchDragInteraction.EndAndReset(viewModel, complete);
        }

        _workbenchDragVisualOverlay.End(complete);
    }

    private void PreviewWorkbenchDrag(MainWindowViewModel viewModel, Visual? hit)
    {
        _workbenchDragInteraction.Preview(viewModel, hit, this);
    }

    private void PreviewWorkbenchDragFromPointer(Point pointerPosition)
    {
        _workbenchDragInteraction.RefreshTargetFromPointer(
            DataContext,
            this,
            pointerPosition,
            DateTimeOffset.UtcNow);
    }

    private bool HasActiveWorkbenchDrag()
    {
        return _workbenchDragInteraction.IsActive;
    }

    private void OpenEmotionEditor(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border ||
            WorkbenchRowContext.Resolve(border.Tag) is not { } row ||
            !row.IsEmotionEditable)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            if (!row.CanEditTextAndEmotion)
            {
                return;
            }

            viewModel.EndWorkbenchEditCoalescing();
            viewModel.SelectRowCommand.Execute(row);
        }

        var (window, shouldShow) = GetOrCreateEmotionEditorWindow();
        window.DataContext = new EmotionEditorSessionViewModel(row);
        PositionToolWindowNear(window, (Control)sender);
        if (shouldShow)
        {
            window.Show(this);
        }

        window.Activate();
        e.Handled = true;
    }

    private (EmotionEditorWindow Window, bool ShouldShow) GetOrCreateEmotionEditorWindow()
    {
        if (_emotionEditorWindow is { IsVisible: true } existing)
        {
            return (existing, false);
        }

        var window = new EmotionEditorWindow();
        _emotionEditorWindow = window;
        if (DataContext is MainWindowViewModel ownerViewModel)
        {
            window.Closed += (_, _) => ownerViewModel.EndWorkbenchEditCoalescing();
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_emotionEditorWindow, window))
            {
                _emotionEditorWindow = null;
            }
        };
        return (window, true);
    }

    private void PositionToolWindowNear(Window window, Control anchor)
    {
        var screenPoint = anchor.PointToScreen(new Point(0, anchor.Bounds.Height));
        var x = screenPoint.X;
        var y = screenPoint.Y + 6;

        if (Screens.ScreenFromVisual(this) is { WorkingArea: var area })
        {
            var width = (int)Math.Ceiling(window.Width);
            var height = (int)Math.Ceiling(window.Height);
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
        }

        window.Position = new PixelPoint(x, y);
    }

    private bool TryBeginPicker(string key)
    {
        return _activePickerKeys.Add(key);
    }

    private void EndPicker(string key)
    {
        _activePickerKeys.Remove(key);
    }

    private bool ActivateOwnedSingleInstanceWindow<TWindow>()
        where TWindow : Window
    {
        if (!_ownedSingleInstanceWindows.TryGetValue(typeof(TWindow), out var existing))
        {
            return false;
        }

        if (!existing.IsVisible)
        {
            _ownedSingleInstanceWindows.Remove(typeof(TWindow));
            return false;
        }

        existing.Activate();
        return true;
    }

    private void ShowOwnedSingleInstanceWindow(Window window)
    {
        var key = window.GetType();
        _ownedSingleInstanceWindows[key] = window;
        window.Closed += (_, _) => _ownedSingleInstanceWindows.Remove(key);
        window.Show(this);
    }
}

internal static class DynamicDialogBindingExtensions
{
    public static TControl BindTwoWay<TControl>(
        this TControl control,
        AvaloniaProperty property,
        object source,
        string path)
        where TControl : AvaloniaObject
    {
        control.Bind(
            property,
            new Binding(path)
            {
                Source = source,
                Mode = BindingMode.TwoWay
            });
        return control;
    }
}

