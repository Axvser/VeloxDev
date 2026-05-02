namespace VeloxDev.TimeLine
{
    /// <summary>
    /// Enables the instance to run MonoBehaviour-like lifecycle methods in the TimeLine system.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class MonoBehaviourAttribute(string channel = MonoBehaviourManager.DEFAULT_CHANNEL, int fps = -1) : Attribute
    {
        /// <summary>
        /// The named channel this behaviour will be registered to.
        /// </summary>
        public string Channel { get; } = channel;

        /// <summary>
        /// The target FPS for the channel. -1 means use the channel's existing setting.
        /// </summary>
        public int TargetFPS { get; set; } = fps;
    }
}
