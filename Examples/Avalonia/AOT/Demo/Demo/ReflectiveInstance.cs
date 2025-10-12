using VeloxDev.Core.AOT;

namespace Demo
{
    /* 构造器、属性、方法、字段可以选择性的保留反射上下文 */
    /* 此外，Namespace: 可以自动/手动设定 AOTReflection 类的生成位置，你需要使用此类的 Init() 以保留反射 */
    /* 现在，我们可以在 Android 项目启动时执行 Init() 方法了 */
    [AOTReflection(Properties: true)] 
    public class ReflectiveInstance
    {
        /* 假设AOT时，我们仍然可能通过反射获取 Name 属性 */
        /* 当你使用基于反射的序列化工具集时，这样的操作是必须的 */
        public string Name { get; set; } = "AOT Reflective Property";
    }
}
