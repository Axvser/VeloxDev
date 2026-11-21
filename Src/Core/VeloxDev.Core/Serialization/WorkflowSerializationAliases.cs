using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Serialization;

// 改名前的 *DefaultViewModel 仍写在旧文档的 $type 里；注册别名让那些文档继续可读，写出去仍是新名。
internal static class WorkflowSerializationAliases
{
#pragma warning disable CA2255 // 别名必须在任何一次读取之前就位，库内没有可由宿主调用的初始化点。
    [ModuleInitializer]
    internal static void Register()
#pragma warning restore CA2255
    {
        VeloxJsonRegistry.RegisterAlias(
            typeof(TreeDefaultComponent), "VeloxDev.WorkflowSystem.TreeDefaultViewModel, VeloxDev.Core");
        VeloxJsonRegistry.RegisterAlias(
            typeof(NodeDefaultComponent), "VeloxDev.WorkflowSystem.NodeDefaultViewModel, VeloxDev.Core");
        VeloxJsonRegistry.RegisterAlias(
            typeof(SlotDefaultComponent), "VeloxDev.WorkflowSystem.SlotDefaultViewModel, VeloxDev.Core");
        VeloxJsonRegistry.RegisterAlias(
            typeof(LinkDefaultComponent), "VeloxDev.WorkflowSystem.LinkDefaultViewModel, VeloxDev.Core");
    }
}
