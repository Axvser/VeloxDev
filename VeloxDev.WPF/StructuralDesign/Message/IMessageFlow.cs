using VeloxDev.WPF.MessageFlow;

namespace VeloxDev.WPF.StructuralDesign.Message
{
    public interface IMessageFlow
    {
        public void SendMessageFlow(string name, params object?[] messages);
        public void RecieveMessageFlow(object sender, MessageFlowArgs e);
    }
}
