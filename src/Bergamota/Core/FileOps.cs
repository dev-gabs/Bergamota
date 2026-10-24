using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace Bergamota.Core;

/// <summary>
/// Mover/copiar para uma pasta usando a cópia do próprio Windows: mostra a janela de progresso
/// e pergunta o que fazer quando já existe um arquivo com o mesmo nome.
/// </summary>
public static class FileOps
{
    public static Task TransferAsync(IReadOnlyList<string> items, string destination, bool copy)
    {
        var verb = copy ? "Copiado" : "Movido";
        return Task.Run(() =>
        {
            int done = 0;
            Exception? error = null;
            // as janelas de progresso do Windows precisam de uma thread STA
            var t = new Thread(() =>
            {
                foreach (var item in items)
                {
                    var src = item.TrimEnd('\\');
                    var target = Path.Combine(destination, Path.GetFileName(src));
                    if (string.Equals(Path.GetDirectoryName(src), destination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue; // já está lá
                    try
                    {
                        if (Directory.Exists(src))
                        {
                            if (copy) FileSystem.CopyDirectory(src, target, UIOption.AllDialogs, UICancelOption.ThrowException);
                            else FileSystem.MoveDirectory(src, target, UIOption.AllDialogs, UICancelOption.ThrowException);
                        }
                        else
                        {
                            if (copy) FileSystem.CopyFile(src, target, UIOption.AllDialogs, UICancelOption.ThrowException);
                            else FileSystem.MoveFile(src, target, UIOption.AllDialogs, UICancelOption.ThrowException);
                        }
                        done++;
                    }
                    catch (OperationCanceledException) { break; } // cancelou na janela do Windows
                    catch (Exception ex) { error = ex; }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (done > 0) ActivityLog.Info("move", $"{verb} para {Path.GetFileName(destination.TrimEnd('\\'))}", $"{done} item(ns) · {destination}", notify: true);
            if (error != null) ActivityLog.Error(copy ? "Copiar" : "Mover", error.Message);
        });
    }
}
