using System;
using System.Windows;

namespace GitCheckoutManager.Views
{
    internal static class WindowSizing
    {
        private const double ScreenMargin = 40;

        /// <summary>
        /// Sizes <paramref name="w"/> to the preferred size, capped so it fits the work area with a margin, but never below its
        /// MinWidth/MinHeight (a screen smaller than the minimum gets the minimum). Call after InitializeComponent, before Show.
        /// Note: <see cref="SystemParameters.WorkArea"/> is the primary monitor's work area in DIPs, not the monitor the window
        /// will open on; acceptable for now.
        /// </summary>
        public static void FitToWorkArea(Window w, double preferredWidth, double preferredHeight)
        {
            var area = SystemParameters.WorkArea;
            w.Width = Math.Max(w.MinWidth, Math.Min(preferredWidth, area.Width - ScreenMargin));
            w.Height = Math.Max(w.MinHeight, Math.Min(preferredHeight, area.Height - ScreenMargin));
        }
    }
}
