using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bergamota.Models;

public enum SliceKind { Native, ContextMenu, Program, Send }

/// <summary>Ações embutidas da Bergamota (tipo "Ação nativa").</summary>
public record NativeAction(string Id, string Title, string Description, string Icon)
{
    public static readonly NativeAction[] All =
    {
        new("extract-here", "Extrair aqui", "Extrai com o WinRAR na mesma pasta do arquivo", "extract"),
        new("extract-folder", "Extrair para pasta", "Extrai com o WinRAR numa pasta com o nome do arquivo", "extract-folder"),
        new("zip", "Compactar ZIP", "Cria um .zip com os itens arrastados", "zip"),
        new("powerrename", "Renomear", "Abre o PowerRename com os itens", "rename"),
        new("openwith", "Abrir com…", "Escolhe o aplicativo para abrir o arquivo", "openwith"),
        new("properties", "Propriedades", "Mostra as propriedades do Windows", "props"),
        new("copypath", "Copiar caminho", "Copia o caminho completo entre aspas", "link"),
        new("convert", "Converter e ajustar…", "Converte o formato e ajusta imagens e PDFs", "convert"),
        new("moveto", "Mover para…", "Move ou copia para uma pasta favorita", "move"),
    };

    public static NativeAction? Find(string? id) => All.FirstOrDefault(a => a.Id == id);
}

public class Slice : Observable
{
    SliceKind _kind = SliceKind.Native;
    string _action = "zip";
    string _label = "Compactar ZIP";
    string _icon = "zip";
    string _args = "";
    int _index;

    public SliceKind Kind
    {
        get => _kind;
        set
        {
            var oldDefault = DefaultLabel();
            if (!Set(ref _kind, value)) return;
            Action = value switch { SliceKind.Native => "zip", _ => "" };
            ApplyDefaults(oldDefault);
            Raise(nameof(Description));
            Raise(nameof(SwatchColor));
        }
    }

    /// <summary>Native: id da ação · ContextMenu: texto do item · Program: caminho do .exe · Send: fingerprint do aparelho ("" = escolher na hora).</summary>
    public string Action
    {
        get => _action;
        set
        {
            var oldDefault = DefaultLabel();
            if (!Set(ref _action, value ?? "")) return;
            ApplyDefaults(oldDefault);
            Raise(nameof(Description));
        }
    }

    public string Label { get => _label; set => Set(ref _label, value); }
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    /// <summary>Cor da fatia (hex). Vazio = automática (verde-bergamota; laranja para "Enviar para").</summary>
    public string Color { get => _color; set { if (Set(ref _color, value ?? "")) Raise(nameof(SwatchColor)); } }
    string _color = "";

    /// <summary>A cor que realmente aparece (a escolhida ou a automática).</summary>
    [JsonIgnore] public string SwatchColor => _color.Length > 0 ? _color : Kind == SliceKind.Send ? "#C45218" : "#C9DC5A";
    /// <summary>Só para "Programa": argumentos; {files} vira a lista de arquivos entre aspas.</summary>
    public string Args { get => _args; set => Set(ref _args, value); }

    [JsonIgnore] public int Index { get => _index; set { if (Set(ref _index, value)) Raise(nameof(Code)); } }
    [JsonIgnore] public string Code => $"S{Index + 1}";

    /// <summary>Texto curto do que a fatia faz (aparece no popup do hover e no dashboard).</summary>
    [JsonIgnore]
    public string Description => Kind switch
    {
        SliceKind.Native => NativeAction.Find(Action)?.Description ?? "",
        SliceKind.ContextMenu => $"Item “{Action}” do menu de contexto",
        SliceKind.Program => string.IsNullOrWhiteSpace(Action) ? "Escolha um programa" : $"Abre com {Path.GetFileNameWithoutExtension(Action)}",
        SliceKind.Send => string.IsNullOrEmpty(Action) ? "Aparelho (LocalSend) ou pasta favorita, na hora"
            : $"Envia para {App.Devices?.Find(Action)?.Alias ?? "o aparelho"} · LocalSend",
        _ => ""
    };

    [JsonIgnore]
    public string KindText => Kind switch
    {
        SliceKind.Native => "Ação nativa",
        SliceKind.ContextMenu => "Menu de contexto",
        SliceKind.Program => "Programa",
        _ => "Enviar para",
    };

    string DefaultLabel() => Kind switch
    {
        SliceKind.Native => NativeAction.Find(Action)?.Title ?? "",
        SliceKind.ContextMenu => Action,
        SliceKind.Program => string.IsNullOrWhiteSpace(Action) ? "Programa" : Path.GetFileNameWithoutExtension(Action),
        SliceKind.Send => string.IsNullOrEmpty(Action) ? "Enviar para…" : App.Devices?.Find(Action)?.Alias ?? "Aparelho",
        _ => ""
    };

    string DefaultIcon() => Kind switch
    {
        SliceKind.Native => NativeAction.Find(Action)?.Icon ?? "file",
        SliceKind.ContextMenu => "link",
        SliceKind.Program => "terminal",
        SliceKind.Send => string.IsNullOrEmpty(Action) ? "send" : App.Devices?.Find(Action)?.IconName ?? "phone",
        _ => "file"
    };

    // só troca rótulo/ícone se o usuário não personalizou
    void ApplyDefaults(string oldDefaultLabel)
    {
        if (_loading) return;
        if (string.IsNullOrWhiteSpace(Label) || Label == oldDefaultLabel) Label = DefaultLabel();
        Icon = DefaultIcon();
    }

    bool _loading;
    public static Slice Create(SliceKind kind, string action, string? label = null, string? icon = null)
    {
        var s = new Slice { _loading = true };
        s.Kind = kind;
        s.Action = action;
        s._loading = false;
        s.Label = label ?? s.DefaultLabel();
        s.Icon = icon ?? s.DefaultIcon();
        return s;
    }

    public Slice Clone() => new() { _kind = Kind, _action = Action, _label = Label, _icon = Icon, _args = Args, _color = Color };

    public static readonly string[] Palette = { "#C9DC5A", "#E8923A", "#C45218", "#5FB08A", "#4FA3D9", "#B48CE0", "#E0645A", "#F2D45C" };

    [JsonConstructor] public Slice() { }
    internal void EndLoad() => _loading = false;
}

public class Profile : Observable
{
    string _name = "Novo perfil";
    string _extensions = "";
    string _color = "#C9DC5A";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set => Set(ref _name, value); }
    /// <summary>Extensões separadas por espaço (".rar .zip"). Vazio = qualquer arquivo.</summary>
    public string Extensions { get => _extensions; set => Set(ref _extensions, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public ObservableCollection<Slice> Slices { get; set; } = new();

    public Profile()
    {
        Slices.CollectionChanged += (_, _) => Reindex();
    }

    public void Reindex()
    {
        for (int i = 0; i < Slices.Count; i++) Slices[i].Index = i;
    }

    static Slice? _convert;

    /// <summary>
    /// Fatias como aparecem na roda: as do perfil + "Converter para…" (se ligada), e o ângulo inicial
    /// que deixa essa fatia exatamente embaixo.
    /// </summary>
    public (List<Slice> Slices, double Start) WheelLayout(bool withConvert)
    {
        var list = Slices.ToList();
        int k = list.FindIndex(s => s.Kind == SliceKind.Native && s.Action == "convert");
        if (k < 0 && withConvert)
        {
            k = (list.Count + 1) / 2; // metade da roda = embaixo
            list.Insert(k, _convert ??= Slice.Create(SliceKind.Native, "convert"));
        }
        double step = 360.0 / Math.Max(1, list.Count);
        return (list, k >= 0 ? 90 - k * step : -90);
    }

    public bool Matches(IEnumerable<string> files)
    {
        var exts = Extensions.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()).ToHashSet();
        if (exts.Count == 0) return false;
        var list = files.ToList();
        return list.Count > 0 && list.All(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())
                                               || (exts.Contains(".pasta") && Directory.Exists(f)));
    }
}

public class TriggerSettings : Observable
{
    bool _keyEnabled = true, _edgeEnabled = true, _hotkeyEnabled;
    string _key = "CapsLock";
    int _dwellMs = 300;

    public bool KeyEnabled { get => _keyEnabled; set => Set(ref _keyEnabled, value); }
    /// <summary>Nome da tecla (Key do WPF) ou "XButton1", "XButton2", "MButton".</summary>
    public string Key { get => _key; set => Set(ref _key, value); }
    public bool EdgeEnabled { get => _edgeEnabled; set => Set(ref _edgeEnabled, value); }
    /// <summary>Zonas ligadas: Top, Bottom, Left, Right, TL, TR, BL, BR.</summary>
    public ObservableCollection<string> Edges { get; set; } = new() { "Right" };
    public int DwellMs { get => _dwellMs; set => Set(ref _dwellMs, value); }
    /// <summary>Win+Shift+B abre a roda com o que está selecionado no Explorer.</summary>
    public bool HotkeyEnabled { get => _hotkeyEnabled; set => Set(ref _hotkeyEnabled, value); }
}

public class SavedDevice
{
    public string Fingerprint { get; set; } = "";
    public string Alias { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? DeviceType { get; set; }
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 53317;
    public string Protocol { get; set; } = "https";
    public DateTime LastSeen { get; set; }
    public bool ShowInWheel { get; set; } = true;
}

public class AppSettings : Observable
{
    bool _autoProfile = true, _startWithWindows = true, _closeOnOutside = true;
    int _wheelSize = 520, _scanIntervalMin = 5;
    string _alias = Environment.MachineName;

    public int Version { get; set; } = 1;
    public ObservableCollection<Profile> Profiles { get; set; } = new();
    public TriggerSettings Triggers { get; set; } = new();
    public bool AutoProfile { get => _autoProfile; set => Set(ref _autoProfile, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool CloseOnOutside { get => _closeOnOutside; set => Set(ref _closeOnOutside, value); }
    /// <summary>Fatia "Converter para…" fixa embaixo em todos os perfis.</summary>
    public bool ConvertSlice { get => _convertSlice; set => Set(ref _convertSlice, value); }
    bool _convertSlice = true;

    /// <summary>Pastas da fatia "Mover para…" (gerenciadas em Configurações).</summary>
    public ObservableCollection<string> FavoriteFolders { get; set; } = new();
    /// <summary>Últimas pastas escolhidas em "Outra pasta…" (até 4).</summary>
    public List<string> RecentFolders { get; set; } = new();
    /// <summary>"Mover para…" copia em vez de mover.</summary>
    public bool MoveCopies { get => _moveCopies; set => Set(ref _moveCopies, value); }
    bool _moveCopies;
    /// <summary>Diâmetro da roda em DIPs.</summary>
    public int WheelSize { get => _wheelSize; set => Set(ref _wheelSize, value); }
    /// <summary>Intervalo da busca automática de aparelhos; 0 = desligada.</summary>
    public int ScanIntervalMin { get => _scanIntervalMin; set => Set(ref _scanIntervalMin, value); }
    public string Alias { get => _alias; set => Set(ref _alias, value); }
    public string Fingerprint { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public List<SavedDevice> Devices { get; set; } = new();

    // ---------- persistência ----------
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bergamota");
    static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (s != null && s.Profiles.Count > 0)
                {
                    foreach (var p in s.Profiles) { foreach (var sl in p.Slices) sl.EndLoad(); p.Reindex(); }
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Core.ActivityLog.Error("Configurações", "Não consegui ler settings.json, usando o padrão: " + ex.Message);
        }
        var d = new AppSettings();
        d.AddDefaultProfiles();
        return d;
    }

    /// <summary>Para testes: nunca grava no disco.</summary>
    [JsonIgnore] public bool NoSave { get; set; }

    public void Save()
    {
        if (NoSave) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Core.ActivityLog.Error("Configurações", "Não consegui salvar: " + ex.Message);
        }
    }

    void AddDefaultProfiles()
    {
        var padrao = new Profile { Name = "Padrão", Color = "#C9DC5A" };
        foreach (var s in new[]
        {
            Slice.Create(SliceKind.Native, "extract-here"),
            Slice.Create(SliceKind.Native, "zip"),
            Slice.Create(SliceKind.Native, "powerrename"),
            Slice.Create(SliceKind.Send, ""),
            Slice.Create(SliceKind.Native, "openwith"),
            Slice.Create(SliceKind.Native, "properties"),
            Slice.Create(SliceKind.Native, "copypath"),
            Slice.Create(SliceKind.Native, "extract-folder"),
        }) padrao.Slices.Add(s);

        var comp = new Profile { Name = "Compactados", Extensions = ".rar .zip .7z .tar .gz .tgz .bz2 .xz", Color = "#E8923A" };
        foreach (var s in new[]
        {
            Slice.Create(SliceKind.Native, "extract-here"),
            Slice.Create(SliceKind.Native, "extract-folder"),
            Slice.Create(SliceKind.Send, ""),
            Slice.Create(SliceKind.Native, "properties"),
            Slice.Create(SliceKind.Native, "openwith"),
            Slice.Create(SliceKind.Native, "copypath"),
        }) comp.Slices.Add(s);

        var img = new Profile { Name = "Imagens", Extensions = ".png .jpg .jpeg .webp .heic .gif .bmp .tiff", Color = "#5FB08A" };
        foreach (var s in new[]
        {
            Slice.Create(SliceKind.Send, ""),
            Slice.Create(SliceKind.Native, "zip"),
            Slice.Create(SliceKind.Native, "powerrename"),
            Slice.Create(SliceKind.Native, "openwith"),
            Slice.Create(SliceKind.Native, "properties"),
            Slice.Create(SliceKind.Native, "copypath"),
        }) img.Slices.Add(s);

        Profiles.Add(padrao);
        Profiles.Add(comp);
        Profiles.Add(img);
    }
}
