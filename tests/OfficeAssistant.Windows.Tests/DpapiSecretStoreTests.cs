using OfficeAssistant.Core.Chat;
using OfficeAssistant.Platform.Windows;

namespace OfficeAssistant.Windows.Tests;

public class DpapiSecretStoreTests
{
    [WindowsFact]
    public void TheApiKeyIsSavedEncrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oa-settings-{Guid.NewGuid():N}.json");
        try
        {
            new SettingsStore(path, new DpapiSecretStore()) { ApiKey = "sk-ant-test" }.Save();

            Assert.DoesNotContain("sk-ant-test", File.ReadAllText(path));
            Assert.Equal("sk-ant-test", new SettingsStore(path, new DpapiSecretStore()).ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
