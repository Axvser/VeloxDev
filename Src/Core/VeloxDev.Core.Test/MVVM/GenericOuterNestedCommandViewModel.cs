using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// A nested view model whose outer class is generic. The generator rewrites the outer declaration when it emits
/// the nested one, and it used to drop the <c>&lt;T&gt;</c> — <c>partial class Outer</c> and
/// <c>partial class Outer&lt;T&gt;</c> are different arities that never merge, so the compiler produced an empty
/// <c>Outer</c> and every member inside became CS0103, reported against the generated file.
/// </summary>
/// <typeparam name="T">Unused: the point is that the outer class has a type parameter at all.</typeparam>
public partial class GenericOuter<T>
{
    public partial class Inner
    {
        internal bool Ran { get; private set; }

        [VeloxCommand]
        private Task RunAsync()
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }
}
