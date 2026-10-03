using System.Windows;

namespace GitCheckoutManager.Services
{
    public class ClipboardService
    {
        public void CopyText(string text) => Clipboard.SetText(text);
    }
}
