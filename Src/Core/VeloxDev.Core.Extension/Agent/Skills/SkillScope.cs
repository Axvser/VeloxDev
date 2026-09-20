using Microsoft.Agents.AI;
using VeloxDev.AI.Pipelines;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// The data layer for skills: discovers them from one or more <see cref="ISkillSource"/> instances,
/// exposes each one as a bindable, individually switchable <see cref="SkillStatusViewModel"/>, and
/// renders the two prompt shapes a skill can take.
/// <para>
/// <b>Two shapes, decided by origin.</b> Skills embedded in the assembly are architectural prompt text:
/// when enabled, their body is injected in full. Skills read from disk follow the Agent Skills
/// convention and are <i>advertised</i> only — name and description reach the prompt, and the body is
/// fetched on demand through the <c>load_skill</c> tool. That keeps a large external skill library from
/// costing every turn.
/// </para>
/// <para>
/// <b>Versioning.</b> <see cref="Version"/> advances whenever the discovered set or any skill's enabled
/// flag changes. A prompt provider caches on it, so an unchanged turn costs nothing and a changed one
/// re-renders exactly once.
/// </para>
/// </summary>
public class SkillScope
{
    private readonly List<ISkillSource> _sources = [];
    private readonly Dictionary<string, ISkillSource> _owningSource = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private long _version;

    /// <summary>Bindable skill list and aggregate counts. Held for the scope's lifetime.</summary>
    public SkillsViewModel Status { get; } = new();

    /// <summary>
    /// Monotonic version of the discovered set and the enabled flags. Read it to decide whether a cached
    /// prompt render is still valid.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Identifies this scope for a context provider's session-state key. The Agent Framework throws when
    /// two providers attached to one agent share a key, and keys default to the provider's type name — so
    /// the discriminator has to live on the scope, not on the provider.
    /// <para>
    /// Per scope, deliberately: two providers built from <i>one</i> scope then collide and fail loudly at
    /// agent construction, which is the right outcome. A per-provider id would instead let both through
    /// and duplicate every skill tool and the whole instruction block in the same turn.
    /// </para>
    /// </summary>
    internal string InstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The language skill text is rendered in and read back in. Kept here rather than passed per call so
    /// that a context provider can cache its render on it — a language change has to invalidate that
    /// cache, and it is not otherwise observable through <see cref="Version"/>.
    /// </summary>
    public AgentLanguages PromptLanguage { get; private set; } = AgentLanguages.English;

    /// <summary>Sets the language skill text is rendered and read in. Advances <see cref="Version"/>.</summary>
    public SkillScope WithPromptLanguage(AgentLanguages language)
    {
        if (PromptLanguage == language) return this;
        PromptLanguage = language;
        Interlocked.Increment(ref _version);
        return this;
    }

    /// <summary>
    /// Creates the context provider that contributes this scope's skill text and skill tools on every
    /// agent invocation — everything a host needs to use skills without the workflow layer.
    /// </summary>
    /// <param name="policy">
    /// How the contributed tools should behave. Omit for standalone use: the provider then builds a
    /// thread-only policy from this scope's own <see cref="WithSynchronizationContext"/>. A composing
    /// host passes <i>its</i> policy instead, so its budgets, callbacks and side effects still apply —
    /// and must pass the same instance it gives every other source, or the call counts diverge.
    /// </param>
    /// <param name="embeddedCorpusDelivered">
    /// <c>true</c> when the prompt the model is reading already carries the embedded corpus, so this
    /// provider must state the difference rather than the corpus itself — see
    /// <see cref="BuildWithdrawnBlock"/>. The workflow scope sets it when its static skeleton was built
    /// before skills were attached.
    /// </param>
    public AIContextProvider CreateContextProvider(
        ToolPipeline? tools = null, AgentPipeline? pipeline = null, bool embeddedCorpusDelivered = false)
        => new SkillAgentContextProvider(this, tools, pipeline, embeddedCorpusDelivered);

    /// <summary>
    /// Optional UI thread context. When registered, discovery results and enabled-flag changes marshal to
    /// it, so a host can bind <see cref="Status"/> from the UI thread.
    /// </summary>
    public SkillScope WithSynchronizationContext(SynchronizationContext? context)
    {
        UIContext = context;
        return this;
    }

    internal SynchronizationContext? UIContext { get; private set; }

    /// <summary>
    /// Adds a disk-backed skill root. A relative path is resolved against
    /// <see cref="AppContext.BaseDirectory"/>; an absolute path is used as given. The directory need not
    /// exist yet — it is examined by <see cref="Refresh"/>.
    /// </summary>
    /// <param name="path">Root directory holding the skill folders.</param>
    public SkillScope WithSkillRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A skill root path is required.", nameof(path));

        var full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

        return WithSource(new FileSkillSource(full));
    }

    /// <summary>Adds a skill source. Sources are consulted in registration order; the first one to
    /// declare a given name owns it.</summary>
    public SkillScope WithSource(ISkillSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        lock (_gate) _sources.Add(source);
        return this;
    }

    /// <summary>
    /// Rediscovers every source and rebuilds <see cref="Status"/>. Enabled flags are preserved by name,
    /// so a refresh (e.g. after a new skill folder appears on disk) does not silently re-enable something
    /// the host switched off.
    /// <para>
    /// This is a host action, not a per-turn one: it always advances <see cref="Version"/>, so calling it
    /// from a prompt provider every turn would defeat the provider's cache. Call it when a skill root's
    /// contents may have changed.
    /// </para>
    /// </summary>
    public void Refresh()
    {
        ISkillSource[] sources;
        lock (_gate) sources = [.. _sources];

        var found = new List<SkillDescriptor>();
        var owners = new Dictionary<string, ISkillSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            IReadOnlyList<SkillDescriptor> discovered;
            try
            {
                discovered = source.Discover();
            }
            catch (Exception ex)
            {
                // A source that throws must not take the others down with it.
                found.Add(new SkillDescriptor
                {
                    Name = source.Kind + "-source",
                    Source = source.Kind,
                    Error = $"Skill source '{source.GetType().Name}' failed to discover: {ex.Message}",
                });
                continue;
            }

            foreach (var descriptor in discovered)
            {
                // First source to declare a name owns it, but descriptors of the same name from the SAME
                // source are all kept — those are the per-language variants of one skill.
                if (owners.TryGetValue(descriptor.Name, out var owner) && !ReferenceEquals(owner, source))
                    continue;

                owners[descriptor.Name] = source;
                found.Add(descriptor);
            }
        }

        Apply(found, owners);
    }

    /// <summary>
    /// Switches a skill on or off. Returns <c>false</c> when no skill of that name was discovered.
    /// </summary>
    /// <param name="name">Skill name as reported by <see cref="SkillStatusViewModel.Name"/>.</param>
    /// <param name="enabled">Whether the skill should reach the prompt.</param>
    public bool SetEnabled(string name, bool enabled)
    {
        SkillStatusViewModel? found = null;
        var changed = false;

        // The lookup happens inside the marshalled block too: Find walks the bound collection, so a host
        // switching a skill from a background thread must not read it there.
        RunOnUI(() =>
        {
            found = Find(name);
            if (found is null || found.IsEnabled == enabled) return;
            found.IsEnabled = enabled;
            changed = true;
        });

        if (found is null) return false;
        if (changed) Interlocked.Increment(ref _version);
        return true;
    }

    /// <summary>Switches a skill on. See <see cref="SetEnabled"/>.</summary>
    public bool Enable(string name) => SetEnabled(name, true);

    /// <summary>Switches a skill off. See <see cref="SetEnabled"/>.</summary>
    public bool Disable(string name) => SetEnabled(name, false);

    /// <summary>
    /// The full text of every enabled embedded skill for <paramref name="language"/>, concatenated in
    /// name order. With every skill enabled this is byte-for-byte what the prompt carried before skills
    /// became switchable.
    /// </summary>
    public string BuildEmbeddedBlock(AgentLanguages language)
    {
        var sb = new StringBuilder();
        foreach (var skill in ActiveSkills(SkillSourceKind.Embedded))
        {
            var body = ReadOwned(skill.Name)?.ReadBody(skill.Name, language);
            if (string.IsNullOrWhiteSpace(body)) continue;
            sb.AppendLine(body!.TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// The counterpart of <see cref="BuildEmbeddedBlock"/> for a prompt that already carries the corpus
    /// somewhere else — the workflow scope's static skeleton, whenever a host built it before attaching
    /// skills. That skeleton is frozen into the agent's own instructions at construction; the corpus in it
    /// cannot be recalled, so this provider names the <i>difference</i> instead: the skills switched off
    /// since, whose text the model can still read above and must now disregard.
    /// <para>
    /// The alternative — contributing nothing — would make <see cref="SetEnabled"/> a silent no-op on such
    /// a scope: it would report success while the model kept following the disabled document. Empty while
    /// nothing has drifted, which is the ordinary state and costs no tokens.
    /// </para>
    /// <para>
    /// File skills never appear here: they are advertised rather than injected, so switching one off just
    /// removes it from the advertisement — there is no text to withdraw.
    /// </para>
    /// </summary>
    public string BuildWithdrawnBlock(AgentLanguages language)
    {
        // Every embedded skill, enabled or not, is in the frozen corpus: the skeleton reads the documents
        // off disk rather than the switch state. So what has to be withdrawn is exactly what is switched
        // off now, whether it was switched off before or after that render.
        var withdrawn = Status.Snapshot
            .Where(s => s.Source == SkillSourceKind.Embedded && !s.IsActive)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (withdrawn.Length == 0) return string.Empty;

        var chinese = language == AgentLanguages.Chinese;
        var sb = new StringBuilder();
        sb.AppendLine(chinese
            ? "## 本提示词生成之后的技能变更"
            : "## Skill changes since this prompt was built");
        sb.AppendLine();
        sb.AppendLine(chinese
            ? "本提示词靠前部分的技能文档是会话开始时写入的。以下技能已停用 —— 请忽略它们的正文："
            : "The skill documents earlier in this prompt were captured when the session started. These are no longer enabled — disregard their instructions:");
        sb.AppendLine();
        foreach (var skill in withdrawn)
            sb.AppendLine($"- `{skill.Name}`");
        return sb.ToString();
    }

    /// <summary>
    /// An advertisement block — each enabled file skill's name and description — plus the instruction to
    /// load a skill's body before following it. The text follows the Agent Skills convention so a model
    /// trained on that convention recognises it.
    /// </summary>
    public string BuildAdvertisement(AgentLanguages language)
    {
        var sb = new StringBuilder();
        foreach (var skill in ActiveSkills(SkillSourceKind.File))
        {
            sb.AppendLine("<skill>");
            sb.AppendLine($"<name>{skill.Name}</name>");
            sb.AppendLine($"<description>{skill.Description}</description>");
            sb.AppendLine("</skill>");
        }

        if (sb.Length == 0) return string.Empty;

        var result = new StringBuilder();
        result.AppendLine("You have access to skills containing domain-specific knowledge and capabilities.");
        result.AppendLine("Each skill provides specialized instructions for specific tasks.");
        result.AppendLine();
        result.AppendLine("<available_skills>");
        result.Append(sb);
        result.AppendLine("</available_skills>");
        result.AppendLine();
        result.AppendLine("When a task aligns with a skill's domain, use `load_skill` to retrieve the skill's");
        result.AppendLine("instructions, follow the guidance, then use `read_skill_resource` for any referenced");
        result.AppendLine("resource. Only load what is needed, when it is needed.");
        return result.ToString();
    }

    /// <summary>A skill's full text, whether it is currently enabled or not.</summary>
    public string? ReadSkillBody(string name, AgentLanguages language)
        => ReadOwned(name)?.ReadBody(name, language);

    /// <summary>One bundled resource of a skill, addressed relative to the skill directory.</summary>
    public string? ReadSkillResource(string name, string relativePath, AgentLanguages language)
        => ReadOwned(name)?.ReadResource(name, relativePath, language);

    /// <summary>
    /// A discovered skill by name, or <c>null</c>. Walks the bound collection, so call it on the thread
    /// <see cref="Status"/> is bound to — <see cref="SetEnabled"/> and the skill tools already do.
    /// </summary>
    public SkillStatusViewModel? Find(string name)
        => Status.Skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Discovered skill names, in listing order. Snapshot-backed, so safe off the bound thread.</summary>
    public IReadOnlyList<string> Names => [.. Status.Snapshot.Select(s => s.Name)];

    // ── Rendering helpers ────────────────────────────────────────────────────

    /// <summary>
    /// The enabled skills of one origin, ordered by name. Reads the status snapshot rather than the bound
    /// collection: the renderers below run on the agent invocation thread, not the UI thread.
    /// </summary>
    private IEnumerable<SkillSummary> ActiveSkills(SkillSourceKind kind)
        => Status.Snapshot
            .Where(s => s.IsActive && s.Source == kind)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

    private ISkillSource? ReadOwned(string name)
    {
        lock (_gate)
            return _owningSource.TryGetValue(name, out var source) ? source : null;
    }

    // ── Discovery application ────────────────────────────────────────────────

    private void Apply(List<SkillDescriptor> found, Dictionary<string, ISkillSource> owners)
    {
        // One entry per name: a skill is toggled as a concept, while its text is rendered per language.
        var byName = new Dictionary<string, List<SkillDescriptor>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var descriptor in found)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name)) continue;
            if (!byName.TryGetValue(descriptor.Name, out var group))
            {
                byName[descriptor.Name] = group = [];
                order.Add(descriptor.Name);
            }
            group.Add(descriptor);
        }

        RunOnUI(() =>
        {
            // Enabled flags survive a refresh; new skills start enabled so that adding a root does not
            // silently produce an empty prompt. Read inside the marshalled block — Skills is an
            // ObservableCollection bound to the host UI.
            var previous = Status.Skills.ToDictionary(s => s.Name, s => s.IsEnabled, StringComparer.OrdinalIgnoreCase);

            Status.Reset();
            foreach (var name in order)
            {
                var group = byName[name];
                var primary = group.FirstOrDefault(d => d.Error is null) ?? group[0];
                var errors = group.Where(d => d.Error is not null).Select(d => d.Error!).ToArray();

                Status.Track(new SkillStatusViewModel
                {
                    Name = name,
                    Description = primary.Description,
                    Source = primary.Source,
                    Path = primary.Path,
                    ResourceCount = group.Max(d => d.ResourceCount),
                    Error = errors.Length > 0 ? string.Join(" | ", errors) : null,
                    State = group.Any(d => d.Error is null) ? SkillState.Ready : SkillState.Error,
                    IsEnabled = !previous.TryGetValue(name, out var wasEnabled) || wasEnabled,
                });
            }
        });

        lock (_gate)
        {
            _owningSource.Clear();
            foreach (var kvp in owners) _owningSource[kvp.Key] = kvp.Value;
        }

        Interlocked.Increment(ref _version);
    }

    private void RunOnUI(Action action)
    {
        var ui = UIContext;
        if (ui is null || ReferenceEquals(ui, SynchronizationContext.Current))
        {
            action();
            return;
        }
        ui.Send(_ => action(), null);
    }
}
