namespace EnergyView;

internal sealed class PowerPanel : Form
{
    private const int FullHeight = 618;
    internal static readonly Color Accent = Color.FromArgb(197, 248, 110);
    private static readonly Color Ink = Color.FromArgb(237, 243, 234);
    private static readonly Color Muted = Color.FromArgb(165, 179, 172);
    private static readonly Color Rule = Color.FromArgb(48, 61, 56);
    private static readonly Color Surface = Color.FromArgb(19, 27, 24);
    private readonly Label primaryName;
    private readonly Label primaryValue;
    private readonly Label device;
    private readonly DataGridView components;
    private readonly DataGridView energyTable;
    private readonly Label energyNote;
    private readonly HashSet<ComponentGroup> expandedGroups = [];
    private IReadOnlyList<ComponentPower> componentRows = [];
    private readonly PowerChart chart;
    private readonly Button driverButton;
    private readonly Button startupButton;
    private readonly Button tariffButton;
    private readonly Button monitorsButton;
    private readonly Button settingsTab;
    private readonly Panel settings;
    private readonly Label updateStatus;
    private readonly Button updateButton;
    private readonly ComboBox powerPlans;
    private bool showingSettings;
    private bool driverActionAvailable;
    private bool updateAvailable;

    public PowerPanel()
    {
        Text = "Energy View";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(352, FullHeight);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Surface;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10);
        KeyPreview = true;
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
                Hide();
        };

        var border = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Rule };
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        border.Controls.Add(content);
        Controls.Add(border);

        content.Controls.Add(LabelAt("Energy View", 19, 11, 123, 28, Ink, 11, FontStyle.Bold));
        var pin = ButtonAt("Fixado", 208, 9, 82, "Alternar painel sempre visível");
        pin.ForeColor = Accent;
        pin.Click += (_, _) =>
        {
            TopMost = !TopMost;
            pin.Text = TopMost ? "Fixado" : "Fixar";
            pin.ForeColor = TopMost ? Accent : Muted;
        };
        var close = ButtonAt("×", 298, 9, 34, "Ocultar painel");
        close.Click += (_, _) => Hide();
        content.Controls.Add(pin);
        content.Controls.Add(close);
        content.Controls.Add(Divider(49));

        primaryName = LabelAt("Potência monitorada", 19, 57, 302, 21, Muted, 10);
        primaryValue = LabelAt("—", 17, 76, 315, 60, Accent, 34, FontStyle.Bold);
        device = LabelAt("Aguardando leitura dos sensores", 19, 136, 310, 23, Muted, 10);
        content.Controls.Add(primaryName);
        content.Controls.Add(primaryValue);
        content.Controls.Add(device);

        chart = new PowerChart
        {
            Bounds = new Rectangle(20, 167, 310, 40),
            AccessibleName = "Gráfico de potência dos últimos 60 segundos"
        };
        content.Controls.Add(chart);
        content.Controls.Add(LabelAt("Últimos 60 s", 19, 209, 308, 19, Muted, 9));

        content.Controls.Add(LabelAt("Plano de energia", 19, 231, 120, 32, Muted, 10));
        powerPlans = new ComboBox
        {
            Bounds = new Rectangle(140, 237, 191, 26),
            AccessibleName = "Plano de energia do Windows",
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Ink
        };
        powerPlans.Items.AddRange(WindowsPowerPlans.Options.Cast<object>().ToArray());
        powerPlans.DropDown += (_, _) => ReadPowerPlanRequested?.Invoke(this, EventArgs.Empty);
        powerPlans.SelectionChangeCommitted += (_, _) =>
        {
            if (powerPlans.SelectedItem is PowerPlanOption plan && plan.Id != Guid.Empty)
                PowerPlanRequested?.Invoke(plan.Id);
        };
        SetPowerPlan(null);
        content.Controls.Add(powerPlans);

        var componentTab = ButtonAt("Componentes", 19, 270, 110, "Ver componentes");
        var energyTab = ButtonAt("Energia", 135, 270, 65, "Ver energia acumulada");
        settingsTab = ButtonAt("Configurações", 206, 270, 125, "Abrir configurações");
        content.Controls.Add(componentTab);
        content.Controls.Add(energyTab);
        content.Controls.Add(settingsTab);
        components = CreateTable(("Componente", 50, false), ("Potência", 21, true), ("Origem", 29, false));
        components.CellClick += (_, args) => ToggleGroup(args.RowIndex);
        components.KeyDown += (_, args) =>
        {
            if (args.KeyCode is Keys.Enter or Keys.Space && ToggleGroup(components.CurrentCell?.RowIndex ?? -1))
                args.SuppressKeyPress = true;
        };
        content.Controls.Add(components);
        energyTable = CreateTable(("Período", 28, false), ("PC medido", 36, true), ("CPU + GPU", 36, true));
        energyTable.AccessibleName = "Energia acumulada em quilowatt-hora e custo em reais";
        energyTable.RowTemplate.Height = 43;
        energyTable.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        energyTable.Rows.Add("Nesta hora", "—", "—");
        energyTable.Rows.Add("Hoje", "—", "—");
        energyTable.Rows.Add("Neste mês", "—", "—");
        content.Controls.Add(energyTable);

        settings = new Panel
        {
            Bounds = new Rectangle(19, 310, 312, 229),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            AutoScroll = true
        };
        settings.Controls.Add(LabelAt("Atualizações", 0, 0, 212, 24, Ink, 10, FontStyle.Bold));
        updateStatus = LabelAt("Preparando verificação…", 0, 28, 204, 46, Muted, 9);
        settings.Controls.Add(updateStatus);
        updateButton = ButtonAt("Verificar", 216, 14, 96, "Verificar ou instalar atualização");
        updateButton.Click += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        settings.Controls.Add(updateButton);
        content.Controls.Add(settings);

        energyNote = LabelAt("Registra enquanto o Energy View está aberto.", 19, 543, 312, 24, Muted, 9);
        energyNote.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        content.Controls.Add(energyNote);
        startupButton = ButtonAt("Iniciar com Windows", 19, 573, 152, "Iniciar com Windows");
        startupButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        startupButton.Click += (_, _) => ToggleStartupRequested?.Invoke(this, EventArgs.Empty);
        content.Controls.Add(startupButton);
        driverButton = ButtonAt("Instalar PawnIO", 179, 573, 152, "Instalar o driver dos sensores da CPU");
        driverButton.ForeColor = Accent;
        driverButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        driverButton.Click += (_, _) => InstallDriverRequested?.Invoke(this, EventArgs.Empty);
        content.Controls.Add(driverButton);
        tariffButton = ButtonAt("Definir tarifa", 179, 573, 152, "Editar tarifa de energia em reais por kWh");
        tariffButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        tariffButton.ForeColor = Accent;
        tariffButton.Click += (_, _) => EditTariffRequested?.Invoke(this, EventArgs.Empty);
        content.Controls.Add(tariffButton);
        monitorsButton = ButtonAt("Monitores", 19, 573, 152, "Cadastrar monitores e potência");
        monitorsButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        monitorsButton.Click += (_, _) => EditMonitorsRequested?.Invoke(this, EventArgs.Empty);
        content.Controls.Add(monitorsButton);
        componentTab.Click += (_, _) => ShowTab(components);
        energyTab.Click += (_, _) => ShowTab(energyTable);
        settingsTab.Click += (_, _) => ShowTab(settings);
        ShowTab(components);

        void ShowTab(Control tab)
        {
            bool showEnergy = tab == energyTable;
            showingSettings = tab == settings;
            components.Visible = tab == components;
            energyTable.Visible = energyNote.Visible = showEnergy;
            settings.Visible = showingSettings;
            driverButton.Visible = showingSettings && driverActionAvailable;
            tariffButton.Visible = monitorsButton.Visible = showEnergy;
            startupButton.Visible = showingSettings;
            componentTab.ForeColor = tab == components ? Accent : Muted;
            energyTab.ForeColor = showEnergy ? Accent : Muted;
            RefreshSettingsTab();
        }
    }

    protected override bool ShowWithoutActivation => true;

    public event EventHandler? InstallDriverRequested;
    public event EventHandler? ToggleStartupRequested;
    public event EventHandler? EditTariffRequested;
    public event EventHandler? EditMonitorsRequested;
    public event EventHandler? ReadPowerPlanRequested;
    public event Action<Guid>? PowerPlanRequested;
    public event EventHandler? UpdateRequested;

    public void ShowUpdateSettings() => settingsTab.PerformClick();

    public void SetUpdateStatus(string status, bool busy, bool available, bool installed)
    {
        updateAvailable = available;
        updateStatus.Text = status;
        updateStatus.AccessibleDescription = status;
        updateButton.Enabled = !busy;
        updateButton.Text = !available ? "Verificar" : installed ? "Atualizar" : "Baixar";
        RefreshSettingsTab();
    }

    private void RefreshSettingsTab()
    {
        settingsTab.ForeColor = showingSettings || updateAvailable ? Accent : Muted;
        settingsTab.Text = updateAvailable ? "Configurações •" : "Configurações";
        settingsTab.AccessibleDescription = updateAvailable ? "Atualização disponível" : "";
    }

    public void SetPowerPlan(Guid? active)
    {
        PowerPlanOption match = WindowsPowerPlans.Options.FirstOrDefault(option => option.Id == active);
        if (match.Id != Guid.Empty)
        {
            powerPlans.SelectedItem = match;
            if (powerPlans.Items.Count > WindowsPowerPlans.Options.Count)
                powerPlans.Items.RemoveAt(WindowsPowerPlans.Options.Count);
            return;
        }

        if (powerPlans.Items.Count > WindowsPowerPlans.Options.Count)
            powerPlans.Items.RemoveAt(WindowsPowerPlans.Options.Count);
        powerPlans.SelectedIndex = powerPlans.Items.Add(
            new PowerPlanOption(Guid.Empty, active.HasValue ? "Outro plano do Windows" : "Plano indisponível"));
    }

    public void SetPowerPlanBusy(bool busy) => powerPlans.Enabled = !busy;

    public void SetStartupEnabled(bool enabled)
    {
        startupButton.Text = enabled ? "✓ Iniciar com Windows" : "Iniciar com Windows";
        startupButton.ForeColor = enabled ? Accent : Muted;
        startupButton.AccessibleDescription = enabled ? "Ativado" : "Desativado";
    }

    public void SetDriverAction(string? caption, bool busy)
    {
        driverActionAvailable = caption is not null;
        driverButton.Visible = driverActionAvailable && showingSettings;
        driverButton.Enabled = !busy;
        driverButton.Text = caption ?? "";
    }

    public void FitHeight(int availableHeight)
    {
        double scale = DeviceDpi / 96d;
        ClientSize = new Size(ClientSize.Width, Math.Clamp(availableHeight,
            (int)Math.Round(429 * scale), (int)Math.Round(FullHeight * scale)));
    }

    public void UpdateReading(LivePower live, double? cpuWatts, double? gpuWatts,
        IReadOnlyList<HardwarePowerSensor> hardwareReadings, bool cpuDriverInstalled, double? monitorWatts)
    {
        primaryName.Text = live.Label;
        primaryValue.Text = live.Watts is double value ? $"{value:N1} W" : "—";
        device.Text = monitorWatts.HasValue ? "Inclui a potência estimada dos monitores" :
            live.Watts.HasValue ? "Leitura dos sensores internos" : "Aguardando leitura dos sensores";
        componentRows = PowerBreakdown.Build(hardwareReadings, cpuDriverInstalled, cpuWatts, gpuWatts, monitorWatts);
        RefreshComponentRows();
        chart.AddReading(live.Watts, live.Label);
    }

    public void UpdateEnergy(EnergySummary summary, string? saveError, decimal? tariff)
    {
        EnergyTotals[] periods = [summary.Hour, summary.Day, summary.Month];
        energyTable.Columns[1].Visible = periods.Any(period => period.MeasuredKwh.HasValue);
        bool includesMonitors = periods.Any(period => period.MonitorsKwh.HasValue);
        bool includesComponents = periods.Any(period => period.ComponentsKwh.HasValue);
        energyTable.Columns[2].HeaderText = !includesMonitors ? "CPU + GPU" :
            includesComponents ? "CPU + GPU + mon." : "Monitores";
        for (int index = 0; index < periods.Length; index++)
        {
            energyTable.Rows[index].Cells[1].Value = EnergyTariff.FormatPeriod(periods[index].MeasuredKwh, tariff);
            DataGridViewCell combined = energyTable.Rows[index].Cells[2];
            combined.Value = EnergyTariff.FormatPeriod(periods[index].EstimatedKwh, tariff);
            combined.ToolTipText = periods[index].MonitorsKwh.HasValue
                ? $"CPU + GPU · {EnergyTariff.FormatPeriod(periods[index].ComponentsKwh, null)}\n" +
                    $"Monitores · {EnergyTariff.FormatPeriod(periods[index].MonitorsKwh, null)}"
                : "";
        }
        if (!energyTable.Focused)
            energyTable.ClearSelection();
        energyNote.Text = saveError ?? (includesMonitors ? "Monitores estimados pelos watts cadastrados." :
            tariff.HasValue ? "Custo estimado pela tarifa cadastrada." : "Registra enquanto o Energy View está aberto.");
        tariffButton.Text = tariff is decimal price ? $"R$ {EnergyTariff.FormatRate(price)}/kWh" : "Definir tarifa";
    }

    private bool ToggleGroup(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= components.Rows.Count ||
            components.Rows[rowIndex].Tag is not ComponentGroup group)
            return false;

        if (!expandedGroups.Add(group))
            expandedGroups.Remove(group);
        RefreshComponentRows();
        return true;
    }

    private void RefreshComponentRows()
    {
        var expandable = componentRows.Where(row => row.IsDetail).Select(row => row.Group).ToHashSet();
        ComponentPower[] rows = componentRows
            .Where(row => !row.IsDetail || (row.Group is ComponentGroup group && expandedGroups.Contains(group)))
            .ToArray();
        while (components.Rows.Count > rows.Length)
            components.Rows.RemoveAt(components.Rows.Count - 1);

        for (int index = 0; index < rows.Length; index++)
        {
            ComponentPower row = rows[index];
            string power = row.Watts is double valueInWatts ? $"{valueInWatts:N1} W" : "—";
            bool canExpand = !row.IsDetail && row.Group.HasValue && expandable.Contains(row.Group);
            bool expanded = row.Group is ComponentGroup group && expandedGroups.Contains(group);
            string name = row.IsDetail ? $"    {row.Component}" : canExpand
                ? $"{(expanded ? "▾" : "▸")} {row.Component}"
                : row.Component;
            if (index == components.Rows.Count)
                components.Rows.Add(name, power, row.Origin);
            else
            {
                components.Rows[index].Cells[0].Value = name;
                components.Rows[index].Cells[1].Value = power;
                components.Rows[index].Cells[2].Value = row.Origin;
            }
            components.Rows[index].Tag = canExpand ? row.Group : null;
            components.Rows[index].Cells[0].ToolTipText = row.FullName ??
                (canExpand ? "Clique ou pressione Enter para mostrar ou ocultar os sensores." : row.Component);
        }
        if (!components.Focused)
            components.ClearSelection();
    }

    private static DataGridView CreateTable(params (string Title, float Weight, bool AlignRight)[] columns)
    {
        var table = new DataGridView
        {
            AccessibleName = "Potência por componente",
            Bounds = new Rectangle(19, 310, 312, 229),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Segoe UI", 9),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ScrollBars = ScrollBars.Vertical,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Rule,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
        };
        table.RowTemplate.Height = 22;
        table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 42, 38);
        table.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        table.DefaultCellStyle.BackColor = Surface;
        table.DefaultCellStyle.ForeColor = Ink;
        table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 64, 54);
        table.DefaultCellStyle.SelectionForeColor = Ink;
        foreach (var (title, weight, alignRight) in columns)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            if (alignRight)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            table.Columns.Add(column);
        }
        return table;
    }

    private static Panel Divider(int y) => new()
    {
        Bounds = new Rectangle(19, y, 312, 1),
        BackColor = Rule
    };

    private static Label LabelAt(string text, int x, int y, int width, int height,
        Color color, float size, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, width, height),
        ForeColor = color,
        Font = new Font("Segoe UI", size, style),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private static Button ButtonAt(string text, int x, int y, int width, string accessibleName)
    {
        var button = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, 32),
            AccessibleName = accessibleName,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Muted,
            BackColor = Surface,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Rule;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 45, 39);
        return button;
    }
}
