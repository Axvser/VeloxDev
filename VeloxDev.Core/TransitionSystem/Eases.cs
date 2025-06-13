using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// ✨ ⌈ 核心 ⌋ 缓动函数
    /// <para>支持 : </para>
    /// <para></para>
    /// </summary>
    public static class Eases
    {
        public static IEaseCalculator Default => new EaseDefault();

        /// <summary>
        /// 正弦缓动
        /// </summary>
        public static class Sine
        {
            public static IEaseCalculator In => new EaseInSine();
            public static IEaseCalculator Out => new EaseOutSine();
            public static IEaseCalculator InOut => new EaseInOutSine();
        }

        /// <summary>
        /// 二次方缓动
        /// </summary>
        public static class Quad
        {
            public static IEaseCalculator In => new EaseInQuad();
            public static IEaseCalculator Out => new EaseOutQuad();
            public static IEaseCalculator InOut => new EaseInOutQuad();
        }

        /// <summary>
        /// 三次方缓动
        /// </summary>
        public static class Cubic
        {
            public static IEaseCalculator In => new EaseInCubic();
            public static IEaseCalculator Out => new EaseOutCubic();
            public static IEaseCalculator InOut => new EaseInOutCubic();
        }

        /// <summary>
        /// 四次方缓动
        /// </summary>
        public static class Quart
        {
            public static IEaseCalculator In => new EaseInQuart();
            public static IEaseCalculator Out => new EaseOutQuart();
            public static IEaseCalculator InOut => new EaseInOutQuart();
        }

        /// <summary>
        /// 五次方缓动
        /// </summary>
        public static class Quint
        {
            public static IEaseCalculator In => new EaseInQuint();
            public static IEaseCalculator Out => new EaseOutQuint();
            public static IEaseCalculator InOut => new EaseInOutQuint();
        }

        /// <summary>
        /// 指数缓动
        /// </summary>
        public static class Expo
        {
            public static IEaseCalculator In => new EaseInExpo();
            public static IEaseCalculator Out => new EaseOutExpo();
            public static IEaseCalculator InOut => new EaseInOutExpo();
        }

        /// <summary>
        /// 圆形缓动
        /// </summary>
        public static class Circ
        {
            public static IEaseCalculator In => new EaseInCirc();
            public static IEaseCalculator Out => new EaseOutCirc();
            public static IEaseCalculator InOut => new EaseInOutCirc();
        }

        /// <summary>
        /// 回弹缓动
        /// </summary>
        public static class Back
        {
            public static IEaseCalculator In => new EaseInBack();
            public static IEaseCalculator Out => new EaseOutBack();
            public static IEaseCalculator InOut => new EaseInOutBack();
        }

        /// <summary>
        /// 弹性缓动
        /// </summary>
        public static class Elastic
        {
            public static IEaseCalculator In => new EaseInElastic();
            public static IEaseCalculator Out => new EaseOutElastic();
            public static IEaseCalculator InOut => new EaseInOutElastic();
        }

        /// <summary>
        /// 反弹缓动
        /// </summary>
        public static class Bounce
        {
            public static IEaseCalculator In => new EaseInBounce();
            public static IEaseCalculator Out => new EaseOutBounce();
            public static IEaseCalculator InOut => new EaseInOutBounce();
        }
    }

    public class EaseDefault : IEaseCalculator
    {
        public double Ease(double t) => t;
    }

    //-----------------------------
    // 正弦缓动
    //-----------------------------
    public class EaseInSine : IEaseCalculator
    {
        public double Ease(double t) => 1 - Math.Cos(t * Math.PI / 2);
    }

    public class EaseOutSine : IEaseCalculator
    {
        public double Ease(double t) => Math.Sin(t * Math.PI / 2);
    }

    public class EaseInOutSine : IEaseCalculator
    {
        public double Ease(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;
    }

    //-----------------------------
    // 二次方缓动
    //-----------------------------
    public class EaseInQuad : IEaseCalculator
    {
        public double Ease(double t) => t * t;
    }

    public class EaseOutQuad : IEaseCalculator
    {
        public double Ease(double t) => 1 - (1 - t) * (1 - t);
    }

    public class EaseInOutQuad : IEaseCalculator
    {
        public double Ease(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    }

    //-----------------------------
    // 三次方缓动
    //-----------------------------
    public class EaseInCubic : IEaseCalculator
    {
        public double Ease(double t) => t * t * t;
    }

    public class EaseOutCubic : IEaseCalculator
    {
        public double Ease(double t) => 1 - Math.Pow(1 - t, 3);
    }

    public class EaseInOutCubic : IEaseCalculator
    {
        public double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    //-----------------------------
    // 四次方缓动
    //-----------------------------
    public class EaseInQuart : IEaseCalculator
    {
        public double Ease(double t) => t * t * t * t;
    }

    public class EaseOutQuart : IEaseCalculator
    {
        public double Ease(double t) => 1 - Math.Pow(1 - t, 4);
    }

    public class EaseInOutQuart : IEaseCalculator
    {
        public double Ease(double t) => t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;
    }

    //-----------------------------
    // 五次方缓动
    //-----------------------------
    public class EaseInQuint : IEaseCalculator
    {
        public double Ease(double t) => t * t * t * t * t;
    }

    public class EaseOutQuint : IEaseCalculator
    {
        public double Ease(double t) => 1 - Math.Pow(1 - t, 5);
    }

    public class EaseInOutQuint : IEaseCalculator
    {
        public double Ease(double t) => t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;
    }

    //-----------------------------
    // 指数缓动
    //-----------------------------
    public class EaseInExpo : IEaseCalculator
    {
        public double Ease(double t) => t == 0 ? 0 : Math.Pow(2, 10 * t - 10);
    }

    public class EaseOutExpo : IEaseCalculator
    {
        public double Ease(double t) => t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
    }

    public class EaseInOutExpo : IEaseCalculator
    {
        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;
    }

    //-----------------------------
    // 圆形缓动
    //-----------------------------
    public class EaseInCirc : IEaseCalculator
    {
        public double Ease(double t) => 1 - Math.Sqrt(1 - Math.Pow(t, 2));
    }

    public class EaseOutCirc : IEaseCalculator
    {
        public double Ease(double t) => Math.Sqrt(1 - Math.Pow(t - 1, 2));
    }

    public class EaseInOutCirc : IEaseCalculator
    {
        public double Ease(double t) => t < 0.5 ? (1 - Math.Sqrt(1 - Math.Pow(2 * t, 2))) / 2 : (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;
    }

    //-----------------------------
    // 回弹缓动
    //-----------------------------
    public class EaseInBack : IEaseCalculator
    {
        private const double c1 = 1.70158;
        private const double c3 = c1 + 1;

        public double Ease(double t) => c3 * t * t * t - c1 * t * t;
    }

    public class EaseOutBack : IEaseCalculator
    {
        private const double c1 = 1.70158;
        private const double c3 = c1 + 1;

        public double Ease(double t) => 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    public class EaseInOutBack : IEaseCalculator
    {
        private const double c1 = 1.70158;
        private const double c2 = c1 * 1.525;

        public double Ease(double t) => t < 0.5 ? (Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2 : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
    }

    //-----------------------------
    // 弹性缓动
    //-----------------------------
    public class EaseInElastic : IEaseCalculator
    {
        private const double c4 = 2 * Math.PI / 3;

        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4);
    }

    public class EaseOutElastic : IEaseCalculator
    {
        private const double c4 = 2 * Math.PI / 3;

        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    }

    public class EaseInOutElastic : IEaseCalculator
    {
        private const double c5 = 2 * Math.PI / 4.5;

        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 : (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 + 1;
    }

    //-----------------------------
    // 反弹缓动
    //-----------------------------
    public class EaseInBounce : IEaseCalculator
    {
        public double Ease(double t) => 1 - Eases.Bounce.Out.Ease(1 - t);
    }

    public class EaseOutBounce : IEaseCalculator
    {
        public double Ease(double t)
        {
            const double n1 = 7.5625;
            const double d1 = 2.75;

            if (t < 1 / d1) return n1 * t * t;
            if (t < 2 / d1) return n1 * (t -= 1.5 / d1) * t + 0.75;
            if (t < 2.5 / d1) return n1 * (t -= 2.25 / d1) * t + 0.9375;
            return n1 * (t -= 2.625 / d1) * t + 0.984375;
        }
    }

    public class EaseInOutBounce : IEaseCalculator
    {
        public double Ease(double t) => t < 0.5 ? (1 - Eases.Bounce.Out.Ease(1 - 2 * t)) / 2 : (1 + Eases.Bounce.Out.Ease(2 * t - 1)) / 2;
    }
}
