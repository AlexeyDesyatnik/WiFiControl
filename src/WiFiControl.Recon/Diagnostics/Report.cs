using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WiFiControl.Recon.Diagnostics;

/// <summary>
/// Текстовый отчёт из разделов с отступами. Перенесён из NetSwitch. Сбой при сборе одного раздела
/// записывается в отчёт и не мешает остальным разделам.
/// </summary>
internal sealed class Report
{
    private readonly List<string> _lines = [];
    private int _depth;

    public IReadOnlyList<string> Lines => _lines;

    public bool HasErrors { get; private set; }

    public string Text => string.Join(Environment.NewLine, _lines) + Environment.NewLine;

    public void Line(string text)
    {
        _lines.Add(new string(' ', _depth * 2) + text);
    }

    public void Value(string name, object? value)
    {
        Line(name + ": " + Format(value));
    }

    /// <summary>Добавляет готовый раздел верхнего уровня, собранный отдельно.</summary>
    public void Append(IEnumerable<string> lines, bool hasErrors)
    {
        if (_lines.Count > 0) _lines.Add("");
        _lines.AddRange(lines);
        HasErrors |= hasErrors;
    }

    public void Section(string title, Action body)
    {
        if (_depth == 0)
        {
            if (_lines.Count > 0) _lines.Add("");
            Line("== " + title + " ==");
        }
        else
        {
            Line("[" + title + "]");
        }

        _depth++;
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Error(ex);
        }
        finally
        {
            _depth--;
        }
    }

    public void Error(Exception ex)
    {
        HasErrors = true;
        Line("ОШИБКА: " + ex.GetType().FullName + ErrorCode(ex) + ": " + ex.Message);
        foreach (string frame in (ex.StackTrace ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            Line("  " + frame.Trim());
        }
        if (ex.InnerException != null)
        {
            Line("Вызвано:");
            Error(ex.InnerException);
        }
    }

    // Ради кодов ошибок Windows разведка и затевалась: в тексте сообщения их нет
    private static string ErrorCode(Exception ex) => ex switch
    {
        Win32Exception win32 => " (код " + win32.NativeErrorCode + ")",
        ExternalException external => " (код 0x" + external.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ")",
        _ => ""
    };

    internal static string Format(object? value)
    {
        switch (value)
        {
            case null:
                return "(нет)";
            case string text:
                return text.Length == 0 ? "(пусто)" : text;
            case bool flag:
                return flag ? "да" : "нет";
            case byte[] bytes:
                return bytes.Length == 0 ? "(пусто)" : BitConverter.ToString(bytes).Replace('-', ' ');
            case Array array:
                if (array.Length == 0) return "(пусто)";
                var items = new List<string>();
                foreach (object? item in array) items.Add(Format(item));
                return string.Join(", ", items);
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }
}

/// <summary>Хранение отчётов рядом с программой: по файлу на каждый сохранённый отчёт.</summary>
internal static class ReportFile
{
    public const string FolderName = "Отчёты";

    public static string NameFor(string computer, DateTime when)
    {
        var name = new StringBuilder();
        foreach (char c in computer.Trim())
        {
            name.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }
        if (name.Length == 0) name.Append("компьютер");

        return name + "_" + when.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture) + ".txt";
    }

    /// <summary>Сохраняет отчёт, не затирая уже существующие, и возвращает путь к файлу.</summary>
    public static string Save(string baseFolder, string computer, DateTime when, string text)
    {
        string folder = Path.Combine(baseFolder, FolderName);
        Directory.CreateDirectory(folder);

        string name = NameFor(computer, when);
        string path = Path.Combine(folder, name);
        for (int n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + "_" + n + ".txt");
        }

        // С BOM, чтобы Блокнот на старых Windows не принял файл за ANSI
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return path;
    }
}
