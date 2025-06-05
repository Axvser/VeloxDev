namespace VeloxDev.WPF.StructuralDesign.Animator
{
    public interface ITransitionWithTarget
    {
        public WeakReference<object>? TransitionApplied { get; set; }
    }
}
