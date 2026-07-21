# Item Templates

VeloxDev provides **7 VS item templates** to generate workflow component views in one command.

---

## Installation

```shell
dotnet new install VeloxDev.WPF.Templates
```

## Usage

Use `-n` for name and `-o` for output directory:

```shell
dotnet new wpf-v-node -n MyCustomNode -o Views
dotnet new wpf-v-link -n MyLinkView -o Views
dotnet new wpf-v-decorator -n MyGridDecorator -o Views
dotnet new wpf-v-slot -n MySlotView -o Views
```

## Template List

| Short Name | Type | Description |
|-----------|------|-------------|
| `wpf-v-tree` | Tree View | Canvas container |
| `wpf-v-node` | Node View | Node card |
| `wpf-v-slot` | Slot View | Connection endpoint |
| `wpf-v-link` | Link View | Polyline connection |
| `wpf-v-decorator` | Grid Decorator | Grid & ruler |
| `wpf-v-overlay` | Minimap Overlay | Mini-map |
| `wpf-v-selector` | Template Selector | Type selector |

See [Deep Dive -> Item Template Parameters] for the full parameter reference.
