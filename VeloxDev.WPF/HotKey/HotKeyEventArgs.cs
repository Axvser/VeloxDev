namespace VeloxDev.WPF.HotKey
{
    public class HotKeyEventArgs(uint modifiers, uint triggers) : EventArgs
    {
        public uint Modifiers => modifiers;
        public uint Keys => triggers;

        public IEnumerable<VirtualModifiers> GetModifierKeys()
        {
            foreach (VirtualModifiers flag in Enum.GetValues(typeof(VirtualModifiers)))
            {
                if ((Modifiers & (uint)flag) == (uint)flag && (uint)flag != 0x0000)
                {
                    yield return flag;
                }
            }
        }
        public VirtualKeys GetVirtualKey()
        {
            VirtualKeys key = 0x0000;
            foreach (VirtualKeys flag in Enum.GetValues(typeof(VirtualKeys)))
            {
                if ((Keys & (uint)flag) == (uint)flag && (uint)flag != 0x0000 && flag > key)
                {
                    key = flag;
                }
            }
            return key;
        }
    }
}