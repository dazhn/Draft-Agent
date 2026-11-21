using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.VisualTree;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchDragTargetRefresh
{
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMilliseconds(32);
    private readonly Func<Visual, Point, IEnumerable<Visual>> _visualsAt;

    private DateTimeOffset _lastRefreshAt = DateTimeOffset.MinValue;

    public WorkbenchDragTargetRefresh()
        : this((root, pointerPosition) => root.GetVisualsAt(pointerPosition))
    {
    }

    public WorkbenchDragTargetRefresh(Func<Visual, Point, IEnumerable<Visual>> visualsAt)
    {
        _visualsAt = visualsAt;
    }

    public bool TryResolveNextRowTarget(
        Visual root,
        Point pointerPosition,
        DateTimeOffset now,
        AnalysisResultRowViewModel? ignoredRow,
        out Visual target)
    {
        return TryResolveNextTarget(
            root,
            pointerPosition,
            now,
            candidates => ResolveRowDragTarget(candidates, ignoredRow),
            out target);
    }

    public bool TryResolveNextRowTarget(
        Visual root,
        Point pointerPosition,
        DateTimeOffset now,
        out Visual target)
    {
        return TryResolveNextRowTarget(root, pointerPosition, now, ignoredRow: null, out target);
    }

    public bool TryResolveNextSceneTarget(
        Visual root,
        Point pointerPosition,
        DateTimeOffset now,
        out Visual target)
    {
        return TryResolveNextTarget(root, pointerPosition, now, ResolveSceneDragTarget, out target);
    }

    private bool TryResolveNextTarget(
        Visual root,
        Point pointerPosition,
        DateTimeOffset now,
        Func<IReadOnlyList<Visual>, ResolvedDragTarget?> resolveTarget,
        out Visual target)
    {
        target = null!;
        if (now - _lastRefreshAt < MinimumRefreshInterval)
        {
            return false;
        }

        _lastRefreshAt = now;
        var candidates = _visualsAt(root, pointerPosition).ToArray();
        var resolved = resolveTarget(candidates);
        if (resolved is null)
        {
            return false;
        }

        target = resolved.Visual;
        return true;
    }

    public void AcceptEnteredRowTarget(Visual? visual)
    {
    }

    public void AcceptEnteredSceneTarget(Visual? visual)
    {
    }

    public void Reset()
    {
        _lastRefreshAt = DateTimeOffset.MinValue;
    }

    private static ResolvedDragTarget? ResolveRowDragTarget(
        IReadOnlyList<Visual> candidates,
        AnalysisResultRowViewModel? ignoredRow)
    {
        ResolvedDragTarget? ignoredSourceTarget = null;
        foreach (var candidate in candidates)
        {
            if (TryResolveRowTarget(candidate, out var visual, out var key))
            {
                if (ignoredRow is not null &&
                    ReferenceEquals(key, ignoredRow))
                {
                    ignoredSourceTarget ??= new ResolvedDragTarget(visual, key);
                    continue;
                }

                return new ResolvedDragTarget(visual, key);
            }
        }

        if (ignoredSourceTarget is not null)
        {
            return ignoredSourceTarget;
        }

        foreach (var candidate in candidates)
        {
            if (TryResolveTableTarget(candidate, out var visual, out var key))
            {
                return new ResolvedDragTarget(visual, key);
            }
        }

        return null;
    }

    private static ResolvedDragTarget? ResolveSceneDragTarget(IReadOnlyList<Visual> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (TryResolveSceneTarget(candidate, out var visual, out var key))
            {
                return new ResolvedDragTarget(visual, key);
            }
        }

        return null;
    }

    private static bool TryResolveRowTarget(
        Visual? candidate,
        out Visual visual,
        out object? key)
    {
        visual = null!;
        key = null;

        if (WorkbenchDropTargetResolver.TryFindDragTargetAncestor<AnalysisResultRowViewModel>(
                candidate,
                out var row,
                out var rowVisual))
        {
            visual = rowVisual;
            key = row;
            return true;
        }

        return false;
    }

    private static bool TryResolveTableTarget(
        Visual? candidate,
        out Visual visual,
        out object? key)
    {
        visual = null!;
        key = null;

        if (candidate is not null &&
            WorkbenchDropTargetResolver.TryResolveExplicitTableTarget(candidate, out var scene))
        {
            visual = candidate;
            key = scene;
            return true;
        }

        return false;
    }

    private static bool TryResolveSceneTarget(
        Visual? candidate,
        out Visual visual,
        out object? key)
    {
        visual = null!;
        key = null;

        if (candidate is StyledElement element &&
            element.Classes.Contains("referenceScene") &&
            WorkbenchDragTarget.GetTarget(element) is SceneTableViewModel scene)
        {
            visual = candidate;
            key = scene;
            return true;
        }

        return false;
    }

    private sealed record ResolvedDragTarget(Visual Visual, object? Key);
}
