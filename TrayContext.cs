using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EnergyView;

internal sealed class TrayContext : ApplicationContext
{
    private readonly PowerSensors sensors = new();
    private readonly HardwarePowerSource hardware = new();
    private readonly EnergyHistory energy = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnergyView", "energy-history.json"), TimeZoneInfo.Local);
    private readonly PowerPanel panel = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon = CreateTrayIcon();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem driverAction = new();
    private readonly ToolStripMenuItem startupAction = new();
    private readonly ToolStripMenuItem updateAction = new("Verificar atualizações");
    private readonly ReleaseUpdater updater = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 5000 };
    private readonly bool installed = AppInstallation.IsInstalled;
    private readonly bool elevated;
    private AppRelease? availableUpdate;
    private bool updating;
    private bool startsWithWindows;
    private bool refreshing;
    private bool exiting;
    private bool installingDriver;
    private bool driverInstalled;
    private IReadOnlyList<HardwarePowerSensor> hardwareReadings = [];

    public TrayContext(bool driverInstalled, bool startHidden, bool elevated)
    {
        this.driverInstalled = driverInstalled;
        this.elevated = elevated;
        MainForm = panel;
        var toggle = new ToolStripMenuItem("Ocultar painel", null, (_, _) => TogglePanel());
        menu.Items.Add(toggle);
        driverAction.Click += async (_, _) => await InstallDriverAsync();
        startupAction.Click += (_, _) => ToggleStartup();
        updateAction.Click += async (_, _) => await HandleUpdateAsync();
        menu.Items.Add(driverAction);
        menu.Items.Add(startupAction);
        menu.Items.Add(updateAction);
        menu.Items.Add("Encerrar Energy View", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => toggle.Text = panel.Visible ? "Ocultar painel" : "Abrir painel";

        tray = new NotifyIcon
        {
            Icon = icon,
            Text = "Energy View",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                TogglePanel();
        };
        tray.BalloonTipClicked += async (_, _) => await HandleUpdateAsync();

        panel.Icon = icon;
        panel.FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                panel.Hide();
            }
        };

        panel.InstallDriverRequested += async (_, _) => await InstallDriverAsync();
        panel.Shown += async (_, _) => await RefreshReadingAsync();
        panel.ToggleStartupRequested += (_, _) => ToggleStartup();

        timer.Tick += async (_, _) => await RefreshReadingAsync();
        UpdateDisplay();
        if (!startHidden)
            ShowPanel();
        UpdateDriverAction();
        ReadStartupState();
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = (int)TimeSpan.FromHours(4).TotalMilliseconds;
            await CheckForUpdatesAsync(interactive: false);
        };
        if (installed)
            updateTimer.Start();
        timer.Start();
    }

    private void ReadStartupState()
    {
        try
        {
            startsWithWindows = installed && WindowsStartup.IsEnabled();
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Windows startup status unavailable: {0}", error);
        }
        UpdateStartupControls();
    }

    private void UpdateStartupControls()
    {
        startupAction.Text = startsWithWindows ? "Desativar início com Windows" : "Iniciar com Windows";
        panel.SetStartupEnabled(startsWithWindows);
    }

    private void ToggleStartup()
    {
        try
        {
            WindowsStartup.SetEnabled(!startsWithWindows, elevated);
            startsWithWindows = !startsWithWindows;
            UpdateStartupControls();
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(panel.Visible ? panel : null, error.Message, "Iniciar com Windows",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (updating || exiting)
            return;

        updating = true;
        try
        {
            AppRelease? release = await updater.FindAsync(lifetime.Token);
            if (exiting)
                return;
            bool newlyAvailable = release.HasValue && release.Value.Version != availableUpdate?.Version;
            availableUpdate = release;
            updateAction.Text = release is AppRelease value
                ? $"Instalar atualização {value.Version}" : "Verificar atualizações";
            if (newlyAvailable && !interactive)
                tray.ShowBalloonTip(6000, "Atualização do Energy View",
                    $"A versão {availableUpdate!.Value.Version} está disponível. Clique para instalar.", ToolTipIcon.Info);
            else if (interactive && !release.HasValue)
                MessageBox.Show(panel.Visible ? panel : null, "O Energy View está atualizado.",
                    "Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or
            System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Trace.TraceWarning("Release check failed: {0}", error);
            if (interactive && !exiting)
                MessageBox.Show(panel.Visible ? panel : null, "Não foi possível consultar as releases do GitHub.",
                    "Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            updating = false;
        }
    }

    private async Task HandleUpdateAsync()
    {
        if (updating || exiting)
            return;
        if (!installed)
        {
            try
            {
                using Process? browser = Process.Start(new ProcessStartInfo(ReleaseUpdater.ReleasesPage)
                    { UseShellExecute = true });
            }
            catch (Win32Exception error)
            {
                MessageBox.Show(panel.Visible ? panel : null, error.Message, "Atualizações",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        if (!availableUpdate.HasValue)
            await CheckForUpdatesAsync(interactive: true);
        if (availableUpdate is not AppRelease release || updating || exiting ||
            MessageBox.Show(panel.Visible ? panel : null, $"Instalar a versão {release.Version} do Energy View?",
                "Atualização disponível", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        updating = true;
        updateAction.Text = "Baixando atualização…";
        try
        {
            string path = await updater.DownloadAsync(release, lifetime.Token);
            if (exiting)
                return;
            using Process setup = Process.Start(new ProcessStartInfo(path,
                "/SILENT /SP- /NORESTART /UPDATE=1")
                { UseShellExecute = true, WorkingDirectory = AppInstallation.DirectoryPath })
                ?? throw new InvalidOperationException("Não foi possível iniciar o instalador.");
            ExitThread();
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException
            or InvalidDataException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            if (!exiting)
                MessageBox.Show(panel.Visible ? panel : null, error.Message,
                    "Falha na atualização", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            updating = false;
            if (!exiting)
                updateAction.Text = $"Instalar atualização {release.Version}";
        }
    }

    private async Task RefreshReadingAsync()
    {
        if (refreshing)
            return;

        refreshing = true;
        try
        {
            hardwareReadings = await Task.Run(hardware.Read);
            if (!exiting)
                UpdateDisplay(recordEnergy: true);
        }
        finally
        {
            refreshing = false;
        }
    }

    private void UpdateDisplay(bool recordEnergy = false)
    {
        PowerReading reading = sensors.Read();
        double? cpu = PowerBreakdown.ReadCpuWatts(hardwareReadings);
        LivePower live = LivePower.FromComponents(cpu, reading.GpuWatts);
        panel.UpdateReading(live, cpu, reading.GpuWatts, hardwareReadings, driverInstalled);
        DateTimeOffset now = DateTimeOffset.Now;
        if (recordEnergy)
        {
            energy.Record(now, reading.BatteryWatts, cpu + reading.GpuWatts);
            energy.Save();
        }
        panel.UpdateEnergy(energy.Summarize(now), energy.SaveError);
        string tooltip = live.Watts is double watts
            ? $"Energy View · {live.Label} · {watts:N1} W"
            : "Energy View · Sensores sem leitura";
        if (tray.Text != tooltip)
            tray.Text = tooltip;
    }

    private void UpdateDriverAction()
    {
        string? caption = installingDriver ? "Instalando…" : !driverInstalled ? "Instalar PawnIO" : null;
        driverAction.Available = caption is not null;
        driverAction.Enabled = !installingDriver;
        driverAction.Text = caption ?? "";
        panel.SetDriverAction(caption, installingDriver);
    }

    private async Task InstallDriverAsync()
    {
        if (installingDriver || driverInstalled)
            return;

        try
        {
            installingDriver = true;
            UpdateDriverAction();
            driverInstalled = PawnIoInstaller.IsInstalled || await PawnIoInstaller.InstallAsync();
            if (exiting || !driverInstalled)
                return;

            Program.RelaunchForSensors();
            ExitThread();
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            // The user dismissed the Windows elevation prompt.
        }
        catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or TaskCanceledException
            or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            if (!exiting)
                MessageBox.Show(panel, $"Não foi possível ativar a leitura da CPU.\n\n{error.Message}",
                    "Sensores do Energy View", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            installingDriver = false;
            if (!exiting)
            {
                UpdateDriverAction();
                if (driverInstalled)
                    UpdateDisplay();
            }
        }
    }

    private void TogglePanel()
    {
        if (panel.Visible)
            panel.Hide();
        else
            ShowPanel();
    }

    private void ShowPanel()
    {
        Point pointer = Cursor.Position;
        panel.Location = pointer;
        panel.Show();
        Rectangle work = Screen.FromPoint(pointer).WorkingArea;
        int margin = (int)Math.Round(12 * panel.DeviceDpi / 96d);
        panel.FitHeight(work.Height - 2 * margin);
        panel.Location = new Point(Math.Max(work.Left, work.Right - panel.Width - margin),
            Math.Max(work.Top, work.Bottom - panel.Height - margin));
        panel.Activate();
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        timer.Stop();
        updateTimer.Stop();
        lifetime.Cancel();
        energy.Save(force: true);
        tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            updateTimer.Dispose();
            updater.Dispose();
            lifetime.Dispose();
            tray.Dispose();
            menu.Dispose();
            panel.Dispose();
            icon.Dispose();
            sensors.Dispose();
            hardware.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(PowerPanel.Accent);
        graphics.FillPolygon(brush,
        [
            new Point(17, 2), new Point(6, 18), new Point(14, 18),
            new Point(12, 30), new Point(27, 12), new Point(18, 12), new Point(21, 2)
        ]);

        nint handle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool DestroyIcon(nint icon);
}
