# XamlGallery

The living catalogue for the VeloxDev WinForms XAML runtime (`VeloxDev.WinForms.Xaml.Visuals`).
The whole window is built from one `VisualHost` — a single HWND — and a tree of visual elements;
no card creates a child window. Reactivity is visual: controls derive from `VisualElement`, not from a
WinForms control. `NativeControlHost` is the one explicit native island; the focused visual `TextBox`
uses a private one for IME and accessibility.

The window is a three-pane workbench:

- **Catalog** (left): a visual `TreeView` of the feature groups and their cards, with a filter box.
- **Preview** (center): the selected card plus its `Usage` snippet.
- **Properties** (right): the editable dependency properties of the card's visual root, discovered by
  reflection; strings, numbers, booleans, enums and colours are editable.

The shell itself is visual too: title bar, docked panes, `GridSplitter` sashes, tree, scroll viewers
and status bar are all `VisualElement`s.

## Run

```bash
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj
```

Useful switches:

```bash
# open and close the window without interaction
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --smoke

# open one entry, a group, or a starting palette (Dark+, Light+, high contrast)
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --entry defaults.controls
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --group data --light
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --group theme --contrast

# scroll the preview by pixels
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --entry data.tree-view --scroll 200

# capture one card to a PNG and dump the visual tree
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --entry layout.stack-panel --dump

# capture every card
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --all
```

`--capture` places the window on the first non-primary screen when one exists, selects each target in
catalog order, waits for layout, renders the window into a bitmap with `PrintWindow` (falling back to
`VisualSurface.RenderTo`), and closes. `--dump` writes a visual tree with bounds and colours next to
each PNG. `--preview-only` hides the title, side panes and status strip for a content-only capture.
`--light` starts the process in the Light+ palette; `--contrast` starts it in the high-contrast one.

## Cards

| Group | Shows |
| --- | --- |
| Defaults | The implicit fallback appearance, authored as visual XAML: representative controls with no style, background or foreground set, each section on a bordered plate. This is the card the gallery opens on. |
| Layout | `StackPanel`, `DockPanel`, `Grid`, `WrapPanel`, `Canvas`, `UniformGrid`, `Viewbox`, `GridSplitter`, `GroupBox`, `Expander`, `TabControl` |
| Text | `TextBlock`, `TextBox`, password/multiline editing, a XAML-templated `TextBox` with `UseNativeEditor=false`, `RichTextBox`, `Image` |
| Data | `ListBox`, `ComboBox`, `VirtualList`, `TreeView`, `Calendar`, `DatePicker` |
| Input | `Button`/`RepeatButton`, toggles, `Slider`, `ProgressBar`, `NativeControlHost` |
| Graphics | Shapes (`Rectangle`, `Ellipse`, `Polygon`, `Path`, `Line`), `VisualPresenter` with a lightweight `Visual` tree, opacity and clip |
| Overlay | `VisualPopup`, `VisualMenu`, `VisualContextMenu` |
| Styling | Implicit/explicit `VisualStyle`, `VisualTrigger`, `VisualControlTemplate` with `ContentPresenter` and a named part, resources |
| Binding | One-way/two-way path bindings, inherited `DataContext`, a converter, and a generated `[VeloxProperty]`/`[VeloxCommand]` view model |
| Theme | Palette swatches, typography, the three-mode palette rebuild, and a XAML resource dictionary with keyed styles, a trigger, a control template and a data template |
| Visuals | A generated XAML view and a real `WorkflowTree` graph on the visual runtime |

## Notes

- The **Defaults** card is a XAML view (`Views/DefaultsView.xaml`); the bordered plates take their
  colours from two resources the code-behind puts in the root dictionary before `InitializeComponent`.
  The controls themselves still set no style, background, foreground or palette override, so it stays
  the reference view for what a host gets without authored styling.
- The **Resources in XAML** and **MVVM view model** cards are XAML views too. The first declares its
  palette colours, keyed styles, a trigger, a control template and a data template in
  `<StackPanel.Resources>`; the second binds to a `TaskBoardViewModel` whose properties and commands
  come from `[VeloxProperty]` and `[VeloxCommand]`.
- Every visual control is a `VisualElement`/`TemplatedControl`; `RichTextBox`, `ComboBox`, `Calendar`
  and the rest are fully self-drawn. A focused `TextBox` optionally hosts a native editor island for IME
  and accessibility — set `UseNativeEditor="False"` to keep the whole field visual (templates, DPs,
  caret, selection, clipboard, undo), losing only IME composition and the native accessibility peer.
  The **XAML custom field** card shows two templated fields built that way. The native island is
  clipped to the `ScrollViewer` viewport, so it no longer covers scrollbars at the edge.
- One `GalleryModel` is shared by every card; data cards bind to its sample collections and values.
- Cards are factories in `GalleryVisualCards.cs`: selecting a card builds a fresh visual tree and
  disposes the previous one.
- Theme switching cycles Dark+ → Light+ → high contrast and rebuilds the visual root so every control
  reads the new `Theme.Current` values.
- The inspector writes local dependency-property values on the card root; it does not edit template
  or child parts.
- The workflow graph card is a demo-level surface (drag/pan/zoom/Delete) built directly on the visual
  controls; the reusable workflow surface is `WorkflowSurfaceBehavior` on a `VisualHost`, as used by
  `Examples/Workflow/WinForms` and `Examples/Workflow/WinForms Trimmed`.
- Platform-wall controls (MediaElement, WebBrowser, InkCanvas, Viewport3D) are intentionally absent.
