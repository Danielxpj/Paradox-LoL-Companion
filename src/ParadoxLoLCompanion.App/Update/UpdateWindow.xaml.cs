using System.Windows;

namespace ParadoxLoLCompanion.App.Update;

/// <summary>Splash de progreso mostrado mientras se descarga y aplica una actualización.</summary>
public partial class UpdateWindow : Window
{
    private int _lastPercent = -1;

    public UpdateWindow(Version current, Version latest)
    {
        InitializeComponent();
        VersionLine.Text = $"v{Short(current)}  →  v{Short(latest)}";
    }

    private static string Short(Version v) =>
        $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    public void SetStatus(string text) => StatusText.Text = text;

    public void SetProgress(double fraction)
    {
        var percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        if (percent == _lastPercent)
            return;                       // evita inundar el dispatcher
        _lastPercent = percent;
        Bar.IsIndeterminate = false;
        Bar.Value = percent;
        StatusText.Text = $"Descargando… {percent}%";
    }

    public void SetIndeterminate(string text)
    {
        Bar.IsIndeterminate = true;
        StatusText.Text = text;
    }
}
