namespace VeloxDev.Core.Generators
{
    /// <summary>
    /// ✨ Generator >>> Enable the Class to have a MonoBehaviour
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class MonoBehaviourAttribute : Attribute
    {
        private readonly TimeSpan _delay;

        public MonoBehaviourAttribute(TimeSpan delay)
        {
            _delay = delay;
        }

        public MonoBehaviourAttribute()
        {
            _delay = TimeSpan.FromMilliseconds(16.667);
        }
    }
}
