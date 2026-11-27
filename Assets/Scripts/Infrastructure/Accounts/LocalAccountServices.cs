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
#if UNITY_EDITOR
            if(!Directory.Exists(bin)) bin=Path.Combine(playerRoot,"Builds","Windows","Services");
#endif
            state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnimeCS","Services");
        }

        internal void Start(string role)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT) return;
            string executable=Path.Combine(bin,role,"AnimeCS."+role+".exe");
            if(!File.Exists(executable))
                throw new FileNotFoundException($"缺少 Services/{role}/AnimeCS.{role}.exe，请补齐游戏目录中的 Services 文件夹。", executable);
            Directory.CreateDirectory(state);
            using var gate=new Mutex(false,@"Local\AnimeCS.Demo.Services");
            try {if(!gate.WaitOne(TimeSpan.FromSeconds(3))) throw new TimeoutException("本机后台服务正忙，请稍后重试。");} catch(AbandonedMutexException) {}
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
                if(child==null) throw new InvalidOperationException($"未能启动本机 {role} 服务，请重试。");
                File.WriteAllText(pidFile,child.Id.ToString());
            }
            finally {gate.ReleaseMutex();}
        }

        internal async Task EnsureHttp(string role,HttpClient http,string url,CancellationToken cancel)
        {
            string health=url+"/health";
            using var startup=CancellationTokenSource.CreateLinkedTokenSource(cancel);
            startup.CancelAfter(TimeSpan.FromSeconds(12));
            var deadline=startup.Token;
            try
            {
                if(await IsReady(http,health,deadline).ConfigureAwait(false)) return;
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT)
                    throw new InvalidOperationException("请先启动本机后台服务，或在 animecs-services.json 中配置服务地址。");
                await Task.Run(()=>Start(role),deadline).ConfigureAwait(false);
                while(!await IsReady(http,health,deadline).ConfigureAwait(false))
                    await Task.Delay(150,deadline).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw new TimeoutException($"本机 {role} 服务在 12 秒内未就绪，请检查服务和端口 {new Uri(url).Port} 后重试。");
            }
        }

        private static async Task<bool> IsReady(HttpClient http,string health,CancellationToken cancel)
        {
            using var probe=CancellationTokenSource.CreateLinkedTokenSource(cancel);
            probe.CancelAfter(TimeSpan.FromMilliseconds(750));
            try
            {
                using var reply=await http.GetAsync(health,HttpCompletionOption.ResponseHeadersRead,probe.Token).ConfigureAwait(false);
                return reply.IsSuccessStatusCode;
            }
            catch(HttpRequestException) { cancel.ThrowIfCancellationRequested(); return false; }
            catch(OperationCanceledException) when (!cancel.IsCancellationRequested) { return false; }
        }
    }
}
