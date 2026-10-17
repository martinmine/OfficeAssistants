using OfficeAssistant.Core.Platform;
using Security;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// Keeps secrets in the login Keychain as a generic password. The settings file only holds
/// a reference ("keychain:account"), so the key itself never touches the disk in plain text.
/// </summary>
public sealed class KeychainSecretStore(string service = "Office Assistant") : ISecretStore
{
    private const string Prefix = "keychain:";
    private const string Account = "anthropic-api-key";

    public string Description => "in your macOS Keychain";

    public string Protect(string secret)
    {
        var data = NSData.FromString(secret, NSStringEncoding.UTF8);
        var existing = Query(Account);
        SecKeyChain.QueryAsRecord(existing, out var found);
        var status = found == SecStatusCode.Success
            ? SecKeyChain.Update(existing, new SecRecord(SecKind.GenericPassword) { ValueData = data })
            : SecKeyChain.Add(new SecRecord(SecKind.GenericPassword) { Service = service, Account = Account, Label = $"{service} API key", ValueData = data });
        if (status != SecStatusCode.Success)
        {
            throw new InvalidOperationException($"Couldn't save the API key in the Keychain ({status}).");
        }

        return Prefix + Account;
    }

    public string? Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Saved by another platform (for example a DPAPI blob copied from Windows).
            return null;
        }

        var record = SecKeyChain.QueryAsRecord(Query(stored[Prefix.Length..]), out var status);
        return status == SecStatusCode.Success && record?.ValueData is { } data
            ? NSString.FromData(data, NSStringEncoding.UTF8)?.ToString()
            : null;
    }

    public void Remove(string stored)
    {
        if (stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            SecKeyChain.Remove(Query(stored[Prefix.Length..]));
        }
    }

    private SecRecord Query(string account) => new(SecKind.GenericPassword) { Service = service, Account = account };
}
