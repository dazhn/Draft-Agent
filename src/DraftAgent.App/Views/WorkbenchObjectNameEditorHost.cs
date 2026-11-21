using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public static class WorkbenchObjectNameEditorHost
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchObjectNameEditorHostMarker, Grid, bool>("IsEnabled");

    public static readonly AttachedProperty<IEnumerable?> CharacterNameOptionsProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchObjectNameEditorHostMarker, Grid, IEnumerable?>("CharacterNameOptions");

    private static readonly AttachedProperty<EditorState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchObjectNameEditorHostMarker, Grid, EditorState?>("State");

    static WorkbenchObjectNameEditorHost()
    {
        IsEnabledProperty.Changed.AddClassHandler<Grid>(OnIsEnabledChanged);
        CharacterNameOptionsProperty.Changed.AddClassHandler<Grid>(OnCharacterNameOptionsChanged);
    }

    public static bool GetIsEnabled(Grid element)
    {
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Grid element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static IEnumerable? GetCharacterNameOptions(Grid element)
    {
        return element.GetValue(CharacterNameOptionsProperty);
    }

    public static void SetCharacterNameOptions(Grid element, IEnumerable? value)
    {
        element.SetValue(CharacterNameOptionsProperty, value);
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

    private static void OnCharacterNameOptionsChanged(Grid host, AvaloniaPropertyChangedEventArgs args)
    {
        host.GetValue(StateProperty)?.UpdateItemsSource(args.NewValue as IEnumerable);
    }

    private sealed class EditorState
    {
        private readonly Grid _host;
        private AnalysisResultRowViewModel? _row;
        private ComboBox? _editor;
        private IEnumerable? _baseOptions;
        private INotifyCollectionChanged? _baseOptionsCollection;
        private IReadOnlyList<string> _editorOptions = [];
        private bool _updatingEditor;

        public EditorState(Grid host)
        {
            _host = host;
        }

        public void Attach()
        {
            SetBaseOptions(GetCharacterNameOptions(_host));
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
            SetBaseOptions(null);
            SetRow(null);
            RemoveEditor();
        }

        public void UpdateItemsSource(IEnumerable? options)
        {
            SetBaseOptions(options);
            if (_editor is not null)
            {
                SyncItemsSource();
                SyncSelectedItem();
            }
        }

        private void SetBaseOptions(IEnumerable? options)
        {
            if (ReferenceEquals(_baseOptions, options))
            {
                return;
            }

            if (_baseOptionsCollection is not null)
            {
                _baseOptionsCollection.CollectionChanged -= OnBaseOptionsCollectionChanged;
            }

            _baseOptions = options;
            _baseOptionsCollection = options as INotifyCollectionChanged;
            if (_baseOptionsCollection is not null)
            {
                _baseOptionsCollection.CollectionChanged += OnBaseOptionsCollectionChanged;
            }
        }

        private void OnBaseOptionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (_editor is null)
            {
                return;
            }

            SyncItemsSource();
            SyncSelectedItem();
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
            if (args.PropertyName is nameof(AnalysisResultRowViewModel.IsSelected))
            {
                RefreshEditor();
            }
            else if (args.PropertyName is nameof(AnalysisResultRowViewModel.SubjectName))
            {
                SyncItemsSource();
                SyncSelectedItem();
            }
        }

        private void RefreshEditor()
        {
            if (_row?.IsSelected is true)
            {
                EnsureEditor();
                SyncItemsSource();
                SyncSelectedItem();
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

            _editor = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 30,
                MinHeight = 30,
                MaxHeight = 30
            };
            _editor.Classes.Add("objectEditingHost");
            _editor.Classes.Add("objectNameSelect");
            _editor.SelectionChanged += OnEditorSelectionChanged;
            _host.Children.Add(_editor);
        }

        private void SyncItemsSource()
        {
            if (_editor is null)
            {
                return;
            }

            _updatingEditor = true;
            _editorOptions = BuildEditorOptions(_baseOptions ?? GetCharacterNameOptions(_host), _row?.SubjectName);
            _editor.ItemsSource = _editorOptions;
            _updatingEditor = false;
        }

        private void RemoveEditor()
        {
            if (_editor is null)
            {
                return;
            }

            _editor.SelectionChanged -= OnEditorSelectionChanged;
            _host.Children.Remove(_editor);
            _editor = null;
            _editorOptions = [];
        }

        private void SyncSelectedItem()
        {
            if (_editor is null || _row is null)
            {
                return;
            }

            _updatingEditor = true;
            _editor.SelectedItem = FindEditorOption(_row.SubjectName);
            _updatingEditor = false;
        }

        private void OnEditorSelectionChanged(object? sender, SelectionChangedEventArgs args)
        {
            if (_updatingEditor || _row is null)
            {
                return;
            }

            _row.SubjectName = _editor?.SelectedItem as string;
        }

        private string? FindEditorOption(string? subjectName)
        {
            if (string.IsNullOrWhiteSpace(subjectName))
            {
                return null;
            }

            return _editorOptions.FirstOrDefault(option => string.Equals(option, subjectName, StringComparison.Ordinal))
                ?? subjectName;
        }

        private static IReadOnlyList<string> BuildEditorOptions(IEnumerable? options, string? currentName)
        {
            var result = new List<string>();
            AddUnique(result, options);
            AddUnique(result, currentName);
            return result;
        }

        private static void AddUnique(ICollection<string> result, IEnumerable? options)
        {
            if (options is null)
            {
                return;
            }

            foreach (var option in options)
            {
                AddUnique(result, option as string ?? option?.ToString());
            }
        }

        private static void AddUnique(ICollection<string> result, string? option)
        {
            var name = option?.Trim();
            if (string.IsNullOrWhiteSpace(name) ||
                result.Any(existing => string.Equals(existing, name, StringComparison.Ordinal)))
            {
                return;
            }

            result.Add(name);
        }
    }

    private sealed class WorkbenchObjectNameEditorHostMarker;
}
