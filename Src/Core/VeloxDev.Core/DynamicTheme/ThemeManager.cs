using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using VeloxDev.Timing;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.DynamicTheme
{
    /// <summary>
    /// Select how to get the initial value when the theme animation starts
    /// <para><see cref="Reflect"/> -> current value of the object</para>
    /// <para><see cref="Cache"/> -> value in cache</para>
    /// </summary>
    [Flags]
    public enum StartModel : int
    {
        Reflect = 1,
        Cache = 2,
    }

    public class ThemeManager
    {
        private static InterpolatorCore? _interpolator;

        private static readonly Dictionary<Type, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>> _def_cache = [];
        private static readonly ConditionalWeakTable<IThemeObject, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>> _act_cache = new();
        private static readonly List<WeakReference<IThemeObject>> activeThemes = [];

        /// <summary>
        /// The runs of the switch in flight, one per target, all anchored to the same timeline.
        /// </summary>
        /// <remarks>
        /// Held so a switch that supersedes this one can cancel exactly what it started, and so nothing has to
        /// remember which targets the previous switch covered — the registered target set can change between two
        /// switches, while these runs cannot.
        /// </remarks>
        private static List<SwitchTarget>? _activeSwitch;

        /// <summary>
        /// The current theme in use and Default is <see cref="Dark"/>
        /// </summary>
        public static Type Current { get; internal set; } = typeof(Dark);

        /// <summary>
        /// Select how to get the initial value when the theme animation starts
        /// </summary>
        public static StartModel StartModel { get; set; } = StartModel.Cache;

        /// <summary>
        /// Sets the platform-specific interpolator to be used by the system
        /// <para>This method only needs to be called once</para>
        /// </summary>
        /// <param name="interpolator">It is usually the Interpolator provided by the adaptation layer of each platform</param>
        /// <remarks>
        /// Beyond forcing the adapter's sampler registrations to run, this is what makes an animated switch possible
        /// at all: the adapter answers <see cref="InterpolatorCore.CreateScheduler"/> from here, since which
        /// inspector, interpreter and dispatcher priority to animate with is the one thing Core cannot name.
        /// Without it a switch still happens — immediately, with no animation.
        /// </remarks>
        public static void SetPlatformInterpolator<T>(T interpolator) where T : InterpolatorCore
        {
            _interpolator = interpolator;
        }

        /// <summary>
        /// Set the current theme without transition effectk
        /// </summary>
        public static void SetCurrent<T>() where T : ITheme => Current = typeof(T);

        /// <summary>
        /// Declaration elements can use the theme system
        /// </summary>
        /// <param name="target">target element</param>
        public static void Register(IThemeObject target)
        {
            if (!_act_cache.TryGetValue(target, out _))
            {
                Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>? cache = [];
                _act_cache.Add(target, cache);
                activeThemes.Add(new WeakReference<IThemeObject>(target));
            }
        }

        /// <summary>
        /// Cancel the registration of elements for the theme system
        /// </summary>
        /// <param name="target">target element</param>
        public static void Unregister(IThemeObject target)
        {
            _act_cache.Remove(target);
            activeThemes.RemoveAll(x => x.TryGetTarget(out var obj) && obj == target);
        }

        /// <summary>
        /// Change theme with transition effect
        /// </summary>
        /// <param name="themeType">target theme</param>
        /// <param name="effect">transition effect</param>
        /// <remarks>
        /// Every target of one switch is anchored to a single shared <see cref="ITimeSourceControl"/>, so the
        /// timeline control the transition system already exposes works on it unchanged: pausing, seeking or
        /// re-rating any one target moves the whole switch, because there is only one transport to move.
        /// <para>
        /// The effect is used as given and is read per frame by every target, so it must be the platform's own
        /// effect type and must not be mutated while a switch that uses it is running. The shared
        /// <c>TransitionEffects</c> instances are the intended argument.
        /// </para>
        /// </remarks>
        public static async void Transition(Type themeType, ITransitionEffectCore effect)
        {
            var current = Current;
            if (themeType == current || !typeof(ITheme).IsAssignableFrom(themeType))
            {
                Debug.WriteLine("[ThemeManager] Invalid theme type, jumping to current theme.");
                return;
            }
            CancelActiveSwitch();
            activeThemes.RemoveAll(x => !x.TryGetTarget(out _));
            var actives = activeThemes.Select(x => x.TryGetTarget(out var obj) ? obj : null).Where(x => x != null).ToArray();
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(current, themeType);
            }

            bool landed;
            try
            {
                landed = await RunSwitch(actives, themeType, effect);
            }
            catch (Exception ex)
            {
                // async void 的调用方接不住异常，而 RunSwitch 里跑的是适配器的 scheduler —— 不能让它把进程带走。
                Debug.WriteLine($"[ThemeManager] Error during theme transition: {ex.Message}");
                return;
            }

            if (!landed)
            {
                return;
            }

            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanged(current, themeType);
            }
        }
        /// <summary>
        /// Change theme with transition effect
        /// </summary>
        /// <typeparam name="T">target theme</typeparam>
        /// <param name="effect">transition effect</param>
        public static void Transition<T>(ITransitionEffectCore effect) where T : ITheme
        {
            Transition(typeof(T), effect);
        }

        /// <summary>
        /// Change theme without transition effect
        /// </summary>
        /// <param name="themeType">target theme</param>
        public static void Jump(Type themeType)
        {
            var current = Current;
            if (themeType == current || !typeof(ITheme).IsAssignableFrom(themeType))
            {
                Debug.WriteLine("[ThemeManager] Invalid theme type, jumping to current theme.");
                return;
            }
            CancelActiveSwitch();
            activeThemes.RemoveAll(x => !x.TryGetTarget(out _));
            var actives = activeThemes.Select(x => x.TryGetTarget(out var obj) ? obj : null).Where(x => x != null).ToArray();
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(current, themeType);
            }

            // 不带动画的切换就是「写终值」本身：不需要时间轴，也不需要平台的 effect，因此 Jump 既不依赖
            // SetPlatformInterpolator，也不受平台那个 ITransitionEffect<TPriority> 类型约束。
            ApplyImmediately(PrepareSamplers(actives, themeType), themeType);

            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanged(current, themeType);
            }
        }
        /// <summary>
        /// Change theme without transition effect
        /// </summary>
        /// <typeparam name="T">target theme</typeparam>
        public static void Jump<T>() where T : ITheme
        {
            Jump(typeof(T));
        }

        /// <summary>
        /// Runs one switch. Returns true when it reached its far end, false when it was cancelled or superseded —
        /// in which case the caller must not advance <see cref="Current"/> nor announce a change that did not land.
        /// </summary>
        private static async Task<bool> RunSwitch(IThemeObject?[] actives, Type themeType, ITransitionEffectCore effect)
        {
            var groups = PrepareSamplers(actives, themeType);
            var interpolator = _interpolator;

            // 没有平台接缝，或没有任何属性可动 —— 退化为瞬时切换，而不是启动一场画不出东西的动画。
            if (interpolator is null || groups.Count == 0)
            {
                return ApplyImmediately(groups, themeType);
            }

            // 先把每个目标的 scheduler 都解析出来再启动。只让一部分目标动起来、另一部分瞬切，比整场都不动更糟；
            // 而且这个判断只取决于平台与 effect，与目标无关，所以答案对整场是一致的。
            var planned = new List<(TransitionSchedulerCore Scheduler, TargetEntries Group)>(groups.Count);
            foreach (var group in groups)
            {
                TransitionSchedulerCore? scheduler;
                try
                {
                    scheduler = interpolator.CreateScheduler(group.Target, effect);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThemeManager] Error resolving the platform scheduler: {ex.Message}");
                    scheduler = null;
                }

                if (scheduler is null)
                {
                    return ApplyImmediately(groups, themeType);
                }
                planned.Add((scheduler, group));
            }

            // 整场共用一条时间轴：所有目标锚在同一个 transport 上，于是对任一目标 Pause / Seek / SetRate 都会同时
            // 移动全部目标。这就是「不新增控制面、直接用既有 Transition.*」能够成立的全部原因。
            var timeline = TimerCore.CreateTimeSource<ITimeSourceControl>();
            var runs = new List<SwitchTarget>(planned.Count);
            var faulted = false;

            try
            {
                foreach (var (scheduler, group) in planned)
                {
                    // Prepare 只能从目标上读起点，也就是只等价于 StartModel.Reflect。先按配置的模式把起点写回目标，
                    // 才能让默认的 Cache 档保持「从当前主题的值出发」，而不是「从目标上碰巧是什么出发」。
                    WriteStartValues(group);

                    var run = new TransitionRun(timeline);
                    // 必须在 Execute 之前 Track：Execute 正是靠 run.Cts 在 scheduler 的活跃表里找回这个 run，
                    // 找回失败的话采样集合会拿一条没人控制得住的私有时间轴。
                    scheduler.Track(run);
                    runs.Add(new SwitchTarget(scheduler, run, BuildState(group)));
                }

                Interlocked.Exchange(ref _activeSwitch, runs);

                var tasks = new List<Task>(runs.Count);
                foreach (var item in runs)
                {
                    tasks.Add(item.Scheduler.Execute(interpolator, item.State, effect, item.Run.Cts));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    // async void 的调用方接不住异常：这里吞掉，与旧循环的 catch 保持同一姿态。
                    faulted = true;
                    Debug.WriteLine($"[ThemeManager] Error during transition execution: {ex.Message}");
                }
            }
            finally
            {
                foreach (var item in runs)
                {
                    item.Scheduler.Untrack(item.Run);
                }
                Interlocked.CompareExchange(ref _activeSwitch, null, runs);
            }

            // 先问再释放：这一场是这些 run 的唯一主人，而释放之后令牌源除了 IsCancellationRequested 之外
            // 什么都不再答。
            var stopped = faulted || runs.Any(static item => WasCancelled(item.Run));
            foreach (var item in runs)
            {
                item.Run.Dispose();
            }

            if (stopped)
            {
                return false;
            }

            // 没有采样器的属性被 Prepare 静默跳过，全程保持旧值，到这里才跳变 —— 这是 SKILL.md 已记载的降级行为。
            ApplyHeldValues(groups);
            Current = themeType;
            return true;
        }

        /// <summary>
        /// True when this run was stopped. A disposed token source counts as stopped: it means the animation was
        /// torn down, and asking a disposed source anything at all throws.
        /// </summary>
        private static bool WasCancelled(TransitionRun run)
        {
            try
            {
                return run.Cts.IsCancellationRequested;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        /// <summary>
        /// Cancels the switch in flight, if any, and forgets it.
        /// </summary>
        private static void CancelActiveSwitch()
        {
            var runs = Interlocked.Exchange(ref _activeSwitch, null);
            if (runs is null) return;

            foreach (var item in runs)
            {
                try
                {
                    item.Run.Cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 适配器的解释器已经释放了 token source —— 没有东西可取消了
                }

                // 暂停中的循环停在时间轴的 gate 上，而 gate 不知道 token 这回事。唤醒它，循环才能看见取消并退出；
                // 少了这一步，被取消的切换会一直停在原地，直到有别的东西把它 resume 起来。
                item.Run.Timeline.Wake();
            }
        }

        private static void WriteStartValues(TargetEntries group)
        {
            foreach (var entry in group.Entries)
            {
                try
                {
                    entry.TransitionProperty.SetValue(entry.Target, entry.StartValue);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThemeManager] Error applying the start value: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Builds the frame state for one target. Only the end values go in: the start is read back off the target by
        /// <c>InterpolatorCore.Prepare</c>, and normalizing an endpoint here would normalize it twice.
        /// </summary>
        private static StateCore BuildState(TargetEntries group)
        {
            var state = new StateCore();

            foreach (var entry in group.Entries)
            {
                // A null end value declares "this theme leaves the property alone" — there is nothing to sample
                // towards, and handing it to a sampler is how a whole switch gets dropped on the floor.
                if (entry.EndValue is null) continue;

                try
                {
                    // 复用 entry 自己那条路径，而不是让 StateCore 再从 PropertyInfo 造一条：两者等值、同一个实例
                    // 还更好（同一个键对象），却省掉一次路径构造。
                    state.SetValue(entry.TransitionProperty, entry.EndValue);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThemeManager] Error declaring the target value: {ex.Message}");
                }
            }

            return state;
        }

        /// <summary>
        /// Writes the end value of every property no sampler claimed.
        /// </summary>
        private static void ApplyHeldValues(List<TargetEntries> groups)
        {
            foreach (var group in groups)
            {
                foreach (var entry in group.Entries)
                {
                    if (entry.HasSampler || entry.EndValue is null) continue;

                    try
                    {
                        entry.TransitionProperty.SetValue(entry.Target, entry.EndValue);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ThemeManager] Error applying the target value: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Writes every end value at once and advances <see cref="Current"/> — the whole of a switch with no animation.
        /// </summary>
        private static bool ApplyImmediately(List<TargetEntries> groups, Type themeType)
        {
            foreach (var group in groups)
            {
                foreach (var entry in group.Entries)
                {
                    if (entry.EndValue is null) continue;

                    try
                    {
                        entry.TransitionProperty.SetValue(entry.Target, entry.EndValue);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ThemeManager] Error applying the target value: {ex.Message}");
                    }
                }
            }

            Current = themeType;
            return true;
        }

        private static List<TargetEntries> PrepareSamplers(IThemeObject?[] targets, Type targetThemeType)
        {
            var groups = new List<TargetEntries>();

            foreach (var target in targets)
            {
                try
                {
                    if (target == null)
                    {
                        Debug.WriteLine("[ThemeManager] Encountered null target, skipping");
                        continue;
                    }

                    Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> staticCache;
                    Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> activeCache;

                    try
                    {
                        staticCache = target.GetStaticThemeCache();
                        activeCache = target.GetActiveThemeCache();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ThemeManager] Error getting cache for target: {ex.Message}");
                        continue;
                    }

                    // 目标可能一个可用属性都没有。那样就不给它建组：一场空动画和一个空采样集合没有任何意义。
                    TargetEntries? group = null;

                    foreach (var propEntry in staticCache)
                    {
                        PropertyInfo? propertyInfo = null;
                        Dictionary<Type, object?>? typeValues = null;

                        try
                        {
                            propertyInfo = propEntry.Value.Keys.First();
                            typeValues = propEntry.Value[propertyInfo];
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ThemeManager] Error getting property info: {ex.Message}");
                            continue;
                        }

                        // Get the current value (dynamic cache first)
                        object? currentValue = null;
                        bool hasCurrentValue = false;

                        try
                        {
                            // Choose the retrieval method based on StartModel
                            switch (StartModel)
                            {
                                case StartModel.Reflect:
                                    // Reflect mode
                                    try
                                    {
                                        currentValue = propertyInfo.GetValue(target);
                                        hasCurrentValue = true;
                                        Debug.WriteLine($"[Reflect] Got value for {propEntry.Key}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[Reflect] Error getting value for {propEntry.Key}: {ex.Message}");
                                    }
                                    break;

                                case StartModel.Cache:
                                    // Cache mode
                                    if (activeCache.TryGetValue(propEntry.Key, out var activePropCache) &&
                                        activePropCache.TryGetValue(propertyInfo, out var activeTypeCache) &&
                                        activeTypeCache.TryGetValue(Current, out currentValue))
                                    {
                                        hasCurrentValue = true;
                                    }
                                    else if (typeValues.TryGetValue(Current, out currentValue))
                                    {
                                        hasCurrentValue = true;
                                    }
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ThemeManager] Error getting current value for {propEntry.Key}: {ex.Message}");
                        }

                        if (!hasCurrentValue)
                        {
                            Debug.WriteLine($"[ThemeManager] No current value found for {propEntry.Key}, skipping");
                            continue;
                        }

                        // Get the target value (dynamic cache first, using the explicit targetThemeType)
                        object? targetValue = null;
                        bool hasTargetValue = false;

                        try
                        {
                            if (activeCache.TryGetValue(propEntry.Key, out var activePropCache) &&
                                activePropCache.TryGetValue(propertyInfo, out var activeTypeCache) &&
                                activeTypeCache.TryGetValue(targetThemeType, out targetValue))
                            {
                                hasTargetValue = true;
                            }
                            else if (typeValues.TryGetValue(targetThemeType, out targetValue))
                            {
                                hasTargetValue = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ThemeManager] Error getting target value for {propEntry.Key}: {ex.Message}");
                        }

                        if (!hasTargetValue)
                        {
                            Debug.WriteLine($"[ThemeManager] No target value found for {propEntry.Key}, skipping");
                            continue;
                        }

                        // 有没有采样器只在这里判一次，用途是决定收尾时要不要替 Prepare 补写终值 —— 采样器的解析
                        // 本身由 Prepare 在启动时重做，这里不预先归一化端点，否则就是归一化两次。
                        var hasSampler = InterpolatorCore.TryGetInterpolator(propertyInfo.PropertyType, out _);

                        group ??= new TargetEntries(target);
                        group.Entries.Add(new TransitionEntry(
                            target,
                            propertyInfo,
                            TransitionProperty.FromProperty(propertyInfo),
                            currentValue,
                            targetValue,
                            hasSampler));
                    }

                    if (group is not null)
                    {
                        groups.Add(group);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThemeManager] Unexpected error processing target: {ex.Message}");
                }
            }

            return groups;
        }

        /// <summary>
        /// The properties one target contributes to a switch. Grouped during preparation rather than by
        /// <c>GroupBy(Target)</c>, which would key on <see cref="object.Equals(object)"/> — a theme object is free to
        /// override it, and reference identity is what a target is here.
        /// </summary>
        private sealed class TargetEntries(object target)
        {
            public object Target { get; } = target;
            public List<TransitionEntry> Entries { get; } = [];
        }

        private sealed class TransitionEntry(
            object target,
            PropertyInfo propertyInfo,
            ITransitionProperty transitionProperty,
            object? startValue,
            object? endValue,
            bool hasSampler)
        {
            public object Target { get; } = target;
            public PropertyInfo PropertyInfo { get; } = propertyInfo;
            public ITransitionProperty TransitionProperty { get; } = transitionProperty;

            /// <summary>起点原值，按 <see cref="StartModel"/> 取；未经采样器归一化，因为它要写回真实对象。</summary>
            public object? StartValue { get; } = startValue;

            /// <summary>终点原值。归一化交给 Prepare，这里预先做会归一化两次。</summary>
            public object? EndValue { get; } = endValue;

            /// <summary>该属性有没有采样器。没有的话 Prepare 会静默跳过它，须由本类在收尾时补写终值。</summary>
            public bool HasSampler { get; } = hasSampler;
        }

        /// <summary>The run one target is animated by, and the frame state it was declared with.</summary>
        private sealed class SwitchTarget(TransitionSchedulerCore scheduler, TransitionRun run, StateCore state)
        {
            public TransitionSchedulerCore Scheduler { get; } = scheduler;
            public TransitionRun Run { get; } = run;
            public StateCore State { get; } = state;
        }
    }
}
