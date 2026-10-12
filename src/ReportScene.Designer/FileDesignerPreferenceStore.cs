using System.Text;

namespace ReportScene.Designer;

/// <summary>
/// Optional desktop/server preference storage at a host-supplied path. Writes a sibling
/// temporary file before replacing the destination; this is not a physical-disk durability
/// guarantee. Multiple independent hosts use last-successful-writer semantics.
/// </summary>
public sealed class FileDesignerPreferenceStore : IDesignerPreferenceStore
{
    private readonly string path;
    public FileDesignerPreferenceStore(string path)
    {
        if (OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException("Use a browser preference store on WebAssembly.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        if (string.IsNullOrEmpty(Path.GetFileName(this.path))) throw new ArgumentException("A preference file name is required.", nameof(path));
    }
    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream input;
        try { input = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        await using (input)
        {
            if (input.Length > DesignerPreferencesJson.MaximumCharacters * 4L) throw new FormatException("The preference file is too large.");
            using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var buffer = new char[DesignerPreferencesJson.MaximumCharacters + 1];
            int count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            if (count > DesignerPreferencesJson.MaximumCharacters) throw new FormatException("The preference file is too large.");
            return new string(buffer, 0, count);
        }
    }
    public async Task SaveAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); _ = DesignerPreferencesJson.Deserialize(json);
        string directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false, true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException cleanup) { System.Diagnostics.Debug.WriteLine(cleanup.Message); }
            catch (UnauthorizedAccessException cleanup) { System.Diagnostics.Debug.WriteLine(cleanup.Message); }
        }
    }
}
