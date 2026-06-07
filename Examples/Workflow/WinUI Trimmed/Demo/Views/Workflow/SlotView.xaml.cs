using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow
{
    public sealed partial class SlotView : UserControl
    {
        public static readonly DependencyProperty SlotStateProperty = DependencyProperty.Register(
            nameof(SlotState),
            typeof(SlotState),
            typeof(SlotView),
            new PropertyMetadata(SlotState.StandBy, OnSlotStateChanged));

        public SlotView()
        {
            InitializeComponent();
            UpdateForeground();
        }

        public SlotState SlotState
        {
            get => (SlotState)GetValue(SlotStateProperty);
            set => SetValue(SlotStateProperty, value);
        }

        private static void OnSlotStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SlotView)d).UpdateForeground();

        private void UpdateForeground()
        {
            RootPath.Fill = new SolidColorBrush(SlotState switch
            {
                var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Microsoft.UI.Colors.Violet,
                var state when state.HasFlag(SlotState.Sender) => Microsoft.UI.Colors.Tomato,
                var state when state.HasFlag(SlotState.Receiver) => Microsoft.UI.Colors.Lime,
                _ => Microsoft.UI.Colors.White,
            });
        }

    }
}
