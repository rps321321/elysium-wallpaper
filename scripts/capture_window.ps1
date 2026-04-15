<#
.SYNOPSIS
    Captures a single window to PNG via Win32 BitBlt — clean, no overlapping windows.

.PARAMETER WindowTitle
    Substring of the window title. First match wins.

.PARAMETER Output
    Absolute path to write the PNG to.
#>
param(
    [Parameter(Mandatory)] [string]$WindowTitle,
    [Parameter(Mandatory)] [string]$Output
)

Add-Type @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;

public class WindowCapture {
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static void Capture(IntPtr hWnd, string outputPath) {
        SetForegroundWindow(hWnd);
        ShowWindow(hWnd, 9);  // SW_RESTORE
        System.Threading.Thread.Sleep(800);

        RECT r;
        if (!GetWindowRect(hWnd, out r)) throw new Exception("GetWindowRect failed.");
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) throw new Exception("Invalid window bounds.");

        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            try {
                // PrintWindow with PW_RENDERFULLCONTENT (0x02) handles DWM-composed (WinUI) windows.
                if (!PrintWindow(hWnd, hdc, 0x02))
                    throw new Exception("PrintWindow failed.");
            } finally {
                g.ReleaseHdc(hdc);
            }
            bmp.Save(outputPath, ImageFormat.Png);
        }
    }
}
"@ -ReferencedAssemblies System.Drawing -ErrorAction Stop

$proc = Get-Process | Where-Object {
    # Use -match with an escaped pattern instead of -like: titles containing [, ], ?, or *
    # are treated as glob metacharacters by -like and either match the wrong window or nothing.
    $_.MainWindowTitle -match [regex]::Escape($WindowTitle) -and $_.MainWindowHandle -ne 0
} | Select-Object -First 1
if (-not $proc) { throw "No window matching '*$WindowTitle*' found." }

Write-Host "Capturing $($proc.MainWindowTitle) (PID $($proc.Id)) to $Output" -ForegroundColor Cyan
[WindowCapture]::Capture($proc.MainWindowHandle, $Output)

$f = Get-Item $Output
Write-Host ("Saved {0} ({1:N0} bytes)" -f $f.FullName, $f.Length) -ForegroundColor Green
