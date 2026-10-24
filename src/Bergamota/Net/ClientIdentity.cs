using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bergamota.Models;

namespace Bergamota.Net;

/// <summary>
/// Certificado próprio da Bergamota para falar com o LocalSend (que exige certificado do cliente).
/// Criado uma vez e guardado em %AppData%\Bergamota\identity.pfx — assim o celular reconhece
/// sempre o mesmo remetente. Fica na pasta do usuário, como o LocalSend faz com o dele.
/// </summary>
public static class ClientIdentity
{
    static string FilePath => Path.Combine(AppSettings.Folder, "identity.pfx");

    public static X509Certificate2 Load(bool ephemeral = false)
    {
        if (!ephemeral && File.Exists(FilePath))
        {
            try { return X509CertificateLoader.LoadPkcs12FromFile(FilePath, null, X509KeyStorageFlags.UserKeySet); }
            catch { /* corrompido: gera outro */ }
        }
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Bergamota", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2" /* autenticação de cliente */), new("1.3.6.1.5.5.7.3.1") }, false));
        using var created = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(20));
        var pfx = created.Export(X509ContentType.Pfx);
        if (!ephemeral)
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllBytes(FilePath, pfx);
        }
        // recarregar do PFX deixa a chave utilizável pelo TLS do Windows (chave efêmera não serve)
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
    }

    /// <summary>Fingerprint no formato do LocalSend: SHA-256 do certificado em hex maiúsculo.</summary>
    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));
}
