using VeloxDev.AspectOriented;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.AspectOriented;

/// <summary>
/// A proxy target: one member of each kind the generator can mirror.
/// </summary>
/// <remarks>
/// The field carries <c>[VeloxProperty]</c> as well as <c>[AspectOriented]</c> because that is what makes the
/// generated interface mirror the <em>property</em> the MVVM generator writes, rather than the field.
/// </remarks>
public partial class AopFixture
{
    /// <summary>What actually ran, in order. Both the member bodies and the aspects append to it.</summary>
    internal List<string> Trace { get; } = [];

    [VeloxProperty]
    [AspectOriented]
    private string _title = "initial";

    [AspectOriented]
    public int Add(int a, int b)
    {
        Trace.Add($"body:Add({a},{b})");
        return a + b;
    }

    [AspectOriented]
    public void Ping() => Trace.Add("body:Ping");

    [AspectOriented]
    public string Echo(string value)
    {
        Trace.Add($"body:Echo({value})");
        return value;
    }
}
