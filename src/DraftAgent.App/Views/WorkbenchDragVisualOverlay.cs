using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchDragVisualOverlay
{
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(140);
    private static readonly Thickness ScenePadding = new(12, 8);
    private static readonly Thickness RowPadding = new(10, 7);

    private Canvas? _host;
    private Border? _ghost;
    private Point _pointerOffset;

    public bool IsActive => _ghost is not null;

    public void Attach(Canvas host)
    {
        _host = host;
    }

    public void BeginRowDrag(AnalysisResultRowViewModel row, Point pointerPosition)
    {
        var title = string.IsNullOrWhiteSpace(row.SubjectName) ? "Row" : row.SubjectName.Trim();
        var detail = string.IsNullOrWhiteSpace(row.LineText) ? row.TtsStatusText : row.LineText.Trim();
        Begin(CreateGhost(title, detail, "dragGhostRow", RowPadding, 520), pointerPosition, new Point(18, 18));
    }

    public void BeginSceneDrag(SceneTableViewModel scene, Point pointerPosition)
    {
        var title = string.IsNullOrWhiteSpace(scene.SceneName) ? "Scene" : scene.SceneName.Trim();
        var detail = string.IsNullOrWhiteSpace(scene.SceneEffect) ? scene.SceneLabel : scene.SceneEffect.Trim();
        Begin(CreateGhost(title, detail, "dragGhostScene", ScenePadding, 560), pointerPosition, new Point(20, 20));
    }

    public void Move(Point pointerPosition)
    {
        if (_ghost is null)
        {
            return;
        }

        Canvas.SetLeft(_ghost, pointerPosition.X + _pointerOffset.X);
        Canvas.SetTop(_ghost, pointerPosition.Y + _pointerOffset.Y);
    }

    public async void End(bool complete)
    {
        if (_host is null || _ghost is null)
        {
            return;
        }

        var ghost = _ghost;
        _ghost = null;
        ghost.Classes.Remove("active");
        ghost.Classes.Add(complete ? "complete" : "cancel");
        ghost.Opacity = 0;

        await System.Threading.Tasks.Task.Delay(FadeOutDuration);
        _host.Children.Remove(ghost);
    }

    public void Reset()
    {
        if (_host is not null && _ghost is not null)
        {
            _host.Children.Remove(_ghost);
        }

        _ghost = null;
    }

    private void Begin(Border ghost, Point pointerPosition, Point pointerOffset)
    {
        if (_host is null)
        {
            return;
        }

        Reset();
        _ghost = ghost;
        _pointerOffset = pointerOffset;
        _host.Children.Add(ghost);
        Move(pointerPosition);
        ghost.Opacity = 1;
        ghost.Classes.Add("active");
    }

    private static Border CreateGhost(string title, string detail, string className, Thickness padding, double maxWidth)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        titleBlock.Classes.Add("dragGhostTitle");

        var detailBlock = new TextBlock
        {
            Text = detail,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap
        };
        detailBlock.Classes.Add("dragGhostDetail");

        var panel = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                titleBlock,
                detailBlock
            }
        };

        var ghost = new Border
        {
            Padding = padding,
            MaxWidth = maxWidth,
            MinWidth = 240,
            IsHitTestVisible = false,
            Opacity = 0,
            Child = panel,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(1.015, 1.015),
            Transitions =
            [
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = FadeOutDuration
                }
            ]
        };
        ghost.Classes.Add("dragGhost");
        ghost.Classes.Add(className);
        return ghost;
    }
}
