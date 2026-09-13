using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using System;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo.Views;

/* We recommend defining theme-related operations in a separate partial class, so interaction logic
   is not cluttered by unrelated code */
/* Note: when using Rider, this may cause generated content to be unrecognized. It does not affect
   compilation, but Rider may need to be restarted to recover recognition. */

//------------------------------------------------------------------------------------------------------------------
// User Part ↓

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow : Window
{
    private readonly WindowNotificationManager _message;

    public MainWindow()
    {
        InitializeComponent();
        _message = new WindowNotificationManager(this) { MaxItems = 3 };
        LoadTheme();
    }

    private void ChangeTheme(object sender, RoutedEventArgs e)
    {
        ReverseThemeWithAnimation();
    }
}

public partial class MainWindow
{
    private void LoadTheme()
    {
        InitializeTheme(); // this call is required and must come after InitializeComponent()

        // [ Applies globally ]
        // If you do not use themed transitions, the interpolator does not need to be configured;
        // otherwise this call is mandatory.
        ThemeManager.SetPlatformInterpolator(new Interpolator());

        // [ Applies globally ]
        // When the theme changes, should the animation's starting state come from the cache, or
        // should reflection read the current state as the starting point?
        ThemeManager.StartModel = StartModel.Cache;
    }

    /// <summary>
    /// Theme switching has a callback
    /// </summary>
    /// <param name="oldValue">The value before switching</param>
    /// <param name="newValue">The value after switching</param>
    partial void OnThemeChanged(Type? oldValue, Type? newValue)
    {
        _message.Show(new Notification("Message", $"Theme changed from {oldValue?.Name} to {newValue?.Name}"));
    }

    /// <summary>
    /// This kind of theme switch loads a gradient animation
    /// </summary>
    private static void ReverseThemeWithAnimation()
    {
        var condition = ThemeManager.Current == typeof(Dark);
        if (condition)
        {
            ThemeManager.Transition<Light>(TransitionEffects.Theme);
        }
        else
        {
            ThemeManager.Transition<Dark>(TransitionEffects.Theme);
        }
    }

    /// <summary>
    /// This kind of theme switch has no gradient animation
    /// </summary>
    private static void ReverseThemeWithOutAnimation()
    {
        var condition = ThemeManager.Current == typeof(Dark);
        if (condition)
        {
            ThemeManager.Jump<Light>();
        }
        else
        {
            ThemeManager.Jump<Dark>();
        }
    }

    /// <summary>
    /// Provides a set of extensions for getting and editing theme resource packages. These methods
    /// are auto-generated; here, for example, they all belong to MainWindow.
    /// </summary>
    private void ThemeValueEx()
    {
        // Dynamically edit theme resource values
        SetThemeValue<Light>(nameof(Background), new object?[] { "#ffffff" });
        // Can be restored to the initial state
        RestoreThemeValue<Light>(nameof(Foreground));

        // Get the static resources
        var staticCache = GetStaticThemeCache();
        // Get the dynamic resources
        var dynamicCache = GetActiveThemeCache();

        /* The "resource" here is a complex auto-generated structure.
           Only modified properties are stored in the dynamic resources; otherwise nothing is stored.
           When the theme switches, dynamic content overrides static content.
           Dictionary<string,Dictionary<PropertyInfo,Dictionary<Type,object?>>>

           From left to right
           string       -> name of property
           PropertyInfo -> target to use theme change
           Type         -> theme
           object?      -> value of property at the theme

           It provides full access to the theme resources.
         */
    }
}