namespace PartialCraft.CSharp.Interfaces;

public interface IContextFilter<TInput, TOut>
{
    public TOut Filter(TInput context);
}
