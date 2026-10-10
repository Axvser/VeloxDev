namespace VeloxDev.AI.Safety;

/// <summary>What a permission check decided about one call.</summary>
public enum PermissionDecision
{
    /// <summary>Run it without asking.</summary>
    Allow = 0,

    /// <summary>Put it to the user. With no handler registered, asking answers as a refusal.</summary>
    Ask = 1,

    /// <summary>Do not run it, and do not offer it.</summary>
    Deny = 2,
}
