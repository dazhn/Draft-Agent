using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public static class WorkbenchRowVoiceToolTip
{
    private const string PlaceholderTip = "语音";

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchRowVoiceToolTipMarker, Control, bool>("IsEnabled");

    static WorkbenchRowVoiceToolTip()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
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
        if (sender is Control control && WorkbenchRowContext.Resolve(control.DataContext) is { } row)
        {
            row.RefreshTtsDetailTextBinding();
            ToolTip.SetTip(control, CreatePreview(row));
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

    private static void ResetTip(Control control)
    {
        if (GetIsEnabled(control))
        {
            ToolTip.SetTip(control, PlaceholderTip);
        }
    }

    private static Control CreatePreview(AnalysisResultRowViewModel row)
    {
        return new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = row.TtsStatusText,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = row.TtsDetailText,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320,
                    Classes = { "muted", "small" }
                }
            }
        };
    }

    private sealed class WorkbenchRowVoiceToolTipMarker;
}
