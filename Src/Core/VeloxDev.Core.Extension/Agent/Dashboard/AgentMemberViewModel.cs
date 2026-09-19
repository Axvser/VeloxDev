using System;
using VeloxDev.MVVM;

namespace VeloxDev.AI.Dashboard;

/// <summary>
/// One switchable row of <see cref="AgentDashboardViewModel"/> — a tool, an MCP server, an MCP server's
/// tool, or a skill.
/// <para>
/// The row is a <i>projection</i>, never a second source of truth: <see cref="IsEnabled"/> writes through
/// <see cref="ApplyToScope"/> into whichever scope owns the member, and changes that originate in the
/// scope come back through <see cref="SetFromScope"/>. That matters for the members the scope already
/// publishes a bindable flag for (skills, MCP servers) — setting those directly would leave the scope's
/// version behind, and a prompt provider caching on it would keep serving text the host thought it had
/// switched off.
/// </para>
/// </summary>
public abstract partial class AgentMemberViewModel
{
    [VeloxProperty] private string name = string.Empty;
    [VeloxProperty] private string description = string.Empty;
    [VeloxProperty] private bool isEnabled = true;
    [VeloxProperty] private string stateText = string.Empty;
    [VeloxProperty] private string note = string.Empty;
    [VeloxProperty] private int callCount = 0;
    [VeloxProperty] private string lastCalledText = string.Empty;

    /// <summary>
    /// Set while <see cref="IsEnabled"/> is being written from the scope, so the setter's own hook does not
    /// push the value straight back. One flag is enough: the write is synchronous and the scope swallows
    /// same-value writes, so there is no window for a second hop.
    /// </summary>
    private bool _applying;

    /// <summary>Switched off by the host.</summary>
    public bool IsOff => !IsEnabled;

    /// <summary>
    /// Switched on <b>and</b> actually usable — a member can be on and still blocked by host policy, which
    /// is what <see cref="Note"/> spells out.
    /// </summary>
    public bool IsReachable => IsEnabled && Note.Length == 0;

    /// <summary>Whether this member has been called at least once this session.</summary>
    public bool HasBeenCalled => CallCount > 0;

    /// <summary>Call count as display text.</summary>
    public string ActivityText => CallCount == 0 ? "未调用" : $"{CallCount} 次";

    partial void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(IsOff));
        OnPropertyChanged(nameof(IsReachable));
        if (_applying) return;
        ApplyToScope(newValue);
    }

    partial void OnNoteChanged(string oldValue, string newValue)
        => OnPropertyChanged(nameof(IsReachable));

    partial void OnCallCountChanged(int oldValue, int newValue)
    {
        OnPropertyChanged(nameof(ActivityText));
        OnPropertyChanged(nameof(HasBeenCalled));
    }

    /// <summary>Pushes a switch the host flipped into the owning scope.</summary>
    protected abstract void ApplyToScope(bool enabled);

    /// <summary>
    /// Adopts a switch that came from the scope. Called on the dashboard's thread; does not echo back.
    /// </summary>
    internal void SetFromScope(bool enabled)
    {
        if (IsEnabled == enabled) return;
        _applying = true;
        try { IsEnabled = enabled; }
        finally { _applying = false; }
    }

    /// <summary>Records one call. The dashboard calls this on the thread it is bound to.</summary>
    internal void RecordCall(DateTime at)
    {
        CallCount++;
        LastCalledText = at.ToString("HH:mm:ss");
    }
}
