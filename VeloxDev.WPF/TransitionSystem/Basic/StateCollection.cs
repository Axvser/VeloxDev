using System.Collections;
using System.Collections.Concurrent;

namespace VeloxDev.WPF.TransitionSystem.Basic
{
    public sealed class StateCollection : ICollection<State>
    {
        private ConcurrentDictionary<string, State> Nodes { get; set; } = new();
        private int _suffix = 0;
        public int BoardSuffix
        {
            get
            {
                if (_suffix < 50)
                {
                    _suffix++;
                    return _suffix;
                }
                if (_suffix == 50)
                {
                    _suffix = 0;
                    return _suffix;
                }
                return -1;
            }
        }
        public State this[string stateName]
        {
            get
            {
                if (!Nodes.TryGetValue(stateName, out var result)) return State.Empty;
                return result;
            }
        }

        public int Count => Nodes.Count;
        public bool IsReadOnly => false;
        public void Add(State item)
        {
            Nodes.AddOrUpdate(item.StateName, item, (key, oldValue) => item);
        }
        public void Clear()
        {
            Nodes.Clear();
        }
        public bool Contains(State item)
        {
            return Nodes.ContainsKey(item.StateName);
        }
        public void CopyTo(State[] array, int arrayIndex)
        {
            Nodes.Values.CopyTo(array, arrayIndex);
        }
        public bool Remove(State item)
        {
            return Nodes.TryRemove(item.StateName, out _);
        }
        public IEnumerator<State> GetEnumerator()
        {
            return Nodes.Values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return Nodes.Values.GetEnumerator();
        }
    }
}
