using System.Reflection;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.DynamicTheme;

/// <summary>
/// What a theme switch gains by running on the transition system instead of its own timing loop: the timeline the
/// switch is anchored to is the one the transition system exposes, so control reaches it through
/// <see cref="TransitionCore"/> unchanged, and the effect's own flags are honoured rather than ignored.
/// </summary>
/// <remarks>
/// Not parallelized, and every case restores the global state it touches. <see cref="ThemeManager"/> is entirely
/// static — the current theme, the platform interpolator and the switch in flight are process-wide — so running
/// these alongside anything else that switches themes would make both sides' assertions meaningless.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ThemeTransitionTests
{
    private const double StartValue = 0d;
    private const double EndValue = 100d;

    private readonly List<IThemeObject> _registered = [];

    [TestInitialize]
    public void ResetGlobalState()
    {
        ThemeManager.SetCurrent<Dark>();
        ThemeManager.StartModel = StartModel.Cache;
    }

    [TestCleanup]
    public void RestoreGlobals()
    {
        foreach (var target in _registered)
        {
            ThemeManager.Unregister(target);
            TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }
        _registered.Clear();

        ThemeManager.SetCurrent<Dark>();
        ThemeManager.StartModel = StartModel.Cache;
    }

    private sealed class Subject : IThemeObject
    {
        private readonly Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> _static = [];
        private readonly Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> _active = [];

        public double Value { get; set; }

        /// <summary>No sampler is registered for <see cref="string"/> in Core, so this is the held-then-jumped case.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Set every time the manager announces a change, so a cancelled switch can be told from a landed one.</summary>
        public string? ChangedTo { get; private set; }

        public Subject()
        {
            Declare(nameof(Value), StartValue, EndValue);
            Declare(nameof(Label), "dark", "light");
            Value = StartValue;
            Label = "dark";
        }

        private void Declare(string name, object? dark, object? light)
        {
            var property = typeof(Subject).GetProperty(name)!;
            _static[name] = new Dictionary<PropertyInfo, Dictionary<Type, object?>>
            {
                [property] = new Dictionary<Type, object?>
                {
                    [typeof(Dark)] = dark,
                    [typeof(Light)] = light,
                }
            };
        }

        public void InitializeTheme() { }

        public void ExecuteThemeChanging(Type? oldValue, Type? newValue) { }
        public void ExecuteThemeChanged(Type? oldValue, Type? newValue) => ChangedTo = newValue?.Name;

        public void SetThemeValue<T>(string propertyName, object? newValue) where T : ITheme { }
        public void RestoreThemeValue<T>(string propertyName) where T : ITheme { }

        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticThemeCache() => _static;
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetActiveThemeCache() => _active;
    }

    /// <summary>Runs frames inline, so a sampling loop needs no dispatcher.</summary>
    private sealed class ImmediateInspector : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => true;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override bool ProtectedInvoke(object target, Action action) { action(); return true; }
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    /// <summary>The platform seam, answered the way an adapter answers it — minus the platform.</summary>
    private sealed class TestInterpolator : InterpolatorCore
    {
        /// <summary>False stands in for a platform that never opted in, or cannot carry the effect it was handed.</summary>
        public bool HasSeam { get; set; } = true;

        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => HasSeam && effect is ITransitionEffect<NonPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<ImmediateInspector, TestInterpreter, NonPriority>.FindOrCreate(target)
                : null;
    }

    private Subject RegisterSubject()
    {
        var subject = new Subject();
        ThemeManager.Register(subject);
        _registered.Add(subject);
        return subject;
    }

    private static TransitionEffectCore Effect(TimeSpan duration) => new()
    {
        Duration = duration,
        FPS = 60,
    };

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [TestMethod]
    public async Task Switch_LandsExactlyOnTheTargetValue()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var subject = RegisterSubject();

        ThemeManager.Transition<Light>(Effect(TimeSpan.FromMilliseconds(300)));

        Assert.IsTrue(
            await WaitUntilAsync(() => ThemeManager.Current == typeof(Light)),
            "the switch never completed");

        // 末帧是被钉在 end 上的，不依赖 Ease(1) 是否精确为 1。
        Assert.AreEqual(EndValue, subject.Value, 1e-9, "the last frame must land on the declared value, not near it");
        Assert.AreEqual(nameof(Light), subject.ChangedTo, "a landed switch announces the change");
    }

    [TestMethod]
    public async Task Switch_EveryTargetIsAnchoredToTheSameTimeline()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var first = RegisterSubject();
        var second = RegisterSubject();

        ThemeManager.Transition<Light>(Effect(TimeSpan.FromSeconds(5)));

        Assert.IsTrue(
            await WaitUntilAsync(() => TransitionCore.Position(first) > TimeSpan.Zero),
            "the switch never started on the first target");

        // 这是共享 transport 的全部意义：一场切换只有一个时间轴，所以控制任一个目标就是控制整场。
        TransitionCore.Pause(first);
        Assert.IsTrue(
            TransitionCore.IsPaused(second),
            "pausing one target must pause the whole switch — that is what lets the existing Transition.* surface control it");

        TransitionCore.Resume(first);
        TransitionCore.Exit(first, IncludeMutual: true, IncludeNoMutual: true);
        TransitionCore.Exit(second, IncludeMutual: true, IncludeNoMutual: true);
    }

    [TestMethod]
    public async Task Switch_SeekIsReachableAndFinishesThePass()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var subject = RegisterSubject();

        ThemeManager.Transition<Light>(Effect(TimeSpan.FromSeconds(30)));

        Assert.IsTrue(
            await WaitUntilAsync(() => TransitionCore.Position(subject) > TimeSpan.Zero),
            "the switch never started");

        // 拖到超过时长的位置：一程的终点只有一个，所以这必须把这一程跑到头并精确落在终点。
        TransitionCore.Seek(subject, TimeSpan.FromSeconds(31));

        Assert.IsTrue(
            await WaitUntilAsync(() => ThemeManager.Current == typeof(Light)),
            "seeking past the end must finish the switch");
        Assert.AreEqual(EndValue, subject.Value, 1e-9);
    }

    [TestMethod]
    public async Task Switch_HonoursAutoReverseAndLoopTime()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var subject = RegisterSubject();

        var effect = Effect(TimeSpan.FromMilliseconds(150));
        effect.IsAutoReverse = true;
        effect.LoopTime = 1;

        ThemeManager.Transition<Light>(effect);
        Assert.IsTrue(
            await WaitUntilAsync(() => ThemeManager.Current == typeof(Light)),
            "the switch never completed");

        // 往返两程 × (LoopTime + 1) 趟，收在反向程的末端 —— 也就是起点。忽略这两个标志的旧循环会停在终点，
        // 所以这个断言本身就是「标志生效了」的证据。
        Assert.AreEqual(StartValue, subject.Value, 1e-9, "the last pass is a reverse one, so the switch settles on the start value");
    }

    [TestMethod]
    public async Task Switch_HoldsAnUnsampledPropertyUntilTheEnd()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var subject = RegisterSubject();

        ThemeManager.Transition<Light>(Effect(TimeSpan.FromMilliseconds(400)));

        Assert.IsTrue(
            await WaitUntilAsync(() => TransitionCore.Position(subject) > TimeSpan.FromMilliseconds(120)),
            "the switch never got far enough in");

        // 没有采样器的属性不在采样集合里，全程原地不动 —— 这是 SKILL.md 记载的降级行为，不是「忘了写」。
        Assert.AreEqual("dark", subject.Label, "a property with no sampler must hold its value for the whole switch");
        Assert.AreNotEqual(EndValue, subject.Value, "the sampled property must still be animating at this point");

        Assert.IsTrue(
            await WaitUntilAsync(() => ThemeManager.Current == typeof(Light)),
            "the switch never completed");
        Assert.AreEqual("light", subject.Label, "and it jumps to its target when the switch ends");
    }

    [TestMethod]
    public async Task Switch_WithNoPlatformSeam_AppliesImmediately()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator { HasSeam = false });
        var subject = RegisterSubject();

        ThemeManager.Transition<Light>(Effect(TimeSpan.FromSeconds(30)));

        Assert.AreEqual(typeof(Light), ThemeManager.Current, "with no seam the switch is not animated at all");
        Assert.AreEqual(EndValue, subject.Value, 1e-9);
        Assert.AreEqual(nameof(Light), subject.ChangedTo);

        await Task.CompletedTask;
    }

    [TestMethod]
    public void Jump_WritesTheTargetValue()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var subject = RegisterSubject();

        ThemeManager.Jump<Light>();

        Assert.AreEqual(typeof(Light), ThemeManager.Current);
        Assert.AreEqual(EndValue, subject.Value, 1e-9);
    }

    [TestMethod]
    public void Jump_SkipsAnUnregisteredTarget()
    {
        ThemeManager.SetPlatformInterpolator(new TestInterpolator());
        var subject = new Subject();

        ThemeManager.Jump<Light>();

        Assert.AreEqual(StartValue, subject.Value, "a target that was never registered must not be written");
    }
}
