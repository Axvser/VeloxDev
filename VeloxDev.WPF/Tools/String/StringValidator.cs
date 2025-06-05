namespace VeloxDev.WPF.Tools.String
{
    /// <summary>
    /// 🧰 > StringValidator is a class that provides a fluent interface for validating strings.
    /// <para>Core</para>
    /// <para>- <see cref="StringValidator.AddRule(Func{string, bool})"/></para>
    /// <para>- <see cref="StringValidator.Validate(string)"/></para>
    /// <para>Helper</para>
    /// <para>- <see cref="StringValidator.StartWith(string, bool)"/></para>
    /// <para>- <see cref="StringValidator.EndWith(string, bool)"/></para>
    /// <para>- <see cref="StringValidator.VarLength(int, int)"/></para>
    /// <para>- <see cref="StringValidator.FixLength(int)"/></para>
    /// <para>- <see cref="StringValidator.Include(bool, string[])"/></para>
    /// <para>- <see cref="StringValidator.Exclude(bool, string[])"/></para>
    /// <para>- <see cref="StringValidator.Slice(int, string, bool)"/></para>
    /// <para>- <see cref="StringValidator.Regex(string)"/></para>
    /// <para>- <see cref="StringValidator.OnlyNumbers()"/></para>
    /// <para>- <see cref="StringValidator.OnlyWords()"/></para>
    /// </summary>
    public class StringValidator
    {
        private List<Func<string, bool>> _validationRules = [];

        public StringValidator StartWith(string value, bool ignoreCase = false)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _validationRules.Add(input =>
                    input.StartsWith(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            }
            return this;
        }
        public StringValidator EndWith(string value, bool ignoreCase = false)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _validationRules.Add(input =>
                    input.EndsWith(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            }
            return this;
        }
        public StringValidator VarLength(int min, int max)
        {
            if (max >= min)
            {
                _validationRules.Add(input => input.Length >= min && input.Length <= max);
            }
            return this;
        }
        public StringValidator FixLength(int length)
        {
            _validationRules.Add(input => input.Length == length);
            return this;
        }
        public StringValidator Include(params string[] substrings)
        {
            return Include(false, substrings);
        }
        public StringValidator Include(bool ignoreCase, params string[] substrings)
        {
            foreach (var substring in substrings)
            {
                if (!string.IsNullOrEmpty(substring))
                {
                    _validationRules.Add(input =>
                        input.IndexOf(substring, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) >= 0);
                }
            }
            return this;
        }
        public StringValidator Exclude(params string[] substrings)
        {
            return Exclude(false, substrings);
        }
        public StringValidator Exclude(bool ignoreCase, params string[] substrings)
        {
            foreach (var substring in substrings)
            {
                if (!string.IsNullOrEmpty(substring))
                {
                    _validationRules.Add(input =>
                        input.IndexOf(substring, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) < 0);
                }
            }
            return this;
        }
        public StringValidator Slice(int start, string value, bool ignoreCase = false)
        {
            _validationRules.Add(input =>
            {
                if (start < 0 || start + value.Length > input.Length)
                    return false;

                string actual = input.Substring(start, value.Length);
                return actual.Equals(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            });
            return this;
        }
        public StringValidator Regex(string pattern)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern);
                _validationRules.Add(input => regex.IsMatch(input));
            }
            return this;
        }
        public StringValidator OnlyNumbers()
        {
            _validationRules.Add(input => System.Text.RegularExpressions.Regex.IsMatch(input, @"^\d+$"));
            return this;
        }
        public StringValidator OnlyWords()
        {
            _validationRules.Add(input => System.Text.RegularExpressions.Regex.IsMatch(input, @"^[a-zA-Z]+$"));
            return this;
        }

        public StringValidator AddRule(Func<string, bool> rule)
        {
            if (rule != null)
            {
                _validationRules.Add(rule);
            }
            return this;
        }
        public bool Validate(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            foreach (var rule in _validationRules)
            {
                if (!rule(input))
                    return false;
            }
            return true;
        }
    }
}