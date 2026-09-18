using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace SerenadaApp;

internal sealed class FloatingBubbleWindow : Window
{
    private readonly Action _activateMainWindow;
    private bool _configured;

    public FloatingBubbleWindow(Action activateMainWindow)
    {
        _activateMainWindow = activateMainWindow;
        Title = "Serenada";
        ExtendsContentIntoTitleBar = true;

        var button = new Button
        {
            Width = 64,
            Height = 64,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
            BorderThickness = new Thickness(0),
            Content = new TextBlock
            {
                Text = "S",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTipService.SetToolTip(button, "Open Serenada");
        button.Click += (_, _) => _activateMainWindow();

        Content = new Grid
        {
            Background = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x00, 0x00, 0x00, 0x00)),
            Children = { button },
        };

        Activated += (_, _) => ConfigureWindow();
    }

    public void ShowBubble()
    {
        Activate();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        if (_configured)
            return;

        WindowChrome.PreferRoundedCorners(this);
        AppWindow.Resize(new SizeInt32(72, 72));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var display = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary);
        if (display != null)
        {
            var work = display.WorkArea;
            AppWindow.Move(new PointInt32(
                work.X + work.Width - 92,
                work.Y + 88));
        }

        _configured = true;
    }
}
