namespace DraftAgent.Core;

internal static class ExternalBoundary
{
    public static bool ShouldWrap(Exception ex)
    {
        return ex is not OperationCanceledException and not ExternalIntegrationException;
    }
}
