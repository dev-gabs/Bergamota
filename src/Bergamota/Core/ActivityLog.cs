using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Bergamota.Core;

public record LogEntry(DateTime Time, string Icon, string Title, string Detail, bool Ok)
{
    public string TimeText => Time.ToString("HH:mm");
}

/// <summary>Histórico do que foi feito (tela Registros) + arquivo log.txt + avisos na bandeja.</summary>
public static class ActivityLog
{
    public static ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>Pede pra App mostrar um aviso na bandeja (título, texto, é erro?).</summary>
    public static event Action<string, string, bool>? Notify;

    static readonly object FileLock = new();

    public static void Info(string icon, string title, string detail, bool notify = false) => Add(new(DateTime.Now, icon, title, detail, true), notify);
    public static void Error(string title, string detail, bool notify = true) => Add(new(DateTime.Now, "info", title, detail, false), notify);

    static void Add(LogEntry e, bool notify)
    {
        WriteFile(e);
        void ui()
        {
            Entries.Insert(0, e);
            while (Entries.Count > 300) Entries.RemoveAt(Entries.Count - 1);
            if (notify) Notify?.Invoke(e.Title, e.Detail, !e.Ok);
        }
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) ui(); else d.BeginInvoke(ui);
    }

    static void WriteFile(LogEntry e)
    {
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Models.AppSettings.Folder);
                var path = Path.Combine(Models.AppSettings.Folder, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
                File.AppendAllText(path, $"{e.Time:yyyy-MM-dd HH:mm:ss} [{(e.Ok ? "ok" : "erro")}] {e.Title}: {e.Detail}{Environment.NewLine}");
            }
        }
        catch { /* log nunca derruba o app */ }
    }
}
