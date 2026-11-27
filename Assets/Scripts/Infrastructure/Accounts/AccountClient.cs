using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnimeCS.Networking;
using UnityEngine;

namespace AnimeCS.Accounts
{
    public sealed class AccountClient : IDisposable
    {
        [Serializable] private sealed class Endpoints { public string dataUrl, updateUrl, updateManifestSha256; public bool allowPrivateHttp; }
        private readonly HttpClient http = new HttpClient(ServiceEndpoint.Handler()) {Timeout=TimeSpan.FromSeconds(30)};
        private readonly SemaphoreSlim queue = new SemaphoreSlim(1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly string dataUrl, updateUrl, files, manifestPin;
        private readonly bool allowPrivateHttp;
        private readonly LocalAccountServices services;
        private string token;
        public AccountState State {get;private set;}
        public string UpdateDirectory => UpdateCache.Active(files, manifestPin);

        public AccountClient()
        {
            string playerRoot=Path.GetDirectoryName(Application.dataPath);
            string path=Path.Combine(playerRoot,"animecs-services.json");
            var config=File.Exists(path)?JsonUtility.FromJson<Endpoints>(File.ReadAllText(path)):new Endpoints();
            string data=Environment.GetEnvironmentVariable("ANIMECS_DATA_ENDPOINT") ?? config?.dataUrl;
            string updates=Environment.GetEnvironmentVariable("ANIMECS_UPDATE_ENDPOINT") ?? config?.updateUrl;
            allowPrivateHttp = ServiceEndpoint.AllowPrivateHttp(config?.allowPrivateHttp == true);
            dataUrl=ServiceEndpoint.Resolve(data,27841,allowPrivateHttp);
            updateUrl=ServiceEndpoint.Resolve(updates,27842,allowPrivateHttp);
            manifestPin=UpdateCache.NormalizePin(Environment.GetEnvironmentVariable("ANIMECS_UPDATE_MANIFEST_SHA256") ?? config?.updateManifestSha256);
            files=UpdateCache.SourceDirectory(Path.Combine(Application.persistentDataPath,"Updates"),updateUrl);
            if(string.IsNullOrEmpty(data) && string.IsNullOrEmpty(updates))
                services=new LocalAccountServices(playerRoot);
        }

        public Task RegisterAsync(string id,string password) => Authenticate("register",id,password);
        public Task LoginAsync(string id,string password) => Authenticate("login",id,password);

        private Task Authenticate(string action,string id,string password)
        {
            if (!AccountCredentials.TryNormalizeId(id, out var normalized))
                return Task.FromException(new ArgumentException(AccountCredentials.IdRule));
            if (!AccountCredentials.ValidPassword(password))
                return Task.FromException(new ArgumentException(AccountCredentials.PasswordRule));
            return Call(action,new AccountRequest{id=normalized,password=password});
        }
        public Task RefreshAsync() => Call("state");
        public Task SaveProfileAsync(string signature,string avatarId,string showCharacterId,string showSkinId) =>
            Call("profile",new AccountRequest{signature=signature,avatarId=avatarId,showCharacterId=showCharacterId,showSkinId=showSkinId});
        public Task SaveLoadoutAsync(string wardrobeJson) => Call("loadout",new AccountRequest{loadoutJson=wardrobeJson});
        public Task AddFriendAsync(string id) => Call("friend",new AccountRequest{id=id});
        public Task InviteAsync(string id) => Call("invite",new AccountRequest{id=id});
        public Task AcceptInviteAsync(string leaderId) => Call("accept",new AccountRequest{id=leaderId});
        public Task LeavePartyAsync() => Call("leave");
        public Task StartPartyAsync() => Call("start");
        public async Task RestartBattleAsync(string previousBattleId)
        {
            var body=new AccountRequest{battleId=previousBattleId};
            var cancel=lifetime.Token;
            for(int attempt=0;;attempt++)
            {
                try { await Call("restart",body); return; }
                catch(SettlementPending) when(attempt<12) { await Task.Delay(500,cancel); }
            }
        }
        private sealed class SettlementPending : InvalidOperationException
        { internal SettlementPending(string message) : base(message) {} }
        public Task EndBattleAsync(string expectedRoom) => Call("end",new AccountRequest{battleId=expectedRoom});
        public Task EndBattleAsync() => EndBattleAsync(State?.battleId);
        public Task SendChatAsync(string text) => Call("chat",new AccountRequest{text=text});
        public Task BuyAsync(string id) => Call("buy",new AccountRequest{id=id});
        public Task DrawAsync() => Call("draw");
        public Task ClaimAsync(string id) => Call("claim",new AccountRequest{id=id});
        public Task ProgressAsync(string eventId,string kind,int amount=1) => Call("progress",new AccountRequest{eventId=eventId,kind=kind,amount=amount});

        public async Task<string> CheckUpdateAsync()
        {
            if(services!=null) await services.EnsureHttp("HotUpdate",http,updateUrl,lifetime.Token);
            string publisherDigest = manifestPin ?? (services == null ? null : UpdateCache.LocalDigest(services.PublisherManifest));
            return await UpdateCache.Check(http,updateUrl,files,lifetime.Token,publisherDigest,allowPrivateHttp);
        }

        private async Task Call(string action,AccountRequest body=null)
        {
            var cancel=lifetime.Token;
            await queue.WaitAsync(cancel);
            try
            {
                bool auth=action=="login" || action=="register";
                if(auth && services!=null)
                {
                    await services.EnsureHttp("Data",http,dataUrl,cancel);
                }
                if((action=="start" || action=="restart") && services!=null) await Task.Run(()=>services.Start("Battle"),cancel);
                using var request=new HttpRequestMessage(action=="state"?HttpMethod.Get:HttpMethod.Post,dataUrl+"/v2/"+action);
                if(!auth && !string.IsNullOrEmpty(token)) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                if(request.Method==HttpMethod.Post)
                    request.Content=new StringContent(JsonUtility.ToJson(body??new AccountRequest()),Encoding.UTF8,"application/json");
                using var response=await http.SendAsync(request,cancel);
                string json=await response.Content.ReadAsStringAsync();
                AccountReply reply=null;
                if(json.StartsWith("{",StringComparison.Ordinal)) reply=JsonUtility.FromJson<AccountReply>(json);
                if((int)response.StatusCode==425) throw new SettlementPending(reply?.error ?? "战斗服正在确认结算");
                if(!response.IsSuccessStatusCode || reply?.state==null)
                    throw new InvalidOperationException(reply?.error ?? "数据服务暂不可用 ("+(int)response.StatusCode+")");
                if(auth) token=reply.token;
                State=reply.state;
            }
            catch(OperationCanceledException e) when (!cancel.IsCancellationRequested)
            { throw new TimeoutException("连接数据服务超时，请检查服务后重试。",e); }
            catch(HttpRequestException e){throw new InvalidOperationException("无法连接数据服务，请检查服务地址与网络。",e);}
            finally {queue.Release();}
        }

        public void Dispose()
        {
            lifetime.Cancel();
            http.Dispose();
            lifetime.Dispose();
        }
    }
}
