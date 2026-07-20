# Component

A workflow is a graph of four component types. This chapter explains each one's ViewModel contract.

| Component | Parent | Role |
|-----------|--------|------|
| **Tree** | — | Global container, holds all data for one workflow |
| **Node** | Tree | Executable unit, carries configuration and business logic |
| **Slot** | Node | Typed connection point with direction constraints |
| **Link** | — | Visual connection between two Slots |
