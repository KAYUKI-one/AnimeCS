using AnimeCS.HotUpdate;

string root = Path.GetFullPath(Environment.GetEnvironmentVariable("ANIMECS_UPDATE_DIR") ?? "content");
var store = new ContentStore(root);
if (args.Length > 0 && args[0] == "publish")
{
    try
    {
        if (args.Length != 3) throw new ArgumentException("Usage: AnimeCS.HotUpdate publish RELEASE SOURCE_DIRECTORY");
        store.Publish(args[1], args[2]);
    }
    catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    return;
}
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ANIMECS_UPDATE_URL") ?? "http://127.0.0.1:27842");
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { service = "hot-update", schema = 1 }));
app.MapGet("/v1/manifest", (HttpResponse response) =>
{
    response.Headers.CacheControl = "no-cache";
    return File.Exists(store.Current) ? Results.File(store.Current, "application/json") : Results.NoContent();
});
app.MapGet("/v1/blobs/{hash}", (string hash, HttpResponse response) =>
{
    if (hash.Length != 64 || !hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')) return Results.NotFound();
    string path = store.Blob(hash);
    if (!File.Exists(path)) return Results.NotFound();
    response.Headers.CacheControl = "public,max-age=31536000,immutable";
    return Results.File(path, "application/octet-stream", enableRangeProcessing:true);
});
app.Run();
