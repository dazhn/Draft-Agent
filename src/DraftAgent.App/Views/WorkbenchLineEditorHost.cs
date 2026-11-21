using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public static class WorkbenchLineEditorHost
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchLineEditorHostMarker, Grid, bool>("IsEnabled");

    private static readonly AttachedProperty<EditorState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchLineEditorHostMarker, Grid, EditorState?>("State");

    static WorkbenchLineEditorHost()
    {
        IsEnabledProperty.Changed.AddClassHandler<Grid>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(Grid element)
    {
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Grid element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Grid host, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            var state = new EditorState(host);
            host.SetValue(StateProperty, state);
            state.Attach();
        }
        else
        {
            host.GetValue(StateProperty)?.Detach();
            host.ClearValue(StateProperty);
        }
    }

    private sealed class EditorState
    {
        private readonly Grid _host;
        private AnalysisResultRowViewModel? _row;
        private TextBox? _editor;
        private bool _updatingEditor;

        public EditorState(Grid host)
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
            RemoveEditor();
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
            RemoveEditor();
        }

        private void SetRow(AnalysisResultRowViewModel? row)
        {
            if (ReferenceEquals(_row, row))
            {
                RefreshEditor();
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

            RefreshEditor();
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(AnalysisResultRowViewModel.IsSelected)
                or nameof(AnalysisResultRowViewModel.CanEditTextAndEmotion)
                or nameof(AnalysisResultRowViewModel.IsLineEditorActive))
            {
                RefreshEditor();
            }
            else if (args.PropertyName is nameof(AnalysisResultRowViewModel.LineText))
            {
                SyncText();
            }
        }

        private void RefreshEditor()
        {
            if (_row?.IsLineEditorActive is true)
            {
                EnsureEditor();
                SyncText();
            }
            else
            {
                RemoveEditor();
            }
        }

        private void EnsureEditor()
        {
            if (_editor is not null)
            {
                return;
            }

            _editor = new TextBox
            {
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                AcceptsReturn = true,
                MaxLines = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _editor.Classes.Add("lineEditingHost");
            _editor.Classes.Add("lineEditor");
            _editor.TextChanged += OnEditorTextChanged;
            _host.Children.Add(_editor);
        }

        private void RemoveEditor()
        {
            if (_editor is null)
            {
                return;
            }

            _editor.TextChanged -= OnEditorTextChanged;
            _host.Children.Remove(_editor);
            _editor = null;
        }

        private void SyncText()
        {
            if (_editor is null || _row is null)
            {
                return;
            }

            if (string.Equals(_editor.Text, _row.LineText, StringComparison.Ordinal))
            {
                return;
            }

            _updatingEditor = true;
            _editor.Text = _row.LineText;
            _updatingEditor = false;
        }

        private void OnEditorTextChanged(object? sender, TextChangedEventArgs args)
        {
            if (_updatingEditor || _row is null)
            {
                return;
            }

            _row.LineText = _editor?.Text ?? string.Empty;
        }
    }

    private sealed class WorkbenchLineEditorHostMarker;
}
