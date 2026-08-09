using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
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
                _ => ParseColor("#DD1E1E1E"),
            });
        }

        private static Windows.UI.Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return hex.Length == 8
                ? Windows.UI.Color.FromArgb(
                    (byte)(value >> 24),
                    (byte)(value >> 16),
                    (byte)(value >> 8),
                    (byte)value)
                : Windows.UI.Color.FromArgb(
                    0xFF,
                    (byte)(value >> 16),
                    (byte)(value >> 8),
                    (byte)value);
        }

    }
}
