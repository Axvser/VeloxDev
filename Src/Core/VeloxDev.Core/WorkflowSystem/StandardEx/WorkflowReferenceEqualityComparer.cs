using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem.StandardEx;

// 组件按引用身份比较：它们不重写 Equals，Join 注册表、产物表与空间索引都以同一次实例化为键。
internal sealed class WorkflowReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly WorkflowReferenceEqualityComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    // 用 RuntimeHelpers 的 hash，避免组件自己的 GetHashCode override 把两个实例算成同一个桶键。
    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
