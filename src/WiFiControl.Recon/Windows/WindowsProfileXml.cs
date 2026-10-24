using System.Xml.Linq;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>
/// Разбор XML профиля сети Windows. Берёт только то, что нужно разведке; ключ сети (<c>keyMaterial</c>) не читает.
/// </summary>
internal static class WindowsProfileXml
{
    public static WindowsProfile Read(string name, string type, int position, string xml)
    {
        XElement root = XDocument.Parse(xml).Root
            ?? throw new FormatException("В XML профиля сети Windows «" + name + "» нет корневого элемента.");

        // Элементы лежат в пространствах имён разных версий схемы (MacRandomization — в v3), поэтому ищутся по локальному имени
        string? ssid = Child(Child(Child(root, "SSIDConfig"), "SSID"), "name")?.Value;
        string? connectionMode = Child(root, "connectionMode")?.Value;
        List<XmlValue>? macRandomization = root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "MacRandomization")
            ?.Elements()
            .Select(element => new XmlValue(element.Name.LocalName, element.Value))
            .ToList();

        return new WindowsProfile(name, type, position, ssid, connectionMode, macRandomization);
    }

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
}
