using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Bergamota.Core;
using Bergamota.Models;

namespace Bergamota.Net;

public class Device : Observable
{
    string _alias = "", _ip = "", _protocol = "https";
    string? _model, _type;
    int _port = 53317;
    DateTime _lastSeen;
    bool _online, _showInWheel = true;

    public string Fingerprint { get; init; } = "";
    public string Alias { get => _alias; set { if (Set(ref _alias, value)) Raise(nameof(Subtitle)); } }
    public string? DeviceModel { get => _model; set { if (Set(ref _model, value)) Raise(nameof(Subtitle)); } }
    public string? DeviceType { get => _type; set { if (Set(ref _type, value)) { Raise(nameof(IconName)); Raise(nameof(Subtitle)); } } }
    public string Ip { get => _ip; set { if (Set(ref _ip, value)) Raise(nameof(Address)); } }
    public int Port { get => _port; set { if (Set(ref _port, value)) Raise(nameof(Address)); } }
    public string Protocol { get => _protocol; set => Set(ref _protocol, value); }
    public DateTime LastSeen { get => _lastSeen; set { if (Set(ref _lastSeen, value)) Raise(nameof(LastSeenText)); } }
    public bool IsOnline { get => _online; set { if (Set(ref _online, value)) Raise(nameof(LastSeenText)); } }
    public bool ShowInWheel { get => _showInWheel; set { if (Set(ref _showInWheel, value)) App.Devices?.Persist(); } }

    public string IconName => DeviceType switch { "mobile" => "phone", "desktop" => "desktop", "web" => "globe", "headless" or "server" => "terminal", _ => "laptop" };
    public string Subtitle => $"{TypeName} · {(string.IsNullOrWhiteSpace(DeviceModel) ? "LocalSend" : DeviceModel)}";
    string TypeName => DeviceType switch { "mobile" => "Celular", "desktop" => "Computador", "web" => "Navegador", _ => "Aparelho" };
    public string Address => $"{Ip} · porta {Port}";
    public string LastSeenText => IsOnline ? "● Online" : LastSeen == default ? "Nunca visto" : $"Visto {Ago(LastSeen)}";

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "agora";
        if (d.TotalHours < 1) return $"há {(int)d.TotalMinutes} min";
        if (d.TotalDays < 1) return $"há {(int)d.TotalHours} h";
        return t.ToString("dd/MM HH:mm");
    }
}

/// <summary>
/// Aparelhos via protocolo LocalSend v2.
/// Busca econômica: (1) escuta passiva de anúncios UDP — fica dormindo até chegar algo, não gasta CPU;
/// (2) varredura leve por intervalo (anúncio multicast + confirma os aparelhos já conhecidos);
/// (3) "Buscar agora" faz também uma varredura completa da rede local (/24).
/// </summary>
public class DeviceService : Observable
{
    const int LsPort = 53317;
    static readonly IPAddress Group = IPAddress.Parse("224.0.0.167");
    const string Api = "/api/localsend/v2";

    readonly AppSettings _settings;
    readonly HttpClient _probe, _upload;
    readonly DispatcherTimer _timer = new();
    UdpClient? _listener;
    CancellationTokenSource? _cts;
    bool _scanning, _listening;
    DateTime _lastScan, _nextScan, _scanStarted;
    string _status = "";

    public ObservableCollection<Device> Devices { get; } = new();
    public bool IsScanning { get => _scanning; private set => Set(ref _scanning, value); }
    public bool IsListening { get => _listening; private set => Set(ref _listening, value); }
    public DateTime LastScan { get => _lastScan; private set { if (Set(ref _lastScan, value)) Raise(nameof(ScheduleText)); } }
    public DateTime NextScan { get => _nextScan; private set { if (Set(ref _nextScan, value)) Raise(nameof(ScheduleText)); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public int OnlineCount => Devices.Count(d => d.IsOnline);
    public string OnlineSummary => OnlineCount == 0 ? "nenhum aparelho online"
        : $"{OnlineCount} aparelho{(OnlineCount > 1 ? "s" : "")} · {string.Join(", ", Devices.Where(d => d.IsOnline).Select(d => d.Alias))}";

    public string ScheduleText =>
        (LastScan == default ? "Ainda não buscou" : $"Última busca {LastScan:HH:mm}") +
        (_settings.ScanIntervalMin > 0 && NextScan != default ? $" · próxima {NextScan:HH:mm}" : " · busca automática desligada");

    public DeviceService(AppSettings settings)
    {
        _settings = settings;
        // O LocalSend 1.18+ (protocolo 2.2) exige certificado do cliente, e o fingerprint anunciado
        // tem que ser o hash dele. A Bergamota guarda um certificado próprio e usa o hash como identidade.
        var cert = ClientIdentity.Load(settings.NoSave);
        settings.Fingerprint = ClientIdentity.Fingerprint(cert);
        _probe = new HttpClient(Handler(cert, TimeSpan.FromMilliseconds(900))) { Timeout = TimeSpan.FromSeconds(3) };
        _upload = new HttpClient(Handler(cert, TimeSpan.FromSeconds(5))) { Timeout = Timeout.InfiniteTimeSpan };

        var mine = LocalAddresses(false).Select(a => a.ToString()).ToHashSet();
        foreach (var s in settings.Devices.Where(d => !mine.Contains(d.Ip))) // o LocalSend deste próprio PC não é destino
            Devices.Add(new Device
            {
                Fingerprint = s.Fingerprint, Alias = s.Alias, DeviceModel = s.DeviceModel, DeviceType = s.DeviceType,
                Ip = s.Ip, Port = s.Port, Protocol = s.Protocol, LastSeen = s.LastSeen, ShowInWheel = s.ShowInWheel,
            });
        Devices.CollectionChanged += (_, _) => RaiseCounts();
        _timer.Tick += async (_, _) => await ScanAsync(deep: false);
        settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.ScanIntervalMin)) Reschedule(); };
    }

    public Device? Find(string fingerprint) => Devices.FirstOrDefault(d => d.Fingerprint == fingerprint);

    static SocketsHttpHandler Handler(System.Security.Cryptography.X509Certificates.X509Certificate2 cert, TimeSpan connectTimeout) => new()
    {
        ConnectTimeout = connectTimeout,
        SslOptions =
        {
            RemoteCertificateValidationCallback = (_, _, _, _) => true, // aparelhos LocalSend usam certificado autoassinado
            ClientCertificates = new() { cert },
            // o Windows só envia certificado de cliente se ele bater com a lista de emissores do servidor; aqui forçamos
            LocalCertificateSelectionCallback = (_, _, _, _, _) => cert,
        },
    };

    /// <summary>
    /// POST com JSON e tamanho declarado (Content-Length). O PostAsJsonAsync manda em partes (chunked),
    /// e o servidor do LocalSend fica esperando o tamanho e nunca responde.
    /// </summary>
    static Task<HttpResponseMessage> PostJson(HttpClient http, string url, object body, CancellationToken ct)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return http.PostAsync(url, content, ct);
    }

    /// <summary>Mensagem de erro com as causas internas (a de fora costuma ser genérica).</summary>
    static string Explain(Exception ex)
    {
        if (ex is TaskCanceledException) return "O aparelho não respondeu a tempo (o LocalSend está aberto nele?)";
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
            if (!parts.Contains(e.Message)) parts.Add(e.Message);
        return string.Join(" → ", parts);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        StartListener(_cts.Token);
        Reschedule();
        _ = ScanAsync(deep: false); // uma busca leve ao abrir
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Dispose();
        _timer.Stop();
    }

    void Reschedule()
    {
        _timer.Stop();
        if (_settings.ScanIntervalMin > 0)
        {
            _timer.Interval = TimeSpan.FromMinutes(_settings.ScanIntervalMin);
            _timer.Start();
            NextScan = DateTime.Now + _timer.Interval;
        }
        else NextScan = default;
        Raise(nameof(ScheduleText));
    }

    /// <summary>Busca leve se a última foi há mais de 1 minuto (usada quando a roda abre).</summary>
    public void RefreshIfStale()
    {
        if (!IsScanning && DateTime.Now - LastScan > TimeSpan.FromMinutes(1)) _ = ScanAsync(deep: false);
    }

    // ---------------- descoberta ----------------

    object OurInfo(bool announce) => new
    {
        alias = _settings.Alias,
        version = "2.1",
        deviceModel = "Bergamota",
        deviceType = "desktop",
        fingerprint = _settings.Fingerprint,
        port = LsPort,
        protocol = "https",
        download = false,
        announce,
    };

    void StartListener(CancellationToken ct)
    {
        try
        {
            var u = new UdpClient { ExclusiveAddressUse = false };
            u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            u.Client.Bind(new IPEndPoint(IPAddress.Any, LsPort));
            foreach (var ip in LocalAddresses(onlyWithGateway: false))
                try { u.JoinMulticastGroup(Group, ip); } catch { }
            _listener = u;
            IsListening = true;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var r = await u.ReceiveAsync(ct); // dorme aqui até chegar um pacote
                        var msg = JsonSerializer.Deserialize<LsInfo>(r.Buffer, JsonOpts);
                        if (msg != null) OnSeen(msg, r.RemoteEndPoint.Address.ToString());
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch { /* pacote inválido: ignora */ }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            IsListening = false;
            ActivityLog.Error("LocalSend", "Não consegui escutar a porta 53317 (usando só a varredura): " + ex.Message, notify: false);
        }
    }

    public async Task ScanAsync(bool deep)
    {
        if (IsScanning) return;
        IsScanning = true;
        _scanStarted = DateTime.Now;
        Status = deep ? "Varrendo a rede…" : "Procurando…";
        try
        {
            SendAnnounce();
            var tasks = new List<Task>();
            // confirma os conhecidos direto (rápido e funciona mesmo sem multicast)
            foreach (var d in Devices.ToList())
                tasks.Add(ProbeAsync(d.Ip, d.Port, d.Protocol));
            if (deep) tasks.Add(SweepAsync());
            tasks.Add(Task.Delay(2500)); // tempo para respostas UDP chegarem
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            ActivityLog.Error("Busca de aparelhos", ex.Message, notify: false);
        }
        finally
        {
            foreach (var d in Devices) d.IsOnline = d.LastSeen >= _scanStarted.AddSeconds(-5);
            RaiseCounts();
            LastScan = DateTime.Now;
            if (_settings.ScanIntervalMin > 0) NextScan = DateTime.Now.AddMinutes(_settings.ScanIntervalMin);
            Status = OnlineCount == 0 ? "Nenhum aparelho encontrado" : $"{OnlineCount} online";
            IsScanning = false;
            Persist();
        }
    }

    void SendAnnounce()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(OurInfo(announce: true));
        foreach (var ip in LocalAddresses(onlyWithGateway: false))
        {
            try
            {
                using var u = new UdpClient(new IPEndPoint(ip, 0));
                u.Send(bytes, bytes.Length, new IPEndPoint(Group, LsPort));
            }
            catch { }
        }
    }

    async Task SweepAsync()
    {
        var targets = new List<string>();
        foreach (var ip in LocalAddresses(onlyWithGateway: true))
        {
            var b = ip.GetAddressBytes();
            for (int i = 1; i < 255; i++)
                if (i != b[3]) targets.Add($"{b[0]}.{b[1]}.{b[2]}.{i}");
        }
        using var gate = new SemaphoreSlim(48); // limita conexões simultâneas
        await Task.WhenAll(targets.Select(async t =>
        {
            await gate.WaitAsync();
            try { await ProbeAsync(t, LsPort, "https"); } finally { gate.Release(); }
        }));
    }

    async Task ProbeAsync(string ip, int port, string protocol)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            var res = await PostJson(_probe, $"{protocol}://{ip}:{port}{Api}/register", OurInfo(false), cts.Token);
            if (!res.IsSuccessStatusCode) return;
            var info = await res.Content.ReadFromJsonAsync<LsInfo>(JsonOpts, cts.Token);
            if (info != null) { info.Port ??= port; info.Protocol ??= protocol; OnSeen(info, ip); }
        }
        catch { /* ninguém nesse endereço */ }
    }

    void OnSeen(LsInfo info, string ip)
    {
        if (string.IsNullOrEmpty(info.Fingerprint) || info.Fingerprint == _settings.Fingerprint) return;
        if (ip == "127.0.0.1" || LocalAddresses(false).Any(a => a.ToString() == ip)) return; // este próprio PC
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var d = Find(info.Fingerprint);
            bool isNew = d == null;
            d ??= new Device { Fingerprint = info.Fingerprint };
            d.Alias = info.Alias ?? d.Alias;
            d.DeviceModel = info.DeviceModel ?? d.DeviceModel;
            d.DeviceType = info.DeviceType ?? d.DeviceType;
            d.Ip = ip;
            d.Port = info.Port ?? LsPort;
            d.Protocol = info.Protocol ?? "https";
            d.LastSeen = DateTime.Now;
            d.IsOnline = true;
            if (isNew)
            {
                Devices.Add(d);
                ActivityLog.Info(d.IconName, "Aparelho encontrado", $"{d.Alias} · {d.Ip}");
            }
            RaiseCounts();
        });
    }

    void RaiseCounts() { Raise(nameof(OnlineCount)); Raise(nameof(OnlineSummary)); }

    public void Forget(Device d)
    {
        Devices.Remove(d);
        Persist();
    }

    public void Persist()
    {
        _settings.Devices = Devices.Select(d => new SavedDevice
        {
            Fingerprint = d.Fingerprint, Alias = d.Alias, DeviceModel = d.DeviceModel, DeviceType = d.DeviceType,
            Ip = d.Ip, Port = d.Port, Protocol = d.Protocol, LastSeen = d.LastSeen, ShowInWheel = d.ShowInWheel,
        }).ToList();
        _settings.Save();
    }

    static IEnumerable<IPAddress> LocalAddresses(bool onlyWithGateway)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = ni.GetIPProperties();
            if (onlyWithGateway && !props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                continue;
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (ua.PrefixLength >= 31) continue; // VPNs ponto a ponto (Tailscale etc.)
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;
                yield return ua.Address;
            }
        }
    }

    // ---------------- envio ----------------

    public async Task SendAsync(Device device, IReadOnlyList<string> items)
    {
        var files = new List<(string path, string name)>();
        foreach (var item in items)
        {
            var p = item.TrimEnd('\\');
            if (File.Exists(p)) files.Add((p, Path.GetFileName(p)));
            else if (Directory.Exists(p))
            {
                var parent = Path.GetDirectoryName(p)!;
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    files.Add((f, Path.GetRelativePath(parent, f).Replace('\\', '/')));
            }
        }
        if (files.Count == 0) { ActivityLog.Error("Enviar", "Nada para enviar."); return; }

        var meta = files.Select((f, i) =>
        {
            var fi = new FileInfo(f.path);
            return new
            {
                id = "f" + i,
                fileName = f.name,
                size = fi.Length,
                fileType = Mime(f.path),
                metadata = new { modified = fi.LastWriteTimeUtc.ToString("o"), accessed = fi.LastAccessTimeUtc.ToString("o") },
            };
        }).ToList();
        long total = meta.Sum(m => m.size);
        var baseUrl = $"{device.Protocol}://{device.Ip}:{device.Port}{Api}";
        var info = OurInfo(false);

        ActivityLog.Info("send", $"Aguardando {device.Alias} aceitar", $"{files.Count} arquivo(s) · {Format.Size(total)}", notify: true);
        string? sessionId = null;
        try
        {
            using var prepCts = new CancellationTokenSource(TimeSpan.FromMinutes(3)); // tempo para aceitar no celular
            var prep = await PostJson(_upload, $"{baseUrl}/prepare-upload",
                new { info, files = meta.ToDictionary(m => m.id, m => (object)m) }, prepCts.Token);

            switch ((int)prep.StatusCode)
            {
                case 204: ActivityLog.Info("send", "Nada a enviar", $"{device.Alias} já tinha os arquivos."); return;
                case 401: ActivityLog.Error("Enviar", $"{device.Alias} pede PIN. Desligue o PIN no LocalSend do aparelho."); return;
                case 403: ActivityLog.Error("Enviar", $"{device.Alias} recusou o envio."); return;
                case 409: ActivityLog.Error("Enviar", $"{device.Alias} está ocupado com outro envio."); return;
                case 429: ActivityLog.Error("Enviar", $"{device.Alias} recebeu pedidos demais, tente de novo."); return;
            }
            prep.EnsureSuccessStatusCode();
            var resp = await prep.Content.ReadFromJsonAsync<PrepareResponse>(JsonOpts) ?? throw new IOException("Resposta vazia do aparelho.");
            sessionId = resp.SessionId;

            int sent = 0;
            for (int i = 0; i < files.Count; i++)
            {
                var id = "f" + i;
                if (resp.Files == null || !resp.Files.TryGetValue(id, out var token)) continue; // o aparelho pulou esse
                await using var fs = new FileStream(files[i].path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
                using var content = new StreamContent(fs, 1 << 16);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                var url = $"{baseUrl}/upload?sessionId={Uri.EscapeDataString(sessionId)}&fileId={id}&token={Uri.EscapeDataString(token)}";
                var up = await _upload.PostAsync(url, content);
                up.EnsureSuccessStatusCode();
                sent++;
            }
            ActivityLog.Info(device.IconName, $"Enviado para {device.Alias}", $"{sent} arquivo(s) · {Format.Size(total)} · {ActionRunner.Describe(items)}", notify: true);
        }
        catch (Exception ex)
        {
            if (sessionId != null)
                try { await _probe.PostAsync($"{baseUrl}/cancel?sessionId={Uri.EscapeDataString(sessionId)}", null); } catch { }
            ActivityLog.Error($"Enviar para {device.Alias}", Explain(ex));
        }
    }

    static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp",
        ".heic" => "image/heic", ".bmp" => "image/bmp", ".svg" => "image/svg+xml",
        ".mp4" => "video/mp4", ".mov" => "video/quicktime", ".mkv" => "video/x-matroska", ".webm" => "video/webm",
        ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".flac" => "audio/flac", ".m4a" => "audio/mp4", ".ogg" => "audio/ogg",
        ".pdf" => "application/pdf", ".txt" => "text/plain", ".html" => "text/html", ".json" => "application/json",
        ".zip" => "application/zip", ".rar" => "application/vnd.rar", ".7z" => "application/x-7z-compressed",
        ".apk" => "application/vnd.android.package-archive",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream",
    };

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    class LsInfo
    {
        public string? Alias { get; set; }
        public string? Version { get; set; }
        public string? DeviceModel { get; set; }
        public string? DeviceType { get; set; }
        public string Fingerprint { get; set; } = "";
        public int? Port { get; set; }
        public string? Protocol { get; set; }
        public bool? Download { get; set; }
        public bool? Announce { get; set; }
    }

    class PrepareResponse
    {
        public string SessionId { get; set; } = "";
        public Dictionary<string, string>? Files { get; set; }
    }
}
