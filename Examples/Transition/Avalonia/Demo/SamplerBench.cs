using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Demo;

/// <summary>
/// 采样器演示台：每条采样器一个可见的小舞台。点它的把手，这条采样器就按缓动跑一遍，肉眼看得见它在动。
/// </summary>
/// <remarks>
/// 每一格托着一个 <see cref="SamplerSubject"/> —— 采样器直接写在它上面，它自己按属性重绘。所以这里不再有
/// 任何"把值映射成画面"的代码：标尺与投影都在控件里，而属性持有的始终是采样器写下的原值。
/// </remarks>
internal sealed class SamplerBench
{
    private const double StageWidth = 96d;
    private const double StageHeight = 62d;

    private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

    /// <summary>造出整块演示台，每条采样器一格。</summary>
    internal Control Build(IReadOnlyList<string> samplers)
    {
        var bench = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        bench.Children.Add(new TextBlock
        {
            Text = "采样器演示台 —— 点把手看它动；每一格显示该采样器写进控件属性的效果",
            FontSize = 10,
            Margin = new Thickness(2, 0, 2, 3),
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
        });

        var cells = new WrapPanel();

        foreach (var sampler in samplers)
        {
            var stage = new Canvas
            {
                Width = StageWidth,
                Height = StageHeight,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                ClipToBounds = true,
            };

            var subject = new SamplerSubject { Kind = sampler };
            stage.Children.Add(subject);
            _subjects[sampler] = subject;

            cells.Children.Add(new StackPanel
            {
                Margin = new Thickness(3),
                Children =
                {
                    new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                        BorderThickness = new Thickness(1),
                        Child = stage,
                    },
                    new TextBlock
                    {
                        Text = sampler,
                        FontSize = 9,
                        Margin = new Thickness(1, 2, 1, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    },
                },
            });
        }

        bench.Children.Add(cells);
        return bench;
    }

    /// <summary>某条采样器那一格的被写控件 —— 采样器写在它上面，载荷也从它读回。</summary>
    internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];
}
