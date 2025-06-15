namespace VeloxDev.Core.Generators
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class MonoBehaviourAttribute : Attribute
    {
        private readonly TimeSpan _delay;

        public MonoBehaviourAttribute(int fps)
        {
            _delay = TimeSpan.FromMilliseconds(1000d / fps);
        }

        public MonoBehaviourAttribute()
        {
            _delay = TimeSpan.FromMilliseconds(16.667);
        }
    }
}
