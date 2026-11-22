# XamlGallery

A WinForms application whose entire UI is authored in the VeloxDev WinForms XAML runtime. It is the
living catalogue for the runtime, not a workflow demo.

The window is a three-pane workbench:

- **Catalog** (left): a native tree of the 11 feature families and their 46 cards.
- **Preview** (center): the selected card, or the whole section page for a group overview and for
  layout-sensitive cards such as the virtualizing `ItemsControl`. The header shows the breadcrumb,
  title, summary and the card's usage sample under the `Usage` expander.
- **Properties** (right): the dependency properties of the element under the pointer. Click any
  element in the preview to inspect it; Alt+Click selects the exact native HWND. Edits write local
  values, the `×` reset clears a dependency property, and the layer outline follows the selection.

## Run

```bash
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj
```

Useful switches:

```bash
# open and close the window without interaction
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --smoke

# open one entry, a group overview, or the whole light-mode shell
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --entry layout.stack-panel
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --group layout --light

# scroll the preview by pixels
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --entry layout.stack-panel --scroll 400

# capture one card to a PNG and dump the control tree
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --entry layout.stack-panel --dump

# capture every card (add --include-groups for the 11 section overviews)
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --all --include-groups
```

`--capture` sets the window top-most, selects each target in catalog order, waits for layout, renders
the window into a bitmap (no screen coordinates involved), writes `<index>-<id>.png`, and closes.
`--dump` also writes a control tree with bounds and colours next to each PNG. `--preview-only` hides
the title, side panes and status strip for a content-only capture. `--light` starts the process in
the light palette, which is the clean way to capture the native tree and editors.

The app runs as a DPI-unaware, GDI-scaled process. The XAML runtime lays out in device pixels while
fonts are in points, so this keeps the authored 1440×900 composition proportional on high-DPI
displays. Native WinForms controls follow the dark color mode set at startup; switching the theme at
runtime re-creates the catalog and preview under the new process color mode.

## Groups

| Group | Shows |
| --- | --- |
| Layout | Stack/Dock/Wrap panels, `Grid`, `Canvas`, `UniformGrid`, `Viewbox`, `GridSplitter`, `Panel.ZIndex`, `Grid.RowSpan`, `GroupBox`, `Expander`, nested `TabControl`, `Decorator`, `AdornerDecorator`, `BulletDecorator` |
| Text | `TextBlock`, `Label`, `TextBox`, `PasswordBox`, `AccessText`, `HeaderedContentControl`, `RichTextBox`, `FlowDocumentScrollViewer`, `DocumentViewer` with one `FlowDocument` model |
| Data | `ItemsControl` with `ItemsPanel`/`ItemTemplate`/selector/`ItemContainerStyle`, `ListBox`, `ComboBox`, `TreeView`, `ListView`/`GridView`, `DataGrid`, `Calendar`, `DatePicker`, `CollectionView` sorting/filtering, virtualized list |
| Input | Buttons and toggles, `Command`, `KeyBinding`/`MouseBinding`, ranges, `Menu`, `ToolBar`, `StatusBar`, `ContextMenu`, `ToolTip`/`ToolTipService`, `Popup`, pointer/key readout |
| Graphics | Shapes, `Path` geometry, gradient brushes, a generated `Image` bitmap, `Viewbox` artwork |
| Animation | Hover `TransitionAction` groups, `KeyFrameAnimation` with `LoopTime`/`IsAutoReverse`, `Thickness` animation, a custom `TriggerAction`, `VisualStateManager` |
| Binding | Converter + parameter, `StringFormat`, `FallbackValue`, `TargetNullValue`, indexer, `ElementName`, `RelativeSource` Self/FindAncestor, `x:Static`, two-way updates, `INotifyDataErrorInfo`, a custom `ValidationRule`, `DynamicResource` |
| Styles | Explicit/implicit styles, `BasedOn`, `Style.Template`/`ControlTemplate` with `TemplateBinding` and template triggers, `DataTrigger`/`MultiTrigger`, generated dependency properties, theme fallback, `ContentPresenter`, `HeaderedItemsControl`, `Separator` |
| Theme | Palette swatches, light/dark switch, gradients, fallback metrics |
| Navigation | `Frame`/`Page` journal, detached `Window`, `NavigationWindow` |
| Visuals | Opt-in lightweight visual tree: `VisualHost` with `ContainerVisual`, text/shape visuals, group opacity, transform, clip, drop shadow and a batched connector scene |

## Notes

- The shell owns one `GalleryModel`; cards inherit its `DataContext`.
- The catalog is static metadata in `GalleryCatalog.cs`. A card entry names its section type and the
  `x:Name` of the card `Border`; the preview creates that section, takes the named card with
  `FindName`, and keeps the section alive for its event handlers and resources. A missing name or a
  layout-sensitive card falls back to the full section page.
- The preview host merges the section's `ResourceDictionary`, so section-local `DynamicResource`
  keys keep working after a card is detached.
- The native `TreeView` renders header text only: `ItemTemplate` is ignored, rows have no icons, and
  the wrapper has no expansion API for `ItemsSource` rows. The catalog uses authored
  `GalleryTreeItem`s so groups start expanded.
- Selection listens to `WM_LBUTTONDOWN`/`WM_RBUTTONDOWN` on the thread message queue because native
  child controls are separate HWNDs and do not bubble XAML pointer events. The click is never
  swallowed, so buttons and editors keep their normal behaviour.
- The preview's `AdornerLayer` does not reliably receive `WS_EX_LAYERED` in this environment; a
  native `Region` clipped to the outline keeps its colour-key fill and hit testing off the rest of
  the preview.
- The property panel discovers public dependency properties (and a curated set of attached
  `Grid`/`DockPanel`/`Canvas`/`Panel` properties) by reflection, with a common/advanced split.
  Editors cover strings, numbers, booleans, enums, `Color`/`Brush`, `Font`, and `Thickness`/`Size`/
  `Point`; unsupported values stay read-only.
- Platform-wall controls (MediaElement, WebBrowser, InkCanvas, Viewport3D) are intentionally absent.
