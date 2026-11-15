namespace EnergyView;

internal sealed class MonitorsDialog : Form
{
    public MonitorsDialog(IReadOnlyList<MonitorEstimate> monitors, Action<MonitorEstimate[]> saveMonitors)
    {
        Text = "Monitores";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(435, 328);
        Controls.Add(new Label
        {
            Text = "Cadastre a potência de cada tela e marque as que estão ligadas.",
            Bounds = new Rectangle(14, 13, 406, 36)
        });
        var table = new DataGridView
        {
            AccessibleName = "Cadastro de monitores e potência",
            Bounds = new Rectangle(14, 53, 406, 192),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false
        };
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Monitor", FillWeight = 59 });
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Potência (W)", FillWeight = 25 });
        table.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Ligado", FillWeight = 16 });
        table.DefaultValuesNeeded += (_, args) => args.Row.Cells[2].Value = true;
        foreach (MonitorEstimate monitor in monitors)
            table.Rows.Add(monitor.Name, EnergyTariff.FormatRate(monitor.Watts), monitor.Enabled);
        Controls.Add(table);
        Controls.Add(new Label
        {
            Text = "As telas marcadas entram na estimativa enquanto o app roda.",
            Bounds = new Rectangle(14, 251, 406, 21)
        });
        var remove = new Button { Text = "Remover linha", Bounds = new Rectangle(14, 281, 110, 30) };
        var save = new Button { Text = "Salvar", Bounds = new Rectangle(236, 281, 85, 30) };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(335, 281, 85, 30) };
        Controls.AddRange([remove, save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;
        remove.Click += (_, _) =>
        {
            if (table.CurrentRow is { IsNewRow: false } row)
                table.Rows.Remove(row);
        };
        save.Click += (_, _) =>
        {
            if (!table.EndEdit())
                return;

            var entries = new List<MonitorEstimate>();
            foreach (DataGridViewRow row in table.Rows)
            {
                if (row.IsNewRow)
                    continue;
                string name = row.Cells[0].Value?.ToString()?.Trim() ?? "";
                string watts = row.Cells[1].Value?.ToString() ?? "";
                if (name.Length == 0 && string.IsNullOrWhiteSpace(watts))
                    continue;
                if (name.Length == 0 || !EnergyTariff.TryParse(watts, out decimal? power) || power is null)
                {
                    MessageBox.Show(this, $"Preencha o nome e a potência em watts na linha {row.Index + 1}.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                entries.Add(new(name, power.Value, row.Cells[2].Value is true));
            }
            try
            {
                saveMonitors(entries.ToArray());
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Não foi possível salvar os monitores.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }
}
