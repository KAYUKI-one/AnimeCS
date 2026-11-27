using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace AnimeCS.Networking
{
    internal sealed class BattleResultReporter : IDisposable
    {
        private readonly HttpClient client=new HttpClient(ServiceEndpoint.Handler()){Timeout=TimeSpan.FromSeconds(8)};
        private readonly string directory, endpoint;
        private Task pending;
        private double nextAttempt;

        internal BattleResultReporter(string secret,string defaultState)
        {
            directory=Environment.GetEnvironmentVariable("ANIMECS_BATTLE_STATE_DIR") ?? defaultState;
            Directory.CreateDirectory(directory);
            endpoint=ServiceEndpoint.Resolve(Environment.GetEnvironmentVariable("ANIMECS_BATTLE_DATA_ENDPOINT"),27841,ServiceEndpoint.AllowPrivateHttp());
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",secret);
        }

        internal void Record(string room,int winner,int rounds,int enteredSeats)
        {
            Store(room+".result",FormattableString.Invariant($"{winner}/{rounds}/{enteredSeats}"));
        }

        internal void RecordProgress(string room,int roundOrdinal,int seatMask)
        {
            long occurred=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string file=FormattableString.Invariant($"{room}.{roundOrdinal}.{seatMask}.progress");
            Store(file,FormattableString.Invariant($"{roundOrdinal}/{seatMask}/{occurred}"));
        }

        private void Store(string name,string value)
        {
            string file=Path.Combine(directory,name);
            if(File.Exists(file)) return;
            string stage=file+".tmp";
            using(var stream=new FileStream(stage,FileMode.Create,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream))
            {
                writer.Write(value); writer.Flush(); stream.Flush(true);
            }
            File.Move(stage,file);
            nextAttempt=0;
        }

        internal Task<string> ReadLineup(string room) => client.GetStringAsync(endpoint+"/internal/battles/"+room+"/lineup");

        internal void Tick(double now)
        {
            if(now<nextAttempt || pending!=null && !pending.IsCompleted) return;
            nextAttempt=now+5;
            pending=Task.Run(Deliver);
        }

        private async Task Deliver()
        {
            try
            {
                var progress=Directory.EnumerateFiles(directory,"*.progress");
                var results=Directory.EnumerateFiles(directory,"*.result");
                foreach(string file in progress.Take(16).Concat(results.Take(16)))
                {
                    bool mission=Path.GetExtension(file)==".progress";
                    string room=Path.GetFileName(file).Split('.')[0];
                    if(!Guid.TryParseExact(room,"N",out _)) continue;
                    string body=File.ReadAllText(file);
                    var parts=body.Split('/');
                    bool valid=mission ? parts.Length==3 : parts.Length>=1 && parts.Length<=3 &&
                        (parts[0]=="0" || parts[0]=="1" || parts[0]=="-1");
                    if(!valid || parts.Skip(mission?0:1).Any(x=>!long.TryParse(x,NumberStyles.None,CultureInfo.InvariantCulture,out _))) continue;
                    // A restarted simulation may only remember seats that reconnected to this process.
                    // Let Data receive the room's earlier admissions before finalizing its winner list.
                    if(!mission && parts.Length==3 && Directory.EnumerateFiles(directory,room+".0.*.progress").Any()) continue;
                    string route=mission ? "/progress/" : "/settle/";
                    using var response=await client.PostAsync(endpoint+"/internal/battles/"+room+route+body,null);
                    bool legacy=!mission && parts.Length<3;
                    if(response.IsSuccessStatusCode || response.StatusCode==HttpStatusCode.Gone ||
                        legacy && response.StatusCode==HttpStatusCode.NotFound) File.Delete(file);
                }
            }
            catch(Exception error) when(error is IOException || error is HttpRequestException ||
                error is OperationCanceledException || error is ObjectDisposedException) {}
        }

        public void Dispose() => client.Dispose();
    }
}
