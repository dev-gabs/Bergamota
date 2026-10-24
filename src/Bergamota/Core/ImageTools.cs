using System.IO;

namespace Bergamota.Core;

/// <summary>Uma ação de ferramenta mostrada como cartão no painel (nome, detalhe, o que faz).</summary>
public record ToolAction(string Name, string Note, Func<Task> Run);

/// <summary>
/// Ajustes rápidos de imagem com o ImageMagick: reduzir, comprimir para web, tirar metadados.
/// Upscale com a IA do Upscayl (Real-ESRGAN, na placa de vídeo) quando ele está instalado;
/// senão, ampliação comum (Lanczos) pelo ImageMagick.
/// </summary>
public static class ImageTools
{
    static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".jfif", ".webp", ".avif", ".heic", ".heif", ".bmp", ".gif", ".tif", ".tiff", ".tga", ".psd" };
    // formatos com transparência: a versão "web" vira WEBP para não perder o fundo transparente
    static readonly HashSet<string> Alpha = new(StringComparer.OrdinalIgnoreCase) { ".png", ".webp", ".gif", ".tif", ".tiff", ".tga", ".psd", ".avif" };

    public static bool IsImage(string f) => File.Exists(f) && Exts.Contains(Path.GetExtension(f));

    static string? _upscayl;
    public static string? UpscaylBin => _upscayl ??= new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Upscayl", "resources", "bin", "upscayl-bin.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Upscayl", "resources", "bin", "upscayl-bin.exe"),
    }.FirstOrDefault(File.Exists);

    static string? UpscaylModel(string bin)
    {
        var models = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bin)!, "..", "models"));
        foreach (var m in new[] { "upscayl-standard-4x", "realesrgan-x4plus", "ultrasharp-4x" })
            if (File.Exists(Path.Combine(models, m + ".param"))) return m;
        return Directory.Exists(models) ? Directory.EnumerateFiles(models, "*.param").Select(Path.GetFileNameWithoutExtension).FirstOrDefault() : null;
    }

    /// <summary>Ações disponíveis se TODOS os itens são imagens (e há ImageMagick).</summary>
    public static List<ToolAction> ActionsFor(IReadOnlyList<string> files)
    {
        var list = new List<ToolAction>();
        if (files.Count == 0 || !files.All(IsImage) || Converter.Magick == null) return list;
        list.Add(new("Reduzir 50%", "metade", () => Batch(files, "Reduzir 50%", f => Magick(f, "-resize 50%", "-50"))));
        list.Add(new("1920 px", "Full HD", () => Batch(files, "Largura 1920 px", f => Magick(f, "-resize \"1920x>\"", "-1920"))));
        list.Add(new("1080 px", "redes sociais", () => Batch(files, "Largura 1080 px", f => Magick(f, "-resize \"1080x>\"", "-1080"))));
        list.Add(new("Web", "leve, sem GPS", () => Batch(files, "Comprimir para web", Web)));
        list.Add(new("Limpar", "tira GPS e câmera", () => Batch(files, "Remover metadados", f => Magick(f, "-strip", "-limpo"))));
        if (UpscaylBin is { } bin && UpscaylModel(bin) is { } model)
        {
            list.Add(new("Upscale 2×", "IA · Upscayl", () => Batch(files, "Upscale 2×", f => Upscayl(bin, model, f, 2))));
            list.Add(new("Upscale 4×", "IA · Upscayl", () => Batch(files, "Upscale 4×", f => Upscayl(bin, model, f, 4))));
        }
        else
        {
            list.Add(new("Ampliar 2×", "sem IA · Lanczos", () => Batch(files, "Ampliar 2×", f => Magick(f, "-filter Lanczos -resize 200%", "-2x"))));
        }
        return list;
    }

    // roda o ajuste em cada arquivo, em segundo plano, e registra o resultado
    static async Task Batch(IReadOnlyList<string> files, string what, Func<string, string> one)
    {
        ActivityLog.Info("image", what, ActionRunner.Describe(files), notify: files.Count > 1 || what.StartsWith("Upscale"));
        var outs = new List<string>();
        foreach (var f in files)
        {
            try { outs.Add(await Task.Run(() => one(f))); }
            catch (Exception ex) { ActivityLog.Error(what, $"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        if (outs.Count == 0) return;
        var detail = outs.Count == 1
            ? $"{Path.GetFileName(outs[0])} · {Format.Size(new FileInfo(outs[0]).Length)}"
            : $"{outs.Count} imagens · {Format.Size(outs.Sum(o => new FileInfo(o).Length))} no total";
        ActivityLog.Info("image", what + " ✓", detail, notify: true);
    }

    static string Out(string input, string suffix, string? ext = null) =>
        Converter.Unique(Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + suffix + (ext ?? Path.GetExtension(input))));

    static string Magick(string input, string ops, string suffix, string? ext = null)
    {
        var output = Out(input, suffix, ext);
        // [0]: de GIF animado/PSD em camadas, usa só a primeira imagem
        Converter.Run(Converter.Magick!, $"\"{input}[0]\" -auto-orient {ops} \"{output}\"", TimeSpan.FromMinutes(5));
        return output;
    }

    /// <summary>Para web: no máximo 2560 px, qualidade 82, sem metadados. JPG, ou WEBP se o formato tem transparência.</summary>
    static string Web(string input)
    {
        bool alpha = Alpha.Contains(Path.GetExtension(input));
        return alpha
            ? Magick(input, "-resize \"2560x2560>\" -strip -quality 82", "-web", ".webp")
            : Magick(input, "-resize \"2560x2560>\" -strip -background white -alpha remove -alpha off -sampling-factor 4:2:0 -interlace JPEG -quality 82", "-web", ".jpg");
    }

    static string Upscayl(string bin, string model, string input, int scale)
    {
        var output = Out(input, $"-{scale}x", ".png");
        var models = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bin)!, "..", "models"));
        // o Upscayl não lê HEIC/AVIF/PSD: converte antes para PNG temporário
        string src = input, tmp = "";
        if (!new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(input).ToLowerInvariant()))
        {
            tmp = Path.Combine(Path.GetTempPath(), "bergamota-" + Guid.NewGuid().ToString("N")[..8] + ".png");
            Converter.Run(Converter.Magick!, $"\"{input}[0]\" \"{tmp}\"", TimeSpan.FromMinutes(2));
            src = tmp;
        }
        try
        {
            Converter.Run(bin, $"-i \"{src}\" -o \"{output}\" -s {scale} -m \"{models}\" -n {model} -f png", TimeSpan.FromMinutes(15));
        }
        finally { if (tmp.Length > 0) try { File.Delete(tmp); } catch { } }
        return output;
    }
}
