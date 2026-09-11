using Microsoft.Maui.Layouts;

// 同名类型一律显式取 MAUI 的那一侧：System.Drawing 里也有一份 PointF/RectF/SizeF，量纲不同、不能混。
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Demo;

/// <summary>
/// 采样器演示台：每条采样器一个可见的小舞台。点它的把手，这条采样器就按缓动跑一遍，肉眼看得见它在动。
/// </summary>
/// <remarks>
/// 每一格托着一个 <see cref="SamplerSubject"/> —— 采样器直接写在它上面，它自己按属性重绘。所以这里不再有
/// 任何"把值映射成画面"的代码：标尺与投影都在控件里，而属性持有的始终是采样器写下的原值。
/// <para>
/// <b>舞台是有标尺的。</b>位移类端点跑到了 220，格子按原值画会一步跨出格子、被裁掉 —— 那样看上去反倒像
/// "没动"。标尺在 <see cref="SamplerSubject"/> 里，这里只负责把格子摆出来；载荷里报的始终是原值。
/// </para>
/// <para>
/// 格子做成 84×56、一排十二格，是为了迁就这一页剩下的地方：把手条、读数与载荷都在同一列里，台子每高
/// 一分，它们就往窗口外面挪一分（"排在窗口外的控件"正是验收侧判定"没人够得着"的那条）。所以台子必须扁，
/// 一排十二格刚好放得下。
/// </para>
/// </remarks>
internal sealed class SamplerBench
{
    private const double StageWidth = 84d;
    private const double StageHeight = 56d;

    private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

    /// <summary>造出整块演示台，每条采样器一格。</summary>
    internal View Build(IReadOnlyList<string> samplers)
    {
        var bench = new VerticalStackLayout { Margin = new Thickness(0, 4, 0, 0) };

        bench.Children.Add(new Label
        {
            Text = "采样器演示台 — 点把手看它动；每一格显示该采样器写进控件属性的效果",
            FontSize = 10,
            Margin = new Thickness(2, 0, 2, 3),
            TextColor = MauiColor.FromRgb(0x99, 0x99, 0x99),
        });

        var cells = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap };

        foreach (var sampler in samplers)
        {
            var subject = new SamplerSubject { Kind = sampler };

            var stage = new Grid
            {
                WidthRequest = StageWidth,
                HeightRequest = StageHeight,
                BackgroundColor = MauiColor.FromRgb(0x1E, 0x1E, 0x1E),
                IsClippedToBounds = true,
            };
            stage.Children.Add(subject);

            _subjects[sampler] = subject;

            cells.Children.Add(new VerticalStackLayout
            {
                Margin = new Thickness(3),
                Children =
                {
                    new Border
                    {
                        Stroke = new SolidColorBrush(MauiColor.FromRgb(0x40, 0x40, 0x40)),
                        StrokeThickness = 1,
                        Padding = new Thickness(0),
                        Content = stage,
                    },
                    new Label
                    {
                        Text = sampler,
                        FontSize = 9,
                        Margin = new Thickness(1, 2, 1, 0),
                        TextColor = MauiColor.FromRgb(0x99, 0x99, 0x99),
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
