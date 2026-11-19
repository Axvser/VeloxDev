using Avalonia;
using Avalonia.Themes.Fluent;

namespace VeloxDev.Avalonia.Headless.Tests;

/// <summary>
/// Entry point for <c>HeadlessUnitTestSession</c>. The suite builds its visual tree in code and never
/// loads XAML, but controls still need a theme: without one the <see cref="Avalonia.Controls.ScrollViewer"/>
/// has no template, so its <c>Viewport</c> stays 0 and the surface never sees a viewport.
/// </summary>
public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
