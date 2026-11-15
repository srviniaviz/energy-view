namespace EnergyView;

internal sealed class PowerPanel : Form
{
    private const int FullHeight = 579;
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

        var componentTab = ButtonAt("Componentes", 19, 231, 152, "Ver componentes");
        var energyTab = ButtonAt("Energia acumulada", 179, 231, 152, "Ver energia acumulada");
        content.Controls.Add(componentTab);
        content.Controls.Add(energyTab);
        components = CreateTable(("Componente", 50, false), ("Potência", 21, true), ("Origem", 29, false));
        components.CellClick += (_, args) => ToggleGroup(args.RowIndex);
        components.KeyDown += (_, args) =>
        {
            if (args.KeyCode is Keys.Enter or Keys.Space && ToggleGroup(components.CurrentCell?.RowIndex ?? -1))
                args.SuppressKeyPress = true;
        };
        content.Controls.Add(components);
        energyTable = CreateTable(("Período", 28, false), ("PC medido", 36, true), ("CPU + GPU", 36, true));
        energyTable.AccessibleName = "Energia acumulada em quilowatt-hora";
        energyTable.Rows.Add("Nesta hora", "—", "—");
        energyTable.Rows.Add("Hoje", "—", "—");
        energyTable.Rows.Add("Neste mês", "—", "—");
        content.Controls.Add(energyTable);

        energyNote = LabelAt("Registra enquanto o Energy View está aberto.", 19, 504, 312, 24, Muted, 9);
        energyNote.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        content.Controls.Add(energyNote);
        componentTab.Click += (_, _) => ShowEnergy(false);
        energyTab.Click += (_, _) => ShowEnergy(true);
        ShowEnergy(false);

        void ShowEnergy(bool show)
        {
            components.Visible = !show;
            energyTable.Visible = energyNote.Visible = show;
            componentTab.ForeColor = show ? Muted : Accent;
            energyTab.ForeColor = show ? Accent : Muted;
        }
        startupButton = ButtonAt("Iniciar com Windows", 19, 534, 152, "Iniciar com Windows");
        startupButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        startupButton.Click += (_, _) => ToggleStartupRequested?.Invoke(this, EventArgs.Empty);
        content.Controls.Add(startupButton);
        driverButton = ButtonAt("Instalar PawnIO", 179, 534, 152, "Instalar o driver dos sensores da CPU");
        driverButton.ForeColor = Accent;
        driverButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        driverButton.Click += (_, _) => InstallDriverRequested?.Invoke(this, EventArgs.Empty);
        content.Controls.Add(driverButton);
    }

    protected override bool ShowWithoutActivation => true;

    public event EventHandler? InstallDriverRequested;
    public event EventHandler? ToggleStartupRequested;

    public void SetStartupEnabled(bool enabled)
    {
        startupButton.Text = enabled ? "✓ Iniciar com Windows" : "Iniciar com Windows";
        startupButton.ForeColor = enabled ? Accent : Muted;
        startupButton.AccessibleDescription = enabled ? "Ativado" : "Desativado";
    }

    public void SetDriverAction(string? caption, bool busy)
    {
        driverButton.Visible = caption is not null;
        driverButton.Enabled = !busy;
        driverButton.Text = caption ?? "";
    }

    public void FitHeight(int availableHeight)
    {
        double scale = DeviceDpi / 96d;
        ClientSize = new Size(ClientSize.Width, Math.Clamp(availableHeight,
            (int)Math.Round(390 * scale), (int)Math.Round(FullHeight * scale)));
    }

    public void UpdateReading(LivePower live, double? cpuWatts, double? gpuWatts,
        IReadOnlyList<HardwarePowerSensor> hardwareReadings, bool cpuDriverInstalled)
    {
        primaryName.Text = live.Label;
        primaryValue.Text = live.Watts is double value ? $"{value:N1} W" : "—";
        device.Text = live.Watts.HasValue ? "Leitura dos sensores internos" : "Aguardando leitura dos sensores";
        componentRows = PowerBreakdown.Build(hardwareReadings, cpuDriverInstalled, cpuWatts, gpuWatts);
        RefreshComponentRows();
        chart.AddReading(live.Watts, live.Label);
    }

    public void UpdateEnergy(EnergySummary summary, string? saveError)
    {
        EnergyTotals[] periods = [summary.Hour, summary.Day, summary.Month];
        energyTable.Columns[1].Visible = periods.Any(period => period.MeasuredKwh.HasValue);
        for (int index = 0; index < periods.Length; index++)
        {
            energyTable.Rows[index].Cells[1].Value = FormatEnergy(periods[index].MeasuredKwh);
            energyTable.Rows[index].Cells[2].Value = FormatEnergy(periods[index].ComponentsKwh);
        }
        if (!energyTable.Focused)
            energyTable.ClearSelection();
        energyNote.Text = saveError ?? "Registra enquanto o Energy View está aberto.";
    }

    private static string FormatEnergy(double? energy) => energy is double kwh ? $"{kwh:N4} kWh" : "—";

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
            Bounds = new Rectangle(19, 271, 312, 229),
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
