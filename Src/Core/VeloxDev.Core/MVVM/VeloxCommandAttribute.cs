using System.Windows.Input;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// 标记并自动构建一个 <see cref="IVeloxCommand"/> : <see cref="ICommand"/> 实例。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 被标记的方法必须满足以下签名之一（返回值必须为 <see cref="Task"/> 或 <see langword="true"/>）：
    /// </para>
    /// <list type="bullet">
    ///   <item><c>Task MethodName(object? parameter, CancellationToken ct)</c></item>
    ///   <item><c>Task MethodName(object? parameter)</c></item>
    ///   <item><c>Task MethodName(CancellationToken ct)</c></item>
    ///   <item><c>Task MethodName()</c></item>
    ///   <item><c>void MethodName(object? parameter)</c></item>
    ///   <item><c>void MethodName()</c></item>
    /// </list>
    /// <para>
    /// 框架会根据实际签名自动适配并封装为异步命令，支持取消、并发控制与完整生命周期事件。
    /// </para>
    /// </remarks>
    /// <param name="name">
    /// 命令的名称。若为 <c>"Auto"</c>（默认），则使用方法名自动生成命令属性名（如 <c>MyMethod</c> → <c>MyCommand</c>）。
    /// </param>
    /// <param name="canValidate">
    /// 是否启用命令可执行性验证。若为 <see langword="true"/>，需配套提供名为 <c>CanXxx</c> 的布尔属性或方法（如 <c>CanSave</c> 对应 <c>SaveCommand</c>）。
    /// </param>
    /// <param name="semaphore">
    /// 命令的最大并发执行数量（信号量容量）。默认为 1（串行执行），设为大于 1 可允许多个实例并行运行。
    /// 必须 ≥ 1。
    /// </param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VeloxCommandAttribute(
        string name = "Auto",
        bool canValidate = false,
        int semaphore = 1) : Attribute
    {
        public string Name { get; } = name;
        public bool CanValidate { get; } = canValidate;
        public int Semaphore { get; } = semaphore;
    }
}