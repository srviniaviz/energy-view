using System.Globalization;

namespace EnergyView;

internal static class EnergyTariff
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public static bool TryParse(string input, out decimal? rate)
    {
        rate = null;
        string value = input.Trim();
        if (value.Length == 0)
            return true;
        if (!decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal parsed))
            return false;
        rate = parsed;
        return true;
    }

    public static string FormatRate(decimal rate) => rate.ToString("0.00##########################", Brazil);

    public static string FormatPeriod(double? energy, decimal? rate)
    {
        if (energy is not double kwh)
            return "—";

        string power = $"{kwh.ToString("N4", Brazil)} kWh";
        if (rate is not decimal price)
            return power;

        try
        {
            decimal cost = price * (decimal)kwh;
            string amount = cost is > 0 and < 0.01m ? "< R$ 0,01" : $"R$ {cost.ToString("N2", Brazil)}";
            return $"{power}\n{amount}";
        }
        catch (OverflowException)
        {
            return $"{power}\nCusto indisponível";
        }
    }
}
