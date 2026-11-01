using System.IO;
using System.Text;

namespace GotifyWin.Services;

public static class SoundValidator
{
    public const long MaxAudioSizeBytes = 5 * 1024 * 1024; // 5 MB ceiling

    public static bool IsValidWavFile(string? path, out string? errorMessage)
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

        if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Audio file must have a .wav extension.";
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

            if (fileInfo.Length < 44) // Standard minimal WAV header size
            {
                errorMessage = "File is too small to be a valid WAV file.";
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

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
}
