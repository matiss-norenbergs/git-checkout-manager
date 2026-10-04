using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GitCheckoutManager.Controls
{
    /// <summary>TextBlock that bolds the first case-insensitive occurrence of <see cref="Query"/> in <see cref="Text"/>.</summary>
    public class HighlightTextBlock : TextBlock
    {
        public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
            nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(string.Empty, (d, _) => ((HighlightTextBlock)d).Rebuild()));

        public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
            nameof(Query), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(string.Empty, (d, _) => ((HighlightTextBlock)d).Rebuild()));

        public string SourceText
        {
            get => (string)GetValue(SourceTextProperty);
            set => SetValue(SourceTextProperty, value);
        }

        public string Query
        {
            get => (string)GetValue(QueryProperty);
            set => SetValue(QueryProperty, value);
        }

        private void Rebuild()
        {
            var text = SourceText ?? string.Empty;
            var query = Query ?? string.Empty;
            var index = query.Length == 0 ? -1 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);

            Inlines.Clear();
            if (index < 0)
            {
                Inlines.Add(new Run(text));
                return;
            }

            Inlines.Add(new Run(text[..index]));
            Inlines.Add(new Run(text.Substring(index, query.Length)) { FontWeight = FontWeights.Bold });
            Inlines.Add(new Run(text[(index + query.Length)..]));
        }
    }
}
