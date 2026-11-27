using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ExploreDB.Services;

public static class AppEnvironmentService
{
    public sealed record LogDateSummary(
        bool Exists,
        int FileCount,
        long TotalBytes,
        string FormattedSize,
        string PrimaryFileName,
        IReadOnlyList<string> Files);

    private static readonly Lazy<string> _appDataDir = new(() =>
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExploreDB");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    });

    public static string AppDataDirectory => _appDataDir.Value;

    public static string LogsDirectory => Path.Combine(AppDataDirectory, "logs");

    private static readonly Lazy<string> _cacheDir = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ExploreDB");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    });

    public static string CacheDirectory => _cacheDir.Value;

    public static Version Version
    {
        get
        {
            try
            {
                var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
                return new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
            }
            catch
            {
                return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 1, 2, 0);
            }
        }
    }

    public static string VersionString
    {
        get
        {
            var v = Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static IReadOnlyList<string> GetLogFilesForDate(DateTime date)
    {
        if (!Directory.Exists(LogsDirectory))
            return Array.Empty<string>();

        string stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var files = new List<string>();

        string primary = Path.Combine(LogsDirectory, $"log-{stamp}.txt");
        if (File.Exists(primary))
            files.Add(primary);

        var rolled = Directory.EnumerateFiles(LogsDirectory, $"log-{stamp}_*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        files.AddRange(rolled);

        return files;
    }

    public static LogDateSummary GetLogSummaryForDate(DateTime date)
    {
        var files = GetLogFilesForDate(date);
        if (files.Count == 0)
            return new LogDateSummary(false, 0, 0, "0 B", string.Empty, files);

        long totalBytes = 0;
        foreach (var file in files)
        {
            try { totalBytes += new FileInfo(file).Length; } catch { }
        }

        string primaryName = Path.GetFileName(files[0]);
        string label = files.Count == 1 ? primaryName : $"{primaryName} (+{files.Count - 1} more)";
        return new LogDateSummary(true, files.Count, totalBytes, FormatByteSize(totalBytes), label, files);
    }

    public static async Task<long> ExportLogSnapshotAsync(DateTime date, string destinationPath)
    {
        var files = GetLogFilesForDate(date);
        if (files.Count == 0)
            throw new FileNotFoundException($"No log file found for {date:yyyy-MM-dd}.");

        string? destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        long writtenBytes = 0;
        byte[] newline = Encoding.UTF8.GetBytes(Environment.NewLine);

        await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        for (int i = 0; i < files.Count; i++)
        {
            if (i > 0)
            {
                await destStream.WriteAsync(newline);
                writtenBytes += newline.Length;
            }

            // Use ReadWrite | Delete share so today's actively written Serilog file can be read without locking errors.
            await using var srcStream = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await srcStream.CopyToAsync(destStream);
            writtenBytes += srcStream.Length;
        }

        await destStream.FlushAsync();
        return writtenBytes;
    }

    public static async Task<string> CreateTempLogSnapshotAsync(DateTime date)
    {
        string snapshotPath = Path.Combine(CacheDirectory, $"ExploreDB_Log_{date:yyyy-MM-dd}.txt");
        await ExportLogSnapshotAsync(date, snapshotPath);
        return snapshotPath;
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        return $"{mb:0.##} MB";
    }
}
