namespace MementoMori.Exporter.Android.Services;

/// <summary>Owns only generated exports, never login configuration or arbitrary private files.</summary>
public sealed class ExportFileStore
{
    private const string Prefix = "mementomori-account-";
    private readonly string directory;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public ExportFileStore(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        directory = Path.GetFullPath(Path.Combine(cacheDirectory, "sharing-root"));
    }

    public async Task<string> WriteAsync(byte[] bytes, bool zip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        RemoveExpiredFiles();
        var name = $"{Prefix}{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.{(zip ? "zip" : "json")}";
        var path = Path.Combine(directory, name);
        var temporary = path + ".partial";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Publish only a completely written export. A failed write never becomes shareable.
            File.Move(temporary, path);
            return path;
        }
        finally { TryDelete(temporary); }
    }

    public string Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(fullPath), directory, PathComparison) ||
            !Path.GetFileName(fullPath).StartsWith(Prefix, StringComparison.Ordinal) ||
            !IsExportExtension(Path.GetExtension(fullPath)))
            throw new InvalidOperationException("Only generated exports can be shared or saved.");
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The temporary export is no longer available.");
        if (info.LinkTarget != null)
            throw new InvalidOperationException("Symbolic links cannot be exported.");
        return fullPath;
    }

    public bool IsAvailable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { Validate(path); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or NotSupportedException) { return false; }
    }

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".zip" => "application/zip",
        _ => throw new InvalidOperationException("Unsupported export format.")
    };

    public async Task CopyToAsync(string path, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("The destination is not writable.");
        await using var source = new FileStream(Validate(path), FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        // The caller owns the destination (for Android this is a content-provider stream).
    }

    public void Clear()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private void RemoveExpiredFiles()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var path in Directory.EnumerateFiles(directory, Prefix + "*"))
        {
            var extension = Path.GetExtension(path);
            if ((IsExportExtension(extension) || extension == ".partial") && File.GetLastWriteTimeUtc(path) < cutoff)
                TryDelete(path);
        }
    }

    private static bool IsExportExtension(string extension) =>
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
