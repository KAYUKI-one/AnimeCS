using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeCS.Accounts
{
    // A single local service set is shared by every client running under this Windows account.
    internal sealed class LocalAccountServices
    {
        private readonly string bin, state;
        internal string PublisherManifest => Path.Combine(state,"HotUpdate","current.json");
        internal LocalAccountServices(string playerRoot)
        {
            bin=Path.Combine(playerRoot,"Services");
            if(!Directory.Exists(bin)) bin=Path.Combine(playerRoot,"Builds","Windows","Services");
            state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnimeCS","Services");
        }

        internal void Start(string role)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT) return;
            string executable=Path.Combine(bin,role,"AnimeCS."+role+".exe");
            if(!File.Exists(executable)) return;
            Directory.CreateDirectory(state);
            using var gate=new Mutex(false,@"Local\AnimeCS.Demo.Services");
            try {if(!gate.WaitOne(TimeSpan.FromSeconds(3))) return;} catch(AbandonedMutexException) {}
            try
            {
                string pidFile=Path.Combine(state,role+".pid");
                if(File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile),out int pid))
                {
                    try
                    {
                        using var existing=Process.GetProcessById(pid);
                        if(!existing.HasExited && string.Equals(existing.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase)) return;
                    }
                    catch(ArgumentException){} catch(InvalidOperationException){}
                }
                string keyFile=Path.Combine(state,"battle.key");
                if(!File.Exists(keyFile))
                {
                    byte[] key=new byte[32];
                    using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(key);
                    File.WriteAllText(keyFile,Convert.ToBase64String(key));
                }
                var info=new ProcessStartInfo(executable) {WorkingDirectory=Path.GetDirectoryName(executable),
                    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                info.EnvironmentVariables["ANIMECS_BATTLE_SECRET"]=File.ReadAllText(keyFile).Trim();
                info.EnvironmentVariables["ANIMECS_DATA_DIR"]=Path.Combine(state,"Data");
                if(role=="Data") info.EnvironmentVariables["ANIMECS_LOCAL_PRACTICE_PROGRESS"]="1";
                info.EnvironmentVariables["ANIMECS_BATTLE_STATE_DIR"]=Path.Combine(state,"BattleResults");
                info.EnvironmentVariables["ANIMECS_UPDATE_DIR"]=Path.Combine(state,"HotUpdate");
                if(role=="Battle") info.Arguments="-batchmode -nographics -logFile \""+Path.Combine(state,"battle.log")+"\"";
                using var child=Process.Start(info);
                if(child!=null) File.WriteAllText(pidFile,child.Id.ToString());
            }
            finally {gate.ReleaseMutex();}
        }

        internal async Task EnsureHttp(string role,HttpClient http,string url,CancellationToken cancel)
        {
            try {using var reply=await http.GetAsync(url+"/health",cancel);if(reply.IsSuccessStatusCode) return;}
            catch(HttpRequestException) {}
            await Task.Run(()=>Start(role),cancel);
            await WaitHttp(http,url,cancel);
        }

        private async Task WaitHttp(HttpClient http,string url,CancellationToken cancel)
        {
            for(int i=0;i<24;i++)
            {
                try {using var response=await http.GetAsync(url+"/health",cancel); if(response.IsSuccessStatusCode) return;}
                catch(HttpRequestException) {}
                await Task.Delay(125,cancel);
            }
        }
    }
}
