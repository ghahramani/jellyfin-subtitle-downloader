using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SubtitleDownloader.Models;

namespace Jellyfin.Plugin.SubtitleDownloader.Services;

/// <summary>
/// Converts subtitle formats and detects character encodings for seamless player display.
/// </summary>
public static class SubtitleFormatConverter
{
    private static readonly Regex TimestampRegex = new(
        @"(\d{2}:\d{2}:\d{2}),(\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}),(\d{3})",
        RegexOptions.Compiled);

    static SubtitleFormatConverter()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // Provider may already be registered
        }
    }

    /// <summary>
    /// Decodes raw bytes to a clean UTF-8 string by detecting encoding (UTF-8, UTF-16, Windows-1256 for Persian/Arabic, Windows-1252, etc.).
    /// </summary>
    /// <param name="bytes">Raw subtitle bytes.</param>
    /// <param name="targetLanguage">Target language code to help with codepage disambiguation.</param>
    /// <returns>Decoded string.</returns>
    public static string DecodeBytesToUtf8(byte[] bytes, string? targetLanguage = null)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        // 1. Check for BOMs
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // 2. Test if valid UTF-8
        try
        {
            var utf8Strict = new UTF8Encoding(false, true);
            string decoded = utf8Strict.GetString(bytes);

            // Verify if there are replacement characters
            if (!decoded.Contains('\uFFFD'))
            {
                return decoded;
            }
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8, fall through
        }

        // 3. Fallback based on language or Persian/Arabic detection
        bool isPersianOrArabic = string.Equals(targetLanguage, "fa", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetLanguage, "per", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetLanguage, "fas", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetLanguage, "ar", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetLanguage, "ara", StringComparison.OrdinalIgnoreCase);

        if (isPersianOrArabic)
        {
            try
            {
                var win1256 = Encoding.GetEncoding(1256); // Windows-1256 (Arabic / Persian)
                return win1256.GetString(bytes);
            }
            catch
            {
                // Fall back to default
            }
        }

        // 4. Default to Windows-1252 / ISO-8859-1
        try
        {
            var win1252 = Encoding.GetEncoding(1252);
            return win1252.GetString(bytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>
    /// Converts SubRip (.srt) text content into WebVTT (.vtt) text content.
    /// </summary>
    /// <param name="srtContent">The SRT content string.</param>
    /// <returns>WebVTT formatted string.</returns>
    public static string ConvertSrtToVtt(string srtContent)
    {
        if (string.IsNullOrWhiteSpace(srtContent))
        {
            return "WEBVTT\n\n";
        }

        // If it already starts with WEBVTT, return it as is
        if (srtContent.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            return srtContent;
        }

        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        using var reader = new StringReader(srtContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Convert timestamps 00:00:00,000 --> 00:00:00,000 to 00:00:00.000 --> 00:00:00.000
            if (line.Contains("-->"))
            {
                string convertedLine = TimestampRegex.Replace(line, "$1.$2 --> $3.$4");
                sb.AppendLine(convertedLine);
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses SRT or WebVTT content into Jellyfin-compatible TrackEvents.
    /// </summary>
    public static List<SubtitleTrackEventDto> ParseSrtToTrackEvents(string content)
    {
        var events = new List<SubtitleTrackEventDto>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return events;
        }

        using var reader = new StringReader(content);
        string? line;
        int eventCounter = 1;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            // Match timestamp line (handles both comma and dot for milliseconds)
            var match = Regex.Match(line, @"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})");
            if (match.Success)
            {
                long startTicks = ParseTimestampToTicks(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
                long endTicks = ParseTimestampToTicks(match.Groups[5].Value, match.Groups[6].Value, match.Groups[7].Value, match.Groups[8].Value);

                var textSb = new StringBuilder();
                string? textLine;
                while ((textLine = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(textLine))
                    {
                        break;
                    }
                    if (textSb.Length > 0)
                    {
                        textSb.Append('\n');
                    }
                    textSb.Append(textLine.TrimEnd());
                }

                events.Add(new SubtitleTrackEventDto
                {
                    Id = eventCounter.ToString(),
                    StartPositionTicks = startTicks,
                    EndPositionTicks = endTicks,
                    Text = textSb.ToString()
                });
                eventCounter++;
            }
        }

        return events;
    }

    private static long ParseTimestampToTicks(string h, string m, string s, string ms)
    {
        int hours = int.Parse(h);
        int minutes = int.Parse(m);
        int seconds = int.Parse(s);
        int millis = int.Parse(ms);
        TimeSpan span = new TimeSpan(0, hours, minutes, seconds, millis);
        return span.Ticks;
    }
}
