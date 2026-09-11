namespace MementoMori.Exporter.Android.Services;

/// <summary>Testable secure-storage boundary; failed reads never silently delete credentials.</summary>
public sealed class SavedCredentials(Func<Task<string?>> read, Func<string, Task> write, Action remove)
{
    public bool NeedsRecovery { get; private set; }

    public async Task<AuthOption?> LoadAsync()
    {
        try
        {
            var json = await read();
            var value = string.IsNullOrWhiteSpace(json) ? null : CredentialImport.Parse(json);
            NeedsRecovery = false;
            return value;
        }
        catch
        {
            NeedsRecovery = true;
            throw;
        }
    }

    public async Task<AuthOption> SaveAsync(string json)
    {
        var parsed = CredentialImport.Parse(json);
        // Validate and persist before the caller replaces its working in-memory account.
        await write(CredentialImport.Serialize(parsed));
        NeedsRecovery = false;
        return parsed;
    }

    public void Clear()
    {
        remove();
        NeedsRecovery = false;
    }
}
