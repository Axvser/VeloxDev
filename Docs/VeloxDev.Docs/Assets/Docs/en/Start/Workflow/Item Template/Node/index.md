# Node

> **Create - {$plateform} available ava / wpf / winui / maui**

```shellscript
dotnet new {$plateform}-v-node -n NodeView -ns MyApp.Views
```

> **Personality parameter**

| Option | Meaning |
| --- | --- |
| -bg | Background color |
| -fg | Foreground color |
| -bb | Border brush |
| -bt | Border thickness |
| -cr | Corner radius |

> **fine-tuning**

For the item template of the Node component, it is often necessary to modify the rendering of its internal Slot members. As mentioned in previous documentation, the view model of the Node component can define a single Slot or multiple Slot collections in two ways. In XAML, they need to pass their Name to the corresponding attached properties, namely SlotNames and SlotEnumeratorNames. Both can use commas to separate multiple members.

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

        <!-- Node drag behavior -->
		<Grid behaviors:WorkflowNodeDragBehavior.CoordinateHostName="PART_Canvas" behaviors:WorkflowNodeDragBehavior.IsEnabled="True"
			  Grid.Row="0"
			  Margin="12,0">
		</Grid>
        
        <!-- Single Slot component -->
		<local:SlotView x:Name="PART_InputSlot" DataContext="{Binding InputSlot}"
						Width="18"
						Height="18"
						Margin="2,0,0,0"
						HorizontalAlignment="Left"
						VerticalAlignment="Center"
						Foreground="TemplateNodeForeground" />
     
        <!-- Multiple Slot components -->
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