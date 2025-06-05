#if NETFRAMEWORK
using VeloxDev.WPF;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.TransitionSystem;
using VeloxDev.WPF.TransitionSystem.Basic;
namespace VeloxDev.WPF.FrameworkSupport
{
    internal class MetaMergeExtension
    {
        internal static ITransitionMeta MergeMetas(ICollection<ITransitionMeta> metas)
        {
            var state = new State()
            {
                StateName = Transition.TempName
            };
            foreach (var meta in metas)
            {
                state.Merge(meta);
            }
            return state;
        }
    }
}
#endif