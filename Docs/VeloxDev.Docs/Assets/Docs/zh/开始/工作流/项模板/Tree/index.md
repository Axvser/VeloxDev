# Tree

> **创建 - {$plateform} 可选 ava / wpf / winui / maui**

```shellscript
dotnet new {$plateform}-v-tree -n TreeView -ns MyApp.Views
```

> **个性参数**

| 选项 | 含义 |
| --- | --- |
| -bg | 背景色 |
| -bb | 边框画刷 |
| -bt | 边框粗细 |
| -cr | 圆角半径 |

> **微调**

对于 Tree 组件的项模板，常需修改 ViewModel → View 的映射，一般对于 Avalonia 是不需要写模板选择器的，其它平台则必须创建

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