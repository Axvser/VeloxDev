using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Demo
{
    /// <summary>
    /// 采样器演示台：每条采样器一个可见的小舞台。点它的把手，这条采样器就按缓动跑一遍，肉眼看得见它在动。
    /// </summary>
    /// <remarks>
    /// 每一格托着一个 <see cref="SamplerSubject"/> —— 采样器直接写在它上面，它自己按属性重绘。所以这里不再有
    /// 任何"把值映射成画面"的代码：标尺与投影都在控件里，而属性持有的始终是采样器写下的原值。
    /// </remarks>
    internal sealed class SamplerBench
    {
        private const int StageWidth = 96;
        private const int StageHeight = 62;

        /// <summary>标签那一列的宽度，舞台从它右边开始。</summary>
        private const int CaptionWidth = 124;

        private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

        /// <summary>造出整块演示台，每条采样器一格。</summary>
        internal Control Build(IReadOnlyList<string> samplers)
        {
            var bench = new Panel
            {
                Location = new Point(30, 920),
                Size = new Size(940, StageHeight + 2),
            };

            bench.Controls.Add(new Label
            {
                Text = $"演示台 ×{SamplerSubject.DrawScale:0.##}",
                Location = new Point(0, 0),
                Size = new Size(CaptionWidth - 4, 22),
                ForeColor = Color.FromArgb(0x99, 0x99, 0x99),
                Font = new Font("Consolas", 8),
            });

            var left = CaptionWidth;
            foreach (var sampler in samplers)
            {
                // 格子比舞台大一圈：固定单线边框占掉 1 像素，舞台要完整落在格子里，不能被边框裁掉。
                var cell = new Panel
                {
                    Location = new Point(left, 0),
                    Size = new Size(StageWidth + 2, StageHeight + 2),
                    BorderStyle = BorderStyle.FixedSingle,
                };

                var subject = new SamplerSubject
                {
                    Location = new Point(0, 0),
                    Size = new Size(StageWidth, StageHeight),
                };
                cell.Controls.Add(subject);
                _subjects[sampler] = subject;

                bench.Controls.Add(cell);
                left += StageWidth + 2 + 6;
            }

            return bench;
        }

        /// <summary>某条采样器那一格的被写控件 —— 采样器写在它上面，载荷也从它读回。</summary>
        internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];
    }
}
