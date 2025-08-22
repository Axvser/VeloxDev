using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.DynamicTheme;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Core.DynamicTheme
{
    public class ThemeManager
    {
        private static InterpolatorCore? _interpolator;

        private static readonly Dictionary<Type, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>> _def_cache = new();
        private static readonly ConditionalWeakTable<IThemeObject, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>> _act_cache = new();
        private static readonly List<WeakReference<IThemeObject>> activeThemes = [];

        /// <summary>
        /// The current theme in use and Default is <see cref="Dark"/>
        /// </summary>
        public static Type Current { get; internal set; } = typeof(Dark);

        /// <summary>
        /// Sets the platform-specific interpolator to be used by the system
        /// <para>This method only needs to be called once</para>
        /// </summary>
        /// <param name="interpolator">It is usually the Interpolator provided by the adaptation layer of each platform</param>
        public static void SetPlatformInterpolator<T>(T interpolator) where T : InterpolatorCore
        {
            _interpolator = interpolator;
        }

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
        public static async void Transition(Type themeType, ITransitionEffectCore effect)
        {
            var current = Current;
            if (themeType == current || !typeof(ITheme).IsAssignableFrom(themeType))
            {
                Debug.WriteLine("[ThemeManager] Invalid theme type, jumping to current theme.");
                return;
            }
            CancleTransition();
            activeThemes.RemoveAll(x => !x.TryGetTarget(out _));
            var actives = activeThemes.Select(x => x.TryGetTarget(out var obj) ? obj : null).Where(x => x != null).ToArray();
            int steps = effect.FPS * (int)effect.Duration.TotalSeconds;
            if (steps <= 0)
            {
                steps = 1;
            }
            int deltaTime = (int)(1000.0 / effect.FPS);
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(current, themeType);
            }
            await ExecuteTransition(CalculateFrames(actives, steps, effect.EaseCalculator, themeType), deltaTime, themeType);
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
        public static async void Jump(Type themeType)
        {
            var current = Current;
            if (themeType == current || !typeof(ITheme).IsAssignableFrom(themeType))
            {
                Debug.WriteLine("[ThemeManager] Invalid theme type, jumping to current theme.");
                return;
            }
            CancleTransition();
            activeThemes.RemoveAll(x => !x.TryGetTarget(out _));
            var actives = activeThemes.Select(x => x.TryGetTarget(out var obj) ? obj : null).Where(x => x != null).ToArray();
            int steps = 1;
            int deltaTime = 0;
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(current, themeType);
            }
            await ExecuteTransition(CalculateFrames(actives, steps, Eases.Default, themeType), deltaTime, themeType);
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

        private static Queue<Action> CalculateFrames(IThemeObject?[] targets, int steps, IEaseCalculator ease, Type targetThemeType)
        {
            var updates = new Queue<Action>(steps);
            if (steps <= 0) return updates;

            // 预计算缓动曲线映射的帧索引
            var easedFrameIndices = new int[steps];
            for (int i = 0; i < steps; i++)
            {
                try
                {
                    double t = (double)i / (steps - 1);
                    double easedT = ease.Ease(t);
                    easedFrameIndices[i] = (int)Math.Round(easedT * (steps - 1));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CalculateFrames] Error calculating eased frame index: {ex.Message}");
                    easedFrameIndices[i] = i; // 回退到线性索引
                }
            }

            // 为每个目标对象和属性预先计算所有帧的值
            var allFrames = new Dictionary<IThemeObject, Dictionary<PropertyInfo, object?[]>>();

            foreach (var target in targets)
            {
                try
                {
                    if (target == null)
                    {
                        Debug.WriteLine("[CalculateFrames] Encountered null target, skipping");
                        continue;
                    }

                    Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> staticCache;
                    Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> activeCache;

                    try
                    {
                        staticCache = target.GetStaticCache();
                        activeCache = target.GetActiveCache();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CalculateFrames] Error getting cache for target: {ex.Message}");
                        continue;
                    }

                    var propertyFrames = new Dictionary<PropertyInfo, object?[]>();

                    // 处理每个属性
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
                            Debug.WriteLine($"[CalculateFrames] Error getting property info: {ex.Message}");
                            continue;
                        }

                        // 获取当前值（优先动态缓存）
                        object? currentValue = null;
                        bool hasCurrentValue = false;

                        try
                        {
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
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CalculateFrames] Error getting current value for {propEntry.Key}: {ex.Message}");
                        }

                        if (!hasCurrentValue)
                        {
                            Debug.WriteLine($"[CalculateFrames] No current value found for {propEntry.Key}, skipping");
                            continue;
                        }

                        // 获取目标值（优先动态缓存，使用明确的targetThemeType）
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
                            Debug.WriteLine($"[CalculateFrames] Error getting target value for {propEntry.Key}: {ex.Message}");
                        }

                        if (!hasTargetValue)
                        {
                            Debug.WriteLine($"[CalculateFrames] No target value found for {propEntry.Key}, skipping");
                            continue;
                        }

                        // 计算过渡帧
                        var frames = new object?[steps];
                        bool framesCalculated = false;

                        try
                        {
                            if (_interpolator?.TryGetValue(propertyInfo.PropertyType, out var interpolator) ?? false)
                            {
                                var interpolated = interpolator!.Interpolate(currentValue, targetValue, steps);
                                for (int i = 0; i < steps && i < interpolated.Count; i++)
                                {
                                    frames[i] = interpolated[i];
                                }
                                framesCalculated = true;
                            }
                            else if (currentValue is IInterpolable interpolable)
                            {
                                var interpolated = interpolable.Interpolate(currentValue, targetValue, steps);
                                for (int i = 0; i < steps && i < interpolated.Count; i++)
                                {
                                    frames[i] = interpolated[i];
                                }
                                framesCalculated = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CalculateFrames] Error interpolating values for {propEntry.Key}: {ex.Message}");
                        }

                        if (!framesCalculated)
                        {
                            Debug.WriteLine($"[CalculateFrames] No interpolator found for {propEntry.Key}, using simple transition");
                            try
                            {
                                for (int i = 0; i < steps; i++)
                                {
                                    frames[i] = i == steps - 1 ? targetValue : currentValue;
                                }
                                framesCalculated = true;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[CalculateFrames] Error creating simple transition for {propEntry.Key}: {ex.Message}");
                            }
                        }

                        if (framesCalculated)
                        {
                            propertyFrames[propertyInfo] = frames;
                        }
                    }

                    if (propertyFrames.Count > 0)
                    {
                        allFrames[target] = propertyFrames;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CalculateFrames] Unexpected error processing target: {ex.Message}");
                }
            }

            // 构建每一帧的更新操作
            for (int frameIndex = 0; frameIndex < steps; frameIndex++)
            {
                try
                {
                    int easedIndex = easedFrameIndices[frameIndex];
                    int actualFrameIndex = easedIndex < 0 ? 0 : (easedIndex >= steps ? steps - 1 : easedIndex);

                    updates.Enqueue(() =>
                    {
                        foreach (var targetEntry in allFrames)
                        {
                            try
                            {
                                var target = targetEntry.Key;
                                foreach (var propEntry in targetEntry.Value)
                                {
                                    try
                                    {
                                        var value = propEntry.Value[actualFrameIndex];
                                        if (value != null)
                                        {
                                            propEntry.Key.SetValue(target, value);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[CalculateFrames] Error setting property value: {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[CalculateFrames] Error processing target in frame {frameIndex}: {ex.Message}");
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CalculateFrames] Error creating update action for frame {frameIndex}: {ex.Message}");
                }
            }

            return updates;
        }

        private static CancellationTokenSource? _cts_transition = null;
        private static readonly SemaphoreSlim _asyncLock_transition = new(1, 1);
        private static void CancleTransition()
        {
            Interlocked.Exchange(ref _cts_transition, null)?.Cancel();
        }
        private static async Task ExecuteTransition(Queue<Action> updates, int deltaTime, Type themeType)
        {
            await _asyncLock_transition.WaitAsync();

            Interlocked.Exchange(ref _cts_transition, new CancellationTokenSource())?.Cancel();
            var cts = _cts_transition ?? new CancellationTokenSource();

            try
            {
                while (updates.Count > 0)
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }
                    var action = updates.Dequeue();
                    action.Invoke();
                    await Task.Delay(deltaTime, cts.Token);
                }
                if (!cts.IsCancellationRequested)
                {
                    Current = themeType;
                    return;
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeManager] Error during transition execution: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _cts_transition, null)?.Cancel();
                _asyncLock_transition.Release();
            }
        }
    }
}
