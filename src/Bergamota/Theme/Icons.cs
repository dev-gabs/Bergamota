using System.Windows.Media;

namespace Bergamota.Theme;

/// <summary>
/// Ícones em traço (grade 24x24, estilo Lucide), desenhados como geometria do WPF.
/// O usuário escolhe entre os de <see cref="Pickable"/> no Editar Roda.
/// </summary>
public static class Icons
{
    // círculo como dois arcos: M cx-r,cy A r,r 0 1 0 cx+r,cy A r,r 0 1 0 cx-r,cy
    static string C(double cx, double cy, double r) =>
        FormattableString.Invariant($"M{cx - r},{cy} A{r},{r} 0 1 0 {cx + r},{cy} A{r},{r} 0 1 0 {cx - r},{cy} ");

    const string FileShape = "M14,2 H6 A2,2 0 0 0 4,4 V20 A2,2 0 0 0 6,22 H18 A2,2 0 0 0 20,20 V8 Z M14,2 V8 H20 ";
    const string FolderShape = "M3,6 A2,2 0 0 1 5,4 H9 L11,6.5 H19 A2,2 0 0 1 21,8.5 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 Z ";

    static readonly Dictionary<string, string> Data = new()
    {
        ["extract"] = "M3,3 H21 V8 H3 Z M5,8 V19 A2,2 0 0 0 7,21 H17 A2,2 0 0 0 19,19 V8 M12,11 V17 M9,14 L12,17 L15,14",
        ["extract-folder"] = FolderShape + "M12,10 V16 M9,13 L12,16 L15,13",
        ["zip"] = FileShape + "M10,6 H11 M10,9 H11 M10,12 H11 M9,15 H12 V18 H9 Z",
        ["rename"] = "M12,20 H21 M16.5,3.5 A2.12,2.12 0 0 1 19.5,6.5 L7,19 L3,20 L4,16 Z",
        ["openwith"] = "M4,3 H9 A1,1 0 0 1 10,4 V9 A1,1 0 0 1 9,10 H4 A1,1 0 0 1 3,9 V4 A1,1 0 0 1 4,3 Z M15,3 H20 A1,1 0 0 1 21,4 V9 A1,1 0 0 1 20,10 H15 A1,1 0 0 1 14,9 V4 A1,1 0 0 1 15,3 Z M4,14 H9 A1,1 0 0 1 10,15 V20 A1,1 0 0 1 9,21 H4 A1,1 0 0 1 3,20 V15 A1,1 0 0 1 4,14 Z M17.5,14 V21 M14,17.5 H21",
        ["props"] = "M4,6 H14 M18,6 H20 M4,12 H8 M12,12 H20 M4,18 H16 " + C(16, 6, 2) + C(10, 12, 2) + C(18, 18, 2),
        ["info"] = C(12, 12, 9) + "M12,16 V11 M12,8 L12.01,8",
        ["link"] = "M10,13 A5,5 0 0 0 17.5,13.5 L20.5,10.5 A5,5 0 0 0 13.5,3.5 L11.8,5.2 M14,11 A5,5 0 0 0 6.5,10.5 L3.5,13.5 A5,5 0 0 0 10.5,20.5 L12.2,18.8",
        ["copy"] = "M10,8 H19 A2,2 0 0 1 21,10 V19 A2,2 0 0 1 19,21 H10 A2,2 0 0 1 8,19 V10 A2,2 0 0 1 10,8 Z M16,8 V5 A2,2 0 0 0 14,3 H5 A2,2 0 0 0 3,5 V14 A2,2 0 0 0 5,16 H8",
        ["phone"] = "M8.5,2 H15.5 A2.5,2.5 0 0 1 18,4.5 V19.5 A2.5,2.5 0 0 1 15.5,22 H8.5 A2.5,2.5 0 0 1 6,19.5 V4.5 A2.5,2.5 0 0 1 8.5,2 Z M11,18 H13",
        ["laptop"] = "M5.5,4 H18.5 A1.5,1.5 0 0 1 20,5.5 V15 H4 V5.5 A1.5,1.5 0 0 1 5.5,4 Z M2,19 H22",
        ["desktop"] = "M3.5,3 H20.5 A1.5,1.5 0 0 1 22,4.5 V16 H2 V4.5 A1.5,1.5 0 0 1 3.5,3 Z M8,21 H16 M12,16 V21",
        ["send"] = "M22,2 L11,13 M22,2 L15,22 L11,13 L2,9 Z",
        ["share"] = C(18, 5, 3) + C(6, 12, 3) + C(18, 19, 3) + "M8.6,13.5 L15.4,17.5 M15.4,6.5 L8.6,10.5",
        ["folder"] = FolderShape,
        ["file"] = FileShape,
        ["filetext"] = FileShape + "M8,13 H16 M8,17 H16 M8,9 H10",
        ["image"] = "M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M21,15 L16,10 L5,21 " + C(9, 9, 2),
        ["music"] = "M9,18 V5 L21,3 V16 " + C(6, 18, 3) + C(18, 16, 3),
        ["video"] = "M4,5 H14 A2,2 0 0 1 16,7 V17 A2,2 0 0 1 14,19 H4 A2,2 0 0 1 2,17 V7 A2,2 0 0 1 4,5 Z M16,10 L22,7 V17 L16,14",
        ["code"] = "M16,18 L22,12 L16,6 M8,6 L2,12 L8,18",
        ["terminal"] = "M4,17 L10,12 L4,7 M12,19 H20",
        ["trash"] = "M3,6 H21 M8,6 V4 H16 V6 M6,6 L7,21 H17 L18,6",
        ["scissors"] = C(6, 6, 3) + C(6, 18, 3) + "M20,4 L8.1,15.9 M14.5,14.5 L20,20 M8.1,8.1 L12,12",
        ["print"] = "M6,9 V2 H18 V9 M6,18 H4 A2,2 0 0 1 2,16 V11 A2,2 0 0 1 4,9 H20 A2,2 0 0 1 22,11 V16 A2,2 0 0 1 20,18 H18 M6,14 H18 V22 H6 Z",
        ["mail"] = "M4,4 H20 A2,2 0 0 1 22,6 V18 A2,2 0 0 1 20,20 H4 A2,2 0 0 1 2,18 V6 A2,2 0 0 1 4,4 Z M22,7 L12,13 L2,7",
        ["cloud"] = "M17.5,19 H9 A7,7 0 1 1 15.7,10 H17.5 A4.5,4.5 0 1 1 17.5,19 Z",
        ["download"] = "M12,3 V15 M7,10 L12,15 L17,10 M4,21 H20",
        ["upload"] = "M12,21 V9 M7,14 L12,9 L17,14 M4,3 H20",
        ["lock"] = "M5,11 H19 A2,2 0 0 1 21,13 V20 A2,2 0 0 1 19,22 H5 A2,2 0 0 1 3,20 V13 A2,2 0 0 1 5,11 Z M7,11 V7 A5,5 0 0 1 17,7 V11",
        ["shield"] = "M12,22 C12,22 20,18 20,12 V5 L12,2 L4,5 V12 C4,18 12,22 12,22 Z",
        ["search"] = C(11, 11, 8) + "M21,21 L16.65,16.65",
        ["globe"] = C(12, 12, 10) + "M2,12 H22 M12,2 A15.3,15.3 0 0 1 16,12 A15.3,15.3 0 0 1 12,22 A15.3,15.3 0 0 1 8,12 A15.3,15.3 0 0 1 12,2 Z",
        ["star"] = "M12,2 L15.1,8.3 L22,9.3 L17,14.1 L18.2,21 L12,17.8 L5.8,21 L7,14.1 L2,9.3 L8.9,8.3 Z",
        ["heart"] = "M19,14 C20.5,12.5 22,10.8 22,8.5 A5.5,5.5 0 0 0 16.5,3 C14.7,3 13.5,3.5 12,5 C10.5,3.5 9.3,3 7.5,3 A5.5,5.5 0 0 0 2,8.5 C2,10.8 3.5,12.5 5,14 L12,21 Z",
        ["bolt"] = "M13,2 L4,14 H11 L10,22 L19,10 H12 Z",
        ["palette"] = "M12,22 A10,10 0 1 1 22,12 C22,14.8 19.8,16 18,16 H16 A2,2 0 0 0 14.5,19.3 A1.6,1.6 0 0 1 12,22 Z M7.5,10.5 L7.51,10.5 M12,7 L12.01,7 M16.5,10.5 L16.51,10.5",
        ["move"] = FolderShape + "M9,13 H15 M12.5,10.5 L15,13 L12.5,15.5",
        ["convert"] = "M4,8 H17 M13,4 L17,8 L13,12 M20,16 H7 M11,12 L7,16 L11,20",
        ["gear"] = C(12, 12, 3) + C(12, 12, 7) + "M12,2 V5 M12,19 V22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M2,12 H5 M19,12 H22 M4.9,19.1 L7,17 M17,7 L19.1,4.9",

        // só da interface (não aparecem no seletor)
        ["home"] = "M3,10.5 L12,3 L21,10.5 V20 A1,1 0 0 1 20,21 H15 V15 H9 V21 H4 A1,1 0 0 1 3,20 Z",
        ["wheel"] = C(12, 12, 9) + C(12, 12, 3.5) + "M12,3 V8.5 M12,15.5 V21 M3,12 H8.5 M15.5,12 H21 M5.6,5.6 L9.5,9.5 M14.5,14.5 L18.4,18.4 M18.4,5.6 L14.5,9.5 M9.5,14.5 L5.6,18.4",
        ["plus-circle"] = C(12, 12, 9) + "M12,8 V16 M8,12 H16",
        ["plus"] = "M12,5 V19 M5,12 H19",
        ["x"] = "M6,6 L18,18 M18,6 L6,18",
        ["check"] = "M5,12 L10,17 L19,7",
        ["play"] = "M7,4 V20 L20,12 Z",
        ["eye"] = "M2,12 C2,12 5.5,5 12,5 C18.5,5 22,12 22,12 C22,12 18.5,19 12,19 C5.5,19 2,12 2,12 Z " + C(12, 12, 3),
        ["keyboard"] = "M4,5 H20 A2,2 0 0 1 22,7 V17 A2,2 0 0 1 20,19 H4 A2,2 0 0 1 2,17 V7 A2,2 0 0 1 4,5 Z M6,9 L6.01,9 M10,9 L10.01,9 M14,9 L14.01,9 M18,9 L18.01,9 M6,13 L6.01,13 M18,13 L18.01,13 M10,13 H14 M7,16 H17",
        ["edge"] = "M4,4 H20 A2,2 0 0 1 22,6 V16 A2,2 0 0 1 20,18 H4 A2,2 0 0 1 2,16 V6 A2,2 0 0 1 4,4 Z M19,7 V15",
        ["wifi"] = "M2,8.8 A15,15 0 0 1 22,8.8 M5,12.6 A10,10 0 0 1 19,12.6 M8.5,16.4 A5,5 0 0 1 15.5,16.4 M12,20 L12.01,20",
        ["refresh"] = "M21,12 A9,9 0 1 1 18.4,5.6 M21,3 V8 H16",
        ["up"] = "M18,15 L12,9 L6,15",
        ["down"] = "M6,9 L12,15 L18,9",
        ["log"] = "M4,17 L10,12 L4,7 M12,19 H20",
    };

    public static readonly string[] Pickable =
    {
        "extract", "extract-folder", "zip", "convert", "move", "rename", "openwith", "props", "info", "link", "copy",
        "phone", "laptop", "desktop", "send", "share", "folder", "file", "filetext", "image", "music",
        "video", "code", "terminal", "trash", "scissors", "print", "mail", "cloud", "download", "upload",
        "lock", "shield", "search", "globe", "star", "heart", "bolt", "palette", "gear",
    };

    static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry Get(string? name)
    {
        name ??= "file";
        if (Cache.TryGetValue(name, out var g)) return g;
        var data = Data.TryGetValue(name, out var d) ? d : Data["file"];
        g = Geometry.Parse(data);
        g.Freeze();
        return Cache[name] = g;
    }
}
