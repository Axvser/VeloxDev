# XamlGallery

A WinForms application whose entire UI is authored in the VeloxDev WinForms XAML runtime. It is the
living catalogue for the runtime, not a workflow demo.

## Run

```bash
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj
```

Useful switches:

```bash
# open and close the window without interaction
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --smoke

# open one tab (0-based) in the light palette
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --tab 3 --light

# scroll the selected tab by pixels
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --tab 2 --scroll 900

# capture one tab to a PNG and dump the control tree
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --tab 7 --dump
```

`--capture` sets the window top-most, waits for layout, renders the window into a bitmap (no screen
coordinates involved), writes `<tab>-<name>.png`, and closes. `--dump` also writes a control tree
with bounds and colours next to each PNG.

The app runs as a DPI-unaware, GDI-scaled process. The XAML runtime lays out in device pixels while
fonts are in points, so this keeps the authored 1280×820 composition proportional on high-DPI
displays. Native WinForms controls follow the dark color mode set at startup; `--light` starts the
process in the light mode instead.

## Sections

| Tab | Shows |
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

## Notes

- The shell owns one `GalleryModel`; sections inherit its `DataContext`.
- Theme mode swaps the palette brushes; consumers use `{DynamicResource ...}` so they follow.
  `ThemeStyles` re-applies the implicit fallback to every unstyled library control.
- Sections scroll through a native-auto-scroll `ScrollableControl`; `GalleryLayout` measures the
  content panel to its preferred height, so cards can be edited without counting pixels.
- Platform-wall controls (MediaElement, WebBrowser, InkCanvas, Viewport3D, arbitrary child-window
  transforms/opacity/effects) are intentionally absent.
