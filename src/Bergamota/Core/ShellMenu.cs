using System.IO;
using System.Runtime.InteropServices;

namespace Bergamota.Core;

/// <summary>
/// Acessa o menu de contexto do Explorer (IContextMenu) para um grupo de arquivos:
/// lista os itens, chama um item pelo texto ou um verbo (properties, openas...).
/// Precisa rodar na thread de interface (STA).
/// </summary>
public static class ShellMenu
{
    /// <summary>Lista os textos dos itens do menu de contexto de um arquivo (para o editor).</summary>
    public static List<string> ListItems(string file)
    {
        var result = new List<string>();
        WithMenu(new[] { file }, IntPtr.Zero, (menu, hmenu) =>
        {
            Walk(hmenu, "", (text, _) => result.Add(text));
            return true;
        });
        return result.Distinct().ToList();
    }

    /// <summary>Executa o item cujo texto bate com <paramref name="text"/> (ignora maiúsculas e o &amp;).</summary>
    public static bool InvokeByText(IReadOnlyList<string> files, string text, IntPtr hwnd)
    {
        var target = Normalize(text);
        bool done = false;
        foreach (var group in GroupByFolder(files))
        {
            done |= WithMenu(group, hwnd, (menu, hmenu) =>
            {
                uint? exact = null, partial = null;
                Walk(hmenu, "", (t, id) =>
                {
                    var n = Normalize(t.Contains(" › ") ? t[(t.LastIndexOf(" › ") + 3)..] : t);
                    if (exact == null && (n == target || Normalize(t) == target)) exact = id;
                    else if (partial == null && n.Contains(target)) partial = id;
                });
                var id = exact ?? partial;
                if (id == null) return false;
                return Invoke(menu, hwnd, (IntPtr)(id.Value - FirstId), null) == 0;
            });
        }
        return done;
    }

    /// <summary>Executa um verbo do shell (ex.: "properties", "openas").</summary>
    public static bool InvokeVerb(IReadOnlyList<string> files, string verb, IntPtr hwnd)
    {
        bool ok = false;
        foreach (var group in GroupByFolder(files))
            ok |= WithMenu(group, hwnd, (menu, _) => Invoke(menu, hwnd, IntPtr.Zero, verb) == 0);
        return ok;
    }

    // ------------------------------------------------------------------

    const uint FirstId = 1, LastId = 0x7FFF;
    const uint CMF_NORMAL = 0, CMF_EXTENDEDVERBS = 0x100;

    static string Normalize(string s) => s.Replace("&", "").Split('\t')[0].Trim().TrimEnd('.', '…').ToLowerInvariant();

    static IEnumerable<string[]> GroupByFolder(IReadOnlyList<string> files) =>
        files.GroupBy(f => Path.GetDirectoryName(f.TrimEnd('\\')) ?? "", StringComparer.OrdinalIgnoreCase).Select(g => g.ToArray());

    static bool WithMenu(string[] files, IntPtr hwnd, Func<IContextMenu, IntPtr, bool> body)
    {
        var pidls = new List<IntPtr>();
        IntPtr hmenu = IntPtr.Zero, menuPtr = IntPtr.Zero, folderPtr = IntPtr.Zero;
        try
        {
            foreach (var f in files)
            {
                if (SHParseDisplayName(f, IntPtr.Zero, out var pidl, 0, out _) == 0) pidls.Add(pidl);
            }
            if (pidls.Count == 0) return false;

            var iidFolder = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidls[0], ref iidFolder, out folderPtr, out _) != 0) return false;
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            var children = pidls.Select(ILFindLastID).ToArray();

            var iidMenu = typeof(IContextMenu).GUID;
            if (folder.GetUIObjectOf(hwnd, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out menuPtr) != 0) return false;
            var menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);

            hmenu = CreatePopupMenu();
            menu.QueryContextMenu(hmenu, 0, FirstId, LastId, CMF_NORMAL | CMF_EXTENDEDVERBS);
            return body(menu, hmenu);
        }
        catch (Exception ex)
        {
            ActivityLog.Error("Menu de contexto", ex.Message, notify: false);
            return false;
        }
        finally
        {
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (menuPtr != IntPtr.Zero) Marshal.Release(menuPtr);
            if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
            foreach (var p in pidls) Marshal.FreeCoTaskMem(p);
        }
    }

    static int Invoke(IContextMenu menu, IntPtr hwnd, IntPtr idOffset, string? verb)
    {
        IntPtr verbA = IntPtr.Zero, verbW = IntPtr.Zero;
        try
        {
            if (verb != null) { verbA = Marshal.StringToHGlobalAnsi(verb); verbW = Marshal.StringToHGlobalUni(verb); }
            Native.GetCursorPos(out var pt);
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = hwnd,
                lpVerb = verb != null ? verbA : idOffset,
                lpVerbW = verb != null ? verbW : idOffset,
                nShow = 1, // SW_SHOWNORMAL
                ptInvoke = pt,
            };
            return menu.InvokeCommand(ref info);
        }
        finally
        {
            if (verbA != IntPtr.Zero) Marshal.FreeHGlobal(verbA);
            if (verbW != IntPtr.Zero) Marshal.FreeHGlobal(verbW);
        }
    }

    // percorre o menu (e submenus) chamando visit(texto, id)
    static void Walk(IntPtr hmenu, string prefix, Action<string, uint> visit)
    {
        int count = GetMenuItemCount(hmenu);
        for (uint i = 0; i < count; i++)
        {
            var mii = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = MIIM_STRING | MIIM_ID | MIIM_SUBMENU | MIIM_FTYPE };
            if (!GetMenuItemInfo(hmenu, i, true, ref mii)) continue;
            if ((mii.fType & MFT_SEPARATOR) != 0) continue;
            string text = "";
            if (mii.cch > 0)
            {
                mii.cch++;
                var buf = Marshal.AllocHGlobal((int)mii.cch * 2);
                try
                {
                    mii.dwTypeData = buf;
                    if (GetMenuItemInfo(hmenu, i, true, ref mii)) text = Marshal.PtrToStringUni(buf) ?? "";
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            text = text.Replace("&", "").Split('\t')[0].Trim();
            if (text.Length == 0) continue;
            if (mii.hSubMenu != IntPtr.Zero) Walk(mii.hSubMenu, prefix + text + " › ", visit);
            else if (mii.wID >= FirstId && mii.wID <= LastId) visit(prefix + text, mii.wID);
        }
    }

    // ---------------- interop ----------------

    const int CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    const uint MIIM_ID = 0x2, MIIM_SUBMENU = 0x4, MIIM_STRING = 0x40, MIIM_FTYPE = 0x100, MFT_SEPARATOR = 0x800;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);
    [DllImport("shell32.dll")] static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMenuItemInfo(IntPtr hMenu, uint item, bool byPosition, ref MENUITEMINFO mii);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MENUITEMINFO
    {
        public uint cbSize, fMask, fType, fState, wID;
        public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize, fMask;
        public IntPtr hwnd, lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow, dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
        public Native.POINT ptInvoke;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint inOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    }
}
