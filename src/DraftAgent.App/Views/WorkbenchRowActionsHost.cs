using System;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public static class WorkbenchRowActionsHost
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchRowActionsHostMarker, Grid, bool>("IsEnabled");

    public static readonly AttachedProperty<ICommand?> PreviewCommandProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchRowActionsHostMarker, Grid, ICommand?>("PreviewCommand");

    public static readonly AttachedProperty<ICommand?> RegenerateCommandProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchRowActionsHostMarker, Grid, ICommand?>("RegenerateCommand");

    private static readonly AttachedProperty<ActionsState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchRowActionsHostMarker, Grid, ActionsState?>("State");

    static WorkbenchRowActionsHost()
    {
        IsEnabledProperty.Changed.AddClassHandler<Grid>(OnIsEnabledChanged);
        PreviewCommandProperty.Changed.AddClassHandler<Grid>(OnCommandChanged);
        RegenerateCommandProperty.Changed.AddClassHandler<Grid>(OnCommandChanged);
    }

    public static bool GetIsEnabled(Grid element)
    {
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Grid element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static ICommand? GetPreviewCommand(Grid element)
    {
        return element.GetValue(PreviewCommandProperty);
    }

    public static void SetPreviewCommand(Grid element, ICommand? value)
    {
        element.SetValue(PreviewCommandProperty, value);
    }

    public static ICommand? GetRegenerateCommand(Grid element)
    {
        return element.GetValue(RegenerateCommandProperty);
    }

    public static void SetRegenerateCommand(Grid element, ICommand? value)
    {
        element.SetValue(RegenerateCommandProperty, value);
    }

    private static void OnIsEnabledChanged(Grid host, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            var state = new ActionsState(host);
            host.SetValue(StateProperty, state);
            state.Attach();
        }
        else
        {
            host.GetValue(StateProperty)?.Detach();
            host.ClearValue(StateProperty);
        }
    }

    private static void OnCommandChanged(Grid host, AvaloniaPropertyChangedEventArgs args)
    {
        host.GetValue(StateProperty)?.RefreshCommands();
    }

    private sealed class ActionsState
    {
        private readonly Grid _host;
        private AnalysisResultRowViewModel? _row;
        private StackPanel? _actions;
        private Button? _previewButton;
        private Button? _regenerateButton;
        private TextBlock? _durationText;

        public ActionsState(Grid host)
        {
            _host = host;
        }

        public void Attach()
        {
            _host.DataContextChanged += OnDataContextChanged;
            _host.AttachedToVisualTree += OnAttachedToVisualTree;
            _host.DetachedFromVisualTree += OnDetachedFromVisualTree;
            SetRow(WorkbenchRowContext.Resolve(_host.DataContext));
        }

        public void Detach()
        {
            _host.DataContextChanged -= OnDataContextChanged;
            _host.AttachedToVisualTree -= OnAttachedToVisualTree;
            _host.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            SetRow(null);
            RemoveActions();
        }

        public void RefreshCommands()
        {
            ApplyCommands();
        }

        private void OnDataContextChanged(object? sender, EventArgs args)
        {
            SetRow(WorkbenchRowContext.Resolve(_host.DataContext));
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            SetRow(WorkbenchRowContext.Resolve(_host.DataContext));
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            SetRow(null);
            RemoveActions();
        }

        private void SetRow(AnalysisResultRowViewModel? row)
        {
            if (ReferenceEquals(_row, row))
            {
                RefreshActions();
                return;
            }

            if (_row is not null)
            {
                _row.PropertyChanged -= OnRowPropertyChanged;
            }

            _row = row;

            if (_row is not null)
            {
                _row.PropertyChanged += OnRowPropertyChanged;
            }

            RefreshActions();
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(AnalysisResultRowViewModel.IsSelected))
            {
                RefreshActions();
            }
            else if (args.PropertyName is nameof(AnalysisResultRowViewModel.HasSynthesizedVoice))
            {
                RefreshActions();
            }
            else if (args.PropertyName is nameof(AnalysisResultRowViewModel.CanPreviewVoice)
                     or nameof(AnalysisResultRowViewModel.CanRegenerateVoice))
            {
                RefreshButtonStates();
            }
            else if (args.PropertyName is nameof(AnalysisResultRowViewModel.VoiceDurationText))
            {
                RefreshDurationText();
            }
        }

        private void RefreshActions()
        {
            if (_row?.HasSynthesizedVoice is true)
            {
                EnsureActions();
                RefreshButtonStates();
                RefreshDurationText();
                ApplyCommands();
            }
            else
            {
                RemoveActions();
            }
        }

        private void EnsureActions()
        {
            if (_actions is not null)
            {
                return;
            }

            _previewButton = CreateButton("听", "compact");
            _regenerateButton = CreateButton("重", "compact");
            _durationText = new TextBlock
            {
                MinWidth = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 11
            };
            _durationText.Classes.Add("muted");

            _actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _previewButton,
                    _regenerateButton,
                    _durationText
                }
            };
            _actions.Classes.Add("rowActions");
            _host.Children.Add(_actions);
        }

        private void RemoveActions()
        {
            if (_actions is null)
            {
                return;
            }

            _host.Children.Remove(_actions);
            _actions = null;
            _previewButton = null;
            _regenerateButton = null;
            _durationText = null;
        }

        private void RefreshButtonStates()
        {
            if (_row is null)
            {
                return;
            }

            if (_previewButton is not null)
            {
                _previewButton.IsEnabled = _row.CanPreviewVoice;
            }

            if (_regenerateButton is not null)
            {
                _regenerateButton.IsEnabled = _row.CanRegenerateVoice;
            }
        }

        private void ApplyCommands()
        {
            if (_row is null)
            {
                return;
            }

            if (_previewButton is not null)
            {
                _previewButton.Command = GetPreviewCommand(_host);
                _previewButton.CommandParameter = _row;
            }

            if (_regenerateButton is not null)
            {
                _regenerateButton.Command = GetRegenerateCommand(_host);
                _regenerateButton.CommandParameter = _row;
            }
        }

        private void RefreshDurationText()
        {
            if (_durationText is null)
            {
                return;
            }

            _durationText.Text = _row?.VoiceDurationText ?? string.Empty;
        }

        private static Button CreateButton(string content, string classes)
        {
            var button = new Button
            {
                Content = content
            };

            foreach (var @class in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                button.Classes.Add(@class);
            }

            return button;
        }
    }

    private sealed class WorkbenchRowActionsHostMarker;
}
