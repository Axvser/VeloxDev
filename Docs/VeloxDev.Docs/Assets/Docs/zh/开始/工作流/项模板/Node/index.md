# Node

> **创建 - {$plateform} 可选 ava / wpf / winui / maui**

```shellscript
dotnet new {$plateform}-v-node -n NodeView -ns MyApp.Views
```

> **个性参数**

| 选项 | 含义 |
| --- | --- |
| -bg | 背景色 |
| -fg | 前景色 |
| -bb | 边框画刷 |
| -bt | 边框粗细 |
| -cr | 圆角半径 |

> **微调**

对于 Node 组件的项模板，常需修改其内部 Slot 成员的渲染方式，在之前的文档中已经提到过，Node 组件的视图模型内部可通过两种方式定义单个Slot或多Slot集合，那么它们在XAML中需要将自己的Name传递给对应附加属性，也就是SlotNames和SlotEnumeratorNames，二者均可用逗号隔开多个成员

```xml
<!-- Customize the content, but keep PART_* names synchronized with WorkflowSlotLayoutBehavior. -->
<UserControl xmlns="https://github.com/avaloniaui"
			 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			 xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
			 xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
			 xmlns:local="using:TemplateNamespace"
			 xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"
			 mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
			 x:Class="TemplateNamespace.TemplateClass"
			 behaviors:WorkflowSlotLayoutBehavior.IsEnabled="True" behaviors:WorkflowSlotLayoutBehavior.SlotNames="PART_InputSlot" behaviors:WorkflowSlotLayoutBehavior.SlotEnumeratorNames="PART_OutputSlots" behaviors:WorkflowSlotLayoutBehavior.CoordinateHostName="PART_Canvas"
			 Foreground="TemplateNodeForeground"
			 x:CompileBindings="False">
			 <!-- Replace x:CompileBindings=False with a concrete x:DataType after defining your ViewModel. -->

        <!-- 节点拖动行为 -->
		<Grid behaviors:WorkflowNodeDragBehavior.CoordinateHostName="PART_Canvas" behaviors:WorkflowNodeDragBehavior.IsEnabled="True"
			  Grid.Row="0"
			  Margin="12,0">
		</Grid>
        
        <!-- 单个 Slot 组件 -->
		<local:SlotView x:Name="PART_InputSlot" DataContext="{Binding InputSlot}"
						Width="18"
						Height="18"
						Margin="2,0,0,0"
						HorizontalAlignment="Left"
						VerticalAlignment="Center"
						Foreground="TemplateNodeForeground" />
     
        <!-- 多个 Slot 组件 -->
		<ItemsControl x:Name="PART_OutputSlots" ItemsSource="{Binding OutputSlots.Items}"
					  Grid.Row="1"
					  Margin="12,6"
					  VerticalAlignment="Center">
			<ItemsControl.ItemTemplate>
				<DataTemplate>
					<Grid ColumnDefinitions="*,Auto" Margin="0,3" ClipToBounds="False">
						<TextBlock Text="{Binding Name}"
								   Margin="0,0,10,0"
								   HorizontalAlignment="Right"
								   VerticalAlignment="Center" />
						<local:SlotView DataContext="{Binding Slot}"
												Grid.Column="1"
												Width="14"
												Height="14"
												Margin="0"
												Foreground="TemplateNodeForeground" />
					</Grid>
				</DataTemplate>
			</ItemsControl.ItemTemplate>
		</ItemsControl>

</UserControl>

```