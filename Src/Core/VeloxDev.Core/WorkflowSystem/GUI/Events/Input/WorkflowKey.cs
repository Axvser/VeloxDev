namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The keys a workflow surface can name. The spellings mirror <c>Avalonia.Input.Key</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a curated subset, not a copy of the platform enums: it holds the keys a graph editor actually binds —
/// editing and navigation keys, the function row, letters and digits. Anything else is reported as
/// <see cref="Unknown"/>, and <see cref="WorkflowKeyEventArgs.RawKeyCode"/> still carries the platform's own code,
/// so a host can pass an unmapped key through without Core enumerating every key there is.
/// </para>
/// <para>
/// <see cref="WorkflowKeyEventArgs.RawKeyCode"/> is platform-native and is therefore <b>not</b> comparable across
/// adapters — it exists for diagnostics and for a host that knows which platform it is on.
/// </para>
/// </remarks>
public enum WorkflowKey
{
    /// <summary>A key this enum does not name; read <see cref="WorkflowKeyEventArgs.RawKeyCode"/> instead.</summary>
    Unknown = 0,

    /// <summary>No key at all.</summary>
    None = 1,

    /// <summary>Cancel.</summary>
    Cancel = 2,

    /// <summary>Backspace.</summary>
    Back = 3,

    /// <summary>Tab.</summary>
    Tab = 4,

    /// <summary>Enter / Return.</summary>
    Enter = 5,

    /// <summary>Escape.</summary>
    Escape = 6,

    /// <summary>Space.</summary>
    Space = 7,

    /// <summary>Page Up.</summary>
    PageUp = 8,

    /// <summary>Page Down.</summary>
    PageDown = 9,

    /// <summary>End.</summary>
    End = 10,

    /// <summary>Home.</summary>
    Home = 11,

    /// <summary>Left arrow.</summary>
    Left = 12,

    /// <summary>Up arrow.</summary>
    Up = 13,

    /// <summary>Right arrow.</summary>
    Right = 14,

    /// <summary>Down arrow.</summary>
    Down = 15,

    /// <summary>Insert.</summary>
    Insert = 16,

    /// <summary>Delete — the one key Core itself acts on (see <see cref="WorkflowInput.AutoDelete"/>).</summary>
    Delete = 17,

    /// <summary>The letter A key.</summary>
    A = 30,
    /// <summary>The letter B key.</summary>
    B = 31,
    /// <summary>The letter C key.</summary>
    C = 32,
    /// <summary>The letter D key.</summary>
    D = 33,
    /// <summary>The letter E key.</summary>
    E = 34,
    /// <summary>The letter F key.</summary>
    F = 35,
    /// <summary>The letter G key.</summary>
    G = 36,
    /// <summary>The letter H key.</summary>
    H = 37,
    /// <summary>The letter I key.</summary>
    I = 38,
    /// <summary>The letter J key.</summary>
    J = 39,
    /// <summary>The letter K key.</summary>
    K = 40,
    /// <summary>The letter L key.</summary>
    L = 41,
    /// <summary>The letter M key.</summary>
    M = 42,
    /// <summary>The letter N key.</summary>
    N = 43,
    /// <summary>The letter O key.</summary>
    O = 44,
    /// <summary>The letter P key.</summary>
    P = 45,
    /// <summary>The letter Q key.</summary>
    Q = 46,
    /// <summary>The letter R key.</summary>
    R = 47,
    /// <summary>The letter S key.</summary>
    S = 48,
    /// <summary>The letter T key.</summary>
    T = 49,
    /// <summary>The letter U key.</summary>
    U = 50,
    /// <summary>The letter V key.</summary>
    V = 51,
    /// <summary>The letter W key.</summary>
    W = 52,
    /// <summary>The letter X key.</summary>
    X = 53,
    /// <summary>The letter Y key.</summary>
    Y = 54,
    /// <summary>The letter Z key.</summary>
    Z = 55,

    /// <summary>The digit 0 key.</summary>
    D0 = 60,
    /// <summary>The digit 1 key.</summary>
    D1 = 61,
    /// <summary>The digit 2 key.</summary>
    D2 = 62,
    /// <summary>The digit 3 key.</summary>
    D3 = 63,
    /// <summary>The digit 4 key.</summary>
    D4 = 64,
    /// <summary>The digit 5 key.</summary>
    D5 = 65,
    /// <summary>The digit 6 key.</summary>
    D6 = 66,
    /// <summary>The digit 7 key.</summary>
    D7 = 67,
    /// <summary>The digit 8 key.</summary>
    D8 = 68,
    /// <summary>The digit 9 key.</summary>
    D9 = 69,

    /// <summary>The F1 key.</summary>
    F1 = 80,
    /// <summary>The F2 key.</summary>
    F2 = 81,
    /// <summary>The F3 key.</summary>
    F3 = 82,
    /// <summary>The F4 key.</summary>
    F4 = 83,
    /// <summary>The F5 key.</summary>
    F5 = 84,
    /// <summary>The F6 key.</summary>
    F6 = 85,
    /// <summary>The F7 key.</summary>
    F7 = 86,
    /// <summary>The F8 key.</summary>
    F8 = 87,
    /// <summary>The F9 key.</summary>
    F9 = 88,
    /// <summary>The F10 key.</summary>
    F10 = 89,
    /// <summary>The F11 key.</summary>
    F11 = 90,
    /// <summary>The F12 key.</summary>
    F12 = 91,
}
