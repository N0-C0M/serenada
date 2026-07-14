namespace Serenada.CallUI;

/// <summary>
/// Configuration for the <see cref="SerenadaCallFlow"/> call UI component.
/// Mirrors <c>SerenadaCallFlowConfig</c> on Android and iOS.
/// </summary>
public sealed record SerenadaCallFlowConfig
{
    /// <summary>Whether screen sharing controls are shown. Defaults to <c>true</c>.</summary>
    public bool ScreenSharingEnabled { get; init; } = true;

    /// <summary>Whether invite/copy-link controls are shown. Defaults to <c>true</c>.</summary>
    public bool InviteControlsEnabled { get; init; } = true;

    /// <summary>Whether the end-call button is shown. Defaults to <c>true</c>.</summary>
    public bool EndCallEnabled { get; init; } = true;

    /// <summary>Title text shown at the top of the call screen. Defaults to <c>"Serenada"</c>.</summary>
    public string Title { get; init; } = "Serenada";

    /// <summary>Accent color for buttons and indicators. Defaults to <c>"#2563EB"</c> (blue).</summary>
    public string AccentColor { get; init; } = "#2563EB";
}
