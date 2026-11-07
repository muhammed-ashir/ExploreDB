using System;
using System.Text;

namespace ExploreDB.Services;

public static class BinaryDataHelper
{
    public const int ShortBinaryThreshold = 64;

    /// <summary>
    /// Checks if a byte array has the magic bytes of a known image format (PNG, JPEG, GIF, BMP, WEBP, ICO, TIFF, SVG).
    /// </summary>
    public static bool IsImage(byte[]? bytes, out string format, out string mimeType)
    {
        format = string.Empty;
        mimeType = string.Empty;

        if (bytes == null || bytes.Length < 3)
            return false;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            format = "PNG";
            mimeType = "image/png";
            return true;
        }

        // JPEG / JPG: FF D8 FF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            format = "JPEG";
            mimeType = "image/jpeg";
            return true;
        }

        // GIF: GIF87a or GIF89a (47 49 46 38)
        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38 &&
            (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            format = "GIF";
            mimeType = "image/gif";
            return true;
        }

        // BMP: 42 4D ("BM")
        if (bytes.Length >= 2 &&
            bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            format = "BMP";
            mimeType = "image/bmp";
            return true;
        }

        // WebP: RIFF....WEBP (52 49 46 46 .... 57 45 42 50)
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            format = "WEBP";
            mimeType = "image/webp";
            return true;
        }

        // ICO: 00 00 01 00
        if (bytes.Length >= 4 &&
            bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00)
        {
            format = "ICO";
            mimeType = "image/x-icon";
            return true;
        }

        // TIFF: 49 49 2A 00 (little endian) or 4D 4D 00 2A (big endian)
        if (bytes.Length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
        {
            format = "TIFF";
            mimeType = "image/tiff";
            return true;
        }

        // SVG: XML text with <svg
        if (bytes.Length >= 5)
        {
            try
            {
                int checkLen = Math.Min(bytes.Length, 512);
                string text = Encoding.UTF8.GetString(bytes, 0, checkLen).TrimStart();
                if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
                    (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
                {
                    format = "SVG";
                    mimeType = "image/svg+xml";
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Checks if a byte array starts with the %PDF magic header.
    /// </summary>
    public static bool IsPdf(byte[]? bytes)
    {
        return bytes != null && bytes.Length >= 4 &&
               bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
    }

    /// <summary>
    /// Converts bytes to a human-readable file size (e.g. 1.2 KB, 45.6 MB).
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{(bytes / 1024.0):F1} KB";
        return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
    }

    /// <summary>
    /// Formats a byte array as an uppercase hexadecimal string with an optional 0x prefix.
    /// </summary>
    public static string ToHexString(byte[]? bytes, bool prefix = true)
    {
        if (bytes == null || bytes.Length == 0) return prefix ? "0x" : string.Empty;
        var hex = Convert.ToHexString(bytes);
        return prefix ? "0x" + hex : hex;
    }

    /// <summary>
    /// Formats binary data for general string/display representations.
    /// </summary>
    public static string FormatBinaryForDisplay(byte[]? bytes)
    {
        if (bytes == null) return "NULL";
        if (bytes.Length <= ShortBinaryThreshold)
        {
            return ToHexString(bytes, prefix: true);
        }
        if (IsImage(bytes, out var format, out _))
        {
            return $"[Image: {format}, {FormatFileSize(bytes.Length)}]";
        }
        if (IsPdf(bytes))
        {
            return $"[PDF: {FormatFileSize(bytes.Length)}]";
        }
        return $"[Binary: {FormatFileSize(bytes.Length)}]";
    }

    /// <summary>
    /// Gets the raw 0x hexadecimal string representation for display in text boxes (with truncation for extreme sizes).
    /// </summary>
    public static string GetRawHexForDisplay(byte[]? bytes, int maxChars = 65536)
    {
        if (bytes == null || bytes.Length == 0) return "0x";
        var hex = Convert.ToHexString(bytes);
        if (hex.Length <= maxChars)
        {
            return "0x" + hex;
        }
        return "0x" + hex.Substring(0, maxChars) + $"\r\n\r\n... [Display truncated: Showing first {maxChars:N0} of {hex.Length:N0} characters. Click 'Copy Hex' to copy the complete data.]";
    }

    /// <summary>
    /// Generates a classic formatted hex dump with Offset, Hex bytes, and ASCII representation.
    /// </summary>
    public static string GenerateHexDump(byte[]? bytes, int maxBytes = 8192)
    {
        if (bytes == null || bytes.Length == 0) return "(empty binary data)";

        var sb = new StringBuilder();
        int length = Math.Min(bytes.Length, maxBytes);

        sb.AppendLine("Offset    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII");
        sb.AppendLine("--------  -----------------------------------------------   ----------------");

        for (int i = 0; i < length; i += 16)
        {
            sb.Append(i.ToString("X8"));
            sb.Append("  ");

            // Hex representation
            for (int j = 0; j < 16; j++)
            {
                if (i + j < length)
                {
                    sb.Append(bytes[i + j].ToString("X2"));
                    sb.Append(j == 7 ? "  " : " ");
                }
                else
                {
                    sb.Append("  ");
                    sb.Append(j == 7 ? "  " : " ");
                }
            }

            sb.Append(" ");

            // ASCII representation
            for (int j = 0; j < 16 && (i + j) < length; j++)
            {
                byte b = bytes[i + j];
                // Printable ASCII
                if (b >= 32 && b <= 126)
                    sb.Append((char)b);
                else
                    sb.Append('.');
            }

            sb.AppendLine();
        }

        if (bytes.Length > maxBytes)
        {
            sb.AppendLine();
            sb.AppendLine($"... [Display truncated: Showing first {FormatFileSize(maxBytes)} of {FormatFileSize(bytes.Length)}. Use Download or Copy Hex for complete data.]");
        }

        return sb.ToString();
    }
}
