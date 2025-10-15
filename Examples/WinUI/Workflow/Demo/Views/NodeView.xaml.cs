using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VeloxDev.Core.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views
{
    public sealed partial class NodeView : UserControl
    {
        private Point _dragStart;       // 拖拽起点
        private bool _isDragging;

        public NodeViewModel? ViewModel => DataContext as NodeViewModel;

        public NodeView()
        {
            InitializeComponent();

            ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        }

        private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            if (ViewModel == null)
                return;

            _isDragging = true;
            _dragStart = new Point(ViewModel.Anchor.Left, ViewModel.Anchor.Top);
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (!_isDragging || ViewModel == null)
                return;

            var total = e.Cumulative.Translation;
            ViewModel.Anchor = new Anchor(_dragStart.X + total.X, _dragStart.Y + total.Y);
        }

        private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
        }
    }
}
