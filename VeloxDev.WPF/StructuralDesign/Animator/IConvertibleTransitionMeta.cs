using VeloxDev.WPF.TransitionSystem;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.StructuralDesign.Animator
{
    public interface IConvertibleTransitionMeta
    {
        State ToState();
        TransitionBoard<T> ToTransitionBoard<T>() where T : class;
        TransitionMeta ToTransitionMeta();
    }
}
