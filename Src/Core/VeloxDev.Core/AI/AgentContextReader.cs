using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Reads <see cref="AgentContextAttribute"/> from types and members.
/// Generic utility — not tied to any specific framework or domain.
/// </summary>
public static class AgentContextReader
{
    /// <summary>
    /// Gets all <see cref="AgentContextAttribute.Context"/> values for the specified language on a type.
    /// <para>
    /// The language selects which annotations to collect, not which ones a type is required to carry:
    /// requesting a language a type was never annotated in yields its English annotations rather than
    /// nothing. See <see cref="Select"/>.
    /// </para>
    /// </summary>
    public static string[] GetContexts(Type type, AgentLanguages language)
        => Select(type.GetCustomAttributes<AgentContextAttribute>(inherit: false), language);
    /// <summary>
    /// Gets all <see cref="AgentContextAttribute.Context"/> values for the specified language on a member —
    /// a property, field, method or event. Falls back to English exactly as the type overload does, so this
    /// is the entry point every annotation reader goes through rather than repeating the filter.
    /// </summary>
    public static string[] GetContexts(MemberInfo member, AgentLanguages language)
        => Select(member.GetCustomAttributes<AgentContextAttribute>(inherit: false), language);

    /// <summary>
    /// Returns <c>true</c> if the type or member has at least one <see cref="AgentContextAttribute"/>.
    /// </summary>
    public static bool HasAgentContext(MemberInfo member)
    {
        return member.GetCustomAttributes<AgentContextAttribute>(inherit: false).Any();
    }

    /// <summary>
    /// Picks the annotations for <paramref name="language"/> out of an already-read attribute set, falling
    /// back to the English ones when it has none of its own.
    /// <para>
    /// This is the one place the rule lives. Which of a target's descriptions reach the model is a single
    /// decision, whether the attributes came off a type, a property, a field or a method — the readers that
    /// fetch them by reflection call in here rather than repeating the filter.
    /// </para>
    /// <para>
    /// The language a host runs in chooses which descriptions reach the model — it is not a claim that every
    /// annotated member has been translated. Without the fallback a member documented only in English would
    /// reach a Chinese-language agent undescribed, which is worse than reaching it in English: an annotation
    /// in the wrong language still names the members and still states the rules, while a missing one leaves
    /// the model to guess. This mirrors how the embedded prompt documents resolve
    /// (<c>Resources/{system}/{lang}/</c> falls back to <c>en</c>).
    /// </para>
    /// <para>
    /// The fallback is all-or-nothing per target, never mixed: a target that has <i>some</i> Chinese
    /// annotations keeps exactly those, so one translated description cannot cause its untranslated siblings
    /// to arrive as well. English itself has nowhere to fall back to.
    /// </para>
    /// </summary>
    private static string[] Select(IEnumerable<AgentContextAttribute> attributes, AgentLanguages language)
    {
        var annotated = attributes.ToArray();

        var localized = annotated.Where(c => c.Language == language).ToArray();
        if (localized.Length > 0 || language == AgentLanguages.English)
            return [.. localized.Select(c => c.Context)];

        return [.. annotated.Where(c => c.Language == AgentLanguages.English).Select(c => c.Context)];
    }
}
