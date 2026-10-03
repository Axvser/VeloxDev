using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A key event translated out of a GUI framework, carrying one of the <see cref="InputKey"/> values the link
/// interaction acts on.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "适配器翻译出来的一次按键事件（只包含连线交互用得到的键）")]
[AgentContext(AgentLanguages.English, "One key event translated out of a GUI framework (only the keys link interaction uses)")]
public sealed record KeyEvent
{
    /// <summary>Creates a key event.</summary>
    /// <param name="key">The key that was pressed.</param>
    public KeyEvent(InputKey key) => Key = key;

    /// <summary>The key that was pressed.</summary>
    public InputKey Key { get; init; }
}
