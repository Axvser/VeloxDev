# Additional Behavior

If you have already read about component construction and business logic injection, then the workflow is now ready to run.

The problem solved in this section is: how to implement a workflow GUI based on additional behaviors.

This section applies to Avalonia / WPF / WinUI / MAUI (WinForms does not have an attached property mechanism, only similar APIs; the recommended approach is to directly port from the repository's Demo; Razor is still in experimental stage).

> **Quote**

```xml
<UserControl xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors" />
```