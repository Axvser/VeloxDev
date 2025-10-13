using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using VeloxDev.Core.Mono;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WinUI.PlatformAdapters;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VeloxDev.WinUI.Test
{
    [MonoBehaviour]
    public sealed partial class MainWindow : Window
    {

        private Test test = new();
        public MainWindow()
        {
            InitializeComponent();
            CanMonoBehaviour = true;
        }

        private int counter = 0;
        partial void Update()
        {
            counter++;
            tb.Text = $"counter {counter}";
            tb1.Text = $"test.counter {test.counter}";
            Task.Delay(5000);
        }

        private readonly TransitionEffect effect = new()
        {
            Duration = TimeSpan.FromSeconds(1),
            FPS = 144
        };

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var linearBrush = new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Red, Offset = 0.0 },
                    new GradientStop() { Color = Colors.Yellow, Offset = 0.5 },
                    new GradientStop() { Color = Colors.Green, Offset = 1.0 }
                ]
            };

            var linearBrush1 = new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Violet, Offset = 0.0 },
                    new GradientStop() { Color = Colors.Red, Offset = 1.0 }
                ]
            };

            // ✅ 定义3D变换起点与终点
            var projectionStart = new PlaneProjection()
            {
                RotationY = 0,
                GlobalOffsetZ = 0,
                CenterOfRotationX = 0.5,
                CenterOfRotationY = 0.5
            };

            var projectionEnd = new PlaneProjection()
            {
                RotationY = 180,
                RotationX = 180,
                GlobalOffsetZ = -80,
                CenterOfRotationX = 0.5,
                CenterOfRotationY = 0.5
            };

            // ✅ Brush 插值动画
            var animation = Transition<TextBlock>
                .Create()
                .Property(x => x.Foreground, linearBrush)
                .Property(x => x.Projection, projectionStart)
                .Effect(effect)
                .Execute(tb);

            // ✅ 第二个目标TextBlock动画
            var animation1 = Transition<TextBlock>
                .Create()
                .Property(x => x.Foreground, linearBrush1)
                .Property(x => x.Projection, projectionEnd)
                .Effect(effect)
                .Execute(tb1);
        }

        private void Button_Click1(object sender, RoutedEventArgs e)
        {
            var linearBrush = new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Red, Offset = 0.0 },
                    new GradientStop() { Color = Colors.Yellow, Offset = 0.5 },
                    new GradientStop() { Color = Colors.Green, Offset = 1.0 }
                ]
            };

            var linearBrush1 = new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Violet, Offset = 0.0 },
                    new GradientStop() { Color = Colors.Red, Offset = 1.0 }
                ]
            };

            // ✅ 定义3D变换起点与终点
            var projectionStart = new PlaneProjection()
            {
                RotationY = 0,
                GlobalOffsetZ = 0,
                CenterOfRotationX = 0.5,
                CenterOfRotationY = 0.5
            };

            var projectionEnd = new PlaneProjection()
            {
                RotationY = 0,
                RotationX = 0,
                GlobalOffsetZ = 0,
                CenterOfRotationX = 0.5,
                CenterOfRotationY = 0.5
            };

            // ✅ Brush 插值动画
            var animation = Transition<TextBlock>
                .Create()
                .Property(x => x.Foreground, linearBrush)
                .Property(x => x.Projection, projectionStart) // 初始Projection
                .Effect(effect)
                .Execute(tb);

            // ✅ 第二个目标TextBlock动画
            var animation1 = Transition<TextBlock>
                .Create()
                .Property(x => x.Foreground, linearBrush1)
                .Property(x => x.Projection, projectionEnd)
                .Effect(effect)
                .Execute(tb1);
        }
    }

    [MonoBehaviour]
    public partial class Test
    {
        public int counter = 0;

        public Test()
        {
            CanMonoBehaviour = true;
        }

        partial void Update()
        {
            counter++;
            Task.Delay(9000);
        }
    }
}
