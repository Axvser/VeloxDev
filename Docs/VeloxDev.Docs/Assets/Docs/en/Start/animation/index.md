# Animation

Almost any GUI framework has dedicated interfaces for handling animations, which makes the creation of interpolation animations depend to some extent on the GUI's syntax design, and there may be limitations. Taking WPF as an example: standard animations must be built based on DependencyProperty. Although this is beneficial for internal performance optimization, it also sacrifices flexibility.

VeloxDev advocates for compatibility, internally designing a complete abstraction layer for animations from construction to execution. Each GUI framework requires only a small amount of adaptation code, and ultimately, animations act on standard properties rather than any GUI-specific objects.

> **Fluent - Elegantly Build Animations**

> **Snapshot - State Recording and Recovery**

> **Theme - Vivid Theme Color Switching Implementation**