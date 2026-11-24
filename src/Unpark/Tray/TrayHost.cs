using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Threading;
using Unpark.Core.Dynamic;
using Unpark.Core.Power;
using Unpark.Services;
using Forms = System.Windows.Forms;

namespace Unpark.Tray;

/// <summary>
/// Mode dynamique : icône dans la zone de notification (lancée avec « Unpark.exe --tray »).
/// Toutes les 5 secondes : Unpark Performance si l'utilisateur est actif, Équilibré après X minutes d'inactivité.
/// </summary>
public sealed class TrayHost : IDisposable
{
    public const string MutexName = @"Local\Unpark.DynamicMode";
    public const string QuitEventName = @"Local\Unpark.DynamicMode.Quit";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly AppServices _s = AppServices.Current;
    private readonly DynamicModeController _mode;
    private readonly DynamicSettingsStore _store;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _status, _pause;
    private readonly DispatcherTimer _timer;
    private readonly EventWaitHandle _quit;
    private readonly Icon _performanceIcon = MakeIcon(Color.FromArgb(61, 220, 151));
    private readonly Icon _economyIcon = MakeIcon(Color.FromArgb(154, 163, 181));
    private readonly Icon _pausedIcon = MakeIcon(Color.FromArgb(245, 181, 68));
    private DateTime _settingsStamp = DateTime.MinValue;
    private DynamicState _lastState = DynamicState.Disabled;

    public TrayHost()
    {
        _store = _s.DynamicStore;
        _mode = new DynamicModeController(_s.Power, _s.Commands, new WindowsActivityProbe(), _s.Log);

        _status = new Forms.ToolStripMenuItem("Mode dynamique") { Enabled = false };
        _pause = new Forms.ToolStripMenuItem("Mettre en pause", null, (_, _) => TogglePause());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ouvrir Unpark", null, (_, _) => OpenMainApp());
        menu.Items.Add(_pause);
        menu.Items.Add("Forcer Unpark Performance", null, (_, _) => Force(PowerSettings.Unpark, PowerSettings.UnparkName));
        menu.Items.Add("Forcer Équilibré", null, (_, _) => Force(PowerSettings.Balanced, "Équilibré"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter le mode dynamique", null, (_, _) => Quit());

        _icon = new Forms.NotifyIcon { Icon = _economyIcon, Text = "Unpark — mode dynamique", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenMainApp();

        // Arrêt demandé depuis l'appli (bouton « Arrêter le mode dynamique »).
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var dispatcher = Dispatcher.CurrentDispatcher;
        ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => dispatcher.BeginInvoke(Quit), null, Timeout.Infinite, executeOnlyOnce: true);

        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _s.Log.Info("Mode dynamique : démarré dans la zone de notification");
        Tick();
    }

    private void Tick()
    {
        try
        {
            if (_store.LastWriteUtc != _settingsStamp)
            {
                _settingsStamp = _store.LastWriteUtc;
                _mode.Settings = _store.Load();
            }
            var state = _mode.Tick();
            Update(state);
        }
        catch (Exception e)
        {
            _s.Log.Error("Mode dynamique : erreur", e);
        }
    }

    private void Update(DynamicState state)
    {
        _icon.Icon = state switch
        {
            DynamicState.Performance => _performanceIcon,
            DynamicState.Paused or DynamicState.NotInstalled => _pausedIcon,
            _ => _economyIcon,
        };
        var text = $"Unpark — {_mode.StatusText}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
        _status.Text = _mode.StatusText;
        _pause.Text = state == DynamicState.Paused ? "Reprendre le mode automatique" : "Mettre en pause";

        if (state != _lastState && state == DynamicState.Paused && _mode.PauseReason?.StartsWith("plan changé") == true)
            _icon.ShowBalloonTip(6000, "Unpark — mode dynamique en pause", $"{_mode.PauseReason}. Clic droit sur l'icône → « Reprendre » pour le relancer.", Forms.ToolTipIcon.Info);
        if (state != _lastState && state == DynamicState.NotInstalled)
            _icon.ShowBalloonTip(6000, "Unpark — mode dynamique", "Le plan Unpark Performance n'est pas installé : ouvre Unpark → Plan d'alimentation.", Forms.ToolTipIcon.Warning);
        _lastState = state;
    }

    private void TogglePause()
    {
        if (_mode.State == DynamicState.Paused) _mode.Resume();
        else _mode.Pause("demandée depuis le menu");
        Tick();
    }

    private void Force(Guid scheme, string name)
    {
        _mode.Force(scheme, name);
        Update(_mode.State);
    }

    private void OpenMainApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            _s.Log.Error("Ouverture d'Unpark depuis la zone de notification impossible", e);
        }
    }

    private void Quit()
    {
        _s.Log.Info("Mode dynamique : arrêté");
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _quit.Dispose();
    }

    /// <summary>Petite icône ronde avec un « U » de la couleur de l'état (dessinée, pas de fichier .ico).</summary>
    private static Icon MakeIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(21, 24, 33));
            g.FillEllipse(background, 0, 0, 31, 31);
            using var ring = new Pen(color, 2.5f);
            g.DrawEllipse(ring, 1.5f, 1.5f, 28, 28);
            using var font = new Font("Segoe UI", 17, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("U", font, brush, new RectangleF(0, 0, 32, 31), format);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>Le mode dynamique tourne-t-il déjà ?</summary>
    public static bool IsRunning() => Mutex.TryOpenExisting(MutexName, out var m) && DisposeAndTrue(m);

    private static bool DisposeAndTrue(Mutex m)
    {
        m.Dispose();
        return true;
    }

    /// <summary>Demande au mode dynamique de s'arrêter.</summary>
    public static bool RequestQuit()
    {
        if (!EventWaitHandle.TryOpenExisting(QuitEventName, out var quit)) return false;
        using (quit) quit.Set();
        return true;
    }
}
