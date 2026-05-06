# VeloxDev.Docs

A cross-platform wiki editor built with [Avalonia](https://avaloniaui.net/) and [VeloxDev](https://github.com/Axvser/VeloxDev). Documents are composed of typed elements (headings, paragraphs, code blocks, Markdown, tables, images, links) organised into a tree of pages, and serialised to a single JSON file for storage.

---

## Project structure

| Project | Target | Role |
|---|---|---|
| `VeloxDev.Docs` | `net10.0` | Shared library — all view-models, views, and serialisation logic |
| `VeloxDev.Docs.Desktop` | `net10.0` (WinExe) | Windows / macOS / Linux desktop entry point |
| `VeloxDev.Docs.Browser` | `net10.0-browser` (WASM) | Browser entry point via `Microsoft.NET.Sdk.WebAssembly` |

The shared library is UI-framework agnostic at the model layer; only the entry-point projects carry platform-specific dependencies (`Avalonia.Desktop` / `Avalonia.Browser`).

---

## Desktop vs. Browser

| Feature | Desktop | Browser (WASM) |
|---|---|---|
| **Edit mode** | ✅ Full editing toolbar | ❌ Read-only (`IsEditorSupported = false`) |
| **Save / Open** | ✅ Native file picker (`StorageProvider`) | ❌ Not available |
| **Image loading** | HTTP + local file paths | HTTP only (no local FS access) |
| **Font rendering** | System fonts + bundled Noto Color Emoji | Same (loaded from `avares://` assets) |

`DocumentProvider.IsEditorSupported` is `!OperatingSystem.IsBrowser()`. The XAML binds toolbar and file-operation buttons to this flag, so the browser build silently hides them without any code duplication.

---

## Architecture

### Document tree

```
DocumentProvider          (root; owns Nodes + the active Children flat-list)
└── NodeProvider[]        (pages in the navigation tree; may nest via Nodes)
    └── IWikiElement[]    (content elements on that page, stored in Children)
```

`NodeProvider` implements `ITreeElement : IWikiElement` and carries its own `Children` (content) and `Nodes` (child pages). `DocumentProvider.Children` is always aliased to `SelectedNode.Children` and is what the content area binds to.

### View-model conventions (`VeloxDev.MVVM`)

Properties are declared with `[VeloxProperty]` and the source generator emits the full MVVM boilerplate. **Only public, publicly-writable properties participate in JSON serialisation** (enforced by `WritablePropertiesOnlyResolver`). Any property that must not be serialised must be marked `[JsonIgnore]`.

### Serialisation

`ComponentModelEx` (from `VeloxDev.Core.Extension`) serialises the document graph with Newtonsoft.Json using:

- `TypeNameHandling.Auto` — concrete types inside `ObservableCollection<IWikiElement>` are round-tripped via `$type`.
- `PreserveReferencesHandling.Objects` — shared references (e.g. a `NodeProvider` appearing in both `Nodes` and `SelectedNode`) are written once and referenced by `$id` / `$ref`.
- `WritablePropertiesOnlyResolver` — only serialises properties where `CanRead && CanWrite`.

After deserialisation, `RepairParents` walks the tree and restores the `Parent` back-pointer on every element (it is serialised but re-set explicitly to guarantee correctness for any edge-cases the reference resolver might miss).

`HydrationScope` suppresses expensive side-effects (syntax highlighting, image downloads) during the deserialisation pass; `FlushDeferredHighlighting` runs them once the full document is materialised.

---

## Document elements

All elements implement `IWikiElement` and declare a `Parent` back-pointer that is set by `RepairParents` after load.

| Element | Provider class | Serialised properties | Notes |
|---|---|---|---|
| **Heading** | `TitleProvider` | `Level` (`"1"`–`"6"`), `Text` | Maps to H1–H6 |
| **Paragraph** | `ParagraphProvider` | `Text` | Plain text body |
| **Quote / Subtitle** | `SubtitleProvider` | `Text` | Rendered as a styled block-quote |
| **Link** | `LinkProvider` | `Text`, `Url` | Launches via `ILauncher`; desktop only opens browser |
| **Code block** | `CodeProvider` | `Language`, `Code` | Syntax-highlighted via TextMateSharp; highlighting is deferred during load and flushed by `FlushDeferredHighlighting` |
| **Markdown block** | `MarkdownProvider` | `Text`, `AutoHeight`, `MaxHeightValue`, `EmbeddedCodeAutoHeight`, `EmbeddedCodeMaxHeightValue` | Full CommonMark + extensions via Markdig; inline images loaded asynchronously with cancellation |
| **Table** | `TableProvider` | `Headers`, `Alignments`, `Rows` | `Alignments` values: `"Left"` / `"Center"` / `"Right"` |
| **Image** | `ImageProvider` | `Source`, `ScaleX`, `ScaleY`, `PixelWidth`, `PixelHeight`, `KeepAspectRatio`, `SizeMode` | Supports HTTP URLs; SVG via `Avalonia.Svg.Skia` |
| **Empty (placeholder)** | `EmptyProvider` | *(none beyond `Parent`)* | Inserted between elements; can be converted to any other type via context menu |

### Adding a new element type

1. Create `FooProvider : IWikiElement` with `[VeloxProperty]` fields. Every property that must survive save/load must be `public` with a public setter.
2. Add a `FooView` UserControl that extends `WikiElementViewBase` and calls `InitializeEditChrome(...)`.
3. Register a factory in `EmptyProvider.CreateDefault(Type, IWikiElement?)`.
4. Add an `AddFoo()` command on `DocumentProvider` and wire a toolbar button in `DocumentView.axaml`.

---

## Editing workflow

1. Click **📝 Edit** (desktop only) to enter edit mode. Every element shows a highlight border and a context menu.
2. Click any element to open its inline editor. Clicking outside or pressing away commits the edit.
3. Right-click an element to insert an empty placeholder above/below, convert it to another type, or remove it.
4. Use the toolbar to append new elements to the current page.
5. Click **💾 Save** to write the entire document tree to a JSON file. Click **📂 Open** to load one.

In browse mode the document is read-only; all editor chrome is hidden and the view renders the display representation of each element.

---

## Localisation

`DocumentProvider.Language` stores an IETF language tag (e.g. `"en"`, `"zh"`). On change it reloads the default wiki asset from `avares://VeloxDev.Docs/Assets/Docs/wiki.{lang}.json`.

The left-toolbar language selector (`Languages`) exposes only the **top-5 languages by global usage** so the picker stays compact. A matching asset file must exist in `Assets/Docs` for each entry.

| Code | Language | Asset file |
|---|---|---|
| `en` | 🌐 English | `wiki.en.json` |
| `zh` | 🌐 中文 | `wiki.zh.json` |
| `es` | 🌐 Español | `wiki.es.json` |
| `fr` | 🌐 Français | `wiki.fr.json` |
| `de` | 🌐 Deutsch | `wiki.de.json` |

`AvailableLanguages` (28 entries) is the source of truth for ordering. `TopLanguages` is `AvailableLanguages.Take(5)`. UI labels on `DocumentProvider` (Save, Open, Navigation, …) are computed properties that switch on `Language`.

---

## LLM-powered translation

VeloxDev.Docs includes a stateless, per-element translation pipeline backed by any OpenAI-compatible endpoint (default: DashScope / Qwen).

### Architecture

```
WikiTranslatorSettings          (static; holds API key, model, endpoint, TranslationMode)
        │
        ▼
WikiTranslator                  (one LLM call per WikiTranslationJob; no session history)
        │
        ▼
WikiTranslationCollector        (reflects [TranslateTarget] properties + table/code special paths)
        │
        ▼
WikiTranslationJob[]            (element ref + property label + hint + write-back Action<string>)
```

#### `[TranslateTarget(hint)]`

Marks a `string` property as translatable. The `hint` string is injected verbatim into the LLM system prompt so the model knows the role of the content.

```csharp
[TranslateTarget("Main heading text. Translate naturally.")]
[VeloxProperty] public partial string Text { get; set; }
```

Properties marked this way on the built-in providers:

| Provider | Property | Hint |
|---|---|---|
| `TitleProvider` | `Text` | heading text |
| `ParagraphProvider` | `Text` | paragraph body text |
| `SubtitleProvider` | `Text` | block-quote / subtitle text |
| `LinkProvider` | `Text` | link display label |
| `MarkdownProvider` | `Text` | full Markdown document |
| `NodeProvider` | `Title` | page / navigation title |
| `CodeProvider` | `Code` | source code block — **translate comments only** |

#### Table translation

`TableProvider.Headers` and every `TableRowProvider.Cells` are `ObservableCollection<string>` and cannot be annotated with `[TranslateTarget]`. `WikiTranslationCollector` handles them explicitly: it iterates each non-empty slot and emits a `WikiTranslationJob` whose write-back delegate updates `collection[index]` directly. Header slots receive hint `"table column header"`; cell slots receive `"table cell text"`.

#### Code comment translation

`CodeProvider.Code` is marked with a detailed `[TranslateTarget]` hint that instructs the LLM to **translate only inline comments** (`// …`, `/* … */`, `# …`, `<!-- … -->`) and leave all identifiers, keywords, and string literals unchanged. The entire code string (including syntax) is sent; the model is expected to return it with only the comment text changed.

#### Prompt rules (`WikiTranslator.BuildSystemPrompt`)

Each LLM call is fully stateless. The system prompt:
1. States the content hint (takes precedence over generic rules).
2. Specifies the target language.
3. Forbids adding explanations or surrounding text.
4. Per-type rules: Markdown — preserve fences and URLs; code — comments only; table — natural-language text only; all — return only the translated result.

### Translation modes

Configured globally in `WikiTranslatorSettings.TranslationMode` and editable from the ⚙ settings flyout.

| Mode | Scope | Updates `Language`? |
|---|---|---|
| `CurrentPage` *(default)* | Selected page only | No |
| `FullDocument` | All pages | No |
| `FullDocumentAndUpdateLanguage` | All pages | Yes — switches UI labels to target locale |

### Configuration

1. Click **⚙** in the toolbar to open the Translation Settings flyout.
2. Enter your DashScope API key (or set the `DASHSCOPE_API_KEY` environment variable — the env var is the fallback when no file-persisted key exists).
3. Optionally change the model name (default `qwen-plus`) and endpoint URL.
4. Choose a **Translation Mode**.
5. Click **Apply**. The key is written to `%APPDATA%\VeloxDev\Docs\dashscope.key` and survives restarts.

The translation target language is chosen from the **full 28-language `AllLanguages` list** in the toolbar and is independent of the document display language.

### Thread safety

`WikiTranslator.RunJobsAsync` awaits each `TranslateJobAsync` with `ConfigureAwait(true)` so that `WikiTranslationJob.Apply(...)` always runs on the UI thread. The I/O path inside `TranslateJobAsync` uses `ConfigureAwait(false)` as normal. This prevents Avalonia cross-thread exceptions when translated text flows into `InlineCollection` or other UI-owned objects.

---


## Key dependencies

| Package | Purpose |
|---|---|
| `Avalonia` | Cross-platform UI framework |
| `Markdig` | CommonMark-compliant Markdown parser |
| `TextMateSharp.Grammars` | Syntax highlighting for code blocks |
| `Svg.Controls.Skia.Avalonia` | SVG image rendering |
| `Newtonsoft.Json` | Document serialisation (via `VeloxDev.Core.Extension`) |
| `VeloxDev.Avalonia` | `[VeloxProperty]` / `[VeloxCommand]` source generators + MVVM base |
| `CommunityToolkit.Mvvm` | `ObservableObject` base for `ViewModelBase` |
| `Microsoft.Extensions.AI.OpenAI` | OpenAI-compatible chat client abstraction used by `WikiTranslator` |
| `OpenAI` | Underlying OpenAI SDK (`2.8.0+`) |
