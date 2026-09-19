using VeloxDev.AI.Skills;
using VeloxDev.MVVM;

namespace VeloxDev.AI.Dashboard;

/// <summary>
/// A skill row of the dashboard. The switch is routed through <see cref="SkillScope.SetEnabled"/> rather
/// than written onto <see cref="SkillStatusViewModel.IsEnabled"/> directly: only the scope advances its
/// version, and a prompt provider caching on that version would otherwise keep injecting the skill.
/// </summary>
public sealed partial class SkillMemberViewModel : AgentMemberViewModel
{
    private readonly SkillScope _scope;

    [VeloxProperty] private string sourceText = string.Empty;
    [VeloxProperty] private int resourceCount = 0;

    internal SkillMemberViewModel(SkillScope scope, SkillStatusViewModel skill)
    {
        _scope = scope;
        Name = skill.Name;
        Description = skill.Description;
        IsEnabled = skill.IsEnabled;
        Sync(skill);
    }

    /// <summary>Whether this skill ships bundled resources that can be read on demand.</summary>
    public bool HasResources => ResourceCount > 0;

    partial void OnResourceCountChanged(int oldValue, int newValue)
        => OnPropertyChanged(nameof(HasResources));

    /// <summary>
    /// Re-reads the scope's own row. Called when the scope changed the switch itself — a refresh, or the
    /// Agent calling <c>UnloadSkill</c> behind the panel's back.
    /// </summary>
    internal void Sync(SkillStatusViewModel skill)
    {
        StateText = skill.StateText;
        SourceText = skill.Source == SkillSourceKind.Embedded ? "内嵌" : "磁盘";
        ResourceCount = skill.ResourceCount;
        // A skill that failed to load is switched on but unusable; the row says so instead of pretending.
        Note = skill.IsError ? skill.Error ?? "加载失败" : string.Empty;
        SetFromScope(skill.IsEnabled);
    }

    /// <inheritdoc />
    protected override void ApplyToScope(bool enabled) => _scope.SetEnabled(Name, enabled);
}
