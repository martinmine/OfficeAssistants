using System.Security.Cryptography;
using System.Text;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Platform.Windows;

/// <summary>Encrypts secrets for the current Windows user with DPAPI; the base64 result is what gets saved.</summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = "OfficeAssistant.ApiKey"u8.ToArray();

    public string Description => "encrypted for your Windows account";

    public string Protect(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string stored)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public void Remove(string stored)
    {
        // Nothing is kept outside the settings file.
    }
}
