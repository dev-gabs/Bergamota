using System.IO;
using Bergamota.Core;
using Microsoft.Win32;

namespace Bergamota.UI;

/// <summary>Os painéis abertos pelas fatias "Converter e ajustar…" e "Mover para…".</summary>
public static class Panels
{
    public static void ConvertAndAdjust(IReadOnlyList<string> files)
    {
        var p = new ActionPanel("Converter e ajustar", ActionRunner.Describe(files), "convert");
        p.AddSection("CONVERTER PARA", Converter.TargetsFor(files).Select(t => new ToolAction(t.Name, t.Note, () => Converter.ConvertAsync(files, t))));
        p.AddSection("AJUSTAR IMAGEM", ImageTools.ActionsFor(files));
        p.AddSection("PDF", PdfTools.ActionsFor(files));
        if (!p.HasCards) p.AddHint("Nada disponível para esses itens.");
        // as duas dicas falam do Ghostscript no caso de PDF: mostra uma só
        var h1 = Converter.MissingToolHint(files);
        var h2 = PdfTools.Hint(files);
        if (h1 != null && h2 != null && h1.Contains("Ghostscript"))
            p.AddHint("Instale o Ghostscript para PDF → imagem e para comprimir PDF (winget install ArtifexSoftware.GhostScript).");
        else { p.AddHint(h1); p.AddHint(h2); }
        p.AddFooter("O original fica intacto · o resultado vai para a mesma pasta · Esc cancela");
        p.Open();
    }

    public static void MoveTo(IReadOnlyList<string> files)
    {
        var s = App.Settings;
        var p = new ActionPanel(s.MoveCopies ? "Copiar para" : "Mover para", ActionRunner.Describe(files), "move");
        ToolAction Folder(string dir) => new(Path.GetFileName(dir.TrimEnd('\\')) is { Length: > 0 } n ? n : dir, ShortParent(dir), () => Go(files, dir));

        var favorites = s.FavoriteFolders.Where(Directory.Exists).ToList();
        p.AddSection("FAVORITAS", favorites.Select(Folder));
        p.AddSection("RECENTES", s.RecentFolders.Where(Directory.Exists).Where(r => !favorites.Contains(r, StringComparer.OrdinalIgnoreCase)).Select(Folder));
        p.AddSection("OUTRA", new[] { new ToolAction("Outra pasta…", "escolher no Windows", () => PickAndGo(files)) });
        if (favorites.Count == 0) p.AddHint("Dica: adicione suas pastas favoritas em Configurações → Pastas favoritas.");
        p.AddToggle("Copiar em vez de mover", s.MoveCopies, v => { s.MoveCopies = v; s.Save(); });
        p.AddFooter("Usa a cópia do Windows: mostra o progresso e pergunta se já existir um arquivo igual · Esc cancela");
        p.Open();
    }

    static Task Go(IReadOnlyList<string> files, string dir) => FileOps.TransferAsync(files, dir, App.Settings.MoveCopies);

    static Task PickAndGo(IReadOnlyList<string> files)
    {
        var dlg = new OpenFolderDialog { Title = App.Settings.MoveCopies ? "Copiar para…" : "Mover para…" };
        if (dlg.ShowDialog() != true) return Task.CompletedTask;
        var dir = dlg.FolderName;
        var recent = App.Settings.RecentFolders;
        recent.RemoveAll(r => string.Equals(r, dir, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, dir);
        if (recent.Count > 4) recent.RemoveRange(4, recent.Count - 4);
        App.Settings.Save();
        return Go(files, dir);
    }

    // "D:\Clientes\2026\Fulano" → "…\Clientes\2026"
    static string ShortParent(string dir)
    {
        var parent = Path.GetDirectoryName(dir.TrimEnd('\\')) ?? dir;
        var parts = parent.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? parent : "…\\" + string.Join("\\", parts[^2..]);
    }
}
