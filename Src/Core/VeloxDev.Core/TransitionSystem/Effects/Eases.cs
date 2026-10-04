namespace VeloxDev.TransitionSystem
{
    /// <summary>The built-in easing curves, grouped by family. Each member returns a fresh <see cref="IEaseCalculator"/>.</summary>
    public static class Eases
    {
        /// <summary>The identity curve: progress equals time.</summary>
        public static IEaseCalculator Default => new EaseDefault();

        /// <summary>Sine-curve easing.</summary>
        public static class Sine
        {
            /// <summary>The ease-in sine curve.</summary>
            public static IEaseCalculator In => new EaseInSine();
            /// <summary>The ease-out sine curve.</summary>
            public static IEaseCalculator Out => new EaseOutSine();
            /// <summary>The ease-in-out sine curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutSine();
        }

        /// <summary>Quadratic easing.</summary>
        public static class Quad
        {
            /// <summary>The ease-in quadratic curve.</summary>
            public static IEaseCalculator In => new EaseInQuad();
            /// <summary>The ease-out quadratic curve.</summary>
            public static IEaseCalculator Out => new EaseOutQuad();
            /// <summary>The ease-in-out quadratic curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutQuad();
        }

        /// <summary>Cubic easing.</summary>
        public static class Cubic
        {
            /// <summary>The ease-in cubic curve.</summary>
            public static IEaseCalculator In => new EaseInCubic();
            /// <summary>The ease-out cubic curve.</summary>
            public static IEaseCalculator Out => new EaseOutCubic();
            /// <summary>The ease-in-out cubic curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutCubic();
        }

        /// <summary>Quartic easing.</summary>
        public static class Quart
        {
            /// <summary>The ease-in quartic curve.</summary>
            public static IEaseCalculator In => new EaseInQuart();
            /// <summary>The ease-out quartic curve.</summary>
            public static IEaseCalculator Out => new EaseOutQuart();
            /// <summary>The ease-in-out quartic curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutQuart();
        }

        /// <summary>Quintic easing.</summary>
        public static class Quint
        {
            /// <summary>The ease-in quintic curve.</summary>
            public static IEaseCalculator In => new EaseInQuint();
            /// <summary>The ease-out quintic curve.</summary>
            public static IEaseCalculator Out => new EaseOutQuint();
            /// <summary>The ease-in-out quintic curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutQuint();
        }

        /// <summary>Exponential easing.</summary>
        public static class Expo
        {
            /// <summary>The ease-in exponential curve.</summary>
            public static IEaseCalculator In => new EaseInExpo();
            /// <summary>The ease-out exponential curve.</summary>
            public static IEaseCalculator Out => new EaseOutExpo();
            /// <summary>The ease-in-out exponential curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutExpo();
        }

        /// <summary>Circular easing.</summary>
        public static class Circ
        {
            /// <summary>The ease-in circular curve.</summary>
            public static IEaseCalculator In => new EaseInCirc();
            /// <summary>The ease-out circular curve.</summary>
            public static IEaseCalculator Out => new EaseOutCirc();
            /// <summary>The ease-in-out circular curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutCirc();
        }

        /// <summary>Easing that overshoots the target and settles back.</summary>
        public static class Back
        {
            /// <summary>The ease-in back curve.</summary>
            public static IEaseCalculator In => new EaseInBack();
            /// <summary>The ease-out back curve.</summary>
            public static IEaseCalculator Out => new EaseOutBack();
            /// <summary>The ease-in-out back curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutBack();
        }

        /// <summary>Spring-like easing that oscillates around the target.</summary>
        public static class Elastic
        {
            /// <summary>The ease-in elastic curve.</summary>
            public static IEaseCalculator In => new EaseInElastic();
            /// <summary>The ease-out elastic curve.</summary>
            public static IEaseCalculator Out => new EaseOutElastic();
            /// <summary>The ease-in-out elastic curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutElastic();
        }

        /// <summary>Easing that bounces as it settles.</summary>
        public static class Bounce
        {
            /// <summary>The ease-in bounce curve.</summary>
            public static IEaseCalculator In => new EaseInBounce();
            /// <summary>The ease-out bounce curve.</summary>
            public static IEaseCalculator Out => new EaseOutBounce();
            /// <summary>The ease-in-out bounce curve.</summary>
            public static IEaseCalculator InOut => new EaseInOutBounce();
        }
    }

    /// <summary>Progress equals time.</summary>
    public class EaseDefault : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t;
    }

    /// <summary>Eases in on a sine curve.</summary>
    public class EaseInSine : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => 1 - Math.Cos(t * Math.PI / 2);
    }
    /// <summary>Eases out on a sine curve.</summary>
    public class EaseOutSine : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => Math.Sin(t * Math.PI / 2);
    }
    /// <summary>Eases in and out on a sine curve.</summary>
    public class EaseInOutSine : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;
    }

    /// <summary>Eases in on a quadratic curve.</summary>
    public class EaseInQuad : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t * t;
    }
    /// <summary>Eases out on a quadratic curve.</summary>
    public class EaseOutQuad : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => 1 - (1 - t) * (1 - t);
    }
    /// <summary>Eases in and out on a quadratic curve.</summary>
    public class EaseInOutQuad : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    }

    /// <summary>Eases in on a cubic curve.</summary>
    public class EaseInCubic : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t * t * t;
    }
    /// <summary>Eases out on a cubic curve.</summary>
    public class EaseOutCubic : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => 1 - Math.Pow(1 - t, 3);
    }
    /// <summary>Eases in and out on a cubic curve.</summary>
    public class EaseInOutCubic : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>Eases in on a quartic curve.</summary>
    public class EaseInQuart : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t * t * t * t;
    }
    /// <summary>Eases out on a quartic curve.</summary>
    public class EaseOutQuart : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => 1 - Math.Pow(1 - t, 4);
    }
    /// <summary>Eases in and out on a quartic curve.</summary>
    public class EaseInOutQuart : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;
    }

    /// <summary>Eases in on a quintic curve.</summary>
    public class EaseInQuint : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t * t * t * t * t;
    }
    /// <summary>Eases out on a quintic curve.</summary>
    public class EaseOutQuint : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => 1 - Math.Pow(1 - t, 5);
    }
    /// <summary>Eases in and out on a quintic curve.</summary>
    public class EaseInOutQuint : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;
    }

    /// <summary>Eases in on an exponential curve.</summary>
    public class EaseInExpo : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t == 0 ? 0 : Math.Pow(2, 10 * t - 10);
    }
    /// <summary>Eases out on an exponential curve.</summary>
    public class EaseOutExpo : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
    }
    /// <summary>Eases in and out on an exponential curve.</summary>
    public class EaseInOutExpo : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;
    }

    /// <summary>Eases in on a circular curve.</summary>
    public class EaseInCirc : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => 1 - Math.Sqrt(1 - Math.Pow(t, 2));
    }
    /// <summary>Eases out on a circular curve.</summary>
    public class EaseOutCirc : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => Math.Sqrt(1 - Math.Pow(t - 1, 2));
    }
    /// <summary>Eases in and out on a circular curve.</summary>
    public class EaseInOutCirc : IEaseCalculator
    {
        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? (1 - Math.Sqrt(1 - Math.Pow(2 * t, 2))) / 2 : (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;
    }

    /// <summary>Eases in with a small overshoot.</summary>
    public class EaseInBack : IEaseCalculator
    {
        private const double c1 = 1.70158;
        private const double c3 = c1 + 1;

        /// <inheritdoc />
        public double Ease(double t) => c3 * t * t * t - c1 * t * t;
    }
    /// <summary>Eases out with a small overshoot.</summary>
    public class EaseOutBack : IEaseCalculator
    {
        private const double c1 = 1.70158;
        private const double c3 = c1 + 1;

        /// <inheritdoc />
        public double Ease(double t) => 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
    /// <summary>Eases in and out with a small overshoot.</summary>
    public class EaseInOutBack : IEaseCalculator
    {
        private const double c1 = 1.70158;
        private const double c2 = c1 * 1.525;

        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? (Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2 : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
    }

    /// <summary>Eases in with a spring-like oscillation.</summary>
    public class EaseInElastic : IEaseCalculator
    {
        private const double c4 = 2 * Math.PI / 3;

        /// <inheritdoc />
        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4);
    }
    /// <summary>Eases out with a spring-like oscillation.</summary>
    public class EaseOutElastic : IEaseCalculator
    {
        private const double c4 = 2 * Math.PI / 3;

        /// <inheritdoc />
        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    }
    /// <summary>Eases in and out with a spring-like oscillation.</summary>
    public class EaseInOutElastic : IEaseCalculator
    {
        private const double c5 = 2 * Math.PI / 4.5;

        /// <inheritdoc />
        public double Ease(double t) => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 : (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 + 1;
    }

    /// <summary>Eases in with a bounce.</summary>
    public class EaseInBounce : IEaseCalculator
    {
        // 缓存而非走 Eases.Bounce.Out：那个属性会新建一个计算器，每帧走它会在采样热路径上分配对象。
        private static readonly EaseOutBounce Out = new();

        /// <inheritdoc />
        public double Ease(double t) => 1 - Out.Ease(1 - t);
    }
    /// <summary>Eases out with a bounce.</summary>
    public class EaseOutBounce : IEaseCalculator
    {
        /// <inheritdoc />
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
    /// <summary>Eases in and out with a bounce.</summary>
    public class EaseInOutBounce : IEaseCalculator
    {
        // 同 EaseInBounce：属性会分配，而这每帧都跑。
        private static readonly EaseOutBounce Out = new();

        /// <inheritdoc />
        public double Ease(double t) => t < 0.5 ? (1 - Out.Ease(1 - 2 * t)) / 2 : (1 + Out.Ease(2 * t - 1)) / 2;
    }
}
