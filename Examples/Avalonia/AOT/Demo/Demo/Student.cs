using VeloxDev.Core.AOT;

namespace Demo;

/// 假设AOT编译的程序在运行时需要从object反射获取Student的信息,例如一些基于反射的序列化工具集
/// 需要为类标记下述特性以保留反射上下文
/// 自动在当前根命名空间Demo生成一个 AOTReflection.Init() 方法
/// 需要在 Demo.Android 启动函数中执行一次
/// ⚠ 注意,示例 Demo 并非 NativeAOT，因此这个项目不使用 AOTReflection 也是没问题的
/// AOTReflection特性具备5个可选配置

[AOTReflection(Namespace: "Auto", Constructors: true, Properties: true, Methods: true, Fields: true)]
public class Student
{
    public string Name { get; set; } = "You can use reflect at AOT !";
}