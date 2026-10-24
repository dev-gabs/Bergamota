using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Bergamota.Core;

/// <summary>
/// Ferramentas de PDF: juntar, separar páginas e imagens → PDF com o PDFsharp (vem dentro do app,
/// mantém texto e vetores). Comprimir usa o Ghostscript, se estiver instalado.
/// </summary>
public static class PdfTools
{
    static bool IsPdf(string f) => File.Exists(f) && Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ordem "natural" pelo nome (pagina2 antes de pagina10), já que a ordem do arraste não é confiável.</summary>
    static List<string> Natural(IEnumerable<string> files) =>
        files.OrderBy(f => System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(f), @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase).ToList();

    public static List<ToolAction> ActionsFor(IReadOnlyList<string> files)
    {
        var list = new List<ToolAction>();
        if (files.Count == 0) return list;
        if (files.All(IsPdf))
        {
            if (files.Count > 1) list.Add(new("Juntar", $"{files.Count} PDFs em um", () => Safe("Juntar PDFs", () => Merge(Natural(files)))));
            list.Add(new("Separar", "uma página por PDF", () => Safe("Separar páginas", () => Split(files))));
            if (Converter.Ghostscript != null) list.Add(new("Comprimir", "menor · bom p/ e-mail", () => Safe("Comprimir PDF", () => Compress(files))));
        }
        else if (files.All(ImageTools.IsImage) && Converter.Magick != null)
        {
            list.Add(new("Em PDF", files.Count > 1 ? $"{files.Count} imagens, 1 PDF" : "vira um PDF", () => Safe("Imagens para PDF", () => ImagesToPdf(Natural(files)))));
        }
        return list;
    }

    /// <summary>Por que "Comprimir" não aparece (para mostrar no painel).</summary>
    public static string? Hint(IReadOnlyList<string> files) =>
        files.Count > 0 && files.All(IsPdf) && Converter.Ghostscript == null
            ? "Para comprimir PDF, instale o Ghostscript (winget install ArtifexSoftware.GhostScript)." : null;

    static async Task Safe(string what, Func<string> work)
    {
        try
        {
            var detail = await Task.Run(work);
            ActivityLog.Info("filetext", what + " ✓", detail, notify: true);
        }
        catch (Exception ex) { ActivityLog.Error(what, ex.Message); }
    }

    static string Merge(List<string> files)
    {
        var output = Converter.Unique(Path.Combine(Path.GetDirectoryName(files[0])!, Path.GetFileNameWithoutExtension(files[0]) + " (juntado).pdf"));
        using var doc = new PdfDocument();
        int pages = 0;
        foreach (var f in files)
        {
            using var src = PdfReader.Open(f, PdfDocumentOpenMode.Import);
            foreach (var p in src.Pages) { doc.AddPage(p); pages++; }
        }
        doc.Save(output);
        return $"{files.Count} PDFs → {Path.GetFileName(output)} · {pages} páginas · {Format.Size(new FileInfo(output).Length)}";
    }

    static string Split(IReadOnlyList<string> files)
    {
        int total = 0;
        string? lastFolder = null;
        foreach (var f in files)
        {
            using var src = PdfReader.Open(f, PdfDocumentOpenMode.Import);
            var folder = Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + " (páginas)");
            Directory.CreateDirectory(folder);
            int digits = src.PageCount.ToString().Length;
            for (int i = 0; i < src.PageCount; i++)
            {
                using var one = new PdfDocument();
                one.AddPage(src.Pages[i]);
                one.Save(Converter.Unique(Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(f)} - p{(i + 1).ToString().PadLeft(digits, '0')}.pdf")));
                total++;
            }
            lastFolder = folder;
        }
        return $"{total} páginas separadas" + (files.Count == 1 ? $" em “{Path.GetFileName(lastFolder)}”" : "");
    }

    static string Compress(IReadOnlyList<string> files)
    {
        long before = 0, after = 0;
        foreach (var f in files)
        {
            var output = Converter.Unique(Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + " (comprimido).pdf"));
            // /ebook = imagens a 150 dpi: bem menor e ainda nítido na tela
            Converter.Run(Converter.Ghostscript!, $"-sDEVICE=pdfwrite -dCompatibilityLevel=1.5 -dPDFSETTINGS=/ebook -dNOPAUSE -dQUIET -dBATCH -sOutputFile=\"{output}\" \"{f}\"", TimeSpan.FromMinutes(10));
            before += new FileInfo(f).Length;
            after += new FileInfo(output).Length;
        }
        var pct = before > 0 ? 100 - after * 100 / before : 0;
        return $"{Format.Size(before)} → {Format.Size(after)} ({pct}% menor)";
    }

    static string ImagesToPdf(List<string> files)
    {
        var output = Converter.Unique(Path.Combine(Path.GetDirectoryName(files[0])!, Path.GetFileNameWithoutExtension(files[0]) + ".pdf"));
        var inputs = string.Join(" ", files.Select(f => $"\"{f}[0]\""));
        // cada imagem vira uma página do tamanho dela, sem perder qualidade (JPG vai sem recompressão)
        Converter.Run(Converter.Magick!, $"{inputs} -auto-orient \"{output}\"", TimeSpan.FromMinutes(10));
        return $"{files.Count} imagem(ns) → {Path.GetFileName(output)} · {Format.Size(new FileInfo(output).Length)}";
    }
}
