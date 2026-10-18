using System;
using System.IO;

namespace Jellyfin.Plugin.SubtitleDownloader.Services;

/// <summary>
/// Computes OpenSubtitles 64-bit file hash for exact subtitle synchronization.
/// </summary>
public static class MovieHashCalculator
{
    private const int BufferChunkSize = 65536; // 64KB

    /// <summary>
    /// Computes the OpenSubtitles hash for a given file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>16-char hex hash string or null if computation fails.</returns>
    public static string? ComputeHash(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ComputeHash(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Computes the OpenSubtitles hash for a stream.
    /// </summary>
    /// <param name="stream">The file stream.</param>
    /// <returns>16-char hex hash string or null if computation fails.</returns>
    public static string? ComputeHash(Stream stream)
    {
        try
        {
            long fileSize = stream.Length;
            if (fileSize < BufferChunkSize)
            {
                return null;
            }

            ulong hash = (ulong)fileSize;

            byte[] buffer = new byte[BufferChunkSize];

            // Read first 64KB
            stream.Seek(0, SeekOrigin.Begin);
            int read = stream.Read(buffer, 0, BufferChunkSize);
            if (read < BufferChunkSize)
            {
                return null;
            }

            for (int i = 0; i < BufferChunkSize; i += 8)
            {
                hash += BitConverter.ToUInt64(buffer, i);
            }

            // Read last 64KB
            stream.Seek(Math.Max(0, fileSize - BufferChunkSize), SeekOrigin.Begin);
            read = stream.Read(buffer, 0, BufferChunkSize);
            if (read < BufferChunkSize)
            {
                return null;
            }

            for (int i = 0; i < BufferChunkSize; i += 8)
            {
                hash += BitConverter.ToUInt64(buffer, i);
            }

            return hash.ToString("x16");
        }
        catch
        {
            return null;
        }
    }
}
