using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serenada.Core;

namespace Serenada.CallUI;

/// <summary>
/// Pre-built call flow component — orchestrates the full call lifecycle
/// from join through in-call to end. Can be used URL-first (pass a call URL)
/// or session-first (pass an already-created <see cref="SerenadaSession"/>).
///
/// Mirrors <c>SerenadaCallFlow</c> on Android (Compose) and iOS (SwiftUI).
/// </summary>
public sealed class SerenadaCallFlow : UserControl
{
    private CallViewModel? _vm;
    private CallScreen? _screen;

    public static readonly DependencyProperty ConfigProperty =
        DependencyProperty.Register(nameof(Config), typeof(SerenadaCallFlowConfig),
            typeof(SerenadaCallFlow), new PropertyMetadata(
                new SerenadaCallFlowConfig(),
                OnConfigChanged));

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(nameof(Session), typeof(SerenadaSession),
            typeof(SerenadaCallFlow), new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty OnEndCallProperty =
        DependencyProperty.Register(nameof(OnEndCall), typeof(Action),
            typeof(SerenadaCallFlow), new PropertyMetadata(null));

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

    public SerenadaCallFlow()
    {
        Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x0F, 0x17, 0x2A));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateContent();
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SerenadaCallFlow flow)
            flow.UpdateContent();
    }

    private static void OnConfigChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is SerenadaCallFlow flow && flow.IsLoaded)
            flow.UpdateContent();
    }

    private void UpdateContent()
    {
        if (Session == null)
        {
            _screen?.Dispose();
            _screen = null;
            _vm?.Dispose();
            _vm = null;
            Content = new TextBlock
            {
                Text = "No active session.",
                Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x94, 0xA3, 0xB8)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        _screen?.Dispose();
        _vm?.Dispose();
        _vm = new CallViewModel(Session);

        _screen = new CallScreen(
            _vm,
            Config,
            OnEndCall,
            OnDismiss);

        Content = _screen;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _screen?.Dispose();
        _screen = null;
        _vm?.Dispose();
        _vm = null;
    }
}
