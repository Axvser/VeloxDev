using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;

namespace Demo
{
    /// <summary>
    /// 采样器演示台：每条采样器一个可见的小舞台。点它的把手，这条采样器就按缓动跑一遍，肉眼看得见它在动。
    /// </summary>
    /// <remarks>
    /// 每一格托着一个 <see cref="SamplerSubject"/> —— 采样器直接写在它上面，它自己按属性重绘。所以这里不再有
    /// 任何"把值映射成画面"的代码：标尺与投影都在控件里，而属性持有的始终是采样器写下的原值。
    /// <para>
    /// 格子仍然单行排开（用列数算得出来的 Grid，不用会折行的面板）：窗口的高是屏幕给的、宽不是，所以省高度
    /// 比省宽度要紧；而 VariableSizedWrapGrid 的方向一旦取默认值，格子会排成一条几千像素高的竖列，
    /// UI Automation 照样报它在、可点，肉眼却看不到 —— 这块台子的全部意义就是看得见。
    /// </para>
    /// </remarks>
    internal sealed class SamplerBench
    {
        private const double StageWidth = 96d;
        private const double StageHeight = 62d;

        private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

        /// <summary>造出整块演示台，每条采样器一格。</summary>
        internal FrameworkElement Build(IReadOnlyList<string> samplers)
        {
            var bench = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            bench.Children.Add(new TextBlock
            {
                Text = $"采样器演示台 — 点把手看它动；每一格显示该采样器写进控件属性的效果"
                     + $"（位移/尺寸/栅格长度按 ×{SamplerSubject.DrawScale:0.##} 画，其余按原值；载荷里始终是原值）",
                FontSize = 10,
                Margin = new Thickness(2, 0, 2, 3),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x99, 0x99, 0x99)),
            });

            var grid = new Grid();

            var column = 0;
            foreach (var sampler in samplers)
            {
                var (view, subject) = BuildCell(sampler);
                _subjects[sampler] = subject;

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(view, column++);
                grid.Children.Add(view);
            }

            bench.Children.Add(grid);
            return bench;
        }

        /// <summary>
        /// 造一格：一块裁过的舞台、一个被写对象，加一条标签。
        /// </summary>
        /// <remarks>
        /// 标签带上自动化 id（与把手同一套 token 方案）：一格在屏幕上到底占了哪里，只有量出来才算数 ——
        /// 演示台本身没有可读的观测面，而"格子被排到窗口外"正是这类界面最容易悄悄发生的故障。
        /// </remarks>
        private static (FrameworkElement View, SamplerSubject Subject) BuildCell(string sampler)
        {
            var subject = new SamplerSubject { Kind = sampler };

            var stage = new Canvas
            {
                Width = StageWidth,
                Height = StageHeight,
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)),
                // WinUI 的 Panel 没有 ClipToBounds，裁切要靠 Clip：不裁的话跑出格子的被写对象会压到邻格上。
                Clip = new RectangleGeometry { Rect = new Rect(0, 0, StageWidth, StageHeight) },
            };
            stage.Children.Add(subject);

            var label = new TextBlock
            {
                Text = sampler,
                FontSize = 9,
                Margin = new Thickness(1, 2, 1, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x99, 0x99, 0x99)),
            };
            AutomationProperties.SetAutomationId(label, $"over.cell.{sampler}");

            var view = new StackPanel { Margin = new Thickness(3) };
            view.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x40)),
                BorderThickness = new Thickness(1),
                Child = stage,
            });
            view.Children.Add(label);

            return (view, subject);
        }

        /// <summary>某条采样器那一格的被写控件 —— 采样器写在它上面，载荷也从它读回。</summary>
        internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];
    }
}
