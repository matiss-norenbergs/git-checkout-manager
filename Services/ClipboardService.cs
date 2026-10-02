using System.Windows;

namespace GitSparseManager.Services
{
    public class ClipboardService
    {
        public void CopyText(string text) => Clipboard.SetText(text);
    }
}
