namespace Serenada.Core.Models;

/// <summary>
/// Aggregated WebRTC call statistics (bitrate, packet loss, jitter, codec, resolution).
/// Updated live during the call.
/// </summary>
public sealed record CallStats
{
    /// <summary>Transport path: "direct" or "turn_relay".</summary>
    public string? TransportPath { get; init; }

    /// <summary>Round-trip time in milliseconds.</summary>
    public double? RttMs { get; init; }

    /// <summary>Estimated available outbound bandwidth in kbps.</summary>
    public double? AvailableOutgoingKbps { get; init; }

    // Audio
    /// <summary>Audio receive packet loss percentage.</summary>
    public double? AudioRxPacketLossPct { get; init; }

    /// <summary>Audio transmit packet loss percentage.</summary>
    public double? AudioTxPacketLossPct { get; init; }

    /// <summary>Audio jitter in milliseconds.</summary>
    public double? AudioJitterMs { get; init; }

    /// <summary>Audio playout delay in milliseconds.</summary>
    public double? AudioPlayoutDelayMs { get; init; }

    /// <summary>Concealed audio samples percentage.</summary>
    public double? AudioConcealedPct { get; init; }

    /// <summary>Audio receive bitrate in kbps.</summary>
    public double? AudioRxKbps { get; init; }

    /// <summary>Audio transmit bitrate in kbps.</summary>
    public double? AudioTxKbps { get; init; }

    // Video
    /// <summary>Video receive packet loss percentage.</summary>
    public double? VideoRxPacketLossPct { get; init; }

    /// <summary>Video transmit packet loss percentage.</summary>
    public double? VideoTxPacketLossPct { get; init; }

    /// <summary>Video receive bitrate in kbps.</summary>
    public double? VideoRxKbps { get; init; }

    /// <summary>Video transmit bitrate in kbps.</summary>
    public double? VideoTxKbps { get; init; }

    /// <summary>Video frames per second.</summary>
    public double? VideoFps { get; init; }

    /// <summary>Video resolution as "WxH" string.</summary>
    public string? VideoResolution { get; init; }

    /// <summary>Video freeze count in the last 60 seconds.</summary>
    public double? VideoFreezeCount60s { get; init; }

    /// <summary>Video freeze duration in ms over the last 60 seconds.</summary>
    public double? VideoFreezeDuration60s { get; init; }

    /// <summary>Video retransmission percentage.</summary>
    public double? VideoRetransmitPct { get; init; }

    /// <summary>Cumulative inbound-video frames decoded, summed across peer slots.</summary>
    public long? VideoFramesDecoded { get; init; }

    /// <summary>Cumulative inbound-video frames dropped, summed across peer slots.</summary>
    public long? VideoFramesDropped { get; init; }

    /// <summary>Cumulative inbound-audio packets lost, summed across peer slots.</summary>
    public long? AudioPacketsLost { get; init; }

    /// <summary>Cumulative inbound-audio packets received, summed across peer slots.</summary>
    public long? AudioPacketsReceived { get; init; }

    /// <summary>Wall-clock timestamp in ms when these stats were sampled.</summary>
    public long UpdatedAtMs { get; init; }
}
