using System.Net.Http.Headers;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace ExploreDB.Services;

public record UpdateInfo(string DownloadUrl, bool IsMandatory);

public class GitHubUpdateService
{
    private static readonly HttpClient _httpClient;
    private const string GitHubApiUrl = "https://api.github.com/repos/zerinapps/ExploreDB-Releases/releases/latest";

    static GitHubUpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ExploreDB", "1.0"));
    }

    /// <summary>
    /// Checks for a new release on GitHub. 
    /// Returns the UpdateInfo if a new version is available, otherwise returns null.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement)) return null;

            string tagName = tagElement.GetString() ?? "";
            
            // Clean the 'v' prefix if it exists
            string latestVersionStr = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
                ? tagName.Substring(1) 
                : tagName;

            if (Version.TryParse(latestVersionStr, out var latestVersion))
            {
                var currentVersion = AppInfo.Current.Version;

                if (latestVersion > currentVersion)
                {
                    // Look for the .msix asset
                    if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assetsElement.EnumerateArray())
                        {
                            if (asset.TryGetProperty("name", out var nameElement) && 
                                nameElement.GetString()?.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                if (asset.TryGetProperty("browser_download_url", out var urlElement))
                                {
                                    var downloadUrl = urlElement.GetString() ?? "";
                                    bool isMandatory = false;
                                    
                                    if (root.TryGetProperty("body", out var bodyElement))
                                    {
                                        var bodyText = bodyElement.GetString() ?? "";
                                        if (bodyText.Contains("[CRITICAL UPDATE]", StringComparison.OrdinalIgnoreCase))
                                        {
                                            isMandatory = true;
                                        }
                                    }
                                    
                                    return new UpdateInfo(downloadUrl, isMandatory);
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }
        catch
        {
            // Fail silently so it doesn't crash the app if offline
            return null;
        }
    }

    /// <summary>
    /// Downloads the update to the cache directory and executes it.
    /// </summary>
    public async Task<bool> DownloadAndExecuteUpdateAsync(string downloadUrl)
    {
        try
        {
            var fileName = "ExploreDB_Update.msix";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

            // Download the file
            var response = await _httpClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
            fs.Close();

            // Execute the installer
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
}
