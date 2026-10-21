using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.Configuration;

namespace ExploreDB.Services;

public class WindowManagerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly QueryStateService _queryStateService;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<Guid, Window> _openQueryPopouts = new();
    private readonly List<Window> _openSchemaPopouts = new();

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

    public string? ShowSaveFileDialog(string defaultFileName, string filter = "SQL Script (*.sql)|*.sql|Plain Text (*.txt)|*.txt|All Files (*.*)|*.*")
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = filter,
                DefaultExt = ".sql"
            };

            return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
        });
    }

    public string? ShowOpenFileDialog(string filter = "SQL Script (*.sql)|*.sql|All Files (*.*)|*.*")
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                DefaultExt = ".sql"
            };

            return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
        });
    }

    public void SetClipboardText(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
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
}
