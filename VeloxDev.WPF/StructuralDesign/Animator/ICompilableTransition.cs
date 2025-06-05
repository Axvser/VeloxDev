namespace VeloxDev.WPF.StructuralDesign.Animator
{
    public interface ICompilableTransition
    {
        public IExecutableTransition Compile();
    }
}
