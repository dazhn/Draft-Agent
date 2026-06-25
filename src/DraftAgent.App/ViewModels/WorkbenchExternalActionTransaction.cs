using System;
using System.Threading;
using System.Threading.Tasks;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal sealed class WorkbenchExternalActionTransaction
{
    private readonly Func<Task<InteractionProject>> _commitWorkbenchAsync;
    private readonly Func<InteractionProject> _getProject;
    private readonly Action<InteractionProject> _setProject;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private long _revision;

    public WorkbenchExternalActionTransaction(
        Func<Task<InteractionProject>> commitWorkbenchAsync,
        Func<InteractionProject> getProject,
        Action<InteractionProject> setProject)
    {
        _commitWorkbenchAsync = commitWorkbenchAsync;
        _getProject = getProject;
        _setProject = setProject;
    }

    public async Task<InteractionProject> RunAsync(Func<InteractionProject, Task<InteractionProject>> action)
    {
        await _operationGate.WaitAsync();
        try
        {
            var committedProject = await _commitWorkbenchAsync();
            SetProject(committedProject);
            var updatedProject = await action(_getProject());
            SetProject(updatedProject);
            return updatedProject;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<InteractionProject> PublishAsync(Task<InteractionProject> projectTask)
    {
        var updatedProject = await projectTask;
        SetProject(updatedProject);
        return updatedProject;
    }

    public long CaptureRevision()
    {
        return Interlocked.Read(ref _revision);
    }

    public bool IsCurrentRevision(long revision)
    {
        return Interlocked.Read(ref _revision) == revision;
    }

    public void NotifyExternalProjectChange()
    {
        Interlocked.Increment(ref _revision);
    }

    private void SetProject(InteractionProject project)
    {
        _setProject(project);
    }
}
