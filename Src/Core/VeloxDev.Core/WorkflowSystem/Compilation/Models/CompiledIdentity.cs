namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Composite identity for a compiled item: a per-result UID plus a per-result sequential ID.
/// Each <see cref="CompilationResult"/> produced by a single <see cref="WorkflowCompiler.Compile"/>
/// call gets its own UID, so in Omni mode (multiple results) the sequential IDs restart at 0
/// in every result without ever colliding — <c>(Uid, OrderId)</c> is globally unique across
/// all results of a Compile call.
/// </summary>
public readonly struct CompiledIdentity : IEquatable<CompiledIdentity>
{
    /// <summary>
    /// Unique ID for the item's <see cref="CompilationResult"/>. Every item in the same result
    /// shares it; items in different results (e.g. Omni mode) have distinct UIDs.
    /// </summary>
    public Guid Uid { get; }

    /// <summary>0-based sequential ID of the item within its own result (restarts per result).</summary>
    public int OrderId { get; }

    public CompiledIdentity(Guid uid, int orderId)
    {
        Uid = uid;
        OrderId = orderId;
    }

    /// <inheritdoc />
    public bool Equals(CompiledIdentity other) => Uid == other.Uid && OrderId == other.OrderId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CompiledIdentity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Uid, OrderId);

    public static bool operator ==(CompiledIdentity left, CompiledIdentity right) => left.Equals(right);
    public static bool operator !=(CompiledIdentity left, CompiledIdentity right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"{Uid}:{OrderId}";
}
