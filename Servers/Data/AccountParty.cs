using System.Text.Json;
using Microsoft.Data.Sqlite;
using AnimeCS.Accounts;
using AnimeCS.Match;
using static AnimeCS.Data.AccountDb;

namespace AnimeCS.Data;

internal sealed class AccountParty
{
    private const int ReservationIdleSeconds=60*60;
    private readonly string? secret=Environment.GetEnvironmentVariable("ANIMECS_BATTLE_SECRET");
    private readonly string host=Environment.GetEnvironmentVariable("ANIMECS_BATTLE_PUBLIC_HOST") ?? "127.0.0.1";
    private readonly int port=int.Parse(Environment.GetEnvironmentVariable("ANIMECS_BATTLE_PORT") ?? "27840");
    private readonly string map=Environment.GetEnvironmentVariable("ANIMECS_BATTLE_MAP") ?? "map.dust2";
    private readonly string revision=Environment.GetEnvironmentVariable("ANIMECS_BATTLE_CONTENT_VERSION") ?? MatchFormat.ContentRevision;
    internal static string Lead(SqliteConnection db,SqliteTransaction tx,string id) =>
        Scalar(db,tx,"SELECT lead FROM party WHERE member=$p0",id) ?? id;

    internal static string[] Members(SqliteConnection db,SqliteTransaction tx,string lead) =>
        new[]{lead}.Concat(Strings(db,tx,"SELECT member FROM party WHERE lead=$p0 AND member!=$p0 ORDER BY member",lead)).ToArray();

    internal static string Resolve(SqliteConnection db,SqliteTransaction tx,string id) =>
        Scalar(db,tx,"SELECT id FROM accounts WHERE id=$p0",id) ?? throw new Rejection("未找到该玩家",404);

    internal void Apply(SqliteConnection db,SqliteTransaction tx,string id,string action,AccountRequest request)
    {
        string lead=Lead(db,tx,id);
        string target="";
        if(action is "friend" or "invite" or "accept")
        {
            target=Resolve(db,tx,AccountService.NormalizeId(request.id));
            if(id==target) throw new Rejection("请选择其他玩家");
        }
        switch(action)
        {
            case "friend":
                Execute(db,tx,"INSERT OR IGNORE INTO friends VALUES($p0,$p1),($p1,$p0)",id,target);
                break;
            case "invite":
                if(lead!=id || Members(db,tx,lead).Length>=MatchFormat.TeamSize) throw new Rejection("仅未满员队伍的队长可邀请");
                if(Scalar(db,tx,"SELECT 1 FROM friends WHERE owner=$p0 AND other=$p1",id,target)==null) throw new Rejection("请先添加好友");
                if(Lead(db,tx,target)==id) throw new Rejection("对方已在队伍内");
                if(InBattle(db,tx,lead)) throw new Rejection("队伍已进入对局");
                Execute(db,tx,"INSERT OR IGNORE INTO invites VALUES($p0,$p1)",id,target);
                break;
            case "accept":
                if(lead!=id || Members(db,tx,lead).Length>1 || Lead(db,tx,target)!=target ||
                    Members(db,tx,target).Length>=MatchFormat.TeamSize || InBattle(db,tx,lead) || InBattle(db,tx,target))
                    throw new Rejection("当前队伍无法加入");
                if(Execute(db,tx,"DELETE FROM invites WHERE lead=$p0 AND guest=$p1",target,id)!=1) throw new Rejection("邀请已失效");
                Execute(db,tx,"INSERT INTO party(member,lead) VALUES($p0,$p1)",id,target);
                Execute(db,tx,"DELETE FROM invites WHERE guest=$p0 OR lead=$p0",id);
                break;
            case "leave":
                if(InBattle(db,tx,lead)) throw new Rejection("请先结束对局");
                if(lead==id) Execute(db,tx,"DELETE FROM party WHERE lead=$p0",id);
                else Execute(db,tx,"DELETE FROM party WHERE member=$p0",id);
                Execute(db,tx,"DELETE FROM invites WHERE lead=$p0",id);
                break;
            case "chat":
                string body=(request.text??"").Trim();
                if(body.Length is <1 or >240 || body.Any(char.IsControl)) throw new Rejection("消息长度应为 1 至 240 个字符");
                Execute(db,tx,"INSERT INTO chat(owner,body) VALUES($p0,$p1)",id,body);
                Execute(db,tx,"DELETE FROM chat WHERE seq <= (SELECT MAX(seq)-500 FROM chat)");
                break;
            case "start":
                if(lead!=id) throw new Rejection("请等待队长开始对局");
                if(InBattle(db,tx,lead)) return;
                StartBattle(db,tx,id);
                break;
            case "restart":
                if(lead!=id) throw new Rejection("请由队长开始下一场对局");
                string previous=ExpectedRoom(request);
                string? current=Scalar(db,tx,"SELECT room FROM battle WHERE lead=$p0 AND expires>$p1",lead,Now);
                if(current!=null && current==Scalar(db,tx,"SELECT successor FROM battle_restarts WHERE previous=$p0",previous)) return;
                if(current==previous) throw new Rejection("战斗服正在确认终局，请稍候",425);
                if(current!=null || Scalar(db,tx,"SELECT room FROM party_results WHERE lead=$p0",lead)!=previous)
                    throw new Rejection("房间状态已变化，请刷新大厅",409);
                string successor=StartBattle(db,tx,id);
                Execute(db,tx,"INSERT INTO battle_restarts(previous,successor) VALUES($p0,$p1)",previous,successor);
                break;
            case "end":
                if(id!=lead) throw new Rejection("请由队长结束队伍对局");
                string room=ExpectedRoom(request);
                Execute(db,tx,"DELETE FROM battle WHERE lead=$p0 AND room=$p1",id,room);
                Execute(db,tx,"DELETE FROM party_results WHERE lead=$p0 AND room=$p1",id,room);
                break;
            default: throw new Rejection("未知操作",404);
        }
    }

    private static string ExpectedRoom(AccountRequest request) =>
        Guid.TryParseExact(request.battleId,"N",out _) ? request.battleId : throw new Rejection("请选择要操作的房间");

    private string StartBattle(SqliteConnection db,SqliteTransaction tx,string lead)
    {
        if(string.IsNullOrEmpty(secret)) throw new Rejection("战斗服务尚未配置联机密钥",503);
        long started=Now;
        Execute(db,tx,"DELETE FROM battle WHERE expires<=$p0",started);
        if(Scalar(db,tx,"SELECT room FROM battle")!=null) throw new Rejection("战斗服务器使用中",409);
        string room=Guid.NewGuid().ToString("N");
        var members=Members(db,tx,lead);
        if(members.Length>MatchFormat.TeamSize) throw new Rejection("队伍人数超限",409);
        var seats=JsonSerializer.Serialize(members);
        Execute(db,tx,"INSERT INTO battle VALUES(1,$p0,$p1,$p2,$p3)",room,lead,seats,started+ReservationIdleSeconds);
        Execute(db,tx,"INSERT INTO battle_history(room,seats,created) VALUES($p0,$p1,$p2)",room,seats,started);
        var lineup=members.Select((member,seat)=>{
            var profile=JsonSerializer.Deserialize<SavedPlayer>(Scalar(db,tx,"SELECT state FROM accounts WHERE id=$p0",member)!,Json)!;
            return new{seat,id=member,owned=profile.Inventory,loadoutJson=profile.LoadoutJson};
        });
        Execute(db,tx,"INSERT INTO battle_lineups VALUES($p0,$p1)",room,JsonSerializer.Serialize(new{members=lineup},Json));
        Execute(db,tx,"DELETE FROM party_results WHERE lead=$p0",lead);
        Execute(db,tx,"DELETE FROM battle_history WHERE created<$p0",Now-7*86400);
        Execute(db,tx,"DELETE FROM invites WHERE lead=$p0",lead);
        return room;
    }

    internal static void RenewFromActivity(SqliteConnection db,SqliteTransaction tx,string room,long occurredAt)
    {
        long now=Now;
        long deadline=Math.Min(occurredAt,now)+ReservationIdleSeconds;
        Execute(db,tx,"UPDATE battle SET expires=$p1 WHERE room=$p0 AND expires>$p2 AND expires<$p1",room,deadline,now);
    }

    private static bool InBattle(SqliteConnection db,SqliteTransaction tx,string lead) =>
        Scalar(db,tx,"SELECT 1 FROM battle WHERE lead=$p0 AND expires>$p1",lead,Now)!=null;

    internal void Read(SqliteConnection db,SqliteTransaction tx,string id,AccountState state)
    {
        string lead=Lead(db,tx,id);
        state.party=Members(db,tx,lead);
        state.members=state.party.Select(member=>{
            var profile=JsonSerializer.Deserialize<SavedPlayer>(Scalar(db,tx,"SELECT state FROM accounts WHERE id=$p0",member)!,Json)!;
            return new PartyMemberState{id=member,showCharacterId=profile.ShowCharacterId,showSkinId=profile.ShowSkinId};
        }).ToArray();
        state.friends=Strings(db,tx,"SELECT other FROM friends WHERE owner=$p0 ORDER BY other",id);
        state.invites=Strings(db,tx,"SELECT lead FROM invites WHERE guest=$p0 ORDER BY lead",id);
        using(var cmd=Command(db,tx,"SELECT owner,body FROM (SELECT * FROM chat ORDER BY seq DESC LIMIT 50) ORDER BY seq"))
        using(var rows=cmd.ExecuteReader())
        {
            var messages=new List<ChatLine>();
            while(rows.Read()) messages.Add(new(){id=rows.GetString(0),text=rows.GetString(1)});
            state.messages=messages.ToArray();
        }
        using var battle=Command(db,tx,"""
            SELECT b.room,b.seats,b.expires,h.rowid,0 AS finished FROM battle b JOIN battle_history h ON h.room=b.room
            WHERE b.lead=$p0 AND b.expires>$p1 UNION ALL
            SELECT h.room,h.seats,0,h.rowid,1 FROM party_results r JOIN battle_history h ON h.room=r.room WHERE r.lead=$p0
            ORDER BY finished LIMIT 1
            """,lead,Now);
        using var row=battle.ExecuteReader();
        if(string.IsNullOrEmpty(secret) || !row.Read()) return;
        int seat=Array.IndexOf(JsonSerializer.Deserialize<string[]>(row.GetString(1))!,id);
        if(seat<0) return;
        state.battleId=row.GetString(0);state.battleHost=host;state.battlePort=port;state.mapId=map;
        state.contentVersion=revision;
        state.battleFinished=row.GetBoolean(4);
        if(!state.battleFinished)
            state.battleTicket=BattleTicket.Issue(secret,state.battleId,row.GetInt64(3),row.GetInt64(2),seat,id,map,revision);
    }
}
