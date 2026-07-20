# Tree

> **Create - {$plateform} optional ava / wpf / winui / maui**

```shellscript
dotnet new {$plateform}-v-tree -n TreeView -ns MyApp.Views
```

> **Personalized parameters**

| Option | Meaning |
| --- | --- |
| -bg | Background color |
| -bb | Border brush |
| -bt | Border thickness |
| -cr | Corner radius |

> **Fine-tuning**

For the item template of the Tree component, it is often necessary to modify the ViewModel → View mapping. Generally, for Avalonia, there is no need to write a template selector, while other platforms must create one.

```xml
<!-- VeloxDev customization: Generate the Node, Slot, Link, selector, and grid-decorator templates, then update the local type names below if you renamed them. -->
<UserControl xmlns="https://github.com/avaloniaui"
			 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			 xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
			 xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
			 xmlns:local="using:TemplateNamespace"
			 mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
			 x:Class="TemplateNamespace.TemplateClass"
			 xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"
			 xmlns:framework="using:VeloxDev.WorkflowSystem"
			 behaviors:WorkflowSurfaceBehavior.IsEnabled="True" behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer" behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas" behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator" behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_SurfaceBorder"
			 x:CompileBindings="False">
			 <!-- Replace x:CompileBindings=False with a concrete x:DataType after defining your ViewModel. -->
	
	<UserControl.DataTemplates>
		<!-- Node Templates -->
		<DataTemplate DataType="framework:IWorkflowNodeViewModel">
			<local:NodeView Width="{Binding Size.Width}" Height="{Binding Size.Height}" Canvas.Left="{Binding Anchor.Horizontal}" Canvas.Top="{Binding Anchor.Vertical}" ZIndex="{Binding Anchor.Layer}" RenderTransform="{Binding $parent[local:TemplateClass].(behaviors:WorkflowCanvasTransformBehavior.Transform)}" />
		</DataTemplate>

		<!-- Connection Templates -->
		<DataTemplate DataType="framework:IWorkflowLinkViewModel">
			<local:LinkView StartLeft="{Binding Sender.Anchor.Horizontal}" StartTop="{Binding Sender.Anchor.Vertical}" EndLeft="{Binding Receiver.Anchor.Horizontal}" EndTop="{Binding Receiver.Anchor.Vertical}" ZIndex="-1" CanRender="{Binding IsVisible}" Width="{Binding $parent[Canvas].Bounds.Width}" Height="{Binding $parent[Canvas].Bounds.Height}" RenderTransform="{Binding $parent[local:TemplateClass].(behaviors:WorkflowCanvasTransformBehavior.Transform)}" />
		</DataTemplate>
	</UserControl.DataTemplates>

</UserControl>
```