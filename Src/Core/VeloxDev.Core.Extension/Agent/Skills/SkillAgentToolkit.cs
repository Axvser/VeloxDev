using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.AI;

namespace VeloxDev.AI.Skills;

/// <summary>
/// The Agent-facing view of <see cref="SkillScope"/>: list the discovered skills, load one's text,
/// switch one off, and read a skill's bundled resources. Its shape mirrors <c>McpAgentToolkit</c> — the
/// same "host and agent operate one shared data layer" arrangement.
/// <para>
/// Reach the model through <see cref="SkillScope.CreateContextProvider"/>, which contributes these tools
/// already wrapped so they obey the composing host's policy. Use <see cref="CreateTools(AgentToolPolicy)"/>
/// directly only when assembling providers by hand; the parameterless <see cref="CreateTools()"/> returns
/// them unwrapped, with no marshalling or accounting.
/// </para>
/// <para>
/// They are read-only with respect to any graph they are used beside, and the workflow toolkit's
/// query/mutation classification reads their names from <see cref="ToolNames"/>, so that holds whether a
/// provider contributes them or a host registers them.
/// </para>
/// <para>
/// The tool names <c>load_skill</c> and <c>read_skill_resource</c> follow the Agent Skills convention so
/// a model already familiar with it picks them up without extra instruction.
/// </para>
/// </summary>
public sealed class SkillAgentToolkit(SkillScope scope)
{
    private readonly SkillScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    /// <summary>
    /// Prompt language skill text is read in. The host sets this alongside the scope's prompt language,
    /// so a bilingual skill corpus and the tool output agree. Defaults to English.
    /// </summary>
    public AgentLanguages Language { get; set; } = AgentLanguages.English;

    /// <summary>
    /// The names of the tools this toolkit registers. Exposed so that a host composing several tool
    /// sources can classify them without repeating the literals — in particular, all four are read-only
    /// with respect to the workflow graph, so a workflow host must keep them out of its mutation budget
    /// and out of its dirty marking.
    /// </summary>
    public static readonly string[] ToolNames =
        ["ListSkills", "load_skill", "UnloadSkill", "read_skill_resource"];

    /// <summary>Creates the skill management tools, unwrapped.</summary>
    public IList<AITool> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(ListSkills, ToolNames[0]),
            AIFunctionFactory.Create(LoadSkill, ToolNames[1]),
            AIFunctionFactory.Create(UnloadSkill, ToolNames[2]),
            AIFunctionFactory.Create(ReadSkillResource, ToolNames[3]),
        ];
    }

    /// <summary>
    /// Creates the skill management tools wrapped so that every call obeys <paramref name="policy"/> —
    /// marshalled onto the host's thread, gated, and reported afterwards. This is what a context provider
    /// contributes; registering the unwrapped set by hand gets none of it.
    /// </summary>
    public IList<AITool> CreateTools(AgentToolPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        return [.. CreateTools().Select(tool =>
            tool is AIFunction function ? (AITool)new TrackedAIFunction(function, policy) : tool)];
    }

    [Description("Lists every discovered skill with its source (Embedded/File), state (NotStarted/Loading/Ready/Error), whether it is currently enabled, its bundled resource count and any error. Also returns aggregate counts. Pure query — call it first to see what is available and what is already switched on. Embedded skills are injected in full when enabled; file skills are advertised only, so use load_skill to read one.")]
    private string ListSkills()
    {
        var arr = new JArray();
        foreach (var skill in _scope.Status.Skills.OrderBy(s => s.Source).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            arr.Add(new JObject
            {
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["source"] = skill.Source.ToString(),
                ["state"] = skill.State.ToString(),
                ["stateText"] = skill.StateText,
                ["enabled"] = skill.IsEnabled,
                ["resourceCount"] = skill.ResourceCount,
                ["error"] = skill.Error,
            });
        }

        return new JObject
        {
            ["status"] = "ok",
            ["skillCount"] = _scope.Status.Skills.Count,
            ["activeCount"] = _scope.Status.ActiveCount,
            ["errorCount"] = _scope.Status.ErrorCount,
            ["skills"] = arr,
        }.ToString(Formatting.None);
    }

    [Description("Loads a skill's full text and switches it on, so its guidance applies from this point. Use ListSkills to see what is available. For a file skill this is the only way to read its body — only its name and description are advertised. For an embedded skill the text is already in your instructions, but loading returns it anyway. Read any referenced resource afterwards with read_skill_resource.")]
    private string LoadSkill(
        [Description("Skill name exactly as reported by ListSkills.")] string skillName)
    {
        var skill = _scope.Find(skillName);
        if (skill is null)
            return Error($"Skill '{skillName}' not found. Use ListSkills to see the available skills.");
        if (!skill.IsReady)
            return Error($"Skill '{skillName}' is not readable: {skill.Error ?? skill.StateText}");

        _scope.Enable(skill.Name);

        var body = _scope.ReadSkillBody(skill.Name, Language);
        if (string.IsNullOrWhiteSpace(body))
            return Error($"Skill '{skillName}' is marked ready but produced no text.");

        return new JObject
        {
            ["status"] = "ok",
            ["skill"] = skill.Name,
            ["source"] = skill.Source.ToString(),
            ["resourceCount"] = skill.ResourceCount,
            ["content"] = body,
        }.ToString(Formatting.None);
    }

    [Description("Switches a skill off: it stops contributing to your instructions from this point, without unloading or rediscovering anything. Use it to drop guidance that no longer applies and keep the context focused. The skill can be switched back on later with load_skill.")]
    private string UnloadSkill(
        [Description("Skill name exactly as reported by ListSkills.")] string skillName)
    {
        var skill = _scope.Find(skillName);
        if (skill is null)
            return Error($"Skill '{skillName}' not found. Use ListSkills to see the available skills.");

        _scope.Disable(skill.Name);
        return JsonConvert.SerializeObject(
            new { status = "ok", skill = skill.Name, message = $"'{skill.Name}' switched off." }, Formatting.None);
    }

    [Description("Reads a resource bundled with a skill — a reference document, schema or data file shipped beside the skill. Pass the skill name and the resource path as the skill lists it (e.g. \"references/model.md\"). Only meaningful for file skills; embedded skills ship no per-skill resources.")]
    private string ReadSkillResource(
        [Description("Skill name exactly as reported by ListSkills.")] string skillName,
        [Description("Resource path inside the skill, e.g. \"references/model.md\".")] string relativePath)
    {
        var skill = _scope.Find(skillName);
        if (skill is null)
            return Error($"Skill '{skillName}' not found. Use ListSkills to see the available skills.");
        if (skill.ResourceCount == 0)
            return Error($"Skill '{skillName}' ships no resources. Embedded skills carry their text inline; only the skill's own text is available.");

        var content = _scope.ReadSkillResource(skill.Name, relativePath, Language);
        if (content is null)
            return Error($"Resource '{relativePath}' not found in skill '{skillName}'.");

        return new JObject
        {
            ["status"] = "ok",
            ["skill"] = skill.Name,
            ["resource"] = relativePath,
            ["content"] = content,
        }.ToString(Formatting.None);
    }

    /// <summary>Shared error envelope: <c>{"status":"error","message":…}</c>.</summary>
    private static string Error(string message)
        => JsonConvert.SerializeObject(new { status = "error", message }, Formatting.None);
}
