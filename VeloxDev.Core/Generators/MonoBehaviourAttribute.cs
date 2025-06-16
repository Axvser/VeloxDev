namespace VeloxDev.Core.Generators
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class MonoBehaviourAttribute : Attribute
    {
        private readonly TimeSpan _delay;

        public MonoBehaviourAttribute(int fps = 60)
        {
            _delay = TimeSpan.FromMilliseconds(1000d / fps > 0 ? fps : 1);
        }
    }
}
