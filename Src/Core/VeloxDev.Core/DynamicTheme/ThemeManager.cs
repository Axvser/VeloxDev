using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(current, themeType);
            }
            await ExecuteTransition(PrepareSamplers(actives, themeType), effect.Ease, effect.Duration.TotalMilliseconds, themeType);
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
            foreach (var themeObject in actives)
            {
                themeObject?.ExecuteThemeChanging(current, themeType);
            }
            await ExecuteTransition(PrepareSamplers(actives, themeType), Eases.Default, 0d, themeType);
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

        private static List<TransitionEntry> PrepareSamplers(IThemeObject?[] targets, Type targetThemeType)
        {
            var entries = new List<TransitionEntry>();

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

                        // Resolve a sampler: registered native -> simple transition (hold, jump at end)
                        ISampler? resolved = null;
                        try
                        {
                            if (InterpolatorCore.TryGetInterpolator(propertyInfo.PropertyType, out var registered))
                            {
                                resolved = registered;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ThemeManager] Error resolving sampler for {propEntry.Key}: {ex.Message}");
                        }

                        // Normalize: produce the endpoint values and bind the sampler (writes go through the compiled property path).
                        // When no sampler is resolved, keep the raw values for the simple hold-until-end transition.
                        var transitionProperty = TransitionProperty.FromProperty(propertyInfo);
                        object? normStart = currentValue;
                        object? normEnd = targetValue;
                        if (resolved != null)
                        {
                            normStart = resolved.NormalizeStart(currentValue, targetValue, null);
                            normEnd = resolved.NormalizeEnd(currentValue, targetValue, null);
                        }
                        entries.Add(new TransitionEntry(target, transitionProperty, resolved, normStart, normEnd));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThemeManager] Unexpected error processing target: {ex.Message}");
                }
            }

            return entries;
        }

        private sealed class TransitionEntry
        {
            public TransitionEntry(object target, ITransitionProperty transitionProperty, ISampler? sampler, object? current, object? targetValue)
            {
                Target = target;
                TransitionProperty = transitionProperty;
                Sampler = sampler;
                Current = current;
                TargetValue = targetValue;
            }

            public object Target { get; }
            public ITransitionProperty TransitionProperty { get; }
            public ISampler? Sampler { get; }
            public object? Current { get; }
            public object? TargetValue { get; }

            // Per-animation reusable scratch, lazily created by the sampler on the first middle-frame call.
            public object? Working;
        }

        private static CancellationTokenSource? _cts_transition = null;
        private static readonly SemaphoreSlim _asyncLock_transition = new(1, 1);
        private static void CancleTransition()
        {
            Interlocked.Exchange(ref _cts_transition, null)?.Cancel();
        }
        private static async Task ExecuteTransition(List<TransitionEntry> entries, IEaseCalculator ease, double durationMs, Type themeType)
        {
            await _asyncLock_transition.WaitAsync();

            Interlocked.Exchange(ref _cts_transition, new CancellationTokenSource())?.Cancel();
            var cts = _cts_transition ?? new CancellationTokenSource();

            try
            {
                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }
                    var rawT = durationMs <= 0 ? 1 : stopwatch.Elapsed.TotalMilliseconds / durationMs;
                    if (rawT > 1) rawT = 1;
                    var easedT = ease.Ease(rawT);
                    if (easedT < 0) easedT = 0;
                    else if (easedT > 1) easedT = 1;

                    var isEnd = rawT >= 1;
                    var applyT = isEnd ? 1.0 : easedT;
                    foreach (var entry in entries)
                    {
                        try
                        {
                            if (entry.Sampler == null)
                            {
                                // simple transition: hold the current value until the end
                                if (isEnd && entry.TargetValue != null)
                                    entry.TransitionProperty.SetValue(entry.Target, entry.TargetValue);
                                continue;
                            }
                            entry.Sampler.InsertFrame(entry.Target, entry.TransitionProperty, ref entry.Working, entry.Current, entry.TargetValue, null, applyT);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ThemeManager] Error setting property value: {ex.Message}");
                        }
                    }

                    if (rawT >= 1)
                    {
                        break;
                    }
                    await Task.Delay(1, cts.Token);
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
