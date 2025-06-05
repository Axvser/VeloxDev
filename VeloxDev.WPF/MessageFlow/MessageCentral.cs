using System.Collections.Concurrent;
using System.Diagnostics;
using VeloxDev.WPF.StructuralDesign.Message;

namespace VeloxDev.WPF.MessageFlow
{
    /// <summary>
    /// ✨ ViewModel >> Parameters for message flow
    /// <para>Args</para>
    /// <para>- <see cref="Name"/></para>
    /// <para>- <see cref="Messages"/></para>
    /// <para>- <see cref="FlowSequence"/> → access fragments in the current message flow that have been triggered </para>
    /// <para>- <see cref="Handled"/></para>
    /// </summary>
    public class MessageFlowArgs : EventArgs
    {
        public string Name { get; internal set; } = string.Empty;
        public object?[] Messages { get; internal set; } = [];
        public List<object> FlowSequence { get; internal set; } = [];
        public bool Handled { get; set; } = false;
    }

    /// <summary>
    /// ✨ ViewModel >> Manage message flow between ViewModels
    /// <para>Core</para>
    /// <para>- <see cref="PublishMessageFlow"/></para>
    /// <para>- <see cref="SubscribeMessageFlow"/></para>
    /// <para>- <see cref="UnsubscribeMessageFlow"/></para>
    /// <para>- <see cref="SendMessage"/></para>
    /// <para>- <see cref="ClearMessageFlows()"/></para>
    /// <para>- <see cref="ClearMessageFlows(string)"/></para>
    /// </summary>
    public static class MessageCentral
    {
        public static ConcurrentDictionary<string, List<WeakReference<IMessageFlow>>> MessageFlows { get; internal set; } = [];

        public static void ClearMessageFlows()
        {
            MessageFlows.Clear();
        }
        public static void ClearMessageFlows(string name)
        {
            MessageFlows.TryRemove(name, out _);
        }
        public static void PublishMessageFlow(string name)
        {
            if (!MessageFlows.TryGetValue(name, out _))
            {
                MessageFlows.TryAdd(name, []);
            }
        }
        public static void UnsubscribeMessageFlow(string name, IMessageFlow messageFlow)
        {
            if (MessageFlows.TryGetValue(name, out var values))
            {
                values.RemoveAll(x => x.TryGetTarget(out var target) && target == messageFlow);
            }
        }
        public static void SubscribeMessageFlow(string name, IMessageFlow target)
        {
            if (MessageFlows.TryGetValue(name, out var values))
            {
                values.Add(new WeakReference<IMessageFlow>(target));
            }
            else
            {
                MessageFlows.TryAdd(name, [new WeakReference<IMessageFlow>(target)]);
            }
        }
        public static void SendMessage(object sender, string name, params object?[] messages)
        {
            if (MessageFlows.TryGetValue(name, out var subscribers))
            {
                try
                {
                    if (subscribers.Count > 0) subscribers.RemoveAll(subscribers => !subscribers.TryGetTarget(out _));
                    var args = new MessageFlowArgs
                    {
                        Name = name,
                        Messages = messages
                    };
                    foreach (var subscriber in subscribers)
                    {
                        if (subscriber.TryGetTarget(out var target) && target is IMessageFlow mf)
                        {
                            mf.RecieveMessageFlow(sender, args);
                            args.FlowSequence.Add(mf);
                            if (args.Handled) break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"MessageCentral.SendMessage: {e.Message}");
                }
            }
        }
    }
}
