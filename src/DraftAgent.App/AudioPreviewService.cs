using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace DraftAgent.App;

public interface IAudioPreviewService : IDisposable
{
    Task PlayAsync(string target, CancellationToken cancellationToken = default);

    void Stop();
}

public sealed class AudioPreviewService : IAudioPreviewService
{
    private readonly HttpClient _httpClient;
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _audioReader;
    private Process? _externalPreviewProcess;
    private readonly string _cacheRoot;

    public AudioPreviewService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _cacheRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "audio-preview-cache");
    }

    public async Task PlayAsync(string target, CancellationToken cancellationToken = default)
    {
        Stop();
        if (!OperatingSystem.IsWindows())
        {
            _externalPreviewProcess = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            return;
        }

        var playablePath = await ResolvePlayablePathAsync(target, cancellationToken);
        _audioReader = new AudioFileReader(playablePath);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioReader);
        _outputDevice.Play();
    }

    public void Stop()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _outputDevice = null;
        _audioReader?.Dispose();
        _audioReader = null;
        if (_externalPreviewProcess is not null)
        {
            try
            {
                if (!_externalPreviewProcess.HasExited)
                {
                    _externalPreviewProcess.CloseMainWindow();
                }
            }
            catch
            {
                // Shell-launched preview apps may not expose a controllable process.
            }
            finally
            {
                _externalPreviewProcess.Dispose();
                _externalPreviewProcess = null;
            }
        }

    }

    public void Dispose()
    {
        Stop();
    }

    private async Task<string> ResolvePlayablePathAsync(string target, CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(target))
        {
            return target;
        }

        if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        var uri = new Uri(target);
        var fileName = ResolveFilenameFromUri(uri);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
        {
            fileName = Path.ChangeExtension(fileName, ".flac");
        }

        CleanupPreviewCache();
        var cacheDirectory = Path.Combine(_cacheRoot, StableCacheKey(target));
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, fileName);
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
        {
            File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
            return cachePath;
        }

        var bytes = await _httpClient.GetByteArrayAsync(target, cancellationToken);
        await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
        return cachePath;
    }

    internal static string ResolveFilenameFromUri(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], "filename", StringComparison.OrdinalIgnoreCase))
            {
                return SafeFileName(Uri.UnescapeDataString(pair[1]));
            }
        }

        return SafeFileName(Uri.UnescapeDataString(uri.AbsolutePath));
    }

    private static string SafeFileName(string value)
    {
        var normalized = value.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? "preview.flac" : fileName;
    }

    internal static string StableCacheKey(string target)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(target));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private void CleanupPreviewCache()
    {
        try
        {
            if (!Directory.Exists(_cacheRoot))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var directory in Directory.EnumerateDirectories(_cacheRoot))
            {
                var info = new DirectoryInfo(directory);
                if (info.LastWriteTimeUtc >= cutoff)
                {
                    continue;
                }

                info.Delete(recursive: true);
            }
        }
        catch
        {
            // Preview cache cleanup must never block playback.
        }
    }
}
