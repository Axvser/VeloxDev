using System.Runtime.InteropServices;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Detects whether the current runtime platform is a touch-primary device
/// where ScrollViewer's gesture recognizer aggressively competes with pointer capture.
/// </summary>
internal static class PlatformDetection
{
    /// <summary>
    /// Returns true on Android and iOS, where ScrollGestureRecognizer intercepts
    /// pointer events more aggressively than on desktop or browser platforms.
    /// On these platforms, PointerPressed handlers must be registered with
    /// Tunnel routing to pre-empt the ScrollViewer gesture recognizer.
    /// </summary>
    internal static bool IsTouchPlatform { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID"))
        || RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"));
}
