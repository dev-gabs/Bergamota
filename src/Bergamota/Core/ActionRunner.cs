using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Bergamota.Models;

namespace Bergamota.Core;

/// <summary>Executa a ação de uma fatia sobre os arquivos soltos nela.</summary>
public static class ActionRunner
{
    /// <summary>Chamado quando uma fatia "Enviar para…" sem aparelho fixo precisa que o usuário escolha.</summary>
    public static Func<IReadOnlyList<string>, Task>? ChooseDeviceAndSend;

    public static async Task RunAsync(Slice slice, IReadOnlyList<string> files, IntPtr hwnd)
    {
        if (files.Count == 0)
        {
            ActivityLog.Info(slice.Icon, "Prévia: " + slice.Label, "Nenhum arquivo foi solto, então nada foi executado.");
            return;
        }
        try
        {
            switch (slice.Kind)
            {
                // pasta favorita no "Enviar para": copia, como o "Enviar para" do Windows
                case SliceKind.Native when slice.Action == "sendfolder": await FileOps.TransferAsync(files, slice.Args, copy: true); break;
                case SliceKind.Native when slice.Action == "addfolder": await AddFolderAndSend(files); break;
                case SliceKind.Native: await RunNative(slice.Action, files, hwnd); break;
                case SliceKind.ContextMenu: RunMenuItem(slice, files, hwnd); break;
                case SliceKind.Program: RunProgram(slice, files); break;
                case SliceKind.Send:
                    var dev = string.IsNullOrEmpty(slice.Action) ? null : App.Devices.Find(slice.Action);
                    if (dev == null)
                    {
                        if (ChooseDeviceAndSend != null) await ChooseDeviceAndSend(files);
                    }
                    else await App.Devices.SendAsync(dev, files);
                    break;
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Error(slice.Label, ex.Message);
        }
    }

    static async Task RunNative(string id, IReadOnlyList<string> files, IntPtr hwnd)
    {
        switch (id)
        {
            case "extract-here": Archives.Extract(files, toFolder: false); break;
            case "extract-folder": Archives.Extract(files, toFolder: true); break;
            case "zip": await Archives.ZipAsync(files); break;
            case "powerrename": PowerRename(files, hwnd); break;
            case "openwith":
                var file = files.FirstOrDefault(File.Exists);
                if (file == null) { ActivityLog.Error("Abrir com…", "Abrir com funciona com arquivos, não com pastas."); return; }
                var info = new Native.OPENASINFO
                {
                    pcszFile = file,
                    oaifInFlags = Native.OAIF_ALLOW_REGISTRATION | Native.OAIF_REGISTER_EXT | Native.OAIF_EXEC,
                };
                Native.SHOpenWithDialog(hwnd, ref info);
                ActivityLog.Info("openwith", "Abrir com…", Path.GetFileName(file));
                break;
            case "properties":
                if (ShellMenu.InvokeVerb(files, "properties", hwnd))
                    ActivityLog.Info("props", "Propriedades", Describe(files));
                else ActivityLog.Error("Propriedades", "O Windows não abriu as propriedades.");
                break;
            case "convert":
                UI.Panels.ConvertAndAdjust(files);
                break;
            case "moveto":
                UI.Panels.MoveTo(files);
                break;
            case "copypath":
                Clipboard.SetText(string.Join(Environment.NewLine, files.Select(f => $"\"{f}\"")));
                ActivityLog.Info("link", "Caminho copiado", Describe(files));
                break;
        }
    }

    /// <summary>"Adicionar pasta…" do "Enviar para": escolhe a pasta, guarda nas favoritas e copia os arquivos para ela.</summary>
    static async Task AddFolderAndSend(IReadOnlyList<string> files)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Nova pasta favorita (os arquivos serão copiados para ela)" };
        if (dlg.ShowDialog() != true) return;
        var dir = dlg.FolderName;
        if (!App.Settings.FavoriteFolders.Contains(dir, StringComparer.OrdinalIgnoreCase))
        {
            App.Settings.FavoriteFolders.Add(dir);
            App.Settings.Save();
            ActivityLog.Info("folder", "Pasta favorita adicionada", dir);
        }
        await FileOps.TransferAsync(files, dir, copy: true);
    }

    static void RunMenuItem(Slice slice, IReadOnlyList<string> files, IntPtr hwnd)
    {
        if (ShellMenu.InvokeByText(files, slice.Action, hwnd))
            ActivityLog.Info(slice.Icon, slice.Label, $"Menu de contexto · {Describe(files)}");
        else
            ActivityLog.Error(slice.Label, $"Não achei “{slice.Action}” no menu de contexto desses itens.");
    }

    static void RunProgram(Slice slice, IReadOnlyList<string> files)
    {
        if (!File.Exists(slice.Action)) { ActivityLog.Error(slice.Label, "Programa não encontrado: " + slice.Action); return; }
        var quoted = string.Join(" ", files.Select(f => $"\"{f}\""));
        var args = string.IsNullOrWhiteSpace(slice.Args) ? quoted
            : slice.Args.Contains("{files}") ? slice.Args.Replace("{files}", quoted) : slice.Args + " " + quoted;
        Process.Start(new ProcessStartInfo(slice.Action, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(files[0]) });
        ActivityLog.Info(slice.Icon, slice.Label, Describe(files));
    }

    /// <summary>
    /// Abre o PowerRename do PowerToys com os itens. O PowerToys lê a lista pela entrada padrão,
    /// cada caminho seguido de '?', em UTF-16 — é o mesmo jeito que o menu do Explorer usa.
    /// Se não achar o executável, tenta pelo item do menu de contexto.
    /// </summary>
    static void PowerRename(IReadOnlyList<string> files, IntPtr hwnd)
    {
        var exe = PowerRenamePath();
        if (exe != null)
        {
            var p = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = new UnicodeEncoding(false, false),
                WorkingDirectory = Path.GetDirectoryName(exe),
            });
            if (p != null)
            {
                foreach (var f in files) p.StandardInput.Write(f + "?");
                p.StandardInput.Close();
                ActivityLog.Info("rename", "PowerRename aberto", Describe(files));
                return;
            }
        }
        if (ShellMenu.InvokeByText(files, "PowerRename", hwnd))
            ActivityLog.Info("rename", "PowerRename aberto", Describe(files));
        else
            ActivityLog.Error("Renomear", "PowerRename não encontrado. Instale o PowerToys e deixe o PowerRename ativado.");
    }

    public static string? PowerRenamePath()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerToys"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerToys"),
        };
        foreach (var r in roots)
            foreach (var sub in new[] { "WinUI3Apps", "" })
            {
                var p = Path.Combine(r, sub, "PowerToys.PowerRename.exe");
                if (File.Exists(p)) return p;
            }
        return null;
    }

    public static string Describe(IReadOnlyList<string> files) =>
        files.Count == 1 ? Path.GetFileName(files[0].TrimEnd('\\')) : $"{files.Count} itens · {Path.GetFileName(files[0].TrimEnd('\\'))} +{files.Count - 1}";
}
