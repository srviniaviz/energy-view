namespace EnergyView;

internal sealed class TariffDialog : Form
{
    public TariffDialog(decimal? current, Action<decimal?> saveTariff)
    {
        Text = "Tarifa de energia";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(348, 151);
        Controls.Add(new Label
        {
            Text = "Valor do kWh na sua conta de luz, em reais",
            Bounds = new Rectangle(14, 13, 318, 24)
        });
        var field = new TextBox
        {
            AccessibleName = "Tarifa em reais por quilowatt-hora",
            PlaceholderText = "0,95",
            Bounds = new Rectangle(14, 43, 318, 24),
            Text = current.HasValue ? EnergyTariff.FormatRate(current.Value) : ""
        };
        Controls.Add(field);
        Controls.Add(new Label
        {
            Text = "Deixe em branco para remover a tarifa.",
            Bounds = new Rectangle(14, 75, 318, 22)
        });
        var save = new Button { Text = "Salvar", Bounds = new Rectangle(148, 105, 85, 30) };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(247, 105, 85, 30) };
        Controls.AddRange([save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;
        ActiveControl = field;
        save.Click += (_, _) =>
        {
            if (!EnergyTariff.TryParse(field.Text, out decimal? rate))
            {
                MessageBox.Show(this, "Digite um valor válido em R$/kWh, como 0,95.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                field.Focus();
                return;
            }
            try
            {
                saveTariff(rate);
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Não foi possível salvar a tarifa.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }
}
