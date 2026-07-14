using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serenada.Core;

namespace Serenada.CallUI;

/// <summary>
/// Pre-built call flow component — orchestrates the full call lifecycle
/// from join through in-call to end. Can be used URL-first (pass a call URL)
/// or session-first (pass an already-created <see cref="SerenadaSession"/>).
///
/// Mirrors <c>SerenadaCallFlow</c> on Android (Compose) and iOS (SwiftUI).
/// </summary>
public sealed class SerenadaCallFlow : ContentControl
{
    // ── Dependency properties ─────────────────────────────────

    /// <summary>Configuration for the call flow.</summary>
    public static readonly DependencyProperty ConfigProperty =
        DependencyProperty.Register(nameof(Config), typeof(SerenadaCallFlowConfig),
            typeof(SerenadaCallFlow), new PropertyMetadata(new SerenadaCallFlowConfig()));

    /// <summary>The active call session. Set this to drive the UI.</summary>
    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(nameof(Session), typeof(SerenadaSession),
            typeof(SerenadaCallFlow), new PropertyMetadata(null, OnSessionChanged));

    /// <summary>Called when the user wants to end the call and leave.</summary>
    public static readonly DependencyProperty OnEndCallProperty =
        DependencyProperty.Register(nameof(OnEndCall), typeof(Action),
            typeof(SerenadaCallFlow), new PropertyMetadata(null));

    /// <summary>Called when the UI should be dismissed entirely.</summary>
    public static readonly DependencyProperty OnDismissProperty =
        DependencyProperty.Register(nameof(OnDismiss), typeof(Action),
            typeof(SerenadaCallFlow), new PropertyMetadata(null));

    public SerenadaCallFlowConfig Config
    {
        get => (SerenadaCallFlowConfig)GetValue(ConfigProperty);
        set => SetValue(ConfigProperty, value);
    }

    public SerenadaSession? Session
    {
        get => (SerenadaSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public Action? OnEndCall
    {
        get => (Action?)GetValue(OnEndCallProperty);
        set => SetValue(OnEndCallProperty, value);
    }

    public Action? OnDismiss
    {
        get => (Action?)GetValue(OnDismissProperty);
        set => SetValue(OnDismissProperty, value);
    }

    // ── Construction ──────────────────────────────────────────

    public SerenadaCallFlow()
    {
        DefaultStyleKey = typeof(SerenadaCallFlow);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateContent();
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SerenadaCallFlow flow)
            flow.UpdateContent();
    }

    private void UpdateContent()
    {
        if (Session == null)
        {
            Content = new TextBlock
            {
                Text = "No active session.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        var viewModel = new CallViewModel(Session);
        var screen = new CallScreen(viewModel)
        {
            FlowConfig = Config,
            EndCallAction = OnEndCall,
            DismissAction = OnDismiss,
        };
        Content = screen;
    }
}
