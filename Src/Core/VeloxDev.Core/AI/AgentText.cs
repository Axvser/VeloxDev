namespace VeloxDev.AI;

/// <summary>
/// One <c>[AgentContext]</c> description, kept with the language it was written in.
/// </summary>
/// <remarks>
/// The compiled form of the annotation: a <see cref="AgentContextAttribute"/> becomes one of these, holding
/// the same two facts the attribute carries. Keeping the language alongside the text is what lets
/// <see cref="AgentTextSelection"/> apply the fallback rule after the attributes are gone.
/// </remarks>
public readonly struct AgentText
{
    /// <summary>
    /// Creates a description.
    /// </summary>
    /// <param name="language">The language the text is written in.</param>
    /// <param name="text">The description itself.</param>
    public AgentText(AgentLanguages language, string text)
    {
        Language = language;
        Text = text;
    }

    /// <summary>The language <see cref="Text"/> is written in.</summary>
    public AgentLanguages Language { get; }

    /// <summary>The description.</summary>
    public string Text { get; }
}

/// <summary>
/// Picks which descriptions reach the model for a requested language.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the rule lives, for annotated attributes read by reflection (<see cref="AgentContextReader"/>)
/// and for descriptions carried in a compiled context tree alike. Which of a target's descriptions reach the model
/// is a single decision, whether they arrived as attributes off a type, a property, a field or a method.
/// </para>
/// <para>
/// The language a host runs in chooses which descriptions reach the model — it is not a claim that every annotated
/// member has been translated. Without the fallback a member documented only in English would reach a
/// Chinese-language agent undescribed, which is worse than reaching it in English: an annotation in the wrong
/// language still names the members and still states the rules, while a missing one leaves the model to guess.
/// </para>
/// <para>
/// The fallback is all-or-nothing per target, never mixed: a target that has <i>some</i> Chinese annotations keeps
/// exactly those, so one translated description cannot cause its untranslated siblings to arrive as well. English
/// itself has nowhere to fall back to.
/// </para>
/// </remarks>
public static class AgentTextSelection
{
    /// <summary>
    /// Returns the texts for <paramref name="language"/>, or all the English ones when there are none.
    /// </summary>
    /// <param name="texts">Every description recorded for one target, in the order they were written.</param>
    /// <param name="language">The language being requested.</param>
    /// <returns>
    /// The matching texts in order; the English ones when none match and <paramref name="language"/> is not English;
    /// an empty array when nothing matches and there is no English either.
    /// </returns>
    public static string[] Select(IEnumerable<AgentText>? texts, AgentLanguages language)
    {
        if (texts is null) return [];

        var annotated = texts as IReadOnlyList<AgentText> ?? [.. texts];

        var localized = new List<string>();
        foreach (var text in annotated)
        {
            if (text.Language == language) localized.Add(text.Text);
        }

        if (localized.Count > 0 || language == AgentLanguages.English)
            return [.. localized];

        var english = new List<string>();
        foreach (var text in annotated)
        {
            if (text.Language == AgentLanguages.English) english.Add(text.Text);
        }

        return [.. english];
    }
}
