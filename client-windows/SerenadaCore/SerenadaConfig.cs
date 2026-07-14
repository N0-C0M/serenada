using Serenada.Core.Models;
using Serenada.Core.Signaling;

namespace Serenada.Core;

/// <summary>
/// Available signaling transport types.
/// </summary>
public enum SerenadaTransport
{
    /// <summary>WebSocket transport.</summary>
    Ws,

    /// <summary>Server-Sent Events transport.</summary>
    Sse,
}

/// <summary>
/// Default preference order for camera modes when
/// <see cref="SerenadaConfig.CameraModes"/> is <c>null</c>.
/// </summary>
public static class DefaultCameraModes
{
    /// <summary>[Selfie, World, Composite] — the standard default order.</summary>
    public static readonly IReadOnlyList<CameraMode> Value =
        [CameraMode.Selfie, CameraMode.World, CameraMode.Composite];
}

/// <summary>
/// Configuration for the Serenada SDK.
/// Mirrors <c>SerenadaConfig</c> on Android, iOS, and Web.
/// </summary>
public sealed record SerenadaConfig
{
    /// <summary>
    /// Server host or origin (e.g. <c>"serenada.app"</c> or <c>"http://localhost:8080"</c>).
    /// Provide exactly one of <see cref="ServerHost"/> or <see cref="SignalingProvider"/>.
    /// </summary>
    public string? ServerHost { get; init; }

    /// <summary>
    /// Custom signaling provider. Provide exactly one of <see cref="ServerHost"/>
    /// or <see cref="SignalingProvider"/>.
    /// </summary>
    public ISignalingProvider? SignalingProvider { get; init; }

    /// <summary>Whether audio is enabled when joining a call. Defaults to <c>true</c>.</summary>
    public bool DefaultAudioEnabled { get; init; } = true;

    /// <summary>Whether video is enabled when joining a call. Defaults to <c>true</c>.</summary>
    public bool DefaultVideoEnabled { get; init; } = true;

    /// <summary>
    /// Whether this call can negotiate any video media. Set <c>false</c> for strict
    /// audio-only calls: camera capture, screen sharing, and remote video are all
    /// disabled. Defaults to <c>true</c>.
    /// </summary>
    public bool VideoMediaEnabled { get; init; } = true;

    /// <summary>
    /// Static build capability: whether this client can negotiate, send, receive,
    /// and render an independent content (screen share) video stream separate from
    /// the camera. Advertised at <c>join</c> as <c>capabilities.independentContentVideo</c>.
    /// Immutable per session. Defaults to <c>false</c>.
    /// </summary>
    public bool EnableIndependentContentVideo { get; init; }

    /// <summary>
    /// Camera modes available in the call UI, in preference order. The first entry
    /// is the initial mode. When only one mode is listed the flip-camera control is
    /// hidden; an empty array disables camera capture. Defaults to
    /// <c>[Selfie, World, Composite]</c>.
    /// </summary>
    public IReadOnlyList<CameraMode>? CameraModes { get; init; }

    /// <summary>
    /// When <c>true</c>, defer the initial-negotiation offer-timeout/ICE-restart while
    /// the host peer awaits its first answer. Use for calls whose answer is gated on
    /// a remote action that may take longer than the offer timeout (e.g. PSTN pickup).
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool DeferInitialAnswer { get; init; }

    /// <summary>
    /// Preferred signaling transports in priority order. Defaults to <c>[Ws, Sse]</c>.
    /// </summary>
    public IReadOnlyList<SerenadaTransport> Transports { get; init; } =
        [SerenadaTransport.Ws, SerenadaTransport.Sse];

    /// <summary>Custom logger for SDK diagnostic output. If <c>null</c>, logs are discarded.</summary>
    public ISerenadaLogger? Logger { get; init; }

    // ── Internal validation ────────────────────────────────────

    /// <summary>
    /// Validates that exactly one of <see cref="ServerHost"/> or
    /// <see cref="SignalingProvider"/> is configured.
    /// </summary>
    internal void Validate()
    {
        if ((ServerHost == null) == (SignalingProvider == null))
        {
            throw new ArgumentException(
                "Provide exactly one of ServerHost or SignalingProvider.");
        }

        if (SignalingProvider is { Version: not SignalingProviderBase.SupportedVersion })
        {
            throw new ArgumentException(
                $"Unsupported signalingProvider version: {SignalingProvider.Version}. " +
                $"Expected {SignalingProviderBase.SupportedVersion}.");
        }
    }
}
