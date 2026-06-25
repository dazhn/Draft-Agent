using System.Runtime.CompilerServices;

namespace DraftAgent.Tests.Infrastructure;

internal static class ScopedTestTempDirectory
{
    private static readonly string OriginalTempPath =
        Path.GetTempPath();

    private static readonly string RootPath =
        Path.Combine(
            OriginalTempPath,
            "DraftAgent",
            "TestRuns",
            $"{AppDomain.CurrentDomain.FriendlyName}-{Environment.ProcessId}-{Guid.NewGuid():N}");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(RootPath);
        Environment.SetEnvironmentVariable("TEMP", RootPath, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("TMP", RootPath, EnvironmentVariableTarget.Process);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            DeleteDirectoryIfEmpty(Path.GetDirectoryName(RootPath));
            DeleteDirectoryIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(RootPath) ?? string.Empty));
        }
        catch
        {
            // Test temp cleanup is best-effort. Locked files can be removed by the OS/user temp cleanup later.
        }
    }

    private static void DeleteDirectoryIfEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}
