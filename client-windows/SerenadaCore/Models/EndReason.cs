namespace Serenada.Core.Models;

/// <summary>
/// Reason a call session ended.
/// </summary>
public abstract record EndReason
{
    /// <summary>The local user chose to leave.</summary>
    public sealed record LocalLeft : EndReason;

    /// <summary>The remote host or server ended the call.</summary>
    public sealed record RemoteEnded : EndReason;

    /// <summary>The session ended due to an error.</summary>
    /// <param name="CallError">The error that caused the session to end.</param>
    public sealed record Error(CallError CallError) : EndReason;
}
