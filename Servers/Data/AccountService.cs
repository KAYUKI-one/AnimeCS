using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using AnimeCS.Accounts;
using AnimeCS.Match;
using static AnimeCS.Data.AccountDb;

namespace AnimeCS.Data;

internal sealed class AccountService(AccountDb store,EconomyConfig config)
{
    private readonly AccountEconomy economy=new(config);
    private readonly AccountParty social=new();
    private readonly bool allowPractice=Environment.GetEnvironmentVariable("ANIMECS_LOCAL_PRACTICE_PROGRESS")=="1";

    internal static string NormalizeId(string? raw)
    {
        string id=(raw??"").Trim().Normalize(NormalizationForm.FormKC);
        if(id.Length is <2 or >32 || !id.All(c=>char.IsLetterOrDigit(c)||c is '_' or '-' or '.'))
            throw new Rejection("ID 须为 2 至 32 个字母、汉字、数字或 . - _");
        return id;
    }

    private IResult Handle(string action,AccountRequest input,string authorization,bool localPractice)
    {
        using var db=store.Open();
        // Credential derivation runs outside the SQLite write transaction.
        string id, token="";
        byte[]? salt=null, hash=null;
        if(action is "login" or "register")
        {
            id=NormalizeId(input.id);
            if(input.password==null || input.password.Length is <8 or >128) throw new Rejection("密码须为 8 至 128 个字符");
            if(action=="register")
            {
                salt=RandomNumberGenerator.GetBytes(16);hash=Derive(input.password,salt);
            }
            else
            {
                using var check=db.CreateCommand();
                check.CommandText="SELECT id,salt,pass FROM accounts WHERE id=$id";
                check.Parameters.AddWithValue("$id",id);
                using var row=check.ExecuteReader();
                bool exists=row.Read();
                byte[] storedSalt=exists?(byte[])row[1]:new byte[16];
                var actual=Derive(input.password,storedSalt);
                if(!exists || !CryptographicOperations.FixedTimeEquals(actual,(byte[])row[2])) throw new Rejection("账号或密码错误",401);
                id=row.GetString(0);
            }
        }
        else id="";
        using var tx=db.BeginTransaction(deferred:action=="state");
        string day=Today;
        if(action=="register")
        {
            if(Scalar(db,tx,"SELECT id FROM players WHERE id=$p0 COLLATE NOCASE",id)!=null ||
                Execute(db,tx,"INSERT OR IGNORE INTO accounts VALUES($p0,$p1,$p2,$p3)",id,salt!,hash!,JsonSerializer.Serialize(economy.Starter(),Json))!=1)
                throw new Rejection("该 ID 已被使用",409);
        }
        if(action is "login" or "register")
        {
            token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Execute(db,tx,"DELETE FROM sessions WHERE expires<=$p0",Now);
            Execute(db,tx,"INSERT INTO sessions VALUES($p0,$p1,$p2)",Hash(token),id,Now+86400);
            AccountEconomy.Progress(db,tx,id,day,"daily-login","login",config.Tasks);
        }
        else
        {
            if(!authorization.StartsWith("Bearer ",StringComparison.Ordinal) || authorization.Length>160) throw new Rejection("请先登录",401);
            id=Scalar(db,tx,"SELECT owner FROM sessions WHERE hash=$p0 AND expires>$p1",Hash(authorization[7..]),Now) ??
                throw new Rejection("登录已过期，请重新登录",401);
        }
        var player=JsonSerializer.Deserialize<SavedPlayer>(Scalar(db,tx,"SELECT state FROM accounts WHERE id=$p0",id)!,Json)!;
        bool changed=true;
        switch(action)
        {
            case "buy": economy.Buy(player,input.id);break;
            case "draw": economy.Draw(player);break;
            case "claim": economy.Claim(db,tx,id,day,input.id,player);break;
            case "profile": AccountEconomy.SaveDisplay(player,input);break;
            case "loadout": AccountEconomy.SaveLoadout(player,input.loadoutJson);break;
            default:
                changed=false;
                if(action=="progress")
                {
                    if(!localPractice || !((input.eventId??"").StartsWith("local-",StringComparison.Ordinal)))
                        throw new Rejection("联机任务由战斗服务结算",403);
                    if(input.amount!=1 || input.kind=="login" || !config.Tasks.Any(t=>t.EventKey==input.kind))
                        throw new Rejection("无效的任务事件");
                    AccountEconomy.Progress(db,tx,id,day,input.eventId??"",input.kind,config.Tasks);
                }
                else if(action is not ("state" or "register" or "login")) social.Apply(db,tx,id,action,input);
                break;
        }
        if(changed) Execute(db,tx,"UPDATE accounts SET state=$p1 WHERE id=$p0",id,JsonSerializer.Serialize(player,Json));
        var state=new AccountState{id=id,localPracticeProgress=localPractice,signature=player.Signature,avatarId=player.AvatarId,rank=player.Rank,
            showCharacterId=player.ShowCharacterId,showSkinId=player.ShowSkinId,loadoutJson=player.LoadoutJson,lastDrawId=player.LastDrawId,
            level=1+player.Xp/1000,xp=player.Xp,rankPoints=player.RankPoints,victories=player.Victories,credits=player.Credits,
            drawCost=config.DrawCost,inventory=player.Inventory.Order().ToArray(),drawPool=config.Characters.Select(x=>x.Id).ToArray(),
            offers=config.Offers,tasks=economy.Tasks(db,tx,id,day)};
        social.Read(db,tx,id,state);
        tx.Commit();
        return Results.Json(new AccountReply{state=state,token=token},Json);
    }

    internal async Task<IResult> Route(string action,HttpContext http)
    {
        try
        {
            if(!HttpMethods.IsGet(http.Request.Method) && !http.Request.HasJsonContentType())
                return Results.Json(new{error="请求应使用 application/json"},statusCode:StatusCodes.Status415UnsupportedMediaType);
            var input=HttpMethods.IsGet(http.Request.Method)?new AccountRequest():
                await http.Request.ReadFromJsonAsync<AccountRequest>(Json,http.RequestAborted) ?? new AccountRequest();
            var remote=http.Connection.RemoteIpAddress;
            bool loopback=remote!=null && IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
            return Handle(action,input,http.Request.Headers.Authorization.ToString(),allowPractice && loopback);
        }
        catch(Rejection error){return Results.Json(new{error=error.Message},statusCode:error.Status);}
        catch(JsonException){return Results.BadRequest(new{error="请求格式无效"});}
    }

    internal IResult BattleProgress(string room,int roundOrdinal,int seatMask,long occurredAt,HttpContext http)
    {
        if(!BattleAuthorized(http)) return Results.Unauthorized();
        if(!Guid.TryParseExact(room,"N",out _) || roundOrdinal<0 || seatMask<=0 || occurredAt<0 || occurredAt>Now+300)
            return Results.BadRequest();
        string day=DateTimeOffset.FromUnixTimeSeconds(occurredAt).UtcDateTime.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        using var db=store.Open();
        using var tx=db.BeginTransaction();
        string? roster=Scalar(db,tx,"SELECT seats FROM battle_history WHERE room=$p0",room);
        if(roster==null) return Results.StatusCode(StatusCodes.Status410Gone);
        var seats=JsonSerializer.Deserialize<string[]>(roster)!;
        if((seatMask>>seats.Length)!=0) return Results.BadRequest();
        string kind=roundOrdinal==0 ? "match.enter" : "round.complete";
        bool firstRoundOrAdmission=roundOrdinal==0 || Scalar(db,tx,
            "SELECT 1 FROM battle_progress WHERE room=$p0 AND ordinal=$p1 LIMIT 1",room,roundOrdinal)==null;
        bool newReceipt=false;
        for(int seat=0;seat<seats.Length;seat++)
        {
            if((seatMask&(1<<seat))==0) continue;
            if(Execute(db,tx,"INSERT OR IGNORE INTO battle_progress(room,ordinal,seat,occurred) VALUES($p0,$p1,$p2,$p3)",
                room,roundOrdinal,seat,occurredAt)==1)
            {
                newReceipt=true;
                AccountEconomy.ApplyProgress(db,tx,seats[seat],day,kind,config.Tasks);
            }
        }
        if(newReceipt && firstRoundOrAdmission) AccountParty.RenewFromActivity(db,tx,room,occurredAt);
        tx.Commit();
        return Results.NoContent();
    }

    internal IResult Settle(string room,int winningTeam,int rounds,HttpContext http,int? enteredSeats=null)
    {
        if(!BattleAuthorized(http)) return Results.Unauthorized();
        if(!Guid.TryParseExact(room,"N",out _) || winningTeam is < -1 or > 1 || rounds<0 || enteredSeats<0) return Results.BadRequest();
        using var db=store.Open();
        using var tx=db.BeginTransaction();
        string? roster=Scalar(db,tx,"SELECT seats FROM battle_history WHERE room=$p0",room);
        if(roster==null) return enteredSeats.HasValue ? Results.StatusCode(StatusCodes.Status410Gone) : Results.NotFound();
        var seats=JsonSerializer.Deserialize<string[]>(roster)!;
        if(enteredSeats.HasValue && (enteredSeats.Value>>seats.Length)!=0) return Results.BadRequest();
        if(Execute(db,tx,"UPDATE battle_history SET settled=1 WHERE room=$p0 AND settled=0",room)==1)
        {
            int participants=enteredSeats ?? ((1<<seats.Length)-1);
            if(enteredSeats.HasValue)
            {
                using var admissions=Command(db,tx,"SELECT seat FROM battle_progress WHERE room=$p0 AND ordinal=0",room);
                using var rows=admissions.ExecuteReader();
                while(rows.Read()) participants|=1<<rows.GetInt32(0);
            }
            for(int seat=0;seat<seats.Length;seat++)
            {
                if((participants&(1<<seat))!=0 && seat/MatchFormat.TeamSize==winningTeam)
                    Execute(db,tx,"UPDATE accounts SET state=json_set(state,'$.victories',COALESCE(json_extract(state,'$.victories'),0)+1) WHERE id=$p0",seats[seat]);
            }
            Execute(db,tx,"INSERT OR REPLACE INTO party_results(lead,room) SELECT lead,room FROM battle WHERE room=$p0",room);
            Execute(db,tx,"DELETE FROM battle WHERE room=$p0",room);
        }
        tx.Commit();
        return Results.NoContent();
    }

    internal IResult Lineup(string room,HttpContext http)
    {
        if(!BattleAuthorized(http)) return Results.Unauthorized();
        using var db=store.Open();
        using var tx=db.BeginTransaction(deferred:true);
        string? json=Scalar(db,tx,"SELECT equipment FROM battle_lineups WHERE room=$p0",room);
        return json==null ? Results.NotFound() : Results.Content(json,"application/json");
    }

    private static bool BattleAuthorized(HttpContext http)
    {
        string? secret=Environment.GetEnvironmentVariable("ANIMECS_BATTLE_SECRET");
        string incoming=http.Request.Headers.Authorization.ToString();
        return !string.IsNullOrEmpty(secret) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes("Bearer "+secret)),SHA256.HashData(Encoding.UTF8.GetBytes(incoming)));
    }
}
