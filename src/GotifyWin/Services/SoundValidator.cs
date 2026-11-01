using System.IO;
using System.Text;

namespace GotifyWin.Services;

public static class SoundValidator
{
    public const long MaxAudioSizeBytes = 5 * 1024 * 1024; // 5 MB ceiling

    public static bool IsValidWavFile(string? path, out string? errorMessage)
        => IsValidAudioFile(path, out errorMessage);

    public static bool IsValidAudioFile(string? path, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Path cannot be empty.";
            return false;
        }

        // SEC: Prevent UNC / SMB paths from leaking NTLM hashes across network shares
        if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsUnc))
        {
            errorMessage = "UNC network paths are rejected for security.";
            return false;
        }

        var ext = Path.GetExtension(path);
        bool isWav = ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
        bool isMp3 = ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase);

        if (!isWav && !isMp3)
        {
            errorMessage = "Audio file must have a .wav or .mp3 extension.";
            return false;
        }

        if (!File.Exists(path))
        {
            errorMessage = "File does not exist.";
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaxAudioSizeBytes)
            {
                errorMessage = $"File size ({fileInfo.Length / 1024} KB) exceeds the 5 MB limit.";
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (isWav)
            {
                return ValidateWav(stream, reader, fileInfo.Length, out errorMessage);
            }

            return ValidateMp3(stream, reader, fileInfo.Length, out errorMessage);
        }
        catch (IOException ex)
        {
            errorMessage = $"I/O error reading audio file: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            errorMessage = $"Access denied: {ex.Message}";
            return false;
        }
        catch (ArgumentException ex)
        {
            errorMessage = $"Invalid file path: {ex.Message}";
            return false;
        }
        catch (NotSupportedException ex)
        {
            errorMessage = $"Path format not supported: {ex.Message}";
            return false;
        }
    }

    private static bool ValidateWav(Stream stream, BinaryReader reader, long length, out string? errorMessage)
    {
        errorMessage = null;

        if (length < 44) // Standard minimal WAV header size
        {
            errorMessage = "File is too small to be a valid WAV file.";
            return false;
        }

        var riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (riff != "RIFF")
        {
            errorMessage = "Invalid WAV header: missing 'RIFF' signature.";
            return false;
        }

        reader.ReadInt32(); // File size minus 8

        var wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wave != "WAVE")
        {
            errorMessage = "Invalid WAV header: missing 'WAVE' format tag.";
            return false;
        }

        // Locate "fmt " chunk
        bool fmtFound = false;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadInt32();

            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
            {
                break;
            }

            if (chunkId == "fmt ")
            {
                fmtFound = true;
                if (chunkSize < 16)
                {
                    errorMessage = "Corrupt WAV fmt chunk.";
                    return false;
                }

                var formatTag = reader.ReadInt16(); // 1 = PCM, 3 = IEEE Float
                var channels = reader.ReadInt16();
                var sampleRate = reader.ReadInt32();

                if (formatTag != 1 && formatTag != 3)
                {
                    errorMessage = "Unsupported WAV compression; only PCM or IEEE float are supported.";
                    return false;
                }

                if (channels < 1 || channels > 8)
                {
                    errorMessage = $"Unsupported channel count ({channels}).";
                    return false;
                }

                if (sampleRate < 8000 || sampleRate > 192000)
                {
                    errorMessage = $"Unsupported sample rate ({sampleRate} Hz).";
                    return false;
                }

                break;
            }

            stream.Seek(chunkSize, SeekOrigin.Current);
        }

        if (!fmtFound)
        {
            errorMessage = "WAV file does not contain a valid 'fmt ' chunk.";
            return false;
        }

        return true;
    }

    private static bool ValidateMp3(Stream stream, BinaryReader reader, long length, out string? errorMessage)
    {
        errorMessage = null;

        if (length < 10)
        {
            errorMessage = "File is too small to be a valid MP3 file.";
            return false;
        }

        var header = reader.ReadBytes(10);

        // 1. Check for ID3v2 container header ("ID3" at bytes 0-2)
        if (header.Length >= 3 && header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
        {
            var majorVersion = header[3];
            if (majorVersion >= 2 && majorVersion <= 4)
            {
                return true;
            }
        }

        // 2. Check for MPEG audio frame synchronization (0xFF followed by 3 sync bits)
        // Check at start or after ID3 tag
        if (header.Length >= 4 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            // Verify layer is not 00 (Layer 1, 2, or 3)
            var layerBits = (header[1] & 0x06) >> 1;
            if (layerBits != 0)
            {
                return true;
            }
        }

        errorMessage = "Invalid MP3 header: missing ID3 tag or MPEG audio frame sync.";
        return false;
    }
}
