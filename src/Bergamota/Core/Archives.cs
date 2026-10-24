using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace Bergamota.Core;

/// <summary>Extrair (WinRAR → 7-Zip → tar do Windows) e compactar em ZIP.</summary>
public static class Archives
{
    static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        { ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab", ".iso", ".arj", ".lz", ".zst", ".001" };

    public static bool IsArchive(string path) => File.Exists(path) && ArchiveExts.Contains(Path.GetExtension(path));

    public static string? WinRarPath()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var k = root.OpenSubKey(@"SOFTWARE\WinRAR");
            if (k?.GetValue("exe64") is string p64 && File.Exists(p64)) return p64;
            if (k?.GetValue("exe32") is string p32 && File.Exists(p32)) return p32;
        }
        foreach (var p in new[] { @"C:\Program Files\WinRAR\WinRAR.exe", @"C:\Program Files (x86)\WinRAR\WinRAR.exe" })
            if (File.Exists(p)) return p;
        return null;
    }

    static string? SevenZipPath()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\7-Zip");
        var dir = k?.GetValue("Path64") as string ?? k?.GetValue("Path") as string ?? @"C:\Program Files\7-Zip\";
        var gui = Path.Combine(dir, "7zG.exe");
        return File.Exists(gui) ? gui : null;
    }

    /// <summary>Extrai cada arquivo compactado. toFolder = cria uma pasta com o nome do arquivo.</summary>
    public static void Extract(IEnumerable<string> files, bool toFolder)
    {
        var archives = files.Where(IsArchive).ToList();
        if (archives.Count == 0)
        {
            ActivityLog.Error("Extrair", "Nenhum arquivo compactado entre os itens soltos.");
            return;
        }
        var winrar = WinRarPath();
        var seven = winrar == null ? SevenZipPath() : null;
        foreach (var a in archives)
        {
            var dir = Path.GetDirectoryName(a)!;
            var named = Path.Combine(dir, Path.GetFileNameWithoutExtension(a));
            try
            {
                if (winrar != null)
                    // x = extrair com pastas · -ibck = em segundo plano (progresso na bandeja) · -ad = pasta com o nome do arquivo
                    Run(winrar, $"x -ibck {(toFolder ? "-ad " : "")}\"{a}\" \"{dir}\\\"");
                else if (seven != null)
                    Run(seven, $"x \"{a}\" -o\"{(toFolder ? named : dir)}\"");
                else
                {
                    var target = toFolder ? named : dir;
                    Directory.CreateDirectory(target);
                    Run("tar.exe", $"-xf \"{a}\" -C \"{target}\"", hidden: true);
                }
                ActivityLog.Info(toFolder ? "extract-folder" : "extract", "Extraindo " + Path.GetFileName(a),
                    (winrar != null ? "WinRAR" : seven != null ? "7-Zip" : "tar do Windows") + " → " + (toFolder ? named : dir));
            }
            catch (Exception ex)
            {
                ActivityLog.Error("Extrair " + Path.GetFileName(a), ex.Message);
            }
        }
    }

    static void Run(string exe, string args, bool hidden = false) =>
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = !hidden, CreateNoWindow = hidden });

    /// <summary>Cria um .zip ao lado do primeiro item, com nome único.</summary>
    public static async Task ZipAsync(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        var first = items[0].TrimEnd('\\');
        var dir = Path.GetDirectoryName(first)!;
        // como o "Enviar para > Pasta compactada" do Windows: nome do primeiro item
        var baseName = Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Arquivos";
        var target = Unique(Path.Combine(dir, baseName + ".zip"));
        try
        {
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
                foreach (var item in items)
                {
                    var path = item.TrimEnd('\\');
                    if (File.Exists(path))
                        zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
                    else if (Directory.Exists(path))
                    {
                        var parent = Path.GetDirectoryName(path)!;
                        var any = false;
                        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        {
                            zip.CreateEntryFromFile(f, Path.GetRelativePath(parent, f).Replace('\\', '/'), CompressionLevel.Optimal);
                            any = true;
                        }
                        if (!any) zip.CreateEntry(Path.GetFileName(path) + "/");
                    }
                }
            });
            var size = new FileInfo(target).Length;
            ActivityLog.Info("zip", "Compactado para ZIP", $"{items.Count} item(ns) → {Path.GetFileName(target)} · {Format.Size(size)}");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(target)) File.Delete(target); } catch { }
            ActivityLog.Error("Compactar ZIP", ex.Message);
        }
    }

    static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}

public static class Format
{
    public static string Size(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.#} {u[i]}";
    }
}
