using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using AnimeCS.Accounts;
using static AnimeCS.Data.AccountDb;

namespace AnimeCS.Data;

internal sealed record CharacterDrop(string Id,string OutfitId,int Weight);
internal sealed record Mission(string Id,string Title,string Description,string EventKey,int Target,int Experience,int Credits);
internal sealed record EconomyConfig(int Schema,int InitialCredits,int DrawCost,string[] StarterItems,
    CharacterDrop[] Characters,ShopOffer[] Offers,Mission[] Tasks)
{
    internal static EconomyConfig Load()
    {
        var file=Environment.GetEnvironmentVariable("ANIMECS_CATALOG") ?? Path.Combine(AppContext.BaseDirectory,"game-catalog.json");
        var c=JsonSerializer.Deserialize<EconomyConfig>(File.ReadAllText(file),Json)!;
        if(c.Schema!=1 || c.InitialCredits<0 || c.DrawCost<=0 || c.StarterItems.Distinct().Count()!=c.StarterItems.Length ||
            c.Characters.Length==0 || c.Characters.Any(x=>x.Weight<=0 || !x.Id.StartsWith("character.") || !x.OutfitId.StartsWith("outfit.")) ||
            c.Offers.Any(x=>x.price<=0 || !(x.itemId.StartsWith("skin.") || x.itemId.StartsWith("outfit.") || x.itemId.StartsWith("wear."))) ||
            c.Tasks.Any(x=>x.Target<1 || x.Experience<0 || x.Credits<0))
            throw new InvalidDataException("Invalid game-catalog.json");
        return c;
    }
}

internal sealed class SavedPlayer
{
    public int Schema {get;set;}=1;
    public int Xp {get;set;}
    public int Credits {get;set;}
    public int Victories {get;set;}
    public int RankPoints {get;set;}
    public string Rank {get;set;}="未定级";
    public string Signature {get;set;}="";
    public string AvatarId {get;set;}="";
    public string ShowCharacterId {get;set;}="";
    public string ShowSkinId {get;set;}="";
    public string LastDrawId {get;set;}="";
    public string LoadoutJson {get;set;}="";
    public HashSet<string> Inventory {get;set;}=[];
}

internal sealed class AccountEconomy(EconomyConfig config)
{
    internal SavedPlayer Starter()
    {
        var p=new SavedPlayer {Credits=config.InitialCredits, Inventory=new(config.StarterItems)};
        p.AvatarId=p.ShowCharacterId=config.StarterItems.FirstOrDefault(x=>x.StartsWith("character."))??"";
        p.ShowSkinId=config.StarterItems.FirstOrDefault(x=>x.StartsWith("skin."))??"";
        return p;
    }

    internal void Buy(SavedPlayer p,string? id)
    {
        var offer=config.Offers.FirstOrDefault(x=>x.id==id) ?? throw new Rejection("商品已下架");
        if(p.Inventory.Contains(offer.itemId)) throw new Rejection("已拥有此商品",409);
        if(p.Credits<offer.price) throw new Rejection("星币不足");
        p.Credits-=offer.price;
        p.Inventory.Add(offer.itemId);
    }

    internal void Draw(SavedPlayer p)
    {
        var pool=config.Characters.Where(x=>!p.Inventory.Contains(x.Id)).ToArray();
        if(pool.Length==0) throw new Rejection("已集齐当期角色",409);
        if(p.Credits<config.DrawCost) throw new Rejection("星币不足");
        int choice=RandomNumberGenerator.GetInt32(pool.Sum(x=>x.Weight));
        foreach(var item in pool)
        {
            choice-=item.Weight;
            if(choice>=0) continue;
            p.Inventory.Add(item.Id); p.Inventory.Add(item.OutfitId);
            p.LastDrawId=item.Id; p.Credits-=config.DrawCost;
            return;
        }
    }

    internal static void Progress(SqliteConnection db,SqliteTransaction tx,string owner,string day,
        string eventId,string kind,Mission[] tasks,int amount=1)
    {
        if(eventId.Length is <1 or >120 || eventId.Any(char.IsControl)) throw new Rejection("事件编号无效");
        if(Execute(db,tx,"INSERT OR IGNORE INTO receipts VALUES($p0,$p1,$p2)",owner,day,eventId)==0) return;
        ApplyProgress(db,tx,owner,day,kind,tasks,amount);
    }

    internal static void ApplyProgress(SqliteConnection db,SqliteTransaction tx,string owner,string day,
        string kind,Mission[] tasks,int amount=1)
    {
        foreach(var task in tasks.Where(t=>t.EventKey==kind))
            Execute(db,tx,"""
                INSERT INTO daily(owner,day,task,progress) VALUES($p0,$p1,$p2,MIN($p3,$p4))
                ON CONFLICT(owner,day,task) DO UPDATE SET progress=MIN(progress+$p4,$p3)
                """,owner,day,task.Id,task.Target,amount);
    }

    internal void Claim(SqliteConnection db,SqliteTransaction tx,string owner,string day,string id,SavedPlayer p)
    {
        var task=config.Tasks.FirstOrDefault(t=>t.Id==id) ?? throw new Rejection("任务不存在");
        if(Execute(db,tx,"UPDATE daily SET claimed=1 WHERE owner=$p0 AND day=$p1 AND task=$p2 AND progress>=$p3 AND claimed=0",
            owner,day,id,task.Target)!=1) throw new Rejection("任务未完成或奖励已领取",409);
        p.Xp=checked(p.Xp+task.Experience);p.Credits=checked(p.Credits+task.Credits);
    }

    internal TaskState[] Tasks(SqliteConnection db,SqliteTransaction tx,string id,string day)
    {
        using var cmd=Command(db,tx,"SELECT task,progress,claimed FROM daily WHERE owner=$p0 AND day=$p1",id,day);
        using var rows=cmd.ExecuteReader();
        var progress=new Dictionary<string,(int count,bool claimed)>();
        while(rows.Read()) progress[rows.GetString(0)]=(rows.GetInt32(1),rows.GetBoolean(2));
        return config.Tasks.Select(t=>{
            var value=progress.GetValueOrDefault(t.Id);
            return new TaskState{id=t.Id,title=t.Title,description=t.Description,eventKey=t.EventKey,
                progress=value.count,claimed=value.claimed,goal=t.Target,experience=t.Experience,credits=t.Credits};
        }).ToArray();
    }

    internal static void SaveDisplay(SavedPlayer p,AccountRequest request)
    {
        string sig=(request.signature??"").Trim();
        if(sig.Length>96 || sig.Any(char.IsControl)) throw new Rejection("签名最多 96 个字符");
        static string Owned(SavedPlayer p,string? item,params string[] kinds)
        {
            if(string.IsNullOrEmpty(item)) return "";
            if(!p.Inventory.Contains(item) || !kinds.Any(k=>item.StartsWith(k,StringComparison.Ordinal))) throw new Rejection("请使用已拥有的展示物品");
            return item;
        }
        p.AvatarId=Owned(p,request.avatarId,"character.","avatar.");
        p.ShowCharacterId=Owned(p,request.showCharacterId,"character.");
        p.ShowSkinId=Owned(p,request.showSkinId,"skin.","weapon.");
        p.Signature=sig;
    }

    internal static void SaveLoadout(SavedPlayer p,string? json)
    {
        if(string.IsNullOrEmpty(json) || json.Length>24000) throw new Rejection("装备配置无效");
        try
        {
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=12});
            if(doc.RootElement.ValueKind!=JsonValueKind.Object) throw new Rejection("装备配置无效");
            Check(doc.RootElement);
            void Check(JsonElement entry)
            {
                switch(entry.ValueKind)
                {
                    case JsonValueKind.Object: foreach(var property in entry.EnumerateObject()) Check(property.Value);break;
                    case JsonValueKind.Array: foreach(var item in entry.EnumerateArray()) Check(item);break;
                    case JsonValueKind.String:
                        var id=entry.GetString();
                        if(!string.IsNullOrEmpty(id) && !p.Inventory.Contains(id)) throw new Rejection("配置包含未拥有的物品");
                        break;
                }
            }
            p.LoadoutJson=json;
        }
        catch(JsonException) { throw new Rejection("装备配置格式错误"); }
    }
}
