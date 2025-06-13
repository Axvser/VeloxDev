using System.Windows;
using System.Windows.Input;

namespace VeloxDev.WPF.HotKey
{
    public enum VirtualModifiers : uint
    {
        Alt = 0x0001,
        Ctrl = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        None = 0x0000
    }

    public enum VirtualKeys : uint
    {
        // 数字键 0-9
        D0 = 0x30,
        D1 = 0x31,
        D2 = 0x32,
        D3 = 0x33,
        D4 = 0x34,
        D5 = 0x35,
        D6 = 0x36,
        D7 = 0x37,
        D8 = 0x38,
        D9 = 0x39,

        // 字母 A-Z
        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,
        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,

        // 小键盘数字
        NumPad0 = 0x60,
        NumPad1 = 0x61,
        NumPad2 = 0x62,
        NumPad3 = 0x63,
        NumPad4 = 0x64,
        NumPad5 = 0x65,
        NumPad6 = 0x66,
        NumPad7 = 0x67,
        NumPad8 = 0x68,
        NumPad9 = 0x69,

        // 功能键
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,

        // 特殊键区
        Escape = 0x1B,
        Oem3 = 0xC0,       // ~键（美式键盘）
        Tab = 0x09,
        Capital = 0x14,     // Caps Lock

        // 符号键
        Plus = 0xBB,        // VK_OEM_PLUS
        Minus = 0xBD,       // VK_OEM_MINUS
        OemOpenBrackets = 0xDB,  // [ 键
        Oem6 = 0xDD,        // ] 键
        Oem1 = 0xBA,        // ;: 键
        OemQuotes = 0xDE,   // '" 键
        OemComma = 0xBC,    // , 键
        OemPeriod = 0xBE,   // . 键
        OemQuestion = 0xBF, // /? 键
        Back = 0x08,        // Backspace
        Oem5 = 0xDC,        // \\| 键
        Enter = 0x0D,       // Return

        // 系统键
        Snapshot = 0x2C,    // Print Screen
        Scroll = 0x91,      // Scroll Lock
        Pause = 0x13,
        Insert = 0x2D,
        Home = 0x24,
        PageUp = 0x21,
        Delete = 0x2E,
        End = 0x23,
        Next = 0x22,        // Page Down

        // 方向键
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28
    }

    /// <summary>
    /// KeyHelper class provides mappings between WPF Key enum and VirtualKeys/VirtualModifiers.
    /// <para>Core</para>
    /// <para>- <see cref="Test(VirtualModifiers)"/></para>
    /// <para>- <see cref="GetModifiers(uint)"/></para>
    /// <para>- <see cref="GetNames(ICollection{VirtualModifiers})"/></para>
    /// </summary>
    public static class HotKeyHelper
    {
        public static Dictionary<Key, VirtualModifiers> WinApiModifiersMapping { get; internal set; } = new()
        {
           { Key.LeftShift, VirtualModifiers.Shift },
           { Key.RightShift, VirtualModifiers.Shift },
           { Key.LeftCtrl, VirtualModifiers.Ctrl },
           { Key.RightCtrl, VirtualModifiers.Ctrl },
           { Key.LeftAlt, VirtualModifiers.Alt },
           { Key.RightAlt, VirtualModifiers.Alt },
           { Key.LWin, VirtualModifiers.Win },
           { Key.RWin, VirtualModifiers.Win },
        };

        public static Dictionary<Key, VirtualKeys> WinApiKeysMapping { get; internal set; } = new()
        {
           { Key.D0, VirtualKeys.D0 },
           { Key.D1, VirtualKeys.D1 },
           { Key.D2, VirtualKeys.D2 },
           { Key.D3, VirtualKeys.D3 },
           { Key.D4, VirtualKeys.D4 },
           { Key.D5, VirtualKeys.D5 },
           { Key.D6, VirtualKeys.D6 },
           { Key.D7, VirtualKeys.D7 },
           { Key.D8, VirtualKeys.D8 },
           { Key.D9, VirtualKeys.D9 },

           { Key.A, VirtualKeys.A },
           { Key.B, VirtualKeys.B },
           { Key.C, VirtualKeys.C },
           { Key.D, VirtualKeys.D },
           { Key.E, VirtualKeys.E },
           { Key.F, VirtualKeys.F },
           { Key.G, VirtualKeys.G },
           { Key.H, VirtualKeys.H },
           { Key.I, VirtualKeys.I },
           { Key.J, VirtualKeys.J },
           { Key.K, VirtualKeys.K },
           { Key.L, VirtualKeys.L },
           { Key.M, VirtualKeys.M },
           { Key.N, VirtualKeys.N },
           { Key.O, VirtualKeys.O },
           { Key.P, VirtualKeys.P },
           { Key.Q, VirtualKeys.Q },
           { Key.R, VirtualKeys.R },
           { Key.S, VirtualKeys.S },
           { Key.T, VirtualKeys.T },
           { Key.U, VirtualKeys.U },
           { Key.V, VirtualKeys.V },
           { Key.W, VirtualKeys.W },
           { Key.X, VirtualKeys.X },
           { Key.Y, VirtualKeys.Y },
           { Key.Z, VirtualKeys.Z },

           { Key.NumPad0, VirtualKeys.NumPad0 },
           { Key.NumPad1, VirtualKeys.NumPad1 },
           { Key.NumPad2, VirtualKeys.NumPad2 },
           { Key.NumPad3, VirtualKeys.NumPad3 },
           { Key.NumPad4, VirtualKeys.NumPad4 },
           { Key.NumPad5, VirtualKeys.NumPad5 },
           { Key.NumPad6, VirtualKeys.NumPad6 },
           { Key.NumPad7, VirtualKeys.NumPad7 },
           { Key.NumPad8, VirtualKeys.NumPad8 },
           { Key.NumPad9, VirtualKeys.NumPad9 },

           { Key.F1, VirtualKeys.F1 },
           { Key.F2, VirtualKeys.F2 },
           { Key.F3, VirtualKeys.F3 },
           { Key.F4, VirtualKeys.F4 },
           { Key.F5, VirtualKeys.F5 },
           { Key.F6, VirtualKeys.F6 },
           { Key.F7, VirtualKeys.F7 },
           { Key.F8, VirtualKeys.F8 },
           { Key.F9, VirtualKeys.F9 },
           { Key.F10, VirtualKeys.F10 },
           { Key.F11, VirtualKeys.F11 },
           { Key.F12, VirtualKeys.F12 },

            {Key.Escape,VirtualKeys.Escape },
            {Key.Oem3,VirtualKeys.Oem3 },
            {Key.Tab,VirtualKeys.Tab },
            {Key.Capital,VirtualKeys.Capital },

            {Key.OemPlus,VirtualKeys.Plus },
            {Key.OemMinus,VirtualKeys.Minus },
            {Key.OemOpenBrackets,VirtualKeys.OemOpenBrackets },
            {Key.Oem6,VirtualKeys.Oem6 },
            {Key.Oem1,VirtualKeys.Oem1 },
            {Key.OemQuotes,VirtualKeys.OemQuotes },
            {Key.OemComma,VirtualKeys.OemComma },
            {Key.OemPeriod,VirtualKeys.OemPeriod },
            {Key.OemQuestion,VirtualKeys.OemQuestion },
            {Key.Back,VirtualKeys.Back },
            {Key.Oem5,VirtualKeys.Oem5 },
            {Key.Return,VirtualKeys.Enter },

            {Key.Snapshot,VirtualKeys.Snapshot },
            {Key.Scroll,VirtualKeys.Scroll },
            {Key.Pause,VirtualKeys.Pause },
            {Key.Insert,VirtualKeys.Insert },
            {Key.Home,VirtualKeys.Home },
            {Key.PageUp,VirtualKeys.PageUp },
            {Key.Delete,VirtualKeys.Delete },
            {Key.End,VirtualKeys.End },
            {Key.Next,VirtualKeys.Next },

            {Key.Left,VirtualKeys.Left },
            {Key.Up,VirtualKeys.Up },
            {Key.Right,VirtualKeys.Right },
            {Key.Down,VirtualKeys.Down }
        };

        public static void Test(VirtualModifiers testModifiers)
        {
            foreach (var keyMapping in WinApiKeysMapping)
            {
                VirtualKeys virtualKey = keyMapping.Value;
#if NETFRAMEWORK
                GlobalHotKey.Register(VirtualModifiers.Ctrl, virtualKey, new EventHandler<HotKeyEventArgs>((instance, args) =>
                {
                    MessageBox.Show($"Pressed Ctrl + {virtualKey}");
                }));
#elif NET
                GlobalHotKey.Register(testModifiers, virtualKey, [(sender,e) =>
                {
                    MessageBox.Show($"Pressed Ctrl + {virtualKey}");
                }]);
#endif
            }
        }

        public static uint CombineModifiers(ICollection<VirtualModifiers> source)
        {
            return source.Any() ? (uint)source.Aggregate((current, next) => current | next) : 0x0000;
        }

        public static IEnumerable<VirtualModifiers> GetModifiers(uint source)
        {
            foreach (VirtualModifiers modifier in Enum.GetValues(typeof(VirtualModifiers)))
            {
                if ((source & (uint)modifier) == (uint)modifier && modifier != VirtualModifiers.None)
                {
                    yield return modifier;
                }
            }
        }

        public static IEnumerable<string> GetNames(ICollection<VirtualModifiers> source)
        {
            return source.Select(m => m.ToString());
        }
    }
}
