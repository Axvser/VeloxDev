# Link Interaction

Links support the following interactions:

- **Delete**: Select and press Delete or call `DeleteCommand()`
- **Auto-routing**: The visual path (Bezier/polyline) auto-adjusts when connected nodes move
- **Visibility**: Controlled by `IsVisible` property on `IWorkflowLinkViewModel`

The platform-specific view layer renders links as Bezier curves or polylines based on the view template.
