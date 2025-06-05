using VeloxDev.WPF.MessageFlow;
using VeloxDev.WPF.SourceGeneratorMark;

namespace WpfTest
{
    [SubscribeMessageFlows(["UpdateTime"])]
    public partial class UserViewModel
    {
        [Observable]
        private string name = string.Empty;
        [Observable]
        private DateTime time = DateTime.MinValue;

        private partial void FlowUpdateTime(object sender, MessageFlowArgs e)
        {
            if (e.Messages.Length > 0 && e.Messages[0] is DateTime newTime)
            {
                Time = newTime;
            }
        }
    }
}
