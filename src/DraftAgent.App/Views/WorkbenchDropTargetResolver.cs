using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal static class WorkbenchDropTargetResolver
{
    public static bool TryCreateRowDropIntent(
        Visual? hit,
        AnalysisResultRowViewModel? draggedRow,
        out WorkbenchDropIntent intent,
        out Visual? indicatorVisual)
    {
        if (TryFindDragTargetAncestor<AnalysisResultRowViewModel>(hit, out var row, out var rowVisual))
        {
            if (ReferenceEquals(row, draggedRow))
            {
                intent = default;
                indicatorVisual = null;
                return false;
            }

            intent = new WorkbenchDropIntent(
                WorkbenchDropIntentKind.RowToRow,
                row,
                ResolveRowDropPosition(draggedRow, row));
            indicatorVisual = rowVisual;
            return true;
        }

        if (TryResolveExplicitTableTarget(hit, out var scene))
        {
            if (draggedRow is not null &&
                ReferenceEquals(scene, draggedRow.OwnerScene))
            {
                intent = default;
                indicatorVisual = null;
                return false;
            }

            intent = new WorkbenchDropIntent(
                WorkbenchDropIntentKind.RowToSceneEnd,
                scene,
                null);
            indicatorVisual = hit;
            return true;
        }

        intent = default;
        indicatorVisual = null;
        return false;
    }

    public static bool TryResolveExplicitTableTarget(Visual? hit, out SceneTableViewModel scene)
    {
        if (hit is AvaloniaObject tableElement &&
            hit is StyledElement element &&
            element.Classes.Contains("eventTable") &&
            WorkbenchDragTarget.GetTarget(tableElement) is SceneTableViewModel targetScene)
        {
            scene = targetScene;
            return true;
        }

        scene = null!;
        return false;
    }

    public static bool TryCreateSceneDropIntent(
        Visual? hit,
        SceneTableViewModel? draggedScene,
        IReadOnlyList<SceneTableViewModel>? currentScenes,
        out WorkbenchDropIntent intent,
        out Visual? indicatorVisual)
    {
        if (WorkbenchDropTargetResolver.IsRowDragTargetSource(hit))
        {
            intent = default;
            indicatorVisual = null;
            return false;
        }

        var sceneVisual = FindStyledAncestor(hit, "referenceScene");
        if (sceneVisual is not null &&
            WorkbenchDragTarget.GetTarget(sceneVisual) is SceneTableViewModel scene)
        {
            if (ReferenceEquals(scene, draggedScene))
            {
                intent = default;
                indicatorVisual = null;
                return false;
            }

            intent = new WorkbenchDropIntent(
                WorkbenchDropIntentKind.SceneToScene,
                scene,
                ResolveSceneDropPosition(draggedScene, scene, currentScenes));
            indicatorVisual = sceneVisual;
            return true;
        }

        intent = default;
        indicatorVisual = null;
        return false;
    }

    public static bool TryResolveRowDragSource(object? sender, out AnalysisResultRowViewModel row)
    {
        if (sender is AvaloniaObject source &&
            WorkbenchDragTarget.GetTarget(source) is AnalysisResultRowViewModel dragTarget)
        {
            row = dragTarget;
            return true;
        }

        if (sender is Control control && WorkbenchRowContext.Resolve(control.Tag) is { } taggedRow)
        {
            row = taggedRow;
            return true;
        }

        row = null!;
        return false;
    }

    public static bool TryResolveSceneDragSource(object? sender, out SceneTableViewModel scene)
    {
        if (sender is AvaloniaObject source &&
            WorkbenchDragTarget.GetTarget(source) is SceneTableViewModel dragTarget)
        {
            scene = dragTarget;
            return true;
        }

        scene = null!;
        return false;
    }

    public static bool IsRowDragTargetSource(Visual? source)
    {
        return FindDragTargetAncestor<AnalysisResultRowViewModel>(source) is not null;
    }

    public static bool TryFindSceneDragTargetOutsideRow(
        Visual source,
        out SceneTableViewModel scene,
        out Visual sceneVisual)
    {
        if (TryFindDragTargetAncestor<SceneTableViewModel>(source, out scene, out sceneVisual) &&
            !IsRowDragTargetSource(source))
        {
            return true;
        }

        scene = null!;
        sceneVisual = null!;
        return false;
    }

    public static Visual? ResolveDropIndicatorVisual(Visual? visual, WorkbenchDropIntent intent)
    {
        return intent.Kind switch
        {
            WorkbenchDropIntentKind.RowToRow => FindStyledAncestor(visual, "eventRow"),
            WorkbenchDropIntentKind.RowToSceneEnd => FindStyledAncestor(visual, "eventTable")
                ?? FindStyledAncestor(visual, "referenceScene"),
            WorkbenchDropIntentKind.SceneToScene => FindStyledAncestor(visual, "referenceScene"),
            _ => visual
        };
    }

    public static WorkbenchDropPosition ResolveRowDropPosition(
        AnalysisResultRowViewModel? draggedRow,
        AnalysisResultRowViewModel targetRow)
    {
        if (draggedRow is not null &&
            ReferenceEquals(draggedRow.OwnerScene, targetRow.OwnerScene) &&
            draggedRow.OwnerScene is { } scene)
        {
            var draggedIndex = scene.Rows.IndexOf(draggedRow);
            var targetIndex = scene.Rows.IndexOf(targetRow);
            if (draggedIndex >= 0 &&
                targetIndex >= 0 &&
                draggedIndex != targetIndex)
            {
                return draggedIndex < targetIndex
                    ? WorkbenchDropPosition.After
                    : WorkbenchDropPosition.Before;
            }
        }

        if (draggedRow is not null &&
            ReferenceEquals(draggedRow.OwnerScene, targetRow.OwnerScene) &&
            draggedRow.Order != targetRow.Order)
        {
            return draggedRow.Order < targetRow.Order
                ? WorkbenchDropPosition.After
                : WorkbenchDropPosition.Before;
        }

        return WorkbenchDropPosition.Before;
    }

    public static WorkbenchDropPosition ResolveSceneDropPosition(
        SceneTableViewModel? draggedScene,
        SceneTableViewModel targetScene,
        IReadOnlyList<SceneTableViewModel>? currentScenes = null)
    {
        if (draggedScene is not null &&
            currentScenes is not null)
        {
            var draggedIndex = IndexOfScene(currentScenes, draggedScene);
            var targetIndex = IndexOfScene(currentScenes, targetScene);
            if (draggedIndex >= 0 &&
                targetIndex >= 0 &&
                draggedIndex != targetIndex)
            {
                return draggedIndex < targetIndex
                    ? WorkbenchDropPosition.After
                    : WorkbenchDropPosition.Before;
            }
        }

        if (draggedScene is not null &&
            draggedScene.Order != targetScene.Order)
        {
            return draggedScene.Order < targetScene.Order
                ? WorkbenchDropPosition.After
                : WorkbenchDropPosition.Before;
        }

        return WorkbenchDropPosition.Before;
    }

    private static int IndexOfScene(IReadOnlyList<SceneTableViewModel> scenes, SceneTableViewModel scene)
    {
        for (var index = 0; index < scenes.Count; index++)
        {
            if (ReferenceEquals(scenes[index], scene))
            {
                return index;
            }
        }

        return -1;
    }

    public static string DescribeVisualAncestors(Visual source)
    {
        var parts = new List<string>(8);
        var visual = source;
        while (visual is not null && parts.Count < 8)
        {
            if (visual is StyledElement element)
            {
                var classes = element.Classes.Count > 0
                    ? "." + string.Join(".", element.Classes)
                    : string.Empty;
                var target = WorkbenchDragTarget.GetTarget(element)?.GetType().Name;
                parts.Add(target is null
                    ? $"{visual.GetType().Name}{classes}"
                    : $"{visual.GetType().Name}{classes}[target={target}]");
            }
            else
            {
                parts.Add(visual.GetType().Name);
            }

            visual = visual.GetVisualParent();
        }

        return string.Join(" > ", parts);
    }

    public static bool TryResolveSceneDragHandle(Visual source, out AvaloniaObject handle, out bool isDedicatedHandle)
    {
        if (FindStyledAncestor(source, "sceneDragHandle") is { } styledHandle)
        {
            handle = styledHandle;
            isDedicatedHandle = true;
            return true;
        }

        var visual = source;
        while (visual is not null)
        {
            if (WorkbenchDragTarget.GetTarget(visual) is SceneTableViewModel &&
                visual is StyledElement { Classes: var classes } &&
                (classes.Contains("sceneDragHandle") ||
                 classes.Contains("scenePreviewBox") ||
                 classes.Contains("eventTable")))
            {
                handle = visual;
                isDedicatedHandle = false;
                return true;
            }

            visual = visual.GetVisualParent();
        }

        handle = null!;
        isDedicatedHandle = false;
        return false;
    }

    public static Visual? FindStyledAncestor(Visual? visual, string className)
    {
        while (visual is not null)
        {
            if (visual is StyledElement element && element.Classes.Contains(className))
            {
                return visual;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    public static T? FindDragTargetAncestor<T>(Visual? visual)
        where T : class
    {
        while (visual is not null)
        {
            if (WorkbenchDragTarget.GetTarget(visual) is T dragTarget)
            {
                return dragTarget;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    public static bool TryFindDragTargetAncestor<T>(
        Visual? visual,
        out T target,
        out Visual targetVisual)
        where T : class
    {
        while (visual is not null)
        {
            if (WorkbenchDragTarget.GetTarget(visual) is T dragTarget)
            {
                target = dragTarget;
                targetVisual = visual;
                return true;
            }

            visual = visual.GetVisualParent();
        }

        target = null!;
        targetVisual = null!;
        return false;
    }
}

internal readonly record struct WorkbenchDropIntent(
    WorkbenchDropIntentKind Kind,
    object Target,
    WorkbenchDropPosition? Position)
{
    public static string Format(WorkbenchDropIntent? intent)
    {
        return intent is { } value ? value.Format() : "none";
    }

    public string Format()
    {
        var target = Target switch
        {
            SceneTableViewModel scene => $"scene:{scene.SceneId}",
            AnalysisResultRowViewModel row => $"row:{row.InteractionId}",
            _ => Target.GetType().Name
        };

        return $"kind={Kind}; target={target}; position={Position?.ToString() ?? "none"}";
    }
}

internal enum WorkbenchDropIntentKind
{
    RowToRow,
    RowToSceneEnd,
    SceneToScene
}
