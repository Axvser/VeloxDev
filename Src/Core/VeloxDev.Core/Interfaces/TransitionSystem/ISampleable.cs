using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem;

/// <summary>
/// 可采样定义（类型级）：用户自定义类型实现它即可直接用于动画，无需注册采样器。注册表
/// <see cref="Abstractions.InterpolatorCore.NativeInterpolators"/> 也按 <see cref="Type"/> 存储它。
/// </summary>
public interface ISampleable
{
    /// <summary>
    /// 归一化 start/end/options，返回该类型的无状态采样处理器。解释器创建并知晓 FrameState 时，对每个要动画的
    /// 属性成员调用一次；start 为 target 上的现值（引用类型即现有实例，供原地修改），end 为目标值。
    /// </summary>
    ISampler Normalize(object? start, object? end, object? options);
}
