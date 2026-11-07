using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ExploreDB;

public static class WindowIconHelper
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, int uType, int cxDesired, int cyDesired, int fuLoad);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static void ApplyWindowIcon(Window window)
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "app.ico");
            if (!File.Exists(iconPath))
            {
                return;
            }

            // 1. Set WPF Window.Icon to the highest resolution frame (256x256)
            // Never default to 16x16 which causes tiny 12px taskbar rendering in WPF.
            var decoder = new IconBitmapDecoder(
                new Uri(iconPath),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);

            var largestFrame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            if (largestFrame != null)
            {
                window.Icon = largestFrame;
            }

            // 2. Set explicit DPI-scaled native icons for titlebar (small) and taskbar (big)
            void OnSourceInitialized()
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero)
                    {
                        return;
                    }

                    int cxSmall = GetSystemMetrics(SM_CXSMICON);
                    int cySmall = GetSystemMetrics(SM_CYSMICON);
                    int cxBig = GetSystemMetrics(SM_CXICON);
                    int cyBig = GetSystemMetrics(SM_CYICON);

                    if (cxSmall <= 0) cxSmall = 16;
                    if (cySmall <= 0) cySmall = 16;
                    if (cxBig <= 0) cxBig = 32;
                    if (cyBig <= 0) cyBig = 32;

                    IntPtr hSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, cxSmall, cySmall, LR_LOADFROMFILE);
                    IntPtr hBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, cxBig, cyBig, LR_LOADFROMFILE);

                    if (hSmall != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
                    }
                    if (hBig != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
                    }
                }
                catch { }
            }

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                OnSourceInitialized();
            }
            else
            {
                window.SourceInitialized += (s, e) => OnSourceInitialized();
            }
        }
        catch { }
    }
}
