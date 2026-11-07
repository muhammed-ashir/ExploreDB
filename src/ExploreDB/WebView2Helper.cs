using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ExploreDB;

public static class WebView2Helper
{
    public static void Configure(BlazorWebView blazorWebView, string windowName)
    {
        blazorWebView.BlazorWebViewInitialized += (sender, args) =>
        {
            try
            {
                var coreWebView = args.WebView?.CoreWebView2;
                if (coreWebView != null)
                {
                    coreWebView.ProcessFailed += (s, e) =>
                    {
                        Log.Error(
                            "WebView2 ProcessFailed in {WindowName}: Kind={ProcessFailedKind}, Reason={Reason}, ExitCode={ExitCode}, Description={ProcessDescription}",
                            windowName, e.ProcessFailedKind, e.Reason, e.ExitCode, e.ProcessDescription);

                        // If a non-fatal process (e.g. GPU process) exited, Chromium auto-recovers.
                        // If the main render process crashed or became unresponsive, attempt automatic reload.
                        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                        {
                            try
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Log.Information("Attempting to reload WebView2 after RenderProcessExited in {WindowName}...", windowName);
                                    args.WebView?.Reload();
                                });
                            }
                            catch (Exception reloadEx)
                            {
                                Log.Error(reloadEx, "Failed to reload WebView2 after RenderProcessExited in {WindowName}", windowName);
                            }
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to attach ProcessFailed handler for {WindowName}", windowName);
            }
        };
    }

    public static async Task SafeDisposeAsync(BlazorWebView? blazorWebView, string windowName)
    {
        if (blazorWebView == null) return;

        try
        {
            await blazorWebView.DisposeAsync();
            Log.Debug("Successfully disposed BlazorWebView for {WindowName}", windowName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception while disposing BlazorWebView for {WindowName}", windowName);
        }
    }
}
