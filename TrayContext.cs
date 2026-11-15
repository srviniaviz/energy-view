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
    private readonly EnergyPreferencesStore energyPreferences = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnergyView", "energy-settings.json"));
    private EnergyPreferences preferences;
    private string? preferencesError;
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
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 1000 };
    private readonly bool installed = AppInstallation.IsInstalled;
    private readonly bool elevated;
    private AppRelease? availableUpdate;
    private bool updating;
    private bool startsWithWindows;
    private bool changingPowerPlan;
    private bool refreshing;
    private bool exiting;
    private bool installingDriver;
    private bool driverInstalled;
    private IReadOnlyList<HardwarePowerSensor> hardwareReadings = [];

    public TrayContext(bool driverInstalled, bool startHidden, bool elevated)
    {
        this.driverInstalled = driverInstalled;
        this.elevated = elevated;
        try
        {
            preferences = energyPreferences.Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            preferences = new(null, []);
            preferencesError = "Não foi possível ler as preferências de energia.";
            Trace.TraceWarning("Energy preferences load failed: {0}", error);
        }
        MainForm = panel;
        var toggle = new ToolStripMenuItem("Ocultar painel", null, (_, _) => TogglePanel());
        menu.Items.Add(toggle);
        driverAction.Click += async (_, _) => await InstallDriverAsync();
        startupAction.Click += (_, _) => ToggleStartup();
        updateAction.Click += async (_, _) =>
        {
            OpenUpdateSettings();
            await HandleUpdateAsync();
        };
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
        tray.BalloonTipClicked += (_, _) => OpenUpdateSettings();

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
        panel.Shown += async (_, _) => await ReadPowerPlanAsync();
        panel.ReadPowerPlanRequested += async (_, _) => await ReadPowerPlanAsync();
        panel.PowerPlanRequested += async plan => await ChangePowerPlanAsync(plan);
        panel.EditTariffRequested += (_, _) => EditTariff();
        panel.EditMonitorsRequested += (_, _) => EditMonitors();
        panel.UpdateRequested += async (_, _) => await HandleUpdateAsync();
        panel.ToggleStartupRequested += (_, _) => ToggleStartup();

        timer.Tick += async (_, _) => await RefreshReadingAsync();
        UpdateDisplay();
        if (!startHidden)
            ShowPanel();
        UpdateDriverAction();
        ReadStartupState();
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Stop();
            await CheckForUpdatesAsync();
        };
        updateTimer.Start();
        timer.Start();
    }

    private void EditTariff()
    {
        using var dialog = new TariffDialog(preferences.Tariff, rate =>
            SaveEnergyPreferences(preferences with { Tariff = rate }));
        if (dialog.ShowDialog(panel) == DialogResult.OK)
            UpdateDisplay();
    }

    private void EditMonitors()
    {
        using var dialog = new MonitorsDialog(preferences.Monitors, monitors =>
            SaveEnergyPreferences(preferences with { Monitors = monitors }));
        if (dialog.ShowDialog(panel) == DialogResult.OK)
            UpdateDisplay();
    }

    private void SaveEnergyPreferences(EnergyPreferences settings)
    {
        energyPreferences.Save(settings);
        preferences = settings;
        preferencesError = null;
    }

    private async Task ReadPowerPlanAsync()
    {
        if (changingPowerPlan || exiting)
            return;

        Guid? active = null;
        try
        {
            active = await WindowsPowerPlans.GetActiveAsync(lifetime.Token);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            Trace.TraceWarning("Power plan query failed: {0}", error);
        }
        if (!changingPowerPlan && !exiting)
            panel.SetPowerPlan(active);
    }

    private async Task ChangePowerPlanAsync(Guid requested)
    {
        if (changingPowerPlan || exiting)
            return;

        changingPowerPlan = true;
        panel.SetPowerPlanBusy(true);
        try
        {
            if (await WindowsPowerPlans.GetActiveAsync(lifetime.Token) != requested)
                await WindowsPowerPlans.ApplyAsync(requested, lifetime.Token);
            if (await WindowsPowerPlans.GetActiveAsync(lifetime.Token) != requested)
                throw new InvalidOperationException("O Windows manteve outro plano ativo.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            if (!exiting)
                MessageBox.Show(panel, error.Message, "Plano de energia",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            changingPowerPlan = false;
            if (!exiting)
            {
                await ReadPowerPlanAsync();
                if (!exiting && !changingPowerPlan)
                    panel.SetPowerPlanBusy(false);
            }
        }
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

    private void OpenUpdateSettings()
    {
        if (!panel.Visible)
            ShowPanel();
        panel.ShowUpdateSettings();
    }

    private void ShowUpdateStatus(string message)
    {
        updateAction.Enabled = !updating;
        updateAction.Text = availableUpdate is AppRelease release
            ? $"Atualizar para v{release.Version}" : "Verificar atualizações";
        panel.SetUpdateStatus(message, updating, availableUpdate.HasValue, installed);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (updating || exiting)
            return;

        updating = true;
        ShowUpdateStatus("Verificando atualizações…");
        string status;
        try
        {
            AppRelease? release = await updater.FindAsync(lifetime.Token);
            if (exiting)
                return;
            bool newlyAvailable = release.HasValue && release.Value.Version != availableUpdate?.Version;
            availableUpdate = release;
            status = release is AppRelease value ? $"Versão {value.Version} disponível."
                : $"Energy View atualizado · v{ReleaseUpdater.CurrentVersion}.";
            if (newlyAvailable && !panel.Visible)
                tray.ShowBalloonTip(6000, "Atualização do Energy View",
                    "Abra as configurações para atualizar.", ToolTipIcon.Info);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or
            System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException
            or InvalidDataException)
        {
            Trace.TraceWarning("Release check failed: {0}", error);
            status = error is InvalidDataException ? error.Message : "Não foi possível verificar atualizações.";
        }
        finally
        {
            updating = false;
        }
        if (!exiting)
            ShowUpdateStatus(status);
    }

    private async Task HandleUpdateAsync()
    {
        if (updating || exiting)
            return;
        if (availableUpdate is not AppRelease release)
        {
            await CheckForUpdatesAsync();
            return;
        }

        if (!installed)
        {
            try
            {
                using Process? browser = Process.Start(new ProcessStartInfo(ReleaseUpdater.ReleasesPage)
                    { UseShellExecute = true });
            }
            catch (Win32Exception error)
            {
                Trace.TraceWarning("Could not open the release page: {0}", error);
                ShowUpdateStatus("Não foi possível abrir a página da versão.");
            }
            return;
        }

        updating = true;
        ShowUpdateStatus("Baixando e validando o instalador…");
        string status = "";
        bool installerVerified = false;
        try
        {
            string path = await updater.DownloadAsync(release, lifetime.Token);
            installerVerified = true;
            if (exiting)
                return;
            using Process setup = Process.Start(new ProcessStartInfo(path,
                "/SP- /NORESTART /UPDATE=1")
                { UseShellExecute = true, WorkingDirectory = AppInstallation.DirectoryPath })
                ?? throw new InvalidOperationException("Não foi possível iniciar o instalador.");
            ExitThread();
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException
            or InvalidDataException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Trace.TraceWarning("Update failed: {0}", error);
            status = error is InvalidDataException ? error.Message : installerVerified
                ? "Não foi possível abrir o instalador da atualização." : "Não foi possível baixar o instalador.";
        }
        finally
        {
            updating = false;
            if (!exiting)
                ShowUpdateStatus(status);
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
        double? monitorWatts = preferences.MonitorWatts;
        LivePower live = LivePower.FromComponents(cpu, reading.GpuWatts, monitorWatts);
        panel.UpdateReading(live, cpu, reading.GpuWatts, hardwareReadings, driverInstalled, monitorWatts);
        DateTimeOffset now = DateTimeOffset.Now;
        if (recordEnergy)
        {
            energy.Record(now, reading.BatteryWatts, cpu + reading.GpuWatts, monitorWatts);
            energy.Save();
        }
        panel.UpdateEnergy(energy.Summarize(now), energy.SaveError ?? preferencesError, preferences.Tariff);
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
