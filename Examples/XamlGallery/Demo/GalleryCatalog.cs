using Demo.Views.Sections;

namespace Demo;

/// <summary>How a catalogue entry is previewed.</summary>
public enum GalleryPreviewKind
{
    /// <summary>One card extracted from its section.</summary>
    Card,

    /// <summary>The whole section page, used for group overviews and layout-sensitive cards.</summary>
    Page,
}

/// <summary>One selectable node of the gallery catalogue.</summary>
public sealed class GalleryEntry
{
    /// <summary>Creates an entry.</summary>
    public GalleryEntry(
        string id,
        string title,
        string summary,
        string keywords,
        string cardName,
        GalleryPreviewKind kind,
        string snippet)
    {
        Id = id;
        Title = title;
        Summary = summary;
        Keywords = keywords;
        CardName = cardName;
        Kind = kind;
        Snippet = snippet;
    }

    /// <summary>The stable identifier used by the tree, the preview and capture mode.</summary>
    public string Id { get; }

    /// <summary>The text shown in the catalogue tree.</summary>
    public string Title { get; }

    /// <summary>One sentence displayed under the preview title.</summary>
    public string Summary { get; }

    /// <summary>Extra search words: type names and concepts.</summary>
    public string Keywords { get; }

    /// <summary>The x:Name of the card Border, or empty for a section page.</summary>
    public string CardName { get; }

    /// <summary>Whether this entry previews a card or the whole section.</summary>
    public GalleryPreviewKind Kind { get; }

    /// <summary>A short XAML usage sample.</summary>
    public string Snippet { get; }

    /// <summary>The owning group; assigned by <see cref="GalleryGroup"/>.</summary>
    public GalleryGroup Group { get; internal set; } = null!;

    /// <summary>The section number, for example <c>01</c>.</summary>
    public string Number => Group.Number;

    /// <summary>The section title.</summary>
    public string GroupTitle => Group.Title;

    /// <summary>The section view type instantiated for this entry.</summary>
    public Type SectionType => Group.SectionType;

    /// <summary>True when this entry shows a whole section page.</summary>
    public bool IsPage => Kind == GalleryPreviewKind.Page;

    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>One family of gallery entries plus its section overview entry.</summary>
public sealed class GalleryGroup
{
    /// <summary>Creates a group and its page entry.</summary>
    public GalleryGroup(
        string id,
        string number,
        string title,
        string summary,
        Type sectionType,
        string snippet,
        params GalleryEntry[] entries)
    {
        Id = id;
        Number = number;
        Title = title;
        Summary = summary;
        SectionType = sectionType;
        Snippet = snippet;
        Entries = entries;
        foreach (var entry in entries)
        {
            entry.Group = this;
        }

        Page = new GalleryEntry(id, title, summary, $"{title} section overview", string.Empty, GalleryPreviewKind.Page, snippet)
        {
            Group = this,
        };
    }

    /// <summary>The stable group identifier, also the id of its page entry.</summary>
    public string Id { get; }

    /// <summary>The section number shown in the breadcrumb.</summary>
    public string Number { get; }

    /// <summary>The section title.</summary>
    public string Title { get; }

    /// <summary>One sentence describing the section.</summary>
    public string Summary { get; }

    /// <summary>The section view type instantiated for page and card previews.</summary>
    public Type SectionType { get; }

    /// <summary>A short usage sample for the section overview.</summary>
    public string Snippet { get; }

    /// <summary>The card entries of this group.</summary>
    public IReadOnlyList<GalleryEntry> Entries { get; }

    /// <summary>The overview entry that shows the whole section page.</summary>
    public GalleryEntry Page { get; }
}

/// <summary>The static catalogue behind the tree, the preview and capture mode.</summary>
public static class GalleryCatalog
{
    private static readonly GalleryGroup[] GroupsSource =
    [
        new GalleryGroup(
            "layout", "01", "Layout", "Panels, tracks and containers.",
            typeof(LayoutSection),
            """
            <StackPanel Dock="Top" Spacing="16" Padding="4,4,20,28">
              <Border>...</Border>
            </StackPanel>
            """,
            new GalleryEntry(
                "layout.stack-panel", "StackPanel",
                "Children stack along one axis with an explicit gap; the cross axis stretches.",
                "StackPanel panel stack spacing orientation vertical horizontal",
                "CardStackPanel", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="10">
                  <Border Width="104" Height="48" Background="{DynamicResource FieldBrush}" />
                  <Border Width="104" Height="48" Background="{DynamicResource AccentGradient}" />
                </StackPanel>
                """),
            new GalleryEntry(
                "layout.dock-panel", "DockPanel",
                "Top, left and bottom edges claim their room; the last child fills what is left.",
                "DockPanel dock left top bottom last child fill",
                "CardDockPanel", GalleryPreviewKind.Card,
                """
                <DockPanel Height="100" LastChildFill="True">
                  <Border Dock="Top" Height="28" Background="{DynamicResource AccentSoftBrush}" />
                  <Border Dock="Left" Width="112" Background="{DynamicResource FieldBrush}" />
                  <Border />
                </DockPanel>
                """),
            new GalleryEntry(
                "layout.wrap-uniform", "WrapPanel and UniformGrid",
                "Wrap rows reflow by available width; uniform cells divide the same room evenly.",
                "WrapPanel UniformGrid wrap reflow item width height rows columns",
                "CardWrapUniform", GalleryPreviewKind.Card,
                """
                <WrapPanel Width="360" ItemWidth="84" ItemHeight="30">
                  <Border Background="{DynamicResource FieldBrush}" />
                  <Border Background="{DynamicResource AccentSoftBrush}" />
                </WrapPanel>
                """),
            new GalleryEntry(
                "layout.grid-canvas", "Grid and Canvas",
                "Rows, columns and spans on the left; explicit left/top coordinates on the right.",
                "Grid Canvas RowDefinition ColumnDefinition RowSpan Canvas.Left Canvas.Top",
                "CardGridCanvas", GalleryPreviewKind.Card,
                """
                <Grid Width="480" Height="128">
                  <Grid.RowDefinitions>
                    <RowDefinition Height="34" />
                    <RowDefinition Height="*" />
                  </Grid.RowDefinitions>
                  <Border Grid.Row="0" Background="{DynamicResource AccentSoftBrush}" />
                  <Border Grid.Row="1" Background="{DynamicResource FieldBrush}" />
                </Grid>
                """),
            new GalleryEntry(
                "layout.splitter-viewbox", "GridSplitter and Viewbox",
                "Drag the grip to resize siblings; a fixed composition scales into the available area.",
                "GridSplitter Viewbox resize splitter uniform scale",
                "CardSplitterViewbox", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Width="400" Spacing="8">
                  <Border Width="160" MinWidth="80" Background="{DynamicResource FieldBrush}" />
                  <GridSplitter Width="12" ResizeDirection="Columns" ResizeBehavior="PreviousAndNext" />
                  <Border Width="190" MinWidth="80" Background="{DynamicResource AccentSoftBrush}" />
                </StackPanel>
                """),
            new GalleryEntry(
                "layout.chrome", "Chrome containers",
                "GroupBox, Expander and a nested TabControl are XAML controls with themed chrome.",
                "GroupBox Expander TabControl TabItem chrome",
                "CardChrome", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="14">
                  <GroupBox Width="250" Header="GroupBox">
                    <TextBlock Text="A titled frame." />
                  </GroupBox>
                  <Expander Width="270" Header="Expander" IsExpanded="True">
                    <TextBlock Text="Collapsible content." />
                  </Expander>
                </StackPanel>
                """),
            new GalleryEntry(
                "layout.decorators", "Decorators",
                "Decorator and AdornerDecorator pass the full area to their content; BulletDecorator pairs a glyph with text.",
                "Decorator AdornerDecorator BulletDecorator",
                "CardDecorators", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="14">
                  <Decorator Width="240" Height="110">
                    <Border Background="{DynamicResource FieldBrush}" />
                  </Decorator>
                  <AdornerDecorator Width="240" Height="110">
                    <Border Background="{DynamicResource FieldBrush}" />
                  </AdornerDecorator>
                </StackPanel>
                """),
            new GalleryEntry(
                "layout.zindex-rowspan", "ZIndex and RowSpan",
                "Panel.ZIndex decides which canvas sibling paints in front; Grid.RowSpan lets one child cover two rows.",
                "Panel.ZIndex ZIndex RowSpan Grid overlap",
                "CardZIndexRowSpan", GalleryPreviewKind.Card,
                """
                <Canvas Width="300" Height="120">
                  <Border Canvas.Left="0" Canvas.Top="0" Width="180" Background="{DynamicResource FieldBrush}" />
                  <Border Canvas.Left="60" Canvas.Top="40" Width="180" Panel.ZIndex="5"
                          Background="{DynamicResource AccentGradient}" />
                </Canvas>
                """)
        ),

        new GalleryGroup(
            "text", "02", "Text", "Text, editors and documents.",
            typeof(TextSection),
            """
            <TextBlock Text="Self-painted text" TextWrapping="Wrap" />
            """,
            new GalleryEntry(
                "text.primitives", "TextBlock, Label and AccessText",
                "Wrapping, trimming, alignment and an access-key marker all come from the text primitives.",
                "TextBlock Label AccessText wrapping trimming alignment mnemonic",
                "CardTextPrimitives", GalleryPreviewKind.Card,
                """
                <TextBlock Text="Wrapping text keeps its words inside the card."
                           TextWrapping="Wrap" MaxWidth="620" />
                <Label Text="Label renders plain text with the theme foreground." />
                """),
            new GalleryEntry(
                "text.editors", "Text, password and multiline boxes",
                "The native editor is themed by the runtime; dependency properties stay two-way in sync.",
                "TextBox PasswordBox multiline readonly placeholder input",
                "CardTextEditors", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="12">
                  <TextBox Width="300" Height="34" PlaceholderText="Type a display name"
                           Text="{Binding OptionalName, Mode=TwoWay}" />
                  <PasswordBox Width="220" Height="34" Password="veloxdev" />
                </StackPanel>
                """),
            new GalleryEntry(
                "text.headered-content", "HeaderedContentControl",
                "A themed header strip above a single content child.",
                "HeaderedContentControl header content",
                "CardHeaderedContent", GalleryPreviewKind.Card,
                """
                <HeaderedContentControl Width="460" Height="96" Header="Header strip">
                  <TextBlock Text="Content sits below the header." Margin="10" />
                </HeaderedContentControl>
                """),
            new GalleryEntry(
                "text.flow-documents", "Flow documents",
                "All three hosts render the same FlowDocument resource; the first two are for reading, the last one is paged.",
                "RichTextBox FlowDocumentScrollViewer DocumentViewer FlowDocument Paragraph Table",
                "CardFlowDocuments", GalleryPreviewKind.Card,
                """
                <RichTextBox Width="440" Height="180" IsReadOnly="True"
                             Document="{StaticResource SampleDocument}" />
                <FlowDocumentScrollViewer Width="440" Height="180"
                                          Document="{StaticResource SampleDocument}" />
                """)
        ),

        new GalleryGroup(
            "data", "03", "Data", "Items, selectors and views.",
            typeof(DataSection),
            """
            <ItemsControl ItemsSource="{Binding Rows}" ItemTemplate="{StaticResource RowTemplate}" />
            """,
            new GalleryEntry(
                "data.items-control", "ItemsControl and containers",
                "Every item is a real control; the selector picks a muted template for disabled rows.",
                "ItemsControl ItemsPanel ItemTemplate TemplateSelector ItemContainerStyle",
                "CardItemsControl", GalleryPreviewKind.Card,
                """
                <ItemsControl ItemsSource="{Binding Rows}"
                              ItemTemplateSelector="{StaticResource RowSelector}">
                  <ItemsControl.ItemsPanel>
                    <StackPanel Spacing="6" />
                  </ItemsControl.ItemsPanel>
                </ItemsControl>
                """),
            new GalleryEntry(
                "data.selectors", "ListBox, ComboBox and TreeView",
                "The native selectors provide selection and scrolling; TreeView discovers children by property name.",
                "ListBox ComboBox TreeView DisplayMemberPath selection native",
                "CardSelectors", GalleryPreviewKind.Card,
                """
                <ListBox Width="220" Height="160" ItemsSource="{Binding Rows}"
                         DisplayMemberPath="Name" />
                <ComboBox Width="220" Height="34" ItemsSource="{Binding Rows}"
                          DisplayMemberPath="Name" />
                <TreeView Width="260" Height="160" ItemsSource="{Binding TreeNodes}"
                          DisplayMemberPath="Name" />
                """),
            new GalleryEntry(
                "data.list-grid", "ListView and DataGrid",
                "Column views backed by the native list and grid, with themed colours from the active palette.",
                "ListView GridView DataGrid columns native",
                "CardListGrid", GalleryPreviewKind.Card,
                """
                <ListView Width="420" Height="194" ItemsSource="{Binding Rows}">
                  <ListView.View>
                    <GridView>
                      <GridViewColumn Header="Name" DisplayMemberBinding="Name" Width="180" />
                    </GridView>
                  </ListView.View>
                </ListView>
                """),
            new GalleryEntry(
                "data.calendar", "Calendar and DatePicker",
                "Both selectors write the same nullable date property; the binding is two-way.",
                "Calendar DatePicker SelectedDate date two-way",
                "CardCalendar", GalleryPreviewKind.Card,
                """
                <Calendar Width="240" Height="170" SelectedDate="{Binding Date, Mode=TwoWay}" />
                <DatePicker Width="260" Height="34" SelectedDate="{Binding Date, Mode=TwoWay}" />
                """),
            new GalleryEntry(
                "data.collection-view", "CollectionView sorting and filtering",
                "The view is a WPF-shaped CollectionView over the same rows; Refresh republishes it to the ItemsControl.",
                "CollectionView sorting filtering SortDescriptions Filter",
                "CardCollectionView", GalleryPreviewKind.Card,
                """
                <CollectionViewSource Source="{Binding Rows}" x:Key="RowsViewSource" />
                <ItemsControl ItemsSource="{Binding RowsView}" />
                """),
            new GalleryEntry(
                "data.virtualized-list", "Virtualizing ItemsControl",
                "Four hundred rows, fixed row height; only the rows inside the viewport are realized.",
                "Virtualizing ItemsControl ItemHeight ScrollViewer extent viewport realized",
                "CardVirtualizedList", GalleryPreviewKind.Page,
                """
                <ScrollViewer ExtentHeight="11200" ViewportHeight="280">
                  <ItemsControl Virtualizing="True" ItemHeight="28"
                                ItemsSource="{Binding VirtualRows}" />
                </ScrollViewer>
                """)
        ),

        new GalleryGroup(
            "input", "04", "Input", "Buttons, ranges, menus and pointer input.",
            typeof(InputSection),
            """
            <Button Content="OK" Command="{Binding SaveCommand}" />
            """,
            new GalleryEntry(
                "input.buttons", "Buttons, toggles and commands",
                "Command drives IsEnabled through CanExecuteChanged; each button paints its own hover and press states.",
                "Button RepeatButton ToggleButton CheckBox RadioButton Command CanExecute",
                "CardButtons", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="10">
                  <Button Content="Save" Command="{Binding SaveCommand}" Height="34" />
                  <ToggleButton Content="Toggle" Height="34" />
                  <CheckBox Content="Check" />
                  <RadioButton Content="Radio" />
                </StackPanel>
                """),
            new GalleryEntry(
                "input.ranges", "Ranges",
                "Slider, ProgressBar and ScrollBar share value clamping and theme brushes.",
                "Slider ProgressBar ScrollBar value minimum maximum range",
                "CardRanges", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="14">
                  <Slider Width="240" Height="34" Minimum="0" Maximum="100" Value="55" />
                  <ProgressBar Width="180" Height="20" Minimum="0" Maximum="100" Value="55" />
                </StackPanel>
                """),
            new GalleryEntry(
                "input.menus-toolbars", "Menus, toolbars and status bars",
                "Declarative wrappers around the native menu, tool strip and status strip.",
                "Menu MenuItem ContextMenu ToolBar ToolBarTray StatusBar StatusBarItem ToolTip Popup",
                "CardMenusToolbars", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="12">
                  <Menu>
                    <MenuItem Header="File">
                      <MenuItem Header="Open" />
                    </MenuItem>
                  </Menu>
                  <ToolBar Height="34">
                    <Button Content="Run" Height="28" />
                  </ToolBar>
                </StackPanel>
                """),
            new GalleryEntry(
                "input.pointer-keyboard", "Pointer and keyboard input",
                "The XAML pointer and key events bubble from the control under the pointer.",
                "PointerPressed PointerMoved PointerReleased KeyInputDown KeyInputUp input modifiers",
                "CardPointerKeyboard", GalleryPreviewKind.Card,
                """
                <Border Width="320" Height="140" Background="{DynamicResource FieldBrush}"
                        PointerEntered="OnPadPointerEntered"
                        PointerMoved="OnPadPointerMoved"
                        PointerExited="OnPadPointerExited"
                        KeyInputDown="OnPadKeyDown" />
                """)
        ),

        new GalleryGroup(
            "graphics", "05", "Graphics", "Shapes, brushes and scaled artwork.",
            typeof(GraphicsSection),
            """
            <Ellipse Width="80" Height="80" Fill="{DynamicResource AccentGradient}" />
            """,
            new GalleryEntry(
                "graphics.shapes", "Basic shapes",
                "Rectangle, Ellipse, Line, Path, Polygon and Polyline.",
                "Rectangle Ellipse Line Path Polygon Polyline shapes geometry",
                "CardShapes", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="16">
                  <Rectangle Width="90" Height="60" Fill="{DynamicResource AccentBrush}" RadiusX="10" RadiusY="10" />
                  <Ellipse Width="70" Height="70" Fill="{DynamicResource AccentGradient}" />
                  <Polygon Points="45,4 86,76 4,76" Fill="{DynamicResource FieldBrush}" />
                </StackPanel>
                """),
            new GalleryEntry(
                "graphics.brushes-bitmap", "Gradients and a bitmap",
                "A LinearGradientBrush can paint borders, shapes and cards; Image draws a GDI+ bitmap.",
                "LinearGradientBrush GradientStop Image bitmap brush",
                "CardBrushesBitmap", GalleryPreviewKind.Card,
                """
                <Border Width="440" Height="150" BorderThickness="0"
                        Background="{StaticResource HeroGradient}" Padding="24">
                  <TextBlock Text="Gradient surfaces" Foreground="White" />
                </Border>
                <Image Width="80" Height="80" Source="{x:Static demo:GalleryArt.Orb}" />
                """),
            new GalleryEntry(
                "graphics.stroked-geometry", "Pen-stroked geometry",
                "StrokeThickness draws the outline; a polygon closes its figure while a polyline stays open.",
                "Pen Stroke StrokeThickness Polygon Polyline geometry",
                "CardStrokedGeometry", GalleryPreviewKind.Card,
                """
                <Polygon Width="120" Height="96" Stretch="Uniform"
                         Points="60,4 116,90 4,90"
                         Fill="{DynamicResource AccentSoftBrush}"
                         Stroke="{DynamicResource AccentBrush}" StrokeThickness="2" />
                """),
            new GalleryEntry(
                "graphics.viewbox-artwork", "Viewbox-scaled artwork",
                "One fixed-size composition scaled uniformly into the card.",
                "Viewbox Stretch Uniform artwork scale",
                "CardViewboxArtwork", GalleryPreviewKind.Card,
                """
                <Viewbox Width="420" Height="100" Stretch="Uniform">
                  <StackPanel Orientation="Horizontal" Width="380" Height="88" Spacing="14" Padding="14"
                              Background="{DynamicResource FieldBrush}">
                    <Ellipse Width="60" Height="60" Fill="{DynamicResource AccentGradient}" />
                    <TextBlock Text="Text and geometry share one scale." VerticalAlignment="Center" />
                  </StackPanel>
                </Viewbox>
                """)
        ),

        new GalleryGroup(
            "animation", "06", "Animation", "Triggers, keyframes and visual states.",
            typeof(AnimationSection),
            """
            <Trigger Property="IsPointerOver" Value="True">
              <Trigger.EnterAction>
                <TransitionAction Property="Opacity" To="1" Duration="0:0:0.3" />
              </Trigger.EnterAction>
            </Trigger>
            """,
            new GalleryEntry(
                "animation.hover-transition", "Hover transition group",
                "One EnterAction animates the background and the border together; ExitAction reverses both.",
                "Trigger TransitionAction EnterAction ExitAction hover transition",
                "CardHoverTransition", GalleryPreviewKind.Card,
                """
                <Border>
                  <Border.Triggers>
                    <Trigger Property="IsPointerOver" Value="True">
                      <Trigger.EnterAction>
                        <TransitionAction Property="Background" To="#FF2B3F66" Duration="0:0:0.2" />
                      </Trigger.EnterAction>
                    </Trigger>
                  </Border.Triggers>
                </Border>
                """),
            new GalleryEntry(
                "animation.keyframes", "Keyframes with loop and auto-reverse",
                "The chain is split into one transition per segment; LoopTime and IsAutoReverse wrap the whole run.",
                "KeyFrameAnimation KeyFrame LoopTime IsAutoReverse Ease",
                "CardKeyframes", GalleryPreviewKind.Card,
                """
                <KeyFrameAnimation Duration="0:0:1.2" LoopTime="0:0:2" IsAutoReverse="True">
                  <KeyFrame Offset="0" Property="CornerRadius" Value="8" />
                  <KeyFrame Offset="0.5" Property="CornerRadius" Value="26" />
                  <KeyFrame Offset="1" Property="CornerRadius" Value="8" />
                </KeyFrameAnimation>
                """),
            new GalleryEntry(
                "animation.custom-action", "Custom action and Thickness animation",
                "PulseAction is a local TriggerAction; BorderThickness is a Thickness value sampled by the adapter.",
                "TriggerAction PulseAction Thickness border thickness custom action",
                "CardCustomAction", GalleryPreviewKind.Card,
                """
                <Border.Triggers>
                  <Trigger Property="IsPointerOver" Value="True">
                    <Trigger.EnterAction>
                      <demo:PulseAction Radius="24" />
                    </Trigger.EnterAction>
                  </Trigger>
                </Border.Triggers>
                """),
            new GalleryEntry(
                "animation.visual-states", "VisualStateManager",
                "States are configured in code-behind; GoToState applies the setters and restores the previous state on exit.",
                "VisualStateManager VisualStateGroup VisualState GoToState states",
                "CardVisualStates", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button Content="Normal" Click="OnNormalStateClick" />
                  <Button Content="Hot" Click="OnHotStateClick" />
                  <Button Content="Pinned" Click="OnPinnedStateClick" />
                </StackPanel>
                """)
        ),

        new GalleryGroup(
            "binding", "07", "Binding", "Values, elements, validation and resources.",
            typeof(BindingSection),
            """
            <TextBlock Text="{Binding Amount, StringFormat='Amount: {0:N2}'}" />
            """,
            new GalleryEntry(
                "binding.value-shaping", "Value shaping",
                "StringFormat, converters, TargetNullValue and FallbackValue shape one bound value.",
                "Binding StringFormat Converter ConverterParameter TargetNullValue FallbackValue",
                "CardValueShaping", GalleryPreviewKind.Card,
                """
                <TextBlock Text="{Binding Amount, StringFormat='Amount: {0:N2}'}" />
                <TextBlock Text="{Binding Amount, Converter={StaticResource Prefix}, ConverterParameter='≈ '}" />
                <TextBlock Text="{Binding OptionalName, TargetNullValue='(null target)'}" />
                <TextBlock Text="{Binding Missing, FallbackValue='fallback value'}" />
                """),
            new GalleryEntry(
                "binding.element-name", "ElementName",
                "A dependency property on one element is the source for another; edits flow immediately.",
                "ElementName Binding Path TextBox Text template",
                "CardElementName", GalleryPreviewKind.Card,
                """
                <TextBox x:Name="elementSource" Width="300" Height="34" Text="Edit this value" />
                <TextBlock Text="{Binding ElementName=elementSource, Path=Text}" />
                """),
            new GalleryEntry(
                "binding.two-way-validation", "Two-way and validation",
                "The first field reads INotifyDataErrorInfo from the model; the second uses a custom ValidationRule attached in code.",
                "TwoWay UpdateSourceTrigger ValidationRule INotifyDataErrorInfo validation",
                "CardTwoWayValidation", GalleryPreviewKind.Card,
                """
                <TextBox Width="300" Height="34"
                         Text="{Binding OptionalName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                """),
            new GalleryEntry(
                "binding.dynamic-resource", "DynamicResource",
                "Replacing a dictionary value repaints every consumer without rebinding.",
                "DynamicResource ResourceDictionary resource replacement",
                "CardDynamicResource", GalleryPreviewKind.Card,
                """
                <Border Width="240" Height="86" Background="{DynamicResource LiveBrush}" />
                Resources["LiveBrush"] = new SolidColorBrush(nextColor);
                """)
        ),

        new GalleryGroup(
            "styles", "08", "Styles", "Styles, templates and triggers.",
            typeof(StylesSection),
            """
            <Style x:Key="CardStyle" TargetType="Border">
              <Style.Setters>
                <Setter Property="CornerRadius" Value="8" />
              </Style.Setters>
            </Style>
            """,
            new GalleryEntry(
                "styles.explicit", "Explicit styles and BasedOn",
                "Both tiles use a keyed style; the second inherits every setter from the first and overrides three.",
                "Style BasedOn Style.Setters explicit style",
                "CardExplicitStyles", GalleryPreviewKind.Card,
                """
                <Style x:Key="ElevatedCard" TargetType="Border" BasedOn="{StaticResource BaseCard}">
                  <Style.Setters>
                    <Setter Property="Background" Value="{DynamicResource AccentSoftBrush}" />
                    <Setter Property="CornerRadius" Value="14" />
                  </Style.Setters>
                </Style>
                """),
            new GalleryEntry(
                "styles.control-template", "ControlTemplate and triggers",
                "GalleryBadge has generated dependency properties; the template binds them and a trigger repaints a named part.",
                "ControlTemplate TemplateBinding Style.Template template trigger generated property XamlProperty",
                "CardControlTemplate", GalleryPreviewKind.Card,
                """
                <Style x:Key="BadgeStyle" TargetType="demo:GalleryBadge" Template="{StaticResource BadgeTemplate}">
                  <Style.Setters>
                    <Setter Property="Width" Value="360" />
                  </Style.Setters>
                </Style>
                <demo:GalleryBadge Style="{StaticResource BadgeStyle}" Label="Templated badge" />
                """),
            new GalleryEntry(
                "styles.data-multi-trigger", "DataTrigger and MultiTrigger",
                "Clear the name to satisfy the data condition; turn on both toggles to satisfy the multi condition.",
                "DataTrigger MultiTrigger Condition Setter trigger",
                "CardDataMultiTrigger", GalleryPreviewKind.Card,
                """
                <DataTrigger Binding="{Binding OptionalName}" Value="">
                  <DataTrigger.Setters>
                    <Setter Property="Note" Value="empty optional name" />
                  </DataTrigger.Setters>
                </DataTrigger>
                """),
            new GalleryEntry(
                "styles.theme-fallback", "Theme fallback",
                "These controls set no colours. ThemeStyles supplies surface, border, foreground and corner radius from the active palette.",
                "ThemeStyles implicit style fallback theme default",
                "CardThemeFallback", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="14">
                  <Button Content="Theme button" Height="34" />
                  <TextBox Width="240" Height="34" PlaceholderText="Theme text box" />
                  <ProgressBar Width="180" Height="20" Value="55" />
                </StackPanel>
                """),
            new GalleryEntry(
                "styles.presenters", "Presenters and headered items",
                "ContentPresenter materializes a template for one object; HeaderedItemsControl adds a themed strip above generated items.",
                "ContentPresenter HeaderedItemsControl ItemsPanel Separator",
                "CardPresenters", GalleryPreviewKind.Card,
                """
                <ContentPresenter Width="220" Height="120"
                                  Content="{Binding Rows[0]}"
                                  ContentTemplate="{StaticResource ChipTemplate}" />
                <HeaderedItemsControl Width="360" Header="Headered items"
                                      ItemsSource="{Binding Rows}"
                                      ItemTemplate="{StaticResource ChipTemplate}" />
                """)
        ),

        new GalleryGroup(
            "theme", "09", "Theme", "Palette, brushes and fallback.",
            typeof(ThemeSection),
            """
            <Border Background="{DynamicResource AccentBrush}" />
            """,
            new GalleryEntry(
                "theme.palette", "Palette",
                "Every swatch is a DynamicResource consumer; the shell swaps the brushes in one place.",
                "Theme ThemePalette palette window surface card field accent",
                "CardPalette", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="12">
                  <Border Width="160" Background="{DynamicResource WindowBrush}" />
                  <Border Width="160" Background="{DynamicResource SurfaceBrush}" />
                  <Border Width="160" Background="{DynamicResource AccentBrush}" />
                </StackPanel>
                """),
            new GalleryEntry(
                "theme.gradient-hero", "Gradient hero",
                "A LinearGradientBrush is a brush like any other: borders, shapes and cards accept it.",
                "LinearGradientBrush GradientStop gradient hero",
                "CardGradientHero", GalleryPreviewKind.Card,
                """
                <Border Width="440" Height="150" BorderThickness="0"
                        Background="{StaticResource HeroGradient}" Padding="24">
                  <TextBlock Text="Gradient surfaces" Foreground="White" />
                </Border>
                """),
            new GalleryEntry(
                "theme.fallback-metrics", "Fallback metrics",
                "The palette also owns corner radius and control height; unstyled controls take both.",
                "Theme ControlHeight CornerRadius metrics fallback",
                "CardFallbackMetrics", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="12">
                  <Button Content="34px control" />
                  <Border Width="160" Height="60" Background="{DynamicResource FieldBrush}" />
                </StackPanel>
                """)
        ),

        new GalleryGroup(
            "navigation", "10", "Navigation", "Frames, pages and detached windows.",
            typeof(NavigationSection),
            """
            <Frame x:Name="frame" Navigate="OnNavigate" />
            """,
            new GalleryEntry(
                "navigation.frame-journal", "Frame and Page journal",
                "Navigate pushes a page; Back and Forward walk the journal without rebuilding the pages.",
                "Frame Page Navigate GoBack GoForward journal",
                "CardFrameJournal", GalleryPreviewKind.Card,
                """
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button Content="Back" Click="OnBackClick" />
                  <Button Content="Forward" Click="OnForwardClick" />
                </StackPanel>
                <Frame x:Name="frame" Height="240" />
                """),
            new GalleryEntry(
                "navigation.navigation-window", "NavigationWindow",
                "NavigationWindow forwards the same journal to a hosted Frame and adds Back/Forward chrome; the detached Window hosts arbitrary XAML content.",
                "NavigationWindow Window Frame journal detached window",
                "CardNavigationWindow", GalleryPreviewKind.Card,
                """
                <Button Content="Open window" Click="OnOpenWindowClick" />
                <Button Content="Open NavigationWindow" Click="OnOpenNavigationWindowClick" />
                """)
        ),

        new GalleryGroup(
            "visuals", "11", "Visuals", "The opt-in lightweight visual tree.",
            typeof(VisualsSection),
            """
            <VisualHost x:Name="visualHost" Height="320" />
            """,
            new GalleryEntry(
                "visuals.composition", "Composition without HWNDs",
                "The scene is a ContainerVisual tree. The sliders and toggles mutate it and invalidate one host.",
                "VisualHost ContainerVisual ShapeVisual TextVisual opacity transform clip drop shadow",
                "CardVisualComposition", GalleryPreviewKind.Card,
                """
                <VisualHost x:Name="visualHost" Height="320"
                            Background="{DynamicResource FieldBrush}" />
                root.Add(new ShapeVisual
                {
                    Bounds = new RectangleF(0, 0, 92, 92),
                    Geometry = new EllipseGeometry(),
                    Fill = new SolidColorBrush(Color.White),
                });
                """),
            new GalleryEntry(
                "visuals.connector-batch", "Connector batch",
                "Hundreds of cubic curves share a few batched paths. They are visual nodes, not controls.",
                "CurveBatchVisual curves connectors batch visual tree",
                "CardConnectorBatch", GalleryPreviewKind.Card,
                """
                <VisualHost x:Name="connectorHost" Height="250"
                            Background="{DynamicResource FieldBrush}" />
                """)
        ),
    ];

    /// <summary>The eleven groups in section-number order.</summary>
    public static IReadOnlyList<GalleryGroup> Groups => GroupsSource;

    /// <summary>Returns the group page and card entries in display order.</summary>
    public static IEnumerable<GalleryEntry> Enumerate(bool includeGroups)
    {
        foreach (var group in GroupsSource)
        {
            if (includeGroups)
            {
                yield return group.Page;
            }

            foreach (var entry in group.Entries)
            {
                yield return entry;
            }
        }
    }

    /// <summary>Finds an entry by stable id, including group page ids.</summary>
    public static GalleryEntry? Find(string id)
    {
        foreach (var group in GroupsSource)
        {
            if (string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return group.Page;
            }

            foreach (var entry in group.Entries)
            {
                if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>Finds a group by id or zero-based index.</summary>
    public static GalleryGroup? FindGroup(string idOrIndex)
    {
        if (int.TryParse(idOrIndex, out var index))
        {
            return index >= 0 && index < GroupsSource.Length ? GroupsSource[index] : null;
        }

        foreach (var group in GroupsSource)
        {
            if (string.Equals(group.Id, idOrIndex, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return null;
    }
}
