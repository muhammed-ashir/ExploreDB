using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using WinDataTransfer = Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ExploreDB.Services;

public enum FileShareOutcome
{
    NativeShareOpened,
    FallbackClipboardAndExplorer,
    Failed
}

public class WindowManagerService
{
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid IidDataTransferManager = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    private readonly IServiceProvider _serviceProvider;
    private readonly QueryStateService _queryStateService;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<Guid, Window> _openQueryPopouts = new();
    private readonly List<Window> _openSchemaPopouts = new();
    private readonly Dictionary<string, Window> _openToolPopouts = new(StringComparer.OrdinalIgnoreCase);

    private IntPtr _shareHookedHwnd = IntPtr.Zero;
    private WinDataTransfer.DataTransferManager? _shareTransferManager;
    private string? _pendingShareFilePath;
    private string _pendingShareTitle = string.Empty;
    private string _pendingShareDescription = string.Empty;

    public event Action<string>? OnNavigateMainWindowRequested;

    public WindowManagerService(IServiceProvider serviceProvider, QueryStateService queryStateService, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _queryStateService = queryStateService;
        _configuration = configuration;
    }

    public void OpenQueryPopout(Guid tabId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openQueryPopouts.TryGetValue(tabId, out var existingWindow))
            {
                if (existingWindow.WindowState == WindowState.Minimized)
                {
                    existingWindow.WindowState = WindowState.Maximized;
                }
                existingWindow.Activate();
                return;
            }

            var tab = _queryStateService.Tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab == null) return;

            tab.IsPoppedOut = true;
            _queryStateService.NotifyTabUpdated(tabId);

            var popoutWindow = new PopoutWindow(tabId, _serviceProvider);
            var title = string.IsNullOrEmpty(tab.FilePath) ? tab.Name : Path.GetFileName(tab.FilePath);
            popoutWindow.Title = title;

            _openQueryPopouts[tabId] = popoutWindow;

            popoutWindow.Closed += (s, e) =>
            {
                _openQueryPopouts.Remove(tabId);
                if (popoutWindow.IsDocking)
                {
                    tab.IsPoppedOut = false;
                    _queryStateService.ActiveTabId = tab.Id;
                }
                else
                {
                    if (tab.QueryCts != null && !tab.QueryCts.IsCancellationRequested)
                    {
                        try { tab.QueryCts.Cancel(); } catch { }
                    }
                    _queryStateService.Tabs.Remove(tab);
                }
                _queryStateService.NotifyTabUpdated(tab.Id);
                _ = _queryStateService.SaveTabsAsync();
            };

            popoutWindow.Show();
        });
    }

    public void DockQueryTab(Guid tabId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openQueryPopouts.TryGetValue(tabId, out var window) && window is PopoutWindow pw)
            {
                pw.IsDocking = true;
                pw.Close();
            }
            Application.Current.MainWindow?.Activate();
        });
    }

    public void SetPopoutTitle(Guid tabId, string title)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openQueryPopouts.TryGetValue(tabId, out var window))
            {
                window.Title = title;
            }
        });
    }

    public bool OpenSchemaPopout(string startPath, string title)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            int maxSchemaPopouts = _configuration.GetValue<int>("MaxSchemaPopouts", 5);
            _openSchemaPopouts.RemoveAll(w => !w.IsLoaded);

            if (_openSchemaPopouts.Count >= maxSchemaPopouts)
            {
                return false;
            }

            var popoutWindow = new SchemaPopoutWindow(startPath, title, _serviceProvider);
            _openSchemaPopouts.Add(popoutWindow);

            popoutWindow.Closed += (s, e) =>
            {
                _openSchemaPopouts.Remove(popoutWindow);
            };

            popoutWindow.Show();
            return true;
        });
    }

    public void NavigateMainWindow(string route)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (Application.Current.MainWindow != null)
            {
                if (Application.Current.MainWindow.WindowState == WindowState.Minimized)
                {
                    Application.Current.MainWindow.WindowState = WindowState.Normal;
                }
                Application.Current.MainWindow.Activate();
            }
            OnNavigateMainWindowRequested?.Invoke(route);
        });
    }

    public bool OpenToolPopout(string toolId, string startPath, string title, bool closeOnConnectionChange = false)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openToolPopouts.TryGetValue(toolId, out var existingWindow))
            {
                if (existingWindow.IsLoaded)
                {
                    if (existingWindow.WindowState == WindowState.Minimized)
                    {
                        existingWindow.WindowState = WindowState.Maximized;
                    }
                    existingWindow.Activate();
                    return true;
                }
                else
                {
                    _openToolPopouts.Remove(toolId);
                }
            }

            var popoutWindow = new ToolPopoutWindow(toolId, startPath, title, _serviceProvider, closeOnConnectionChange);
            _openToolPopouts[toolId] = popoutWindow;

            popoutWindow.Closed += (s, e) =>
            {
                _openToolPopouts.Remove(toolId);
            };

            popoutWindow.Show();
            return true;
        });
    }

    public async Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filter = "SQL Script (*.sql)|*.sql|Plain Text (*.txt)|*.txt|All Files (*.*)|*.*", string defaultExt = ".sql")
    {
        await Task.Yield();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = filter,
                DefaultExt = defaultExt
            };

            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) 
                        ?? Application.Current.MainWindow;
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }, DispatcherPriority.Background);
    }

    public async Task<string?> ShowOpenFileDialogAsync(string filter = "SQL Script (*.sql)|*.sql|All Files (*.*)|*.*", string? title = null, string defaultExt = ".sql")
    {
        await Task.Yield();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt
            };

            if (!string.IsNullOrEmpty(title))
            {
                dialog.Title = title;
            }

            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) 
                        ?? Application.Current.MainWindow;
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }, DispatcherPriority.Background);
    }

    public async Task<bool> TrySetClipboardTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return false;

        bool SetCore()
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (dispatcher.CheckAccess())
        {
            return SetCore();
        }

        return await dispatcher.InvokeAsync(SetCore).Task;
    }

    public void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.Invoke(() =>
        {
            try
            {
                Clipboard.SetDataObject(text, true);
            }
            catch { }
        });
    }

    public void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    public async Task<(FileShareOutcome Outcome, string? ErrorMessage)> ShareFileAsync(string filePath, string title, string description)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return (FileShareOutcome.Failed, "Log file does not exist.");
        }

        await Task.Yield();
        return await Application.Current.Dispatcher.InvokeAsync<(FileShareOutcome Outcome, string? ErrorMessage)>(() =>
        {
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current.MainWindow;

            if (owner != null)
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(owner).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        var interop = WinDataTransfer.DataTransferManager.As<IDataTransferManagerInterop>();
                        if (_shareTransferManager == null || _shareHookedHwnd != hwnd)
                        {
                            if (_shareTransferManager != null)
                            {
                                _shareTransferManager.DataRequested -= OnShareDataRequested;
                            }

                            Guid iid = IidDataTransferManager;
                            IntPtr dtmPtr = interop.GetForWindow(hwnd, ref iid);
                            if (dtmPtr != IntPtr.Zero)
                            {
                                try
                                {
                                    _shareTransferManager = WinDataTransfer.DataTransferManager.FromAbi(dtmPtr);
                                }
                                finally
                                {
                                    Marshal.Release(dtmPtr);
                                }

                                _shareTransferManager.DataRequested += OnShareDataRequested;
                                _shareHookedHwnd = hwnd;
                            }
                        }

                        if (_shareTransferManager != null)
                        {
                            _pendingShareFilePath = filePath;
                            _pendingShareTitle = title;
                            _pendingShareDescription = description;
                            interop.ShowShareUIForWindow(hwnd);
                            return (FileShareOutcome.NativeShareOpened, null);
                        }
                    }
                }
                catch
                {
                    // Native Share sheet may be unavailable in unpackaged/elevated sessions; fall back cleanly below.
                }
            }

            try
            {
                var files = new StringCollection { filePath };
                Clipboard.SetFileDropList(files);
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true
                });
                return (FileShareOutcome.FallbackClipboardAndExplorer, null);
            }
            catch (Exception ex)
            {
                return (FileShareOutcome.Failed, ex.Message);
            }
        }, DispatcherPriority.Background);
    }

    private async void OnShareDataRequested(WinDataTransfer.DataTransferManager sender, WinDataTransfer.DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            string? path = _pendingShareFilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(path);
                args.Request.Data.Properties.Title = _pendingShareTitle;
                args.Request.Data.Properties.Description = _pendingShareDescription;
                args.Request.Data.SetStorageItems(new IStorageItem[] { storageFile });
            }
        }
        catch { }
        finally
        {
            deferral.Complete();
        }
    }
}
