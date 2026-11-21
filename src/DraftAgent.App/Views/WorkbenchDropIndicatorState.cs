using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchDropIndicatorState
{
    private const double IndicatorThickness = 2;
    private static readonly IBrush IndicatorBrush = new SolidColorBrush(Color.Parse("#60A5FA"));
    private Canvas? _host;
    private Border? _line;

    public void Attach(Canvas host)
    {
        _host = host;
    }

    public void Set(Visual? visual, WorkbenchDropIntent intent)
    {
        Clear();
        if (_host is null ||
            visual is not Control target ||
            target.TranslatePoint(default, _host) is not { } origin ||
            target.Bounds.Width <= 0)
        {
            return;
        }

        var y = intent.Position == WorkbenchDropPosition.Before
            ? origin.Y
            : origin.Y + target.Bounds.Height - IndicatorThickness;
        var line = new Border
        {
            Background = IndicatorBrush,
            Height = IndicatorThickness,
            Width = target.Bounds.Width,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(line, origin.X);
        Canvas.SetTop(line, y);
        _host.Children.Add(line);
        _line = line;
    }

    public bool Matches(Visual? visual)
    {
        return false;
    }

    public void Clear()
    {
        if (_host is not null && _line is not null)
        {
            _host.Children.Remove(_line);
        }

        _line = null;
    }
}
