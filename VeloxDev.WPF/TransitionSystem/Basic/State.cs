using System.Collections.Concurrent;
using System.Reflection;
using VeloxDev.WPF.StructuralDesign.Animator;

namespace VeloxDev.WPF.TransitionSystem.Basic
{
    public sealed class State : ITransitionMeta, ICloneable
    {
        internal State() { }

        public static State Empty { get; } = new();

        public string StateName { get; internal set; } = string.Empty;
        public ConcurrentDictionary<string, object?> Values { get; internal set; } = new();
        public ConcurrentDictionary<string, InterpolationHandler> Calculators { get; internal set; } = new();
        public TransitionParams TransitionParams { get; set; } = new();
        public State PropertyState
        {
            get => this;
            set
            {
                StateName = value.StateName;
                Values = value.Values;
                TransitionParams = value.TransitionParams;
            }
        }
        public List<List<Tuple<PropertyInfo, List<object?>>>> FrameSequence => [];
        public void AddCalculator(string propertyName, InterpolationHandler value)
        {
            Calculators.AddOrUpdate(propertyName, value, (key, old) => value);
        }
        public void AddProperty(string propertyName, object? value)
        {
            Values.AddOrUpdate(propertyName, value, (key, old) => value);
        }
        public State Merge(ITransitionMeta meta)
        {
            foreach (var values in meta.PropertyState.Values)
            {
                AddProperty(values.Key, values.Value);
            }
            foreach (var calculator in meta.PropertyState.Calculators)
            {
                AddCalculator(calculator.Key, calculator.Value);
            }
            return this;
        }
        internal State DeepCopy()
        {
            var newState = new State
            {
                StateName = StateName,
                TransitionParams = TransitionParams.DeepCopy(),
            };

            foreach (var kvp in Values)
            {
                newState.Values[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in Calculators)
            {
                newState.Calculators[kvp.Key] = kvp.Value;
            }

            return newState;
        }
        public object Clone()
        {
            return DeepCopy();
        }
    }
}
