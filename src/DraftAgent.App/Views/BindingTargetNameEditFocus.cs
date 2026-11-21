using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DraftAgent.App.Views;

public static class BindingTargetNameEditFocus
{
    public static readonly AttachedProperty<bool> IsEditingProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>(
            "IsEditing",
            typeof(BindingTargetNameEditFocus));

    static BindingTargetNameEditFocus()
    {
        IsEditingProperty.Changed.AddClassHandler<TextBox>(OnIsEditingChanged);
    }

    public static bool GetIsEditing(TextBox element)
    {
        return element.GetValue(IsEditingProperty);
    }

    public static void SetIsEditing(TextBox element, bool value)
    {
        element.SetValue(IsEditingProperty, value);
    }

    private static void OnIsEditingChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!GetIsEditing(textBox) || !textBox.IsVisible)
            {
                return;
            }

            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Input);
    }
}
