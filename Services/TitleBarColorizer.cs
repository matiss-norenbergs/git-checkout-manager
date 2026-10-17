using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GitCheckoutManager.Services
{
    internal static class TitleBarColorizer
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // builds 17763-18984
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE     = 20; // build 18985+
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR    = 36;

        // COLORREF is 0x00BBGGRR
        private static readonly uint DarkCaption  = ToColorRef(0x1E, 0x1E, 0x1E);
        private static readonly uint LightCaption = ToColorRef(0xF7, 0xF7, 0xF7);
        private static readonly uint DarkText     = ToColorRef(0xCC, 0xCC, 0xCC);
        private static readonly uint LightText    = ToColorRef(0x20, 0x20, 0x20);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint pvAttr, int cbAttribute);

        // Environment.OSVersion lies without a compatibility manifest; RtlGetVersion returns the real build.
        [StructLayout(LayoutKind.Sequential)]
        private struct OSVERSIONINFOEX
        {
            public uint dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor, wServicePackMinor, wSuiteMask;
            public byte   wProductType, wReserved;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX lpVersionInformation);

        private static readonly uint _build;

        static TitleBarColorizer()
        {
            var info = new OSVERSIONINFOEX { dwOSVersionInfoSize = (uint)Marshal.SizeOf<OSVERSIONINFOEX>() };
            _build = RtlGetVersion(ref info) == 0 ? info.dwBuildNumber : 0;
        }

        public static void Apply(Window window, bool isDark)
        {
            if (_build < 17763)
                return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var useDark = isDark ? 1u : 0u;
            DwmSetWindowAttribute(hwnd,
                _build >= 18985 ? DWMWA_USE_IMMERSIVE_DARK_MODE : DWMWA_USE_IMMERSIVE_DARK_MODE_OLD,
                ref useDark, sizeof(uint));

            if (_build < 22000)
                return;

            var caption = isDark ? DarkCaption : LightCaption;
            var text    = isDark ? DarkText    : LightText;

            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(uint));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR,    ref text,    sizeof(uint));
        }

        private static uint ToColorRef(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
    }
}
