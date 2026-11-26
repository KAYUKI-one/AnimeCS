using System.Security.Cryptography;
using System.Text.Json;

namespace AnimeCS.HotUpdate;

internal sealed class ContentStore(string root)
{
    internal string Current => Path.Combine(root, "current.json");
    internal string Blob(string hash) => Path.Combine(root, "blobs", hash);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Entry(string Path, string Sha256, long Size);

    internal void Publish(string version, string source)
    {
        if (version.Length is 0 or > 96 || version is "." or ".." ||
            !version.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_'))
            throw new ArgumentException("Invalid release identifier.");
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        static bool Inside(string parent, string child)
        {
            string relative = Path.GetRelativePath(parent, child);
            return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
        }
        if (Inside(source,root) || Inside(root,source)) throw new ArgumentException("The source and content store must be separate directories.");
        Directory.CreateDirectory(Path.Combine(root, "blobs"));
        string releases = Directory.CreateDirectory(Path.Combine(root, "releases")).FullName;
        var options = new EnumerationOptions { RecurseSubdirectories=true, AttributesToSkip=FileAttributes.ReparsePoint, IgnoreInaccessible=false };
        var entries = new List<Entry>();
        foreach (string file in Directory.EnumerateFiles(source, "*", options).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Contains('\\') || relative.Contains(':') || relative.Any(char.IsControl))
                throw new InvalidDataException("Unsupported content path: " + relative);
            string temp = Temp();
            try
            {
                File.Copy(file, temp);
                using var input = File.OpenRead(temp);
                string hash = Convert.ToHexStringLower(SHA256.HashData(input));
                long length = input.Length;
                input.Close();
                InstallImmutable(temp, Blob(hash));
                entries.Add(new Entry(relative, hash, length));
            }
            finally { File.Delete(temp); }
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, version, files = entries }, Json);
        string manifest = Path.Combine(releases, version + ".json");
        string staging = Temp();
        try
        {
            File.WriteAllBytes(staging, bytes);
            InstallImmutable(staging, manifest);
            if (!File.ReadAllBytes(manifest).AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("The release ID already identifies different content.");
            File.WriteAllBytes(staging, bytes);
            File.Move(staging, Current, overwrite:true);
        }
        finally { File.Delete(staging); }
    }

    private string Temp() => Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");

    private static void InstallImmutable(string staging, string destination)
    {
        try { File.Move(staging, destination); }
        catch (IOException) when (File.Exists(destination)) { }
    }
}
