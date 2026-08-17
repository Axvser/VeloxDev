namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 可重定向节点：执行时依据 <see cref="IRuntimeContext"/> 决定是否回退到链内某个前驱编译状态。
/// 由 <see cref="CompilerEngine"/> 在驱动链内节点后调用其 <see cref="ResolveRedirectAsync"/>，
/// 返回非 null 的 <see cref="CompileContext.Order"/> 即回退到该状态重新执行（链内回退，v1）。
/// 编译图本身是无环的，回退是纯运行期契约。
/// </summary>
public interface IRedirectable
{
    /// <summary>
    /// 编译期契约方法：依据运行上下文决定是否回退。返回非 null 的 Order = 回退到
    /// CompileContext.Order 等于该值的链内节点；返回 null = 继续。
    /// </summary>
    Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct);
}
