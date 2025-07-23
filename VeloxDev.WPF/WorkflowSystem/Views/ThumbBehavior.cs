using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public static class ThumbBehavior
    {
        public static bool GetEnableChildMouseEvents(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableChildMouseEventsProperty);
        }

        public static void SetEnableChildMouseEvents(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableChildMouseEventsProperty, value);
        }

        public static readonly DependencyProperty EnableChildMouseEventsProperty =
            DependencyProperty.RegisterAttached("EnableChildMouseEvents", typeof(bool),
            typeof(ThumbBehavior), new PropertyMetadata(false, OnEnableChildMouseEventsChanged));

        private static void OnEnableChildMouseEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Thumb thumb)
            {
                if ((bool)e.NewValue)
                {
                    thumb.PreviewMouseDown += Thumb_PreviewMouseDown;
                }
                else
                {
                    thumb.PreviewMouseDown -= Thumb_PreviewMouseDown;
                }
            }
        }

        private static void Thumb_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var thumb = sender as Thumb;
            var originalSource = e.OriginalSource as DependencyObject;

            if (originalSource != null && originalSource != thumb)
            {
                e.Handled = false;
            }
        }
    }
}
