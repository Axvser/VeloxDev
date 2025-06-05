using System.Reflection;
using VeloxDev.WPF.TransitionSystem;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.StructuralDesign.Animator
{
    public interface ITransitionMeta
    {
        public TransitionParams TransitionParams { get; set; }
        public State PropertyState { get; set; }
        public List<List<Tuple<PropertyInfo, List<object?>>>> FrameSequence { get; }
    }
}
