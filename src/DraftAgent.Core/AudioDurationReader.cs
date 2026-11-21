using System.Text;

namespace DraftAgent.Core;

public static class AudioDurationReader
{
    public static bool TryReadSeconds(string? audioPath, out double durationSeconds)
    {
        durationSeconds = 0;
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return false;
        }

        var extension = Path.GetExtension(audioPath);
        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadFlacDurationSeconds(audioPath, out durationSeconds);
        }

        if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryReadWavDurationSeconds(audioPath, out durationSeconds);
    }

    private static bool TryReadWavDurationSeconds(string audioPath, out double durationSeconds)
    {
        durationSeconds = 0;
        try
        {
            using var stream = File.OpenRead(audioPath);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (stream.Length < 12 || ReadAscii(reader, 4) != "RIFF")
            {
                return false;
            }

            _ = reader.ReadUInt32();
            if (ReadAscii(reader, 4) != "WAVE")
            {
                return false;
            }

            uint? byteRate = null;
            uint? dataSize = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadAscii(reader, 4);
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;
                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    _ = reader.ReadUInt16();
                    _ = reader.ReadUInt16();
                    _ = reader.ReadUInt32();
                    byteRate = reader.ReadUInt32();
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                }

                var nextChunk = chunkStart + chunkSize + chunkSize % 2;
                if (nextChunk > stream.Length)
                {
                    break;
                }

                stream.Position = nextChunk;
            }

            if (byteRate is null or 0 || dataSize is null)
            {
                return false;
            }

            durationSeconds = dataSize.Value / (double)byteRate.Value;
            return durationSeconds > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadFlacDurationSeconds(string audioPath, out double durationSeconds)
    {
        durationSeconds = 0;
        try
        {
            using var stream = File.OpenRead(audioPath);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (stream.Length < 42 || ReadAscii(reader, 4) != "fLaC")
            {
                return false;
            }

            while (stream.Position + 4 <= stream.Length)
            {
                var header = reader.ReadByte();
                var blockType = header & 0x7F;
                var blockLength = ReadBigEndianUInt24(reader);
                if (blockType == 0)
                {
                    if (blockLength < 34 || stream.Position + blockLength > stream.Length)
                    {
                        return false;
                    }

                    _ = reader.ReadUInt16();
                    _ = reader.ReadUInt16();
                    _ = ReadBigEndianUInt24(reader);
                    _ = ReadBigEndianUInt24(reader);
                    var packed = reader.ReadBytes(8);
                    var sampleRate = (packed[0] << 12) | (packed[1] << 4) | (packed[2] >> 4);
                    var totalSamples =
                        ((long)(packed[3] & 0x0F) << 32) |
                        ((long)packed[4] << 24) |
                        ((long)packed[5] << 16) |
                        ((long)packed[6] << 8) |
                        packed[7];
                    if (sampleRate <= 0 || totalSamples <= 0)
                    {
                        return false;
                    }

                    durationSeconds = totalSamples / (double)sampleRate;
                    return true;
                }

                stream.Position += blockLength;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(count));
    }

    private static int ReadBigEndianUInt24(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(3);
        if (bytes.Length != 3)
        {
            return 0;
        }

        return bytes[0] << 16 | bytes[1] << 8 | bytes[2];
    }
}
