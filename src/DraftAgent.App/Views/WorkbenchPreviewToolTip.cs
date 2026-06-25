using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DraftAgent.App;

namespace DraftAgent.App.Views;

public static class WorkbenchPreviewToolTip
{
    private const string PlaceholderTip = "预览";

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchPreviewToolTipMarker, Control, bool>("IsEnabled");

    public static readonly AttachedProperty<string?> ImagePathProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchPreviewToolTipMarker, Control, string?>("ImagePath");

    public static readonly AttachedProperty<string?> LabelProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchPreviewToolTipMarker, Control, string?>("Label");

    public static readonly AttachedProperty<bool> HasImageProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchPreviewToolTipMarker, Control, bool>("HasImage");

    public static readonly AttachedProperty<double> PreviewWidthProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchPreviewToolTipMarker, Control, double>("PreviewWidth", 260);

    public static readonly AttachedProperty<double> PreviewHeightProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchPreviewToolTipMarker, Control, double>("PreviewHeight", 180);

    static WorkbenchPreviewToolTip()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        ImagePathProperty.Changed.AddClassHandler<Control>(OnPreviewPropertyChanged);
        LabelProperty.Changed.AddClassHandler<Control>(OnPreviewPropertyChanged);
        HasImageProperty.Changed.AddClassHandler<Control>(OnPreviewPropertyChanged);
        PreviewWidthProperty.Changed.AddClassHandler<Control>(OnPreviewPropertyChanged);
        PreviewHeightProperty.Changed.AddClassHandler<Control>(OnPreviewPropertyChanged);
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(OnToolTipIsOpenChanged);
    }

    public static bool GetIsEnabled(Control element)
    {
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Control element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static string? GetImagePath(Control element)
    {
        return element.GetValue(ImagePathProperty);
    }

    public static void SetImagePath(Control element, string? value)
    {
        element.SetValue(ImagePathProperty, value);
    }

    public static string? GetLabel(Control element)
    {
        return element.GetValue(LabelProperty);
    }

    public static void SetLabel(Control element, string? value)
    {
        element.SetValue(LabelProperty, value);
    }

    public static bool GetHasImage(Control element)
    {
        return element.GetValue(HasImageProperty);
    }

    public static void SetHasImage(Control element, bool value)
    {
        element.SetValue(HasImageProperty, value);
    }

    public static double GetPreviewWidth(Control element)
    {
        return element.GetValue(PreviewWidthProperty);
    }

    public static void SetPreviewWidth(Control element, double value)
    {
        element.SetValue(PreviewWidthProperty, value);
    }

    public static double GetPreviewHeight(Control element)
    {
        return element.GetValue(PreviewHeightProperty);
    }

    public static void SetPreviewHeight(Control element, double value)
    {
        element.SetValue(PreviewHeightProperty, value);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        ToolTip.RemoveToolTipOpeningHandler(control, OnToolTipOpening);
        ToolTip.RemoveToolTipClosingHandler(control, OnToolTipClosing);

        if (args.NewValue is true)
        {
            ResetTip(control);
            ToolTip.AddToolTipOpeningHandler(control, OnToolTipOpening);
            ToolTip.AddToolTipClosingHandler(control, OnToolTipClosing);
        }
        else
        {
            ToolTip.SetTip(control, null);
        }
    }

    private static void OnToolTipOpening(object? sender, CancelRoutedEventArgs args)
    {
        if (sender is Control control)
        {
            ToolTip.SetTip(control, CreatePreview(control));
        }
    }

    private static void OnToolTipClosing(object? sender, RoutedEventArgs args)
    {
        if (sender is Control control)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!ToolTip.GetIsOpen(control))
                    {
                        ResetTip(control);
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private static void OnToolTipIsOpenChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is false)
        {
            ResetTip(control);
        }
    }

    private static void OnPreviewPropertyChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (GetIsEnabled(control) && ToolTip.GetIsOpen(control))
        {
            ToolTip.SetTip(control, CreatePreview(control));
        }
    }

    private static void ResetTip(Control control)
    {
        if (GetIsEnabled(control))
        {
            ToolTip.SetTip(control, PlaceholderTip);
        }
    }

    private static Control CreatePreview(Control control)
    {
        var path = GetImagePath(control);
        var image = GetHasImage(control) && !string.IsNullOrWhiteSpace(path)
            ? ImageAssetPreviewCache.GetOrLoad(path)
            : null;
        var showImage = image is not null;

        var container = new Border
        {
            Width = GetPreviewWidth(control),
            Height = GetPreviewHeight(control)
        };
        container.Classes.Add("tooltipPreview");

        var grid = new Grid();
        grid.Children.Add(new Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
            IsVisible = showImage
        });
        grid.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(GetLabel(control)) ? "暂无预览" : GetLabel(control),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = !showImage
        });

        container.Child = grid;
        return container;
    }

    private sealed class WorkbenchPreviewToolTipMarker;
}
