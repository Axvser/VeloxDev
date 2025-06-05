using VeloxDev.WPF.HotKey;

namespace VeloxDev.WPF.StructuralDesign.HotKey
{
    public interface IHotKeyComponent
    {
        public uint RecordedModifiers { get; set; }
        public uint RecordedKey { get; set; }
        public event EventHandler<HotKeyEventArgs> HotKeyInvoked;
        public void InvokeHotKey();
        public void CoverHotKey();
    }
}
