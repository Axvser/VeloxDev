# Pitfalls — item templates

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [templates.md](../practices/templates.md) and the GUI references under [gui/](../practices/gui/).

## A generated tree cannot find its node view

**Symptom:** the project builds, but the canvas shows no nodes, or a type cannot be resolved.

**Cause:** the seven items were generated into different namespaces, a sibling was renamed, or a pack does not emit a required sibling. The tree resolves its selector, decorator, minimap and node/link templates by the default class names through a namespace alias or bare type name.

**Fix:** generate all seven into one namespace with their default names (`NodeView`, `SlotView`, `LinkView`, `TemplateSelector`, `GridDecorator`, `MinimapOverlay`). If you rename one, update the tree's reference in the same edit.

## The Avalonia tree view does not compile as generated

**Symptom:** the build fails at `x:DataType="vm:TreeViewModel"`.

**Cause:** the template ships a placeholder `x:DataType` pointing at `MyApp.Views`. The failure is intentional: a clear error beats a canvas that silently renders nothing.

**Fix:** point `xmlns:vm` at your ViewModels namespace and rename the type. See [gui/avalonia.md](../practices/gui/avalonia.md#compiled-bindings).

## A colour parameter does nothing

**Symptom:** passing `-bc`, `-sc` or `-sp` changes no generated file.

**Cause:** the framework has no consumer for that value. The pack still declares the symbol for cross-GUI CLI parity and documents the inert value in its own `template.json`.

**Fix:** edit the generated file directly when you need the visual. Check the per-GUI reference for which symbols are inert on that pack.

## The link menu has no Delete entry

**Symptom:** right-pressing a link opens an empty menu, or no menu.

**Cause:** the packs declare and wire the menu but add no entries. WinForms' base override adds none either.

**Fix:** add the entries in the resource/fragment/override the pack declares. Each entry is bound to the pressed link; `Command="{Binding DeleteCommand}"` is a complete entry. If no menu opens, check `LinkMenuKey` names an existing resource and the platform's right-press wiring.

## Node content is squeezed and clips its text when zoomed out

**Symptom:** the card's text is cut off at low zoom.

**Cause:** the inner design grid was bound to the collapsed `Size` while the outer `Viewbox` already scales it. The design is squeezed a second time.

**Fix:** pin the inner grid to the design size (`260 × 180`) and let the outer container scale it. A `Viewbox` child follows its own `Width`/`Height`; the template sizes the outer view.

## Half of a port is hidden under the card

**Symptom:** the port glyph is clipped on the side that overlaps the card.

**Cause:** the port is drawn inside a clipped container or below the card surface.

**Fix:** give ports `ZIndex="6"` (or the platform equivalent) and keep clipping off on the card and its ancestors. Do not move the port's measured control arbitrarily; the layout behaviour measures its centre.

## A slot's visual state never changes

**Symptom:** the slot stays in one colour while connections are made.

**Cause:** the view bound `Slot.State` instead of the slot's own `State`, or the style trigger reads the wrong property. The binding source must be the `IWorkflowSlotViewModel`.

**Fix:** bind the slot control's `DataContext`/`BindingContext` to the slot and read `State` from it. Both `PreviewSender`/`PreviewReceiver` and `Sender`/`Receiver` are flags; test with `HasFlag`.

## Deleting `SlotView` removes interaction without an error

**Symptom:** a slot still draws, but connecting or hovering stops working.

**Cause:** the slot's interaction lives in the `SlotView` (the connection behaviour wraps itself there). Replacing it with a plain shape removes the gesture and the measurement.

**Fix:** keep `SlotView` (or wrap the connection behaviour yourself exactly once). A plain glyph is a visual, not a port.

## The ruler band has a different thickness than the grid assumes

**Symptom:** ticks and nodes drift apart, or nodes under the ruler are culled.

**Cause:** the pack's ruler default differs between platforms, or the decorator's `RulerBand` and the grid's content offset were set from different values.

**Fix:** read the thickness from the decorator's `RulerBand` and use the same value for the canvas translation, the grid maths and the virtualize inset. Do not assume the same default on every pack; check the GUI reference.

## An installed pack is ignored or ambiguous

**Symptom:** `dotnet new` resolves an older pack, or install exits non-zero.

**Cause:** a duplicate registration, or a folder-mounted pack with the same short name as the NuGet package.

**Fix:** `dotnet new uninstall VeloxDev.<GUI>.Templates` first, then install the NuGet pack. Keep one registration per short name.
