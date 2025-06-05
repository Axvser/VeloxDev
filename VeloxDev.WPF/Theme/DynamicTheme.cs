using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using VeloxDev.WPF.FrameworkSupport;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.StructuralDesign.Theme;
using VeloxDev.WPF.TransitionSystem;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.Theme
{
    public static class DynamicTheme
    {
        private const string SYSTEM_THEME_REGISTRYKEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string SYSTEM_THEME_LIGHT = "AppsUseLightTheme";

        private static bool _isloaded = false;
        private static bool _issysthemeevenadded = false;
        private static Type? _currentTheme = null;
        private static bool _followSystem = false;
        private static Type _alternativeTheme = typeof(Dark);

        public static Type CurrentTheme
        {
            get => _currentTheme ?? (_followSystem ? GetSystemTheme(_alternativeTheme) : _alternativeTheme);
        }
        public static bool IsThemeChanging { get; private set; } = false;
        public static IEnumerable<Type>? Attributes { get; private set; }

        public static ConcurrentDictionary<Type, ConcurrentDictionary<Type, State>> SharedSource { get; internal set; } = new();
        public static ConditionalWeakTable<IThemeApplied, ConcurrentDictionary<Type, State>> IsolatedSource { get; internal set; } = new();
        public static List<WeakReference<IThemeApplied>> GlobalInstance { get; internal set; } = [];
        public static IEnumerable<IThemeApplied> ThemeApplieds
        {
            get
            {
                foreach (var reference in GlobalInstance)
                {
                    if (reference.TryGetTarget(out var item) && item != null)
                    {
                        yield return item;
                    }
                }
            }
        }

        public static event Action? SharedValueInitialized;
        public static event Action<Type, Type>? ThemeChangeLoading;
        public static event Action<Type, Type>? ThemeChangeLoaded;

        /// <summary>
        /// [ App.cs ] Call before anything happens to make sure it works
        /// <para>
        /// Follow system topics dynamically
        /// </para>
        /// </summary>
        /// <param name="alternativeTheme">Alternate topic to take if the system topic fails to be read</param>
        public static void FollowSystem(Type alternativeTheme)
        {
            _followSystem = true;
            _alternativeTheme = alternativeTheme;
            _currentTheme = GetSystemTheme(alternativeTheme);
        }
        /// <summary>
        /// [ App.cs ] Call before anything happens to make sure it works
        /// <para>
        /// Follow the initial theme you specify and do not follow the system
        /// </para>
        /// </summary>
        /// <param name="themeType">initial theme</param>
        public static void StartWith(Type themeType)
        {
            _followSystem = false;
            _alternativeTheme = themeType;
            _currentTheme = themeType;
        }

        private static CancellationTokenSource? cts;
        public static bool TryGetTransitionMeta<T>(T target, Type themeType, out ITransitionMeta result) where T : IThemeApplied
        {
            Awake();

            var state = new State();
            var counter = 0;

            if (SharedSource.TryGetValue(target.GetType(), out var shared) && shared.TryGetValue(themeType, out var value1))
            {
                state.Merge(value1);
                counter++;
            }

            if (IsolatedSource.TryGetValue(target, out var isolated) && isolated.TryGetValue(themeType, out var value2))
            {
                state.Merge(value2);
                counter++;
            }

            result = state;
            return counter > 0;
        }
        public static void Awake<T>(params T[] targets) where T : IThemeApplied
        {
            Awake();
            IsolatedGeneration(targets);
        }
        public static void Awake()
        {
            if (!_isloaded)
            {
                _isloaded = true;
                AddSystemThemeEvent();
                var Assemblies = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());
                var classes = Assemblies.Where(t => typeof(IThemeApplied).IsAssignableFrom(t));
                Attributes = Assemblies.Where(t => typeof(IThemeAttribute).IsAssignableFrom(t) && typeof(Attribute).IsAssignableFrom(t));
                SharedGeneration(classes, Attributes);
                SharedValueInitialized?.Invoke();
            }
        }
        public static void Dispose()
        {
            _currentTheme = null;
            Attributes = null;
            _isloaded = false;
            GlobalInstance.Clear();
            SharedSource.Clear();
            RemoveSystemThemeEvent();
        }

        public static async void Apply(Type themeType, TransitionParams? param = null)
        {
            Awake();

            var source = new CancellationTokenSource();
            var oldsource = Interlocked.Exchange(ref cts, source);
            if (oldsource != null)
            {
                oldsource.Cancel();
                oldsource.Dispose();
            }
            if (GlobalInstance.Count > 0)
            {
                GlobalInstance.RemoveAll(x => !x.TryGetTarget(out _));
            }
            var oldTheme = CurrentTheme;

            var targets = ThemeApplieds;
            var newParam = param ?? TransitionParams.Theme.DeepCopy();
            var spans = GetAccDeltaTime(newParam.DeltaTime, newParam.Acceleration, XMath.Clamp((int)newParam.FrameCount, 2, int.MaxValue));
            var data = await CalculateAtomAsync(targets, themeType, newParam, spans, source);
            if (source.IsCancellationRequested) return;

            ThemeChangeLoading?.Invoke(oldTheme, themeType);
            _currentTheme = themeType;
            IsThemeChanging = true;
            foreach (var target in targets)
            {
                target.RunThemeChanging(oldTheme, themeType);
                target.IsThemeChanging = true;
                target.CurrentTheme = themeType;
            }
            foreach (var atom in data)
            {
                try
                {
                    if (source.Token.IsCancellationRequested) break;
                    atom.Invoke();
                    await Task.Delay(atom.Duration, source.Token);
                }
                catch
                {

                }
            }
            foreach (var target in targets)
            {
                target.RunThemeChanged(oldTheme, themeType);
                target.IsThemeChanging = false;
                IsThemeChanging = false;
            }
            ThemeChangeLoaded?.Invoke(oldTheme, themeType);
        }
        private static Task<IEnumerable<TransitionAtom>> CalculateAtomAsync(
            IEnumerable<IThemeApplied> targets,
            Type theme,
            TransitionParams param,
            List<int> spans,
            CancellationTokenSource cts)
        {
            var tcs = new TaskCompletionSource<IEnumerable<TransitionAtom>>();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var result = CalculateAtom(targets, theme, param, spans, cts);
                    tcs.SetResult(result);
                }
                catch
                {

                }
            });

            return tcs.Task;
        }
        private static IEnumerable<TransitionAtom> CalculateAtom(
            IEnumerable<IThemeApplied> targets,
            Type theme,
            TransitionParams param,
            List<int> spans,
            CancellationTokenSource cts)
        {
            var framecount = XMath.Clamp((int)param.FrameCount, 2, int.MaxValue);
            List<Tuple<ITransitionMeta, IThemeApplied>> metas = new(framecount);
            foreach (var target in targets)
            {
                if (cts.IsCancellationRequested) break;
                if (TryGetTransitionMeta(target, theme, out var meta))
                {
                    metas.Add(Tuple.Create(meta, target));
                }
            }
            List<Tuple<IThemeApplied, List<List<Tuple<PropertyInfo, List<object?>>>>>> framesGroup = new(metas.Count);
            var deltatime = (int)param.DeltaTime;
            foreach (var meta in metas)
            {
                if (cts.IsCancellationRequested) break;
                framesGroup.Add(Tuple.Create(meta.Item2, LinearInterpolation.ComputingFrames(meta.Item2.GetType(), meta.Item1.PropertyState, meta.Item2, framecount)));
            }
            for (int i = 0; i < framecount; i++)
            {
                if (cts.IsCancellationRequested) break;
                var atom = new TransitionAtom() { Duration = param.Acceleration == 0 ? deltatime : spans[i] };
                foreach (var group in framesGroup)
                {
                    if (cts.IsCancellationRequested) break;
                    foreach (var item in group.Item2)
                    {
                        if (cts.IsCancellationRequested) break;
                        foreach (var tuple in item)
                        {
                            if (cts.IsCancellationRequested) break;
                            atom.Updated += () =>
                            {
                                tuple.Item1.SetValue(group.Item1, tuple.Item2[i]);
                            };
                        }
                    }
                }
                yield return atom;
            }
        }
        private static List<int> GetAccDeltaTime(double deltaTime, double acceleration, int steps)
        {
            List<int> result = [];
            if (acceleration == 0) return result;
            var acc = XMath.Clamp(acceleration, -1, 1);
            var start = deltaTime * (1 + acc);
            var end = deltaTime * (1 - acc);
            var delta = end - start;
            for (int i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                result.Add((int)(start + t * delta));
            }
            return result;
        }

        public static void SetSharedValue(Type classType, Type themeType, string propertyName, object? newValue)
        {
            Awake();

            if (SharedSource.TryGetValue(classType, out var dictionary) && dictionary.TryGetValue(themeType, out var value2))
            {
                value2.AddProperty(propertyName, newValue);
            }
        }
        public static object? GetSharedValue(Type classType, Type themeType, string propertyName)
        {
            Awake();

            if (SharedSource.TryGetValue(classType, out var dictionary) && dictionary.TryGetValue(themeType, out var state) && state.Values.TryGetValue(propertyName, out var result))
            {
                return result;
            }

            return null;
        }

        public static void SetIsolatedValue<T>(T target, Type themeType, string propertyName, object? newValue) where T : IThemeApplied
        {
            Awake();

            if (IsolatedSource.TryGetValue(target, out var dictionary) && dictionary.TryGetValue(themeType, out var state))
            {
                state.AddProperty(propertyName, newValue);
            }
        }
        public static object? GetIsolatedValue<T>(T target, Type themeType, string propertyName) where T : IThemeApplied
        {
            Awake();

            if (IsolatedSource.TryGetValue(target, out var dictionary) && dictionary.TryGetValue(themeType, out var state) && state.Values.TryGetValue(propertyName, out var result))
            {
                return result;
            }

            return null;
        }

        private static void AddSystemThemeEvent()
        {
            if (!_issysthemeevenadded)
            {
                SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
                _issysthemeevenadded = true;
            }
        }
        private static void RemoveSystemThemeEvent()
        {
            if (_issysthemeevenadded)
            {
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                _issysthemeevenadded = false;
            }
        }
        private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_followSystem)
            {
                Apply(GetSystemTheme(_alternativeTheme), TransitionParams.Theme.DeepCopy());
            }
        }
        private static Type GetSystemTheme(Type alternativeTheme)
        {
            RegistryKey? key = Registry.CurrentUser.OpenSubKey(SYSTEM_THEME_REGISTRYKEY);
            if (key != null)
            {
                var theme = (int?)key.GetValue(SYSTEM_THEME_LIGHT, -1);
                key.Close();
                if (theme == 1)
                {
                    _currentTheme = typeof(Light);
                    return typeof(Light);
                }
            }
            _currentTheme = alternativeTheme;
            return alternativeTheme;
        }

        private static void SharedGeneration(IEnumerable<Type> classes, IEnumerable<Type> attributes)
        {
            foreach (var cs in classes)
            {
                TransitionScheduler.InitializeTypes(cs);
                if (!TransitionScheduler.SplitedPropertyInfos.TryGetValue(cs, out var group)) break;
                var unit = new ConcurrentDictionary<Type, State>();
                foreach (var attribute in attributes)
                {
                    var properties = cs.GetProperties()
                        .Select(p => new
                        {
                            PropertyInfo = p,
                            Context = p.GetCustomAttribute(attribute, true) as IThemeAttribute,
                        });
                    var state = new State();
                    foreach (var info in properties)
                    {
                        if (info.PropertyInfo.CanWrite && info.PropertyInfo.CanRead && info.Context != null)
                        {
                            Action func = (group.Item1.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item2.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item3.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item4.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item5.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item6.TryGetValue(info.PropertyInfo.Name, out _))
                            switch
                            {
                                (true, false, false, false, false, false) => () =>
                                {
                                    var context = info.Context.Parameters.FirstOrDefault();
                                    if (context is null) return;
                                    var value = Convert.ToDouble(context);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, true, false, false, false, false) => () =>
                                {
                                    var context = info.Context.Parameters.FirstOrDefault();
                                    if (context is null) return;
                                    var value = ParseBrush(cs, context.ToString() ?? string.Empty);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, true, false, false, false) => () =>
                                {
                                    var context = info.Context.Parameters.FirstOrDefault();
                                    if (context is null) return;
                                    var text = context.ToString();
                                    var value = text == null ? Transform.Identity : Transform.Parse(text);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, false, true, false, false) => () =>
                                {
                                    if (info.Context.Parameters.Length == 0) return;
                                    var value = Activator.CreateInstance(typeof(Point), (info.Context.Parameters.Length == 2) ? info.Context.Parameters : [0d, 0d]);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, false, false, true, false) => () =>
                                {
                                    if (info.Context.Parameters.Length == 0) return;
                                    var value = Activator.CreateInstance(typeof(CornerRadius), (info.Context.Parameters.Length == 1 || info.Context.Parameters.Length == 4) ? info.Context.Parameters : [0d]);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, false, false, false, true) => () =>
                                {
                                    if (info.Context.Parameters.Length == 0) return;
                                    var value = Activator.CreateInstance(typeof(Thickness), (info.Context.Parameters.Length == 1 || info.Context.Parameters.Length == 4) ? info.Context.Parameters : [0d]);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                _ => () => { return; }
                            };
                            func.Invoke();
                        }
                    }
                    unit.TryAdd(attribute, state);
                }
                SharedSource.TryAdd(cs, unit);
            }
        }
        private static void IsolatedGeneration<T>(params T[] targets) where T : IThemeApplied
        {
            if (Attributes == null) return;

            foreach (var target in targets)
            {
                GlobalInstance.Add(new WeakReference<IThemeApplied>(target));
                target.CurrentTheme = CurrentTheme;
                if (!TransitionScheduler.SplitedPropertyInfos.TryGetValue(target.GetType(), out var group)) break;
                var unit = new ConcurrentDictionary<Type, State>();
                foreach (var attribute in Attributes)
                {
                    var properties = target.GetType().GetProperties()
                        .Select(p => new
                        {
                            PropertyInfo = p,
                            Context = p.GetCustomAttribute(attribute, true) as IThemeAttribute,
                        });

                    var state = new State();

                    foreach (var info in properties)
                    {
                        if (info.PropertyInfo.CanWrite && info.PropertyInfo.CanRead && info.Context != null)
                        {
                            Action func = (group.Item1.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item2.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item3.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item4.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item5.TryGetValue(info.PropertyInfo.Name, out _),
                                           group.Item6.TryGetValue(info.PropertyInfo.Name, out _))
                            switch
                            {
                                (true, false, false, false, false, false) => () =>
                                {
                                    var context = info.Context.Parameters.FirstOrDefault();
                                    if (context is null) return;
                                    var value = Convert.ToDouble(context);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, true, false, false, false, false) => () =>
                                {
                                    var context = info.Context.Parameters.FirstOrDefault();
                                    if (context is null) return;
                                    var value = ParseBrush(target.GetType(), context.ToString() ?? string.Empty);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, true, false, false, false) => () =>
                                {
                                    var context = info.Context.Parameters.FirstOrDefault();
                                    if (context is null) return;
                                    var text = context.ToString();
                                    var value = text == null ? Transform.Identity : Transform.Parse(text);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, false, true, false, false) => () =>
                                {
                                    if (info.Context.Parameters.Length == 0) return;
                                    var value = Activator.CreateInstance(typeof(Point), (info.Context.Parameters.Length == 2) ? info.Context.Parameters : [0d, 0d]);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, false, false, true, false) => () =>
                                {
                                    if (info.Context.Parameters.Length == 0) return;
                                    var value = Activator.CreateInstance(typeof(CornerRadius), (info.Context.Parameters.Length == 1 || info.Context.Parameters.Length == 4) ? info.Context.Parameters : [0d]);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                (false, false, false, false, false, true) => () =>
                                {
                                    if (info.Context.Parameters.Length == 0) return;
                                    var value = Activator.CreateInstance(typeof(Thickness), (info.Context.Parameters.Length == 1 || info.Context.Parameters.Length == 4) ? info.Context.Parameters : [0d]);
                                    state.AddProperty(info.PropertyInfo.Name, value);
                                    return;
                                }
                                ,
                                _ => () => { return; }
                            };
                            func.Invoke();
                        }
                    }

                    unit.TryAdd(attribute, state);
                }

                IsolatedSource.Add(target, unit);
            }
        }

        private static readonly ConcurrentDictionary<string, Brush> _brushCache = new();
        private static Brush ParseBrush(Type target, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return Brushes.Transparent;

            // 使用缓存加速Brush解析,仅限资源查找与反射查找
            if (_brushCache.TryGetValue(parameter, out var cachedBrush))
                return cachedBrush;

            // 1. 尝试作为资源 Key 查找
            if (Application.Current.TryFindResource(parameter) is Brush resourceBrush)
            {
                _brushCache.TryAdd(parameter, resourceBrush);
                return resourceBrush;
            }

            // 2. 尝试作为 target 的静态字段或属性查找
            Brush? memberBrush = GetStaticBrushFromType(target, parameter);
            if (memberBrush != null)
            {
                _brushCache.TryAdd(parameter, memberBrush);
                return memberBrush;
            }

            // 3. 尝试解析为颜色字符串
            try
            {
                return new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(parameter)
                );
            }
            catch (FormatException)
            {
                return Brushes.Transparent;
            }
        }
        private static Brush? GetStaticBrushFromType(Type type, string memberName)
        {
            // 查找静态字段
            FieldInfo? field = type.GetField(
                memberName,
                BindingFlags.Public | BindingFlags.Static
            );
            if (field?.GetValue(null) is Brush fieldBrush)
                return fieldBrush;

            // 查找静态属性
            PropertyInfo? property = type.GetProperty(
                memberName,
                BindingFlags.Public | BindingFlags.Static
            );
            if (property?.GetValue(null) is Brush propertyBrush)
                return propertyBrush;

            return null;
        }
    }
}
