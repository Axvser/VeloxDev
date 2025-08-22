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

        // 按类型快速访问静态主题资源
        private static readonly Dictionary<Type, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>> _def_cache = new();
        // 按引用快速访问动态主题资源,每次做主题相关操作时优先清理activeThemes,而后再执行主题切换
        private static readonly ConditionalWeakTable<IThemeObject, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>> _act_cache = new();
        private static readonly List<WeakReference<IThemeObject>> activeThemes = [];

        // 当前主题类型,仅在完成主题切换后更新
        public static Type Current { get; internal set; } = typeof(Dark);

        // 设定平台特定插值器
        public static void SetPlatformInterpolator(InterpolatorCore interpolator)
        {
            _interpolator = interpolator;
        }

        // 注册主题对象
        public static void Register(IThemeObject target)
        {
            if (!_act_cache.TryGetValue(target, out _))
            {
                Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>? cache = [];
                _act_cache.Add(target, cache);
                activeThemes.Add(new WeakReference<IThemeObject>(target));
            }
        }

        // 注销主题对象
        public static void Unregister(IThemeObject target)
        {
            _act_cache.Remove(target);
            activeThemes.RemoveAll(x => x.TryGetTarget(out var obj) && obj == target);
        }

        // 渐变至目标主题
        public static async void Transition<T>(ITransitionEffectCore effect) where T : ITheme
        {
            CancleTransition();
            if (typeof(T) == Current)
            {
                Jump<T>();
                return;
            }
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
                themeObject?.ExecuteThemeChanging(Current, typeof(T));
            }
            await ExecuteTransition(CalculateFrames(actives, steps, effect.EaseCalculator), deltaTime);
            Current = typeof(T);
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanged(Current, typeof(T));
            }
        }

        // 跳转到目标主题
        public static void Jump<T>() where T : ITheme
        {
            Jump(typeof(T));
        }
        public static async void Jump(Type themeType)
        {
            CancleTransition();
            activeThemes.RemoveAll(x => !x.TryGetTarget(out _));
            var actives = activeThemes.Select(x => x.TryGetTarget(out var obj) ? obj : null).Where(x => x != null).ToArray();
            int steps = 1;
            int deltaTime = 0;
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(Current, themeType);
            }
            await ExecuteTransition(CalculateFrames(actives, steps, Eases.Default), deltaTime);
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanged(Current, themeType);
            }
            Current = themeType;
        }

        private static Queue<Action> CalculateFrames(IThemeObject?[] targets, int steps, IEaseCalculator ease)
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

                        // 获取目标值（优先动态缓存）
                        object? targetValue = null;
                        bool hasTargetValue = false;

                        try
                        {
                            if (activeCache.TryGetValue(propEntry.Key, out var activePropCache) &&
                                activePropCache.TryGetValue(propertyInfo, out var activeTypeCache))
                            {
                                var targetEntry = activeTypeCache.FirstOrDefault(x => x.Key != Current);
                                if (targetEntry.Value != null)
                                {
                                    targetValue = targetEntry.Value;
                                    hasTargetValue = true;
                                }
                            }

                            if (!hasTargetValue)
                            {
                                var targetEntry = typeValues.FirstOrDefault(x => x.Key != Current);
                                if (targetEntry.Value != null)
                                {
                                    targetValue = targetEntry.Value;
                                    hasTargetValue = true;
                                }
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
        private static async Task ExecuteTransition(Queue<Action> updates, int deltaTime)
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
            }
            catch
            {
                Jump(Current);
            }
            finally
            {
                Interlocked.Exchange(ref _cts_transition, null)?.Cancel();
                _asyncLock_transition.Release();
            }
        }
    }
}
