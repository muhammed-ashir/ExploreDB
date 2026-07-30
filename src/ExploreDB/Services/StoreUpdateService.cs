using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#if WINDOWS
using Windows.Services.Store;
#endif

namespace ExploreDB.Services;

public class StoreUpdateService
{
    private readonly ILogger<StoreUpdateService> _logger;

    public StoreUpdateService(ILogger<StoreUpdateService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if there are any mandatory or optional updates in the Microsoft Store.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync()
    {
#if WINDOWS
        try
        {
            var storeContext = StoreContext.GetDefault();
            
            // In WinUI 3 / MAUI Desktop, we must associate the StoreContext with the main window handle
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(winuiWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(storeContext, hwnd);
            }

            var updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates != null && updates.Count > 0)
            {
                _logger.LogInformation($"Found {updates.Count} available Store updates.");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for Microsoft Store updates.");
        }
#endif
        return false;
    }

    /// <summary>
    /// Prompts the user to download and install the updates.
    /// This will automatically close and restart the app if the update is successful.
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync(Action<double>? progressCallback = null)
    {
#if WINDOWS
        try
        {
            var storeContext = StoreContext.GetDefault();
            
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(winuiWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(storeContext, hwnd);
            }

            var updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates != null && updates.Count > 0)
            {
                // This will show a system UI to download and install the update
                var operation = storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
                
                operation.Progress = (asyncInfo, progressInfo) =>
                {
                    if (progressCallback != null && progressInfo.PackageDownloadSizeInBytes > 0)
                    {
                        double percentage = ((double)progressInfo.PackageBytesDownloaded / progressInfo.PackageDownloadSizeInBytes) * 100.0;
                        progressCallback(Math.Min(100.0, Math.Max(0.0, percentage)));
                    }
                };

                var result = await operation;
                
                if (result.OverallState == StorePackageUpdateState.Completed)
                {
                    // The app usually restarts automatically upon completion, but if we reach here:
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Store update didn't complete successfully. State: {result.OverallState}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download and install Microsoft Store updates.");
        }
#endif
        return false;
    }
}
