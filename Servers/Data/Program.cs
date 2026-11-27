using System.Security.Cryptography;
using System.Text;
using AnimeCS.Data;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ANIMECS_DATA_URL") ?? "http://127.0.0.1:27841");
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 256 * 1024);
var directory = Environment.GetEnvironmentVariable("ANIMECS_DATA_DIR") ?? "state";
var repository = new PlayerRepository(directory);
var accounts = new AccountService(new AccountDb(directory), EconomyConfig.Load());
var credential = Environment.GetEnvironmentVariable("ANIMECS_DATA_TOKEN");
var digest = string.IsNullOrEmpty(credential) ? null : SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + credential));
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { service = "data", schema = 2 }));
app.MapGet("/v2/state", (Func<HttpContext, Task<IResult>>)(context => accounts.Route("state", context)));
app.MapPost("/v2/{action}", (string action, HttpContext context) => accounts.Route(action, context));
app.MapPost("/internal/battles/{room}/settle/{winningTeam:int}", (string room,int winningTeam,HttpContext context) =>
    accounts.Settle(room,winningTeam,0,context));
app.MapPost("/internal/battles/{room}/settle/{winningTeam:int}/{rounds:int}", (string room,int winningTeam,int rounds,HttpContext context) =>
    accounts.Settle(room,winningTeam,rounds,context));
app.MapPost("/internal/battles/{room}/settle/{winningTeam:int}/{rounds:int}/{enteredSeats:int}",
    (string room,int winningTeam,int rounds,int enteredSeats,HttpContext context) => accounts.Settle(room,winningTeam,rounds,context,enteredSeats));
app.MapPost("/internal/battles/{room}/progress/{roundOrdinal:int}/{seatMask:int}/{occurredAt:long}",
    (string room,int roundOrdinal,int seatMask,long occurredAt,HttpContext context) =>
        accounts.BattleProgress(room,roundOrdinal,seatMask,occurredAt,context));
app.MapGet("/internal/battles/{room}/lineup", (string room,HttpContext context) => accounts.Lineup(room,context));
var players = app.MapGroup("/v1/players");
players.AddEndpointFilter(async (context, next) =>
{
    if (digest == null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    string incoming = context.HttpContext.Request.Headers.Authorization.ToString();
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(incoming)), digest))
        return Results.Unauthorized();
    return await next(context);
});
players.MapGet("/{id}", (string id, HttpResponse response) =>
{
    if (!PlayerProfile.ValidId(id)) return Results.BadRequest();
    var record = repository.Read(id);
    if (record == null) return Results.NotFound();
    response.Headers.ETag = PlayerRepository.Tag(record.Revision);
    return Results.Ok(record);
});
players.MapPut("/{id}", (string id, PlayerProfile body, HttpContext http) =>
{
    if (!PlayerProfile.ValidId(id) || !body.Valid()) return Results.BadRequest();
    var request = http.Request.Headers;
    long revision = 0;
    if (request.IfNoneMatch == "*" && request.IfMatch.Count == 0) revision = 0;
    else if (request.IfNoneMatch.Count != 0 || !PlayerRepository.TryRevision(request.IfMatch.ToString(), out revision))
        return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
    if (!repository.TryWrite(id, body, revision)) return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
    http.Response.Headers.ETag = PlayerRepository.Tag(revision + 1);
    return revision == 0 ? Results.Created($"/v1/players/{id}", new { revision = 1 }) : Results.NoContent();
});
app.Run();
