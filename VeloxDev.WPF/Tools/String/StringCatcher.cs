using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VeloxDev.WPF.Tools.String
{
    /// <summary>
    /// 🧰 > StringCatcher is a utility class for extracting and manipulating strings using regular expressions.
    /// <para>Core</para>
    /// <para>- ( Async ) <see cref="Between"/></para>
    /// <para>- ( Async ) <see cref="Like"/></para>
    /// <para>- ( Async ) <see cref="Hierarchical"/></para>
    /// <para>- ( Async ) <see cref="Numbers"/></para>
    /// <para>- ( Async ) <see cref="Words"/></para>
    /// <para>- ( Async ) <see cref="Chinese"/></para>
    /// </summary>
    public static partial class StringCatcher
    {
        #region Precompile regular expressions
        private static readonly Regex HierarchicalRegex = new(
            @"\{((?>[^{}]+|\{(?<Depth>)|}(?<-Depth>))*(?(Depth)(?!)))}",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(500)
        );

        private static readonly Regex ChineseRegex = new(
            @"[\u4e00-\u9fff]+",
            RegexOptions.Compiled
        );

        private static readonly Regex WordsRegex = new(
            @"\b[A-Za-z]+\b",
            RegexOptions.Compiled
        );

        private static readonly Regex NumbersRegex = new(
            @"\d+",
            RegexOptions.Compiled
        );

        private static readonly Dictionary<string, Regex> DynamicRegexCache = [];
        private static readonly object CacheLock = new();
        #endregion

        #region Synchronous method
        public static IEnumerable<string> Between(string input, string start, string end)
        {
            if (string.IsNullOrEmpty(input)) yield break;

            var pattern = $"{EscapeForRegex(start)}(.*?){EscapeForRegex(end)}";
            var regex = GetCachedRegex(pattern);

            foreach (Match match in regex.Matches(input))
            {
                if (match.Success)
                    yield return match.Groups[1].Value;
            }
        }

        public static IEnumerable<string> Like(string input, params string[] features)
        {
            if (features == null || features.Length == 0)
                throw new ArgumentException("At least one feature must be specified");

            string pattern = string.Join(".*?", features.Select(EscapeForRegex));
            var regex = GetCachedRegex(pattern);

            foreach (Match match in regex.Matches(input))
            {
                yield return match.Value;
            }
        }

        public static IEnumerable<IEnumerable<string>> Hierarchical(string input)
        {
            var currentLevel = GetTopLevelBraces(input).Select(s => s.Trim()).ToList();
            if (currentLevel.Count == 0) yield break;

            while (currentLevel.Count > 0)
            {
                yield return currentLevel;
                currentLevel = [.. currentLevel.SelectMany(GetTopLevelBraces).Select(s => s.Trim())];
            }
        }

        private static IEnumerable<string> GetTopLevelBraces(string input)
        {
            foreach (Match match in HierarchicalRegex.Matches(input))
            {
                if (match.Success)
                    yield return match.Groups[1].Value;
            }
        }

        public static IEnumerable<string> Numbers(string input, int minLength = 1, int maxLength = int.MaxValue)
        {
            ValidateLengths(minLength, maxLength);

            foreach (Match match in NumbersRegex.Matches(input))
            {
                if (match.Length >= minLength && match.Length <= maxLength)
                    yield return match.Value;
            }
        }

        public static IEnumerable<string> Words(string input, int minLength = 1, int maxLength = int.MaxValue)
        {
            ValidateLengths(minLength, maxLength);

            foreach (Match match in WordsRegex.Matches(input))
            {
                if (match.Length >= minLength && match.Length <= maxLength)
                    yield return match.Value;
            }
        }

        public static IEnumerable<string> Chinese(string input, int minLength = 1, int maxLength = int.MaxValue)
        {
            ValidateLengths(minLength, maxLength);

            foreach (Match match in ChineseRegex.Matches(input))
            {
                if (match.Length >= minLength && match.Length <= maxLength)
                    yield return match.Value;
            }
        }
        #endregion

        #region Asynchronous method
        public static async Task<IEnumerable<string>> BetweenAsync(
            TextReader reader,
            string start,
            string end,
            int bufferSize = 4096,
            CancellationToken token = default)
        {
            var result = new List<string>();
            var buffer = new char[bufferSize];
            var leftover = new StringBuilder();
            var pattern = $"{EscapeForRegex(start)}(.*?){EscapeForRegex(end)}";
            var regex = GetCachedRegex(pattern);

            while (!token.IsCancellationRequested)
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                var chunk = leftover.ToString() + new string(buffer, 0, bytesRead);
                leftover.Clear();

                foreach (Match match in regex.Matches(chunk))
                {
                    if (match.Success)
                        result.Add(match.Groups[1].Value);
                }

                var lastEnd = chunk.LastIndexOf(end, StringComparison.Ordinal);
                if (lastEnd != -1 && lastEnd < chunk.Length - end.Length)
                {
                    _ = leftover.Append(chunk.Substring(lastEnd + end.Length));
                }
            }

            return result;
        }

        public static async Task<IEnumerable<IEnumerable<string>>> HierarchicalAsync(
            string input,
            CancellationToken token = default)
        {
            var result = new List<List<string>>();
            var currentLevel = GetTopLevelBraces(input).Select(s => s.Trim()).ToList();

            if (currentLevel.Count == 0)
                return result;

            result.Add(currentLevel);

            while (currentLevel.Count > 0 && !token.IsCancellationRequested)
            {
                var nextLevelTasks = currentLevel.Select(str =>
                    Task.Run(() => GetTopLevelBraces(str).Select(s => s.Trim()).ToList(), token)
                ).ToList();

                var nextLevels = await Task.WhenAll(nextLevelTasks);
                var flattened = nextLevels.SelectMany(l => l).ToList();

                if (flattened.Count > 0)
                    result.Add(flattened);

                currentLevel = flattened;
            }

            return result;
        }

        public static async Task<IEnumerable<string>> LikeAsync(
            TextReader reader,
            string[] features,
            int bufferSize = 4096,
            CancellationToken token = default)
        {
            if (features == null || features.Length == 0)
                throw new ArgumentException("At least one feature must be specified");

            var result = new List<string>();
            var buffer = new char[bufferSize];
            var leftover = new StringBuilder();
            string pattern = string.Join(".*?", features.Select(EscapeForRegex));
            var regex = GetCachedRegex(pattern);

            while (!token.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string chunk = leftover + new string(buffer, 0, bytesRead);
                leftover.Clear();

                foreach (Match match in regex.Matches(chunk))
                    result.Add(match.Value);

                int maxFeatureLen = features.Max(f => f.Length);
                leftover.Append(chunk.Substring(Math.Max(0, chunk.Length - maxFeatureLen * 2)));
            }

            return result.Distinct();
        }

        public static async Task<IEnumerable<string>> ChineseAsync(
            TextReader reader,
            int minLength = 1,
            int maxLength = int.MaxValue,
            int bufferSize = 4096,
            CancellationToken token = default)
        {
            var result = new List<string>();
            var buffer = new char[bufferSize];
            var leftover = new StringBuilder();

            while (!token.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string chunk = leftover + new string(buffer, 0, bytesRead);
                leftover.Clear();

                foreach (Match match in ChineseRegex.Matches(chunk))
                {
                    if (match.Length >= minLength && match.Length <= maxLength)
                        result.Add(match.Value);
                }

                int lastIndex = chunk.Length - 1;
                if (lastIndex >= 0 && IsChineseChar(chunk[lastIndex]))
                {
                    int start = lastIndex;
                    while (start > 0 && IsChineseChar(chunk[start - 1]))
                        start--;
                    _ = leftover.Append(chunk.AsSpan(start).ToString());
                }
            }

            return result;
        }

        public static async Task<IEnumerable<string>> WordsAsync(
            TextReader reader,
            int minLength = 1,
            int maxLength = int.MaxValue,
            int bufferSize = 4096,
            CancellationToken token = default)
        {
            var result = new List<string>();
            var buffer = new char[bufferSize];
            var leftover = new StringBuilder();

            while (!token.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string chunk = leftover + new string(buffer, 0, bytesRead);
                leftover.Clear();

                foreach (Match match in WordsRegex.Matches(chunk))
                {
                    if (match.Length >= minLength && match.Length <= maxLength)
                        result.Add(match.Value);
                }

                int lastIndex = chunk.Length - 1;
                while (lastIndex >= 0 && char.IsLetter(chunk[lastIndex]))
                    lastIndex--;

                if (lastIndex < chunk.Length - 1)
                    _ = leftover.Append(chunk.Substring(lastIndex + 1));
            }

            return result;
        }

        public static async Task<IEnumerable<string>> NumbersAsync(
            TextReader reader,
            int minLength = 1,
            int maxLength = int.MaxValue,
            int bufferSize = 4096,
            CancellationToken token = default)
        {
            var result = new List<string>();
            var buffer = new char[bufferSize];
            var leftover = new StringBuilder();

            while (!token.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string chunk = leftover + new string(buffer, 0, bytesRead);
                leftover.Clear();

                foreach (Match match in NumbersRegex.Matches(chunk))
                {
                    if (match.Length >= minLength && match.Length <= maxLength)
                        result.Add(match.Value);
                }

                int lastIndex = chunk.Length - 1;
                while (lastIndex >= 0 && char.IsDigit(chunk[lastIndex]))
                    lastIndex--;

                if (lastIndex < chunk.Length - 1)
                    _ = leftover.Append(chunk.Substring(lastIndex + 1));
            }

            return result;
        }
        #endregion

        #region Auxiliary methods
        private static bool IsChineseChar(char c) => c >= '\u4e00' && c <= '\u9fff';

        private static void ValidateLengths(int minLength, int maxLength)
        {
            if (minLength < 1)
                throw new ArgumentOutOfRangeException(nameof(minLength), "minLength must be at least 1");
            if (maxLength < minLength)
                throw new ArgumentOutOfRangeException(nameof(maxLength), "maxLength must be ≥ minLength");
        }

        private static Regex GetCachedRegex(string pattern)
        {
            lock (CacheLock)
            {
                if (!DynamicRegexCache.TryGetValue(pattern, out var regex))
                {
                    regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
                    DynamicRegexCache[pattern] = regex;
                }
                return regex;
            }
        }

        private static string EscapeForRegex(string input) => Regex.Escape(input);

        public static IEnumerable<string> NumbersSpan(string input, int minLength = 1, int maxLength = int.MaxValue)
        {
            ValidateLengths(minLength, maxLength);

            for (int i = 0; i < input.Length; i++)
            {
                if (!char.IsDigit(input[i])) continue;

                int start = i;
                while (i < input.Length && char.IsDigit(input[i])) i++;

                int length = i - start;
                if (length >= minLength && length <= maxLength)
                    yield return input.Substring(start, length);
            }
        }
        #endregion
    }
}