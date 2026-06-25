using DraftAgent.App;

namespace DraftAgent.App.Tests;

public sealed class AudioPreviewServiceTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8188/view?filename=line.wav&type=output", "line.wav")]
    [InlineData("http://127.0.0.1:8188/view?filename=line%2001.flac&type=temp&subfolder=tts%20queue", "line 01.flac")]
    [InlineData("http://127.0.0.1:8188/view?filename=../escape/line.wav&type=output", "line.wav")]
    [InlineData("http://127.0.0.1:8188/view?filename=..%5Cescape%5Cline.flac&type=output", "line.flac")]
    [InlineData("http://127.0.0.1:8188/audio/sample.ogg", "sample.ogg")]
    public void ResolveFilenameFromUriKeepsSafeAudioFilenameExtension(string target, string expected)
    {
        var filename = AudioPreviewService.ResolveFilenameFromUri(new Uri(target));

        Assert.Equal(expected, filename);
    }

    [Fact]
    public void ResolveFilenameFromUriFallsBackWhenFilenameIsNotAFile()
    {
        var filename = AudioPreviewService.ResolveFilenameFromUri(
            new Uri("http://127.0.0.1:8188/view?filename=../&type=output"));

        Assert.Equal("preview.flac", filename);
    }

    [Fact]
    public void StableCacheKeyReusesSameRemotePreviewTarget()
    {
        var target = "http://127.0.0.1:8188/view?filename=line%2001.flac&type=temp";

        var first = AudioPreviewService.StableCacheKey(target);
        var second = AudioPreviewService.StableCacheKey(target);

        Assert.Equal(first, second);
        Assert.Equal(16, first.Length);
        Assert.DoesNotContain("-", first);
    }
}

