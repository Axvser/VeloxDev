namespace VeloxDev.WPF.SourceGeneratorMark
{
    /// <summary>
    /// ✨ ViewModel >> subscribe message flow by name
    /// </summary>
    /// <param name="flowNames">names of message flows</param>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class SubscribeMessageFlowsAttribute(string[] flowNames) : Attribute
    {
        public string[] FlowNames { get; internal set; } = flowNames;
    }
}
