# Agent Embedded Resources

This directory contains the Markdown prompt fragments that are compiled directly into the `VeloxDev.Core.Extension` assembly as **embedded resources** and injected into Agent system prompts at runtime.

---

## Directory Structure

```
Resources/
└── {System}/               ← one subdirectory per Agent system (e.g. "Workflow")
    ├── {LangCode}/         ← "en" or "zh"  (ISO 639-1, two-letter)
    │   ├── Skills/         ← how-to guides: teach the Agent to use a specific feature
    │   │   └── {Name}.md
    │   └── References/     ← factual tables and rules the Agent must always obey
    │       └── {Name}.md
    └── Scripts/            ← language-neutral instruction scripts, injected verbatim
        └── {Name}.*
```

Currently shipped systems:

| System | Skills (×2 lang) | References (×2 lang) | Scripts |
|---|---|---|---|
| `Workflow` | 5 | 5 | — |

### Workflow — current files

```
Resources/Workflow/
├── en/
│   ├── References/
│   │   ├── CoordinateSystem.md
│   │   ├── CommandReference.md
│   │   ├── ForbiddenProperties.md
│   │   ├── TokenSaving.md
│   │   └── FrameworkBehaviors.md
│   └── Skills/
│       ├── SlotEnumerator.md
│       ├── OperationOrdering.md
│       ├── NodeCreation.md
│       ├── ConnectionValidation.md
│       └── DiscoveryFlow.md
├── zh/                     ← same names, Chinese content
│   ├── References/  (×5)
│   └── Skills/      (×5)
└── Scripts/
    └── README.md           ← placeholder; add language-neutral .md files here
```

---

## How MSBuild Embeds the Files

The `.csproj` uses a single recursive glob:

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj#L46-L49

```xml
<EmbeddedResource Include="Resources\**\*">
  <WithCulture>false</WithCulture>
</EmbeddedResource>
```

**Why `WithCulture=false` is required:**  
MSBuild detects ISO 639-1 codes inside file and directory names and treats them as satellite culture identifiers. Without this flag the directory segments `en` and `zh` would be stripped from the logical resource name, causing both language variants to map to the same key and overwrite each other.

### Logical resource name formula

MSBuild transforms the file path into a dotted logical name by replacing path separators with `.` and prepending the assembly's root namespace:

```
{RootNamespace}.{path with \ → .}
```

| File on disk | Logical name in assembly |
|---|---|
| `Resources\Workflow\en\Skills\SlotEnumerator.md` | `VeloxDev.Core.Extension.Resources.Workflow.en.Skills.SlotEnumerator.md` |
| `Resources\Workflow\zh\References\CoordinateSystem.md` | `VeloxDev.Core.Extension.Resources.Workflow.zh.References.CoordinateSystem.md` |
| `Resources\Workflow\Scripts\MyScript.md` | `VeloxDev.Core.Extension.Resources.Workflow.Scripts.MyScript.md` |

---

## How Resources Are Loaded at Runtime

All access goes through the static class `AgentEmbeddedResources`:

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs

### Assembly handle and namespace prefix

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs#L31-L35

```csharp
private static readonly Assembly _assembly = typeof(AgentEmbeddedResources).Assembly;
private const string Prefix = "VeloxDev.Core.Extension.";
```

### Low-level read

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs#L203-L211

```csharp
private static string? Read(string relativeName)
{
    var fullName = Prefix + relativeName;
    using var stream = _assembly.GetManifestResourceStream(fullName);
    if (stream == null) return null;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

`Read` takes a dotted relative name (e.g. `"Resources.Workflow.en.Skills.SlotEnumerator.md"`) and calls `Assembly.GetManifestResourceStream`. If the resource does not exist the method returns `null`.

### Language fallback

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs#L118-L123

```csharp
private static string? ReadWithFallback(string system, string category, string name, AgentLanguages language)
{
    var lang = ToLangCode(language);
    return Read($"Resources.{system}.{lang}.{category}.{name}.md")
        ?? (lang != "en" ? Read($"Resources.{system}.en.{category}.{name}.md") : null);
}
```

The lookup order for any non-English language is:

1. `Resources.{system}.{lang}.{category}.{name}.md` — requested language
2. `Resources.{system}.en.{category}.{name}.md` — English fallback

If English is requested directly, only step 1 is attempted.

### ReadAll — scanning and deduplication

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs#L129-L158

`ReadAll` concatenates every file in a category for a given system and language. Its scan strategy:

1. Build a scan prefix for the requested language, e.g.  
   `VeloxDev.Core.Extension.Resources.Workflow.zh.Skills.`
2. Iterate `Assembly.GetManifestResourceNames()`, collecting base names (the part after the prefix, stripped of `.md`).
3. A `HashSet<string>` (`seen`) prevents duplicates — e.g. if a zh file is found first, the corresponding en file is skipped.
4. For non-English requests, the scan runs twice: first for `{lang}`, then for `"en"`, so any file that exists only in English is still included via fallback.

### Listing

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs#L162-L180

`ListCategory` follows the same double-scan/dedup pattern as `ReadAll` but yields base names instead of content, allowing callers to iterate available files without reading them.

### Public API surface

| Method | Purpose |
|---|---|
| `ReadSkill(system, name, lang)` | Read one skill by name |
| `ReadAllSkills(system, lang)` | Concatenate all skills |
| `ListSkills(system, lang)` | Enumerate skill base names |
| `ReadReference(system, name, lang)` | Read one reference by name |
| `ReadAllReferences(system, lang)` | Concatenate all references |
| `ListReferences(system, lang)` | Enumerate reference base names |
| `ReadScript(system, name)` | Read one script (language-neutral) |
| `ReadAllScripts(system)` | Concatenate all scripts |
| `ListScripts(system)` | Enumerate script file names |

---

## How Resources Enter the System Prompt

`WorkflowAgentScope` owns the `"Workflow"` system constant:

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs#L25

```csharp
private const string SystemName = "Workflow";
```

### Built-in resources (always injected)

Both `ProvideAllContexts` and `ProvideProgressiveContextPrompt` call `ReadAll*` unconditionally to inject every built-in Reference and Skill:

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs#L194-L202

```csharp
// ── Built-in References ──
result.AppendLine(AgentEmbeddedResources.ReadAllReferences(SystemName, language).TrimEnd());

// ── Built-in Skills ──
result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
```

### Developer-registered additions

Developers can register extra named resources on top of the built-ins via the fluent API:

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs#L155-L190

```csharp
scope.WithSkill("MyFeature")      // appended under ## 🧩 Skills
     .WithReference("MyTable")    // appended under ## 📚 References
     .WithScript("MyScript.md");  // appended under ## 🔧 Scripts
```

These are flushed at the end of the system prompt by `AppendEmbeddedResources`:

https://github.com/Axvser/VeloxDev/blob/master/Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs#L300-L345

Each registered name is resolved through `ReadSkill(SystemName, name, language)` (or `ReadReference` / `ReadScript`) with the same language-fallback logic described above.

---

## Adding New Content

### Adding a file to an existing system

Drop a `.md` file into the correct directory. No C# changes are needed — the MSBuild glob picks it up automatically:

```
Resources/Workflow/en/Skills/MyNewSkill.md   ← English
Resources/Workflow/zh/Skills/MyNewSkill.md   ← Chinese (optional; falls back to en)
```

The file becomes available immediately after the next build:

```csharp
AgentEmbeddedResources.ReadSkill("Workflow", "MyNewSkill", AgentLanguages.English);
```

It is also included automatically in `ReadAllSkills("Workflow", ...)` — no registration required.

### Adding a new system

1. Create the directory tree `Resources/{NewSystem}/en/Skills/` and/or `References/`.
2. Add your `.md` files.
3. Create a scope class (analogous to `WorkflowAgentScope`) with its own `SystemName` constant.

No changes to `AgentEmbeddedResources.cs` or the `.csproj` are needed.

---

## Naming Conventions

| Constraint | Rule |
|---|---|
| System name | PascalCase, matches the directory name exactly (`"Workflow"`) |
| Language code | Lowercase ISO 639-1: `en`, `zh` |
| File stem | PascalCase, no spaces, no language suffix (`SlotEnumerator`, not `SlotEnumerator.en`) |
| Scripts | Any filename; no language suffix; placed directly under `Scripts/` |
| Extension | `.md` for Skills and References; any extension for Scripts |
