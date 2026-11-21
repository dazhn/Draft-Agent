using System;

namespace DraftAgent.App.ViewModels;

internal sealed class WorkbenchProjectionGuard
{
    private int _depth;

    public bool IsApplying => _depth > 0;

    public IDisposable BeginApply()
    {
        _depth++;
        return new Scope(this);
    }

    private void EndApply()
    {
        _depth--;
    }

    private sealed class Scope : IDisposable
    {
        private WorkbenchProjectionGuard? _guard;

        public Scope(WorkbenchProjectionGuard guard)
        {
            _guard = guard;
        }

        public void Dispose()
        {
            var guard = _guard;
            if (guard is null)
            {
                return;
            }

            _guard = null;
            guard.EndApply();
        }
    }
}
