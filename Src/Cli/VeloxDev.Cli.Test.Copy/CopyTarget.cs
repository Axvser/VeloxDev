namespace VeloxDev.Cli.Test.Copy;

/// <summary>
/// A dependency assembly the plugin-loader tests load by path only. No test references this type, so the
/// assembly stays unloaded until the loader copies and loads it — which is what makes the source-lock
/// assertion meaningful.
/// </summary>
public sealed class CopyTarget
{
    /// <summary>A stable marker the loader does not interpret.</summary>
    public string Name => "copy-target";
}
