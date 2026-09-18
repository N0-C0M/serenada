using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace SerenadaApp;

internal static class WindowChrome
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    public static void PreferRoundedCorners(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(
                hwnd,
                DwmwaWindowCornerPreference,
                ref preference,
                sizeof(int));
        }
        catch
        {
            // Rounded corners are visual polish only and must never block startup.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
