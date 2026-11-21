using Avalonia;

namespace DraftAgent.App.Views;

public static class WorkbenchDragTarget
{
    public static readonly AttachedProperty<object?> TargetProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchDragTargetMarker, AvaloniaObject, object?>("Target");

    public static object? GetTarget(AvaloniaObject element)
    {
        return element.GetValue(TargetProperty);
    }

    public static void SetTarget(AvaloniaObject element, object? value)
    {
        element.SetValue(TargetProperty, value);
    }

    private sealed class WorkbenchDragTargetMarker;
}
