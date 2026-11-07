using System;
using System.IO;
using System.Reflection;

namespace ExploreDB.Services;

public static class AppEnvironmentService
{
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
}
