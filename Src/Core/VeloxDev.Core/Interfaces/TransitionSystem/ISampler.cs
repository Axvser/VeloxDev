namespace VeloxDev.TransitionSystem;

/// <summary>
/// 采样处理器（无状态、注册表单例）：标准更新函数，按 [0,1] 时间直接计算并更新属性，不重建中间对象。
/// <para>
/// t&lt;=0 → 写精确 start；t&gt;=1 → 写精确 end；0&lt;t&lt;1 → 值类型算好即赋，引用类型原地修改 start 现有实例（不 new）。
/// </para>
/// </summary>
public interface ISampler
{
    void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t);
}
