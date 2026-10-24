using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bergamota.Core;

/// <summary>Um formato de saída oferecido na janela "Converter para".</summary>
public record TargetFormat(string Ext, string Name, string Note);

/// <summary>
/// Conversão de arquivos pela extensão. Usa o que estiver instalado:
/// ImageMagick (imagens, e PDF→imagem com Ghostscript), FFmpeg (vídeo e áudio),
/// Word/Excel/PowerPoint (documentos) e, sem ImageMagick, o próprio Windows (WIC) para imagens básicas.
/// </summary>
public static class Converter
{
    enum Kind { Image, Video, Audio, Doc, Sheet, Slides, Pdf, Unknown }

    static readonly Dictionary<string, Kind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = Kind.Image, [".jpg"] = Kind.Image, [".jpeg"] = Kind.Image, [".webp"] = Kind.Image, [".avif"] = Kind.Image,
        [".heic"] = Kind.Image, [".heif"] = Kind.Image, [".bmp"] = Kind.Image, [".gif"] = Kind.Image, [".tif"] = Kind.Image,
        [".tiff"] = Kind.Image, [".ico"] = Kind.Image, [".svg"] = Kind.Image, [".psd"] = Kind.Image, [".tga"] = Kind.Image, [".jfif"] = Kind.Image,
        [".mp4"] = Kind.Video, [".mov"] = Kind.Video, [".mkv"] = Kind.Video, [".webm"] = Kind.Video, [".avi"] = Kind.Video,
        [".m4v"] = Kind.Video, [".wmv"] = Kind.Video, [".flv"] = Kind.Video,
        [".mp3"] = Kind.Audio, [".wav"] = Kind.Audio, [".flac"] = Kind.Audio, [".m4a"] = Kind.Audio, [".aac"] = Kind.Audio,
        [".ogg"] = Kind.Audio, [".opus"] = Kind.Audio, [".wma"] = Kind.Audio,
        [".docx"] = Kind.Doc, [".doc"] = Kind.Doc, [".rtf"] = Kind.Doc, [".odt"] = Kind.Doc, [".txt"] = Kind.Doc,
        [".xlsx"] = Kind.Sheet, [".xls"] = Kind.Sheet, [".ods"] = Kind.Sheet, [".csv"] = Kind.Sheet,
        [".pptx"] = Kind.Slides, [".ppt"] = Kind.Slides, [".odp"] = Kind.Slides,
        [".pdf"] = Kind.Pdf,
    };

    static readonly TargetFormat[] ImageTargets =
    {
        new("png", "PNG", "sem perda · transparência"), new("jpg", "JPG", "leve · fotos"), new("webp", "WEBP", "leve · web"),
        new("avif", "AVIF", "bem leve · moderno"), new("heic", "HEIC", "padrão do iPhone"), new("tiff", "TIFF", "impressão"),
        new("bmp", "BMP", "sem compressão"), new("gif", "GIF", "compatível"), new("ico", "ICO", "ícone do Windows"), new("pdf", "PDF", "documento"),
    };
    static readonly TargetFormat[] VideoTargets =
    {
        new("mp4", "MP4", "H.264 · toca em tudo"), new("webm", "WEBM", "VP9 · web"), new("mkv", "MKV", "H.264"), new("mov", "MOV", "H.264 · Apple"),
        new("gif", "GIF", "animação curta"), new("mp3", "MP3", "só o áudio"), new("wav", "WAV", "só o áudio · sem perda"),
    };
    static readonly TargetFormat[] AudioTargets =
    {
        new("mp3", "MP3", "compatível"), new("m4a", "M4A", "AAC · Apple"), new("ogg", "OGG", "Vorbis"), new("flac", "FLAC", "sem perda"), new("wav", "WAV", "sem compressão"),
    };
    static readonly TargetFormat[] DocTargets =
    {
        new("pdf", "PDF", "via Word"), new("docx", "DOCX", "Word"), new("odt", "ODT", "LibreOffice"), new("rtf", "RTF", "texto formatado"), new("txt", "TXT", "só o texto"),
    };
    static readonly TargetFormat[] SheetTargets =
    {
        new("pdf", "PDF", "via Excel"), new("xlsx", "XLSX", "Excel"), new("ods", "ODS", "LibreOffice"), new("csv", "CSV", "UTF-8"),
    };
    static readonly TargetFormat[] SlidesTargets = { new("pdf", "PDF", "via PowerPoint"), new("pptx", "PPTX", "PowerPoint") };

    // ---------------- ferramentas ----------------

    static string? _magick, _ffmpeg, _gs;
    static bool? _word, _excel, _ppt;

    public static string? Magick => _magick ??= Find("magick.exe", @"C:\Program Files", "ImageMagick*");
    public static string? FFmpeg => _ffmpeg ??= Find("ffmpeg.exe", @"C:\Program Files", "ffmpeg*") ?? Find("ffmpeg.exe", @"C:\", "ffmpeg*");
    static string? Ghostscript => _gs ??= FindDeep("gswin64c.exe", @"C:\Program Files\gs");
    static bool Word => _word ??= Type.GetTypeFromProgID("Word.Application") != null;
    static bool Excel => _excel ??= Type.GetTypeFromProgID("Excel.Application") != null;
    static bool PowerPoint => _ppt ??= Type.GetTypeFromProgID("PowerPoint.Application") != null;

    // PATH do processo + o atual do usuário e da máquina (o do processo fica velho se algo for instalado
    // com a Bergamota aberta) + a pasta de atalhos do winget
    static IEnumerable<string> SearchDirs() =>
        new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            .SelectMany(t => (Environment.GetEnvironmentVariable("PATH", t) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    static string? Find(string exe, string root, string dirPattern)
    {
        foreach (var dir in SearchDirs())
        {
            try { var p = Path.Combine(dir.Trim(), exe); if (File.Exists(p)) return p; } catch { }
        }
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root, dirPattern))
            {
                foreach (var p in new[] { Path.Combine(d, exe), Path.Combine(d, "bin", exe) })
                    if (File.Exists(p)) return p;
            }
        }
        catch { }
        return null;
    }

    static string? FindDeep(string exe, string root)
    {
        try { return Directory.Exists(root) ? Directory.EnumerateFiles(root, exe, SearchOption.AllDirectories).FirstOrDefault() : null; }
        catch { return null; }
    }

    static Kind KindOf(string path) =>
        Directory.Exists(path) ? Kind.Unknown : Kinds.TryGetValue(Path.GetExtension(path), out var k) ? k : Kind.Unknown;

    static IEnumerable<TargetFormat> TargetsFor(Kind k) => k switch
    {
        Kind.Image when Magick != null => ImageTargets,
        Kind.Image => ImageTargets.Where(t => t.Ext is "png" or "jpg" or "bmp" or "tiff" or "gif"), // só o Windows (WIC)
        Kind.Video when FFmpeg != null => VideoTargets,
        Kind.Audio when FFmpeg != null => AudioTargets,
        Kind.Doc when Word => DocTargets,
        Kind.Sheet when Excel => SheetTargets,
        Kind.Slides when PowerPoint => SlidesTargets,
        Kind.Pdf => (Magick != null && Ghostscript != null ? ImageTargets.Where(t => t.Ext is "png" or "jpg" or "webp") : [])
                    .Concat(Word ? new[] { new TargetFormat("docx", "DOCX", "via Word (editável)") } : []),
        _ => [],
    };

    /// <summary>Formatos possíveis para TODOS os arquivos (a interseção), sem repetir o formato de origem quando é um só tipo.</summary>
    public static List<TargetFormat> TargetsFor(IReadOnlyList<string> files)
    {
        List<TargetFormat>? common = null;
        foreach (var f in files)
        {
            var src = Norm(Path.GetExtension(f));
            var t = TargetsFor(KindOf(f)).Where(x => x.Ext != src).ToList();
            common = common == null ? t : common.Where(c => t.Any(x => x.Ext == c.Ext)).ToList();
        }
        return common ?? new();
    }

    /// <summary>Por que não há formatos (ou falta ferramenta), para mostrar na janela.</summary>
    public static string? MissingToolHint(IReadOnlyList<string> files)
    {
        var kinds = files.Select(KindOf).Distinct().ToList();
        if (kinds.Contains(Kind.Unknown)) return "Algum item não é um tipo de arquivo que eu sei converter (pastas também não entram).";
        if (kinds.Count > 1) return "Os arquivos são de tipos diferentes; aparecem só os formatos que servem para todos.";
        return kinds[0] switch
        {
            Kind.Video or Kind.Audio when FFmpeg == null => "Para vídeo e áudio instale o FFmpeg (winget install ffmpeg) e abra a Bergamota de novo.",
            Kind.Image when Magick == null => "Instale o ImageMagick para WEBP, AVIF, HEIC, ICO e PDF.",
            Kind.Pdf when Magick == null || Ghostscript == null => "PDF para imagem precisa do ImageMagick e do Ghostscript.",
            Kind.Doc when !Word => "Documentos precisam do Microsoft Word instalado.",
            Kind.Sheet when !Excel => "Planilhas precisam do Microsoft Excel instalado.",
            Kind.Slides when !PowerPoint => "Apresentações precisam do Microsoft PowerPoint instalado.",
            _ => null,
        };
    }

    static string Norm(string ext) => ext.TrimStart('.').ToLowerInvariant() switch { "jpeg" or "jfif" => "jpg", "tif" => "tiff", "heif" => "heic", var e => e };

    // ---------------- converter ----------------

    public static async Task ConvertAsync(IReadOnlyList<string> files, TargetFormat target)
    {
        int ok = 0;
        string? lastOut = null;
        var errors = new List<string>();
        ActivityLog.Info("convert", $"Convertendo para {target.Name}", ActionRunner.Describe(files), notify: files.Count > 1 || KindOf(files[0]) is Kind.Video or Kind.Doc or Kind.Sheet or Kind.Slides);
        foreach (var f in files)
        {
            var output = Unique(Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + "." + target.Ext));
            try
            {
                await Task.Run(() => ConvertOne(f, output, target.Ext));
                ok++;
                lastOut = output;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(f)}: {ex.Message}");
                try { if (File.Exists(output) && new FileInfo(output).Length == 0) File.Delete(output); } catch { }
            }
        }
        if (ok > 0)
            ActivityLog.Info("convert", $"Convertido para {target.Name}",
                ok == 1 && lastOut != null ? $"{Path.GetFileName(lastOut)} · {Format.Size(SafeSize(lastOut))}" : $"{ok} arquivo(s)", notify: true);
        foreach (var e in errors) ActivityLog.Error($"Converter para {target.Name}", e);
    }

    static long SafeSize(string p) { try { return new FileInfo(p).Length; } catch { return 0; } }

    static void ConvertOne(string input, string output, string ext)
    {
        switch (KindOf(input))
        {
            case Kind.Image or Kind.Pdf when ext is not "docx":
                if (Magick != null) RunMagick(input, output, ext, isPdf: KindOf(input) == Kind.Pdf);
                else ConvertWithWic(input, output, ext);
                break;
            case Kind.Video or Kind.Audio:
                RunFFmpeg(input, output, ext);
                break;
            case Kind.Doc or Kind.Pdf:
                Office.Word(input, output, ext);
                break;
            case Kind.Sheet:
                Office.Excel(input, output, ext);
                break;
            case Kind.Slides:
                Office.PowerPoint(input, output, ext);
                break;
            default:
                throw new NotSupportedException("tipo de arquivo não suportado");
        }
        if (!File.Exists(output) && !Directory.GetFiles(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-*." + ext).Any())
            throw new IOException("o conversor terminou sem gerar o arquivo");
    }

    static void RunMagick(string input, string output, string ext, bool isPdf)
    {
        var src = Path.GetExtension(input).ToLowerInvariant();
        var pre = isPdf ? "-density 150 " : src == ".svg" ? "-background none -density 192 " : "";
        var opts = ext switch
        {
            "jpg" or "bmp" => "-background white -alpha remove -alpha off -quality 90",
            "webp" => "-quality 90",
            "avif" => "-quality 70",
            "heic" => "-quality 85",
            "ico" => "-background none -resize 256x256 -define icon:auto-resize=256,128,64,48,32,16",
            _ => "",
        };
        // GIF animado ou PDF de várias páginas para formato estático: um arquivo por quadro/página (nome-0, nome-1...)
        Run(Magick!, $"{pre}\"{input}\" {opts} \"{output}\"", TimeSpan.FromMinutes(5));
    }

    static void RunFFmpeg(string input, string output, string ext)
    {
        var args = ext switch
        {
            "mp4" => "-c:v libx264 -crf 22 -preset medium -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart",
            "mov" => "-c:v libx264 -crf 22 -preset medium -pix_fmt yuv420p -c:a aac -b:a 160k",
            "mkv" => "-c:v libx264 -crf 22 -preset medium -c:a aac -b:a 160k",
            "webm" => "-c:v libvpx-vp9 -crf 32 -b:v 0 -c:a libopus -b:a 128k",
            "gif" => "-vf \"fps=12,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0",
            "mp3" => "-vn -c:a libmp3lame -q:a 2",
            "m4a" => "-vn -c:a aac -b:a 192k",
            "ogg" => "-vn -c:a libvorbis -q:a 5",
            "flac" => "-vn -c:a flac",
            "wav" => "-vn -c:a pcm_s16le",
            _ => "",
        };
        Run(FFmpeg!, $"-hide_banner -loglevel error -y -i \"{input}\" {args} \"{output}\"", TimeSpan.FromHours(3));
    }

    static void Run(string exe, string args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi) ?? throw new IOException("não consegui iniciar " + Path.GetFileName(exe));
        var err = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(timeout)) { try { p.Kill(true); } catch { } throw new TimeoutException("demorou demais"); }
        if (p.ExitCode != 0)
        {
            var msg = err.Result.Trim().Split('\n').LastOrDefault()?.Trim();
            throw new IOException(string.IsNullOrEmpty(msg) ? $"{Path.GetFileNameWithoutExtension(exe)} falhou (código {p.ExitCode})" : msg);
        }
    }

    /// <summary>Sem ImageMagick: decodifica e codifica com o próprio Windows.</summary>
    static void ConvertWithWic(string input, string output, string ext)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            BitmapSource frame;
            using (var fs = File.OpenRead(input))
                frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (ext is "jpg" or "bmp") frame = Flatten(frame); // sem transparência: fundo branco
            BitmapEncoder enc = ext switch
            {
                "png" => new PngBitmapEncoder(),
                "jpg" => new JpegBitmapEncoder { QualityLevel = 90 },
                "bmp" => new BmpBitmapEncoder(),
                "gif" => new GifBitmapEncoder(),
                _ => new TiffBitmapEncoder(),
            };
            enc.Frames.Add(BitmapFrame.Create(frame));
            using var outFs = File.Create(output);
            enc.Save(outFs);
        });
    }

    static BitmapSource Flatten(BitmapSource src)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, src.PixelWidth, src.PixelHeight));
            dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight));
        }
        var rtb = new RenderTargetBitmap(src.PixelWidth, src.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
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

    /// <summary>Automação do Office (invisível). Cada chamada abre e fecha o app.</summary>
    static class Office
    {
        public static void Word(string input, string output, string ext) => Sta(() =>
        {
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
            try
            {
                app.Visible = false;
                app.DisplayAlerts = 0;
                dynamic doc = app.Documents.Open(input, false, true, false); // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
                int fmt = ext switch { "pdf" => 17, "docx" => 16, "rtf" => 6, "txt" => 2, "odt" => 23, _ => 16 };
                doc.SaveAs2(output, fmt);
                doc.Close(false);
            }
            finally { app.Quit(false); }
        });

        public static void Excel(string input, string output, string ext) => Sta(() =>
        {
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application")!)!;
            try
            {
                app.Visible = false;
                app.DisplayAlerts = false;
                dynamic wb = app.Workbooks.Open(input, 0, true); // FileName, UpdateLinks, ReadOnly
                if (ext == "pdf") wb.ExportAsFixedFormat(0, output);
                else wb.SaveAs(output, ext switch { "xlsx" => 51, "csv" => 62, "ods" => 60, _ => 51 });
                wb.Close(false);
            }
            finally { app.Quit(); }
        });

        public static void PowerPoint(string input, string output, string ext) => Sta(() =>
        {
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
            try
            {
                dynamic pres = app.Presentations.Open(input, -1, 0, 0); // ReadOnly, Untitled, WithWindow
                pres.SaveAs(output, ext == "pdf" ? 32 : 24);
                pres.Close();
            }
            finally { app.Quit(); }
        });

        // a automação do Office precisa de uma thread STA própria
        static void Sta(Action work)
        {
            Exception? error = null;
            var t = new Thread(() => { try { work(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new IOException((error.InnerException ?? error).Message);
        }
    }
}
