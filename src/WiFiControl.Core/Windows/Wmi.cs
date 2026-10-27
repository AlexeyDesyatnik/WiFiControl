using System.Globalization;
using System.Management;

namespace WiFiControl.Core.Windows;

// Перенесено из NetSwitch

/// <summary>Строка результата WMI-запроса: имя свойства → значение.</summary>
internal sealed class WmiRow() : Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
{
    public string Text(string name) =>
        TryGetValue(name, out object? value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

    public int? Number(string name) =>
        TryGetValue(name, out object? value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
}

internal static class Wmi
{
    public static List<WmiRow> Query(string wql)
    {
        var rows = new List<WmiRow>();
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", wql);
        using ManagementObjectCollection found = searcher.Get();
        foreach (ManagementBaseObject item in found)
        {
            using (item)
            {
                var row = new WmiRow();
                foreach (PropertyData property in item.Properties) row[property.Name] = property.Value;
                rows.Add(row);
            }
        }
        return rows;
    }
}
