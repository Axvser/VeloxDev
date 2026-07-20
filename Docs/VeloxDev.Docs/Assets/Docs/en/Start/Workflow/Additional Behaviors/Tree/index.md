# Tree

## [ Surface Behavior ]

Provides behavior registration based on Name capture at the UserControl level, covering the interactive behaviors required by elements such as Canvas, ScrollViewer, grid decoration lines, etc.

> **Attribute Overview**

| Additional Property | Type | Description |
|---|---|---|
| `IsEnabled` | `bool` | Enable the entire workflow canvas interaction behavior |
| `ScrollViewerName` | `string?` | Specify the `ScrollViewer` used for scrolling and viewport calculations |
| `CanvasName` | `string?` | Specify the workflow content canvas |
| `GridDecoratorName` | `string?` | Specify the grid/ruler decorator for synchronizing scroll offset and content offset |
| `PointerPressSourceName` | `string?` | Specify the element that receives the press event to initiate canvas panning |

## [ ViewPool Behavior ]

Provide an object pool mechanism for Canvas. Reusing view elements plays a key role in reducing GC pressure and improving rendering performance.

> **Attribute Overview**

| Additional Properties | Type | Purpose |
| --- | --- | --- |
| ItemsSource | INotifyCollectionChanged | Data source |
| TemplateSelector | Depends on the template selector interface corresponding to the GUI framework | Template selector |