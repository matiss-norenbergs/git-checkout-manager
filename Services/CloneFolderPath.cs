using System.IO;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Validates and normalizes the "Folder" input of the Clone tab. It is a name or a relative path such as
    /// "folderA/sub/target"; either slash may separate the segments. Git creates the missing parent folders when it clones.
    /// </summary>
    public static class CloneFolderPath
    {
        private static readonly char[] Separators = ['/', '\\'];

        /// <summary>The segments of a valid path, or null when the input isn't a valid relative folder path.</summary>
        public static IReadOnlyList<string>? Split(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            // A leading slash would mean a rooted path; drive letters ("C:") fail the segment check below.
            if (input[0] is '/' or '\\') return null;

            // Empty segments (a doubled or trailing slash) are ignored.
            var segments = input.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || !segments.All(IsValidSegment)) return null;
            return segments;
        }

        /// <summary>The path with the platform's separator, or null when it isn't valid.</summary>
        public static string? Normalize(string? input) =>
            Split(input) is { } segments ? string.Join(Path.DirectorySeparatorChar, segments) : null;

        /// <summary>
        /// The path with the given separator, for a script that runs under another shell than this app. A trailing separator is
        /// dropped: in a quoted bat argument <c>"a\b\"</c> reads as an escaped quote.
        /// </summary>
        public static string WithSeparator(string path, char separator) =>
            path.Replace('/', separator).Replace('\\', separator).TrimEnd(separator);

        private static bool IsValidSegment(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            name != "." && name != ".." &&
            !name.EndsWith('.') && !name.EndsWith(' ');
    }
}
