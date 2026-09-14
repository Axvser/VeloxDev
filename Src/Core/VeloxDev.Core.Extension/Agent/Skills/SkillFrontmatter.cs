using System;
using System.Text.RegularExpressions;

namespace VeloxDev.AI.Skills;

/// <summary>
/// Reads the YAML frontmatter of an Agent Skills <c>SKILL.md</c>.
/// <para>
/// Only <c>name</c> and <c>description</c> are extracted — the two keys that decide whether a skill is
/// advertised and what it matches against. Other keys (<c>license</c>, <c>compatibility</c>,
/// <c>allowed-tools</c>, <c>metadata</c>) are tolerated and skipped. This is a line-oriented reader, not
/// a YAML parser: the extension project targets <c>netstandard2.0</c> and deliberately carries no YAML
/// dependency, and a skill header is two scalars.
/// </para>
/// <para>
/// The validation rules match the Agent Framework's own file skills source so that one skill directory
/// works in both places: kebab-case names, a 64-character name limit and a 1024-character description
/// limit.
/// </para>
/// </summary>
internal static class SkillFrontmatter
{
    internal const int MaxNameLength = 64;
    internal const int MaxDescriptionLength = 1024;

    /// <summary>
    /// Lowercase kebab-case, no leading/trailing/doubled hyphen. The trailing group is required: without
    /// it the pattern would only accept names whose final hyphen-delimited segment is a single character.
    /// </summary>
    private static readonly Regex s_namePattern = new(
        @"^[a-z0-9]([a-z0-9]*-[a-z0-9])*[a-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits <paramref name="text"/> into its frontmatter values and body.
    /// Returns <c>false</c> (with <paramref name="error"/> set) when the frontmatter is missing or the
    /// name is invalid; the caller decides whether that is fatal for the skill or for the whole load.
    /// </summary>
    public static bool TryParse(
        string text,
        out string? name,
        out string? description,
        out string body,
        out string? error)
    {
        name = null;
        description = null;
        body = text ?? string.Empty;
        error = null;

        if (string.IsNullOrEmpty(text)) { error = "SKILL.md is empty."; return false; }

        // Strip a UTF-8 BOM: a file saved by a Windows editor otherwise fails the opening-delimiter test.
        var content = text!.TrimStart((char)0xFEFF);
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int open = 0;
        while (open < lines.Length && string.IsNullOrWhiteSpace(lines[open])) open++;

        if (open >= lines.Length || lines[open].Trim() != "---")
        {
            error = "SKILL.md does not start with a '---' frontmatter block.";
            return false;
        }

        int close = -1;
        for (int i = open + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { close = i; break; }
        }
        if (close < 0)
        {
            error = "SKILL.md frontmatter is not closed by a '---' line.";
            return false;
        }

        for (int i = open + 1; i < close; i++)
        {
            var line = lines[i];
            // Indented lines belong to a nested block (e.g. `metadata:`), which this reader skips.
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue;

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line.Substring(0, colon).Trim();
            var value = Unquote(line.Substring(colon + 1).Trim());

            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)) name = value;
            else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase)) description = value;
        }

        body = string.Join("\n", lines, close + 1, lines.Length - close - 1).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "SKILL.md frontmatter has no 'name'.";
            return false;
        }
        if (name!.Length > MaxNameLength)
        {
            error = $"Skill name '{name}' exceeds {MaxNameLength} characters.";
            return false;
        }
        if (!s_namePattern.IsMatch(name))
        {
            error = $"Skill name '{name}' is not lowercase kebab-case (expected e.g. 'my-skill-name').";
            return false;
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            error = "SKILL.md frontmatter has no 'description'.";
            return false;
        }
        if (description!.Length > MaxDescriptionLength)
        {
            error = $"Skill description exceeds {MaxDescriptionLength} characters.";
            return false;
        }

        return true;
    }

    /// <summary>Strips one layer of matching single or double quotes, if present.</summary>
    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            if ((first == '"' || first == '\'') && value[value.Length - 1] == first)
                return value.Substring(1, value.Length - 2);
        }
        return value;
    }
}
