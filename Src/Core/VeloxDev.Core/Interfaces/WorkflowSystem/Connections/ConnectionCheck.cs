namespace VeloxDev.WorkflowSystem;

/// <summary>Why a connection check did not pass.</summary>
public enum ConnectionRejectReason
{
    /// <summary>The check passed.</summary>
    None = 0,

    /// <summary>The sender's channel does not allow sending.</summary>
    SenderCannotSend = 1,

    /// <summary>The receiver's channel does not allow receiving.</summary>
    ReceiverCannotReceive = 2,

    /// <summary>Both ports belong to the same node.</summary>
    SameNode = 3,

    /// <summary><see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/> refused the pair.</summary>
    RejectedByRule = 4,

    /// <summary>A <see cref="WorkflowInput.PreviewValidating"/> subscriber refused the candidate for this preview only.</summary>
    RejectedByHost = 5,
}

/// <summary>
/// The result of checking whether a connection could be built: a verdict plus the reason it was refused.
/// </summary>
/// <remarks>
/// <para>
/// The check is the framework's own rule set — direction, same-node, and
/// <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/> — and it has no side effects. The preview path
/// uses the very same check, so a green rubber band and a completed connection cannot disagree about those rules.
/// Channel capacity is not a rejection: completing a connection cleans up existing connections according to the
/// channel, so a passing check means "acceptable, possibly after that cleanup".
/// </para>
/// <para>
/// It deliberately excludes <see cref="IWorkflowTreeEvents.Connecting"/>: that event is a per-attempt veto raised
/// only when the pointer is released, and it may depend on state that a preview should not consume. A host that
/// wants a preview-only restriction subscribes to <see cref="WorkflowInput.PreviewValidating"/> instead.
/// </para>
/// </remarks>
public readonly struct ConnectionCheck : IEquatable<ConnectionCheck>
{
    private ConnectionCheck(bool isValid, ConnectionRejectReason reason, string? message)
    {
        IsValid = isValid;
        Reason = reason;
        Message = message;
    }

    /// <summary>A passing check.</summary>
    public static ConnectionCheck Valid => new(true, ConnectionRejectReason.None, null);

    /// <summary>Whether the framework's rules would accept the connection.</summary>
    public bool IsValid { get; }

    /// <summary>Why the check failed, or <see cref="ConnectionRejectReason.None"/> when it passed.</summary>
    public ConnectionRejectReason Reason { get; }

    /// <summary>An optional explanation supplied by the host; the framework's own reasons leave it <see langword="null"/>.</summary>
    public string? Message { get; }

    /// <summary>Creates a failing check with <paramref name="reason"/>.</summary>
    /// <param name="reason">The reason the pair was refused.</param>
    /// <param name="message">An optional explanation for the host's own UI.</param>
    /// <returns>A failing check.</returns>
    public static ConnectionCheck Rejected(ConnectionRejectReason reason, string? message = null)
        => new(false, reason, message);

    /// <inheritdoc />
    public bool Equals(ConnectionCheck other)
        => IsValid == other.IsValid
           && Reason == other.Reason
           && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConnectionCheck other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = IsValid ? 17 : 0;
            hash = (hash * 31) + (int)Reason;
            return (hash * 31) + (Message?.GetHashCode() ?? 0);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsValid) return "Valid";
        return Message is null ? $"Rejected({Reason})" : $"Rejected({Reason}: {Message})";
    }
}
