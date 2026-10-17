namespace OfficeAssistant.Core.Platform;

/// <summary>
/// Protects a secret for the current user. <see cref="Protect"/> returns the string that
/// is saved in the settings file in its place: the encrypted secret itself (Windows DPAPI)
/// or a reference to where the secret is kept (the macOS Keychain).
/// </summary>
public interface ISecretStore
{
    string Protect(string secret);

    /// <summary>The secret behind a stored value, or null if it can't be recovered.</summary>
    string? Unprotect(string stored);

    /// <summary>Forgets a stored secret.</summary>
    void Remove(string stored);
}
