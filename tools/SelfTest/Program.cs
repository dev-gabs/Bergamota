using System.IO;
using System.IO.Compression;
using System.Windows;
using Bergamota.Core;
using Bergamota.Models;
using Bergamota.Net;
using Bergamota.UI;

// Testes rápidos das peças da Bergamota que não dependem de arrastar com o mouse.
// Uso: dotnet run --project tools/SelfTest [-- --net] [-- --powerrename]
int fails = 0;
void Check(bool ok, string name, string detail = "")
{
    Console.WriteLine($"{(ok ? "OK  " : "FALHOU")} {name}{(detail.Length > 0 ? " · " + detail : "")}");
    if (!ok) fails++;
}

// diagnóstico: SelfTest --probe <ip> → chama /register e mostra a resposta ou o erro completo
var argv = Environment.GetCommandLineArgs();
int pi = Array.IndexOf(argv, "--probe");
if (pi > 0 && pi + 1 < argv.Length)
{
    var ip = argv[pi + 1];
    // certificado próprio, apresentado ao servidor; o fingerprint é o SHA-256 dele
    using var rsa = System.Security.Cryptography.RSA.Create(2048);
    var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Bergamota probe", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
    req.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
        System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature | System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment, false));
    req.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(
        new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.2"), new("1.3.6.1.5.5.7.3.1") }, false));
    using var tmp = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
    var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(tmp.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
    var fp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData)).ToLowerInvariant();
    bool withCert = !argv.Contains("--nocert");
    var handler = new System.Net.Http.SocketsHttpHandler { SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true } };
    if (argv.Contains("--tls12")) handler.SslOptions.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
    if (withCert)
    {
        handler.SslOptions.ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection { cert };
        handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => cert;
    }
    Console.WriteLine($"certificado de cliente: {withCert} · fingerprint {fp[..12]}…");
    using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    foreach (var proto in new[] { "https" })
    {
        try
        {
            var body = new System.Net.Http.StringContent("{\"alias\":\"Bergamota probe\",\"version\":\"2.1\",\"deviceModel\":\"Bergamota\",\"deviceType\":\"desktop\",\"fingerprint\":\"" + fp + "\",\"port\":53317,\"protocol\":\"https\",\"download\":false}", System.Text.Encoding.UTF8, "application/json");
            var r = await http.PostAsync($"{proto}://{ip}:53317/api/localsend/v2/register", body);
            Console.WriteLine($"{proto}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{proto}: ERRO");
            for (var e = ex; e != null; e = e.InnerException) Console.WriteLine($"   {e.GetType().Name}: {e.Message}");
        }
    }
    return 0;
}

var thread = new Thread(() =>
{
    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    app.Dispatcher.InvokeAsync(async () =>
    {
        try { await Run(); }
        catch (Exception ex) { Check(false, "exceção", ex.ToString()); }
        app.Shutdown();
    });
    app.Run();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine(fails == 0 ? "\nTudo certo." : $"\n{fails} falha(s).");
return fails;

async Task Run()
{
    // ---------- geometria ----------
    var c = new Point(0, 0);
    Check(WheelGeometry.HitTest(c, new Point(0, -100), 8, 40, 200) == 0, "fatia do topo é a 0");
    Check(WheelGeometry.HitTest(c, new Point(100, 0), 8, 40, 200) == 2, "fatia da direita é a 2 (8 fatias)");
    Check(WheelGeometry.HitTest(c, new Point(-100, 0), 8, 40, 200) == 6, "fatia da esquerda é a 6");
    Check(WheelGeometry.HitTest(c, new Point(5, 5), 8, 40, 200) == -1, "centro = cancelar");
    Check(WheelGeometry.HitTest(c, new Point(300, 0), 8, 40, 200) == -2, "fora da roda");

    // ---------- perfis ----------
    var comp = new Profile { Extensions = ".rar .zip" };
    Check(comp.Matches(new[] { @"C:\a.RAR", @"C:\b.zip" }), "perfil casa extensões (sem diferenciar maiúsculas)");
    Check(!comp.Matches(new[] { @"C:\a.rar", @"C:\b.png" }), "perfil não casa se algum arquivo é de outro tipo");
    var s = Slice.Create(SliceKind.Native, "zip");
    s.Action = "properties";
    Check(s.Label == "Propriedades" && s.Icon == "props", "trocar a ação atualiza rótulo e ícone padrão", s.Label);
    s.Label = "Meu rótulo"; s.Action = "zip";
    Check(s.Label == "Meu rótulo", "rótulo personalizado é mantido ao trocar a ação");

    // ---------- zip e extração ----------
    var root = Path.Combine(Path.GetTempPath(), "bergamota-selftest-" + Guid.NewGuid().ToString("N")[..6]);
    var folder = Path.Combine(root, "Pasta teste");
    Directory.CreateDirectory(Path.Combine(folder, "sub"));
    File.WriteAllText(Path.Combine(folder, "a.txt"), "olá bergamota");
    File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "subpasta");
    var single = Path.Combine(root, "nota.txt");
    File.WriteAllText(single, "arquivo solto");

    await Archives.ZipAsync(new[] { folder, single });
    var zipPath = Path.Combine(root, "Pasta teste.zip");
    Check(File.Exists(zipPath), "ZIP criado ao lado do primeiro item", Path.GetFileName(zipPath));
    if (File.Exists(zipPath))
    {
        using var z = ZipFile.OpenRead(zipPath);
        var names = z.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        Check(names.SequenceEqual(new[] { "nota.txt", "Pasta teste/a.txt", "Pasta teste/sub/b.txt" }), "ZIP mantém a estrutura de pastas", string.Join(", ", names));
    }
    await Archives.ZipAsync(new[] { folder, single });
    Check(File.Exists(Path.Combine(root, "Pasta teste (2).zip")), "segundo ZIP ganha nome único");

    Check(Archives.WinRarPath() != null, "WinRAR encontrado", Archives.WinRarPath() ?? "");
    var toExtract = Path.Combine(root, "pacote.zip");
    File.Copy(zipPath, toExtract);
    Archives.Extract(new[] { toExtract }, toFolder: true);
    var expected = Path.Combine(root, "pacote", "Pasta teste", "sub", "b.txt");
    for (int i = 0; i < 40 && !File.Exists(expected); i++) await Task.Delay(250);
    Check(File.Exists(expected), "Extrair para pasta (WinRAR) cria a pasta com o nome do arquivo");
    // "Extrair aqui" numa pasta limpa, para o WinRAR não perguntar se substitui
    var clean = Path.Combine(root, "limpa");
    Directory.CreateDirectory(clean);
    var toExtract2 = Path.Combine(clean, "pacote.zip");
    File.Copy(zipPath, toExtract2);
    Archives.Extract(new[] { toExtract2 }, toFolder: false);
    var here = Path.Combine(clean, "Pasta teste", "a.txt");
    for (int i = 0; i < 40 && !File.Exists(here); i++) await Task.Delay(250);
    Check(File.Exists(here), "Extrair aqui (WinRAR)");

    // ---------- conversão ----------
    var png = Path.Combine(root, "foto.png");
    {
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, 64, 64));
            dc.DrawEllipse(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xDC, 0x5A)), null, new Point(32, 32), 28, 28);
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(png);
        enc.Save(fs);
    }
    var targets = Converter.TargetsFor(new[] { png });
    Check(targets.Count > 0 && targets.All(t => t.Ext != "png"), "formatos para PNG (sem repetir PNG)", string.Join(" ", targets.Select(t => t.Name)));
    Check(Converter.TargetsFor(new[] { png, Path.Combine(root, "a.docx") }).All(t => t.Ext == "pdf"), "tipos misturados mostram só o que serve para todos");
    Console.WriteLine("     dica p/ vídeo: " + (Converter.MissingToolHint(new[] { @"C:\x.mp4" }) ?? "(FFmpeg encontrado)"));
    foreach (var t in targets.Where(t => t.Ext is "jpg" or "webp" or "avif" or "heic" or "ico" or "pdf"))
    {
        await Converter.ConvertAsync(new[] { png }, t);
        var outFile = Path.Combine(root, "foto." + t.Ext);
        Check(File.Exists(outFile) && new FileInfo(outFile).Length > 0, $"PNG → {t.Name}", File.Exists(outFile) ? Format.Size(new FileInfo(outFile).Length) : "não gerou");
    }
    // vídeo e áudio (FFmpeg): gera um clipe de 2 s com o próprio FFmpeg e converte
    if (Converter.FFmpeg != null)
    {
        var mp4 = Path.Combine(root, "clipe.mp4");
        var gen = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Converter.FFmpeg,
            $"-hide_banner -loglevel error -y -f lavfi -i testsrc=duration=2:size=320x240:rate=24 -f lavfi -i sine=frequency=440:duration=2 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{mp4}\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
        gen.WaitForExit();
        var vt = Converter.TargetsFor(new[] { mp4 });
        Check(vt.Count > 0, "formatos para MP4", string.Join(" ", vt.Select(t => t.Name)));
        foreach (var t in vt.Where(t => t.Ext is "webm" or "gif" or "mp3"))
        {
            await Converter.ConvertAsync(new[] { mp4 }, t);
            var o = Path.Combine(root, "clipe." + t.Ext);
            Check(File.Exists(o) && new FileInfo(o).Length > 0, $"MP4 → {t.Name}", File.Exists(o) ? Format.Size(new FileInfo(o).Length) : "não gerou");
        }
        var mp3 = Path.Combine(root, "clipe.mp3");
        if (File.Exists(mp3))
        {
            await Converter.ConvertAsync(new[] { mp3 }, new TargetFormat("flac", "FLAC", ""));
            Check(File.Exists(Path.Combine(root, "clipe.flac")), "MP3 → FLAC");
        }
    }
    else Check(false, "FFmpeg encontrado");

    // ---------- ajustes de imagem ----------
    var big = Path.Combine(root, "grande.png");
    Converter.Run(Converter.Magick!, $"-size 2400x1600 gradient:#C9DC5A-#1B2410 -set comment \"teste\" \"{big}\"", TimeSpan.FromMinutes(1));
    var imgActions = ImageTools.ActionsFor(new[] { big });
    Check(imgActions.Count >= 6, "ações de imagem disponíveis", string.Join(" · ", imgActions.Select(a => a.Name)));
    async Task<string?> RunImg(string name, string expected)
    {
        var a = imgActions.FirstOrDefault(x => x.Name == name);
        if (a == null) { Check(false, name, "ação não existe"); return null; }
        await a.Run();
        var o = Path.Combine(root, expected);
        Check(File.Exists(o), name, File.Exists(o) ? Format.Size(new FileInfo(o).Length) : "não gerou " + expected);
        return File.Exists(o) ? o : null;
    }
    string Dim(string f) { var r = new System.Diagnostics.Process { StartInfo = new(Converter.Magick!, $"identify -format \"%wx%h\" \"{f}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } }; r.Start(); var s = r.StandardOutput.ReadToEnd(); r.WaitForExit(); return s.Trim(); }
    if (await RunImg("Reduzir 50%", "grande-50.png") is { } half) Check(Dim(half) == "1200x800", "Reduzir 50% dá 1200x800", Dim(half));
    if (await RunImg("1920 px", "grande-1920.png") is { } fhd) Check(Dim(fhd) == "1920x1280", "1920 px mantém a proporção", Dim(fhd));
    await RunImg("Web", "grande-web.webp");
    await RunImg("Limpar", "grande-limpo.png");
    var small = Path.Combine(root, "pequena.png");
    Converter.Run(Converter.Magick!, $"\"{big}\" -resize 200x133 \"{small}\"", TimeSpan.FromMinutes(1));
    var up = ImageTools.ActionsFor(new[] { small }).FirstOrDefault(a => a.Name.StartsWith("Upscale 2") || a.Name.StartsWith("Ampliar 2"));
    if (up != null)
    {
        var t0 = DateTime.Now;
        await up.Run();
        var o = Path.Combine(root, "pequena-2x.png");
        Check(File.Exists(o) && Dim(o) == "400x266", $"{up.Name} ({up.Note})", File.Exists(o) ? $"{Dim(o)} em {(DateTime.Now - t0).TotalSeconds:0.0}s" : "não gerou");
    }

    // ---------- PDF ----------
    var p1 = Path.Combine(root, "pagina1.png"); var p2 = Path.Combine(root, "pagina2.png");
    File.Copy(small, p1); File.Copy(Path.Combine(root, "grande-50.png"), p2);
    var toPdf = PdfTools.ActionsFor(new[] { p2, p1 }).FirstOrDefault();
    if (toPdf != null) { await toPdf.Run(); }
    var combined = Path.Combine(root, "pagina1.pdf");
    Check(File.Exists(combined), "Imagens → PDF (ordem pelo nome: pagina1 primeiro)", toPdf?.Note ?? "sem ação");
    if (File.Exists(combined))
    {
        await PdfTools.ActionsFor(new[] { combined }).First(a => a.Name == "Separar").Run();
        var pages = Path.Combine(root, "pagina1 (páginas)");
        var parts = Directory.Exists(pages) ? Directory.GetFiles(pages, "*.pdf").OrderBy(x => x).ToArray() : [];
        Check(parts.Length == 2, "Separar: 2 páginas em PDFs separados", string.Join(", ", parts.Select(Path.GetFileName)));
        if (parts.Length == 2)
        {
            await PdfTools.ActionsFor(parts).First(a => a.Name == "Juntar").Run();
            var merged = Directory.GetFiles(pages, "*(juntado).pdf").FirstOrDefault();
            using var doc = merged != null ? PdfSharp.Pdf.IO.PdfReader.Open(merged, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import) : null;
            Check(doc?.PageCount == 2, "Juntar: os 2 PDFs viram um de 2 páginas", merged != null ? Path.GetFileName(merged) : "não gerou");
        }
    }

    // ---------- mover / copiar ----------
    var dest = Path.Combine(root, "destino");
    Directory.CreateDirectory(dest);
    var toMove = Path.Combine(root, "mover-me.txt"); File.WriteAllText(toMove, "x");
    var toCopy = Path.Combine(root, "copiar-me.txt"); File.WriteAllText(toCopy, "y");
    await FileOps.TransferAsync(new[] { toMove }, dest, copy: false);
    Check(!File.Exists(toMove) && File.Exists(Path.Combine(dest, "mover-me.txt")), "Mover para pasta");
    await FileOps.TransferAsync(new[] { toCopy, folder }, dest, copy: true);
    Check(File.Exists(toCopy) && File.Exists(Path.Combine(dest, "copiar-me.txt")) && File.Exists(Path.Combine(dest, "Pasta teste", "sub", "b.txt")), "Copiar arquivo e pasta (com subpastas)");

    var webp = Path.Combine(root, "foto.webp");
    if (File.Exists(webp))
    {
        await Converter.ConvertAsync(new[] { webp }, new TargetFormat("png", "PNG", ""));
        Check(File.Exists(Path.Combine(root, "foto (2).png")), "WEBP → PNG com nome único (não sobrescreve o original)");
    }

    if (argv.Contains("--office"))
    {
        var txt = Path.Combine(root, "carta.txt");
        File.WriteAllText(txt, "Olá! Documento de teste da Bergamota.");
        var docTargets = Converter.TargetsFor(new[] { txt });
        Check(docTargets.Any(t => t.Ext == "pdf"), "Word detectado (TXT → PDF disponível)", string.Join(" ", docTargets.Select(t => t.Name)));
        if (docTargets.Any(t => t.Ext == "pdf"))
        {
            await Converter.ConvertAsync(new[] { txt }, docTargets.First(t => t.Ext == "pdf"));
            Check(File.Exists(Path.Combine(root, "carta.pdf")), "TXT → PDF via Word");
        }
    }

    // ---------- menu de contexto ----------
    var items = ShellMenu.ListItems(single);
    Check(items.Count > 3, "lê o menu de contexto do Explorer", $"{items.Count} itens");
    Console.WriteLine("     itens: " + string.Join(" | ", items.Take(25)));
    Check(items.Any(t => t.Contains("PowerRename", StringComparison.OrdinalIgnoreCase)), "PowerRename aparece no menu clássico");

    // ---------- PowerRename ----------
    Check(ActionRunner.PowerRenamePath() != null, "PowerRename.exe encontrado", ActionRunner.PowerRenamePath() ?? "");
    if (Environment.GetCommandLineArgs().Contains("--powerrename"))
    {
        // arquivos fora da pasta temporária do teste, que é apagada no fim
        var keep = Path.Combine(Path.GetTempPath(), "bergamota-powerrename-teste");
        Directory.CreateDirectory(keep);
        var f1 = Path.Combine(keep, "foto viagem 01.txt"); var f2 = Path.Combine(keep, "foto viagem 02.txt");
        File.WriteAllText(f1, "1"); File.WriteAllText(f2, "2");
        await ActionRunner.RunAsync(Slice.Create(SliceKind.Native, "powerrename"), new[] { f1, f2 }, IntPtr.Zero);
        Console.WriteLine("     PowerRename aberto com 2 arquivos — confira a janela.");
    }

    // ---------- rede (LocalSend) ----------
    int si = Array.IndexOf(argv, "--send");
    if (si > 0 && si + 1 < argv.Length)
    {
        var settings = new AppSettings { Alias = "Bergamota (teste)", NoSave = true };
        var svc = new DeviceService(settings);
        var file = Path.Combine(Path.GetTempPath(), "teste-bergamota.txt");
        File.WriteAllText(file, "Olá! Este arquivo foi enviado pela Bergamota via LocalSend.");
        var dev = new Device { Fingerprint = "teste", Alias = argv[si + 1], Ip = argv[si + 1], Port = 53317, Protocol = "https" };
        Console.WriteLine("     enviando teste-bergamota.txt — aceite no aparelho se ele pedir…");
        await svc.SendAsync(dev, new[] { file });
        var last = ActivityLog.Entries.FirstOrDefault();
        Check(last?.Ok == true && last.Title.StartsWith("Enviado"), "envio via LocalSend", last?.Detail ?? "");
    }

    if (Environment.GetCommandLineArgs().Contains("--net"))
    {
        var settings = new AppSettings { Alias = "Bergamota SelfTest", NoSave = true };
        var svc = new DeviceService(settings);
        var t0 = DateTime.Now;
        await svc.ScanAsync(deep: true);
        Check(true, "varredura completa terminou", $"{(DateTime.Now - t0).TotalSeconds:0.0}s · {svc.Devices.Count} aparelho(s)");
        foreach (var d in svc.Devices) Console.WriteLine($"     · {d.Alias} ({d.Subtitle}) {d.Ip}:{d.Port} {d.Protocol} online={d.IsOnline}");
    }

    foreach (var e in ActivityLog.Entries.Reverse()) Console.WriteLine($"     log: [{(e.Ok ? "ok" : "erro")}] {e.Title} · {e.Detail}");
    try { Directory.Delete(root, true); } catch { }
}
