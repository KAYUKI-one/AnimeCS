using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    // Use the published localhost services and a new database for each test.
    sealed class LobbyTestServices : IDisposable
    {
        readonly string directory=Path.Combine(Path.GetTempPath(),"AnimeCS-LobbyTests",Guid.NewGuid().ToString("N"));
        readonly Dictionary<string,string> previous=new();
        readonly List<Process> processes=new();
        readonly List<string> endpoints=new();
        readonly string lastPlayer;
        readonly bool hadPlayer;
        public LobbyTestServices()
        {
            hadPlayer=PlayerPrefs.HasKey("AnimeCS.LastPlayerId");lastPlayer=PlayerPrefs.GetString("AnimeCS.LastPlayerId","");
            try { Start("Data");Start("HotUpdate"); } catch { Dispose(); throw; }
        }

        void Start(string role)
        {
            var root=Directory.GetParent(Application.dataPath).FullName;
            bool windows=Environment.OSVersion.Platform==PlatformID.Win32NT;
            var bundled=Path.Combine(root,"Services");
            var defaultServices=windows ? Path.Combine(root,"Builds","Windows","Services") : Path.Combine(root,"Builds","Debian13");
            if (windows && Directory.Exists(bundled)) defaultServices=bundled;
            var services=Environment.GetEnvironmentVariable("ANIMECS_TEST_SERVICE_ROOT") ?? defaultServices;
            string exe=Path.Combine(services,role,"AnimeCS."+role+(windows?".exe":""));
            Assert.That(File.Exists(exe),Is.True,"请先构建 "+role+" 的本机服务");
            var reservation=new TcpListener(IPAddress.Loopback,0);reservation.Start();
            int port=((IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();
            var endpoint="http://127.0.0.1:"+port;
            var prefix=role=="Data"?"ANIMECS_DATA":"ANIMECS_UPDATE";
            var setting=prefix+"_ENDPOINT";
            previous[setting]=Environment.GetEnvironmentVariable(setting);
            Environment.SetEnvironmentVariable(setting,endpoint);
            var args=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)};
            args.EnvironmentVariables[prefix+"_URL"]=endpoint;
            args.EnvironmentVariables[prefix+"_DIR"]=Path.Combine(directory,role);
            args.EnvironmentVariables["ANIMECS_BATTLE_STATE_DIR"]=Path.Combine(directory,"Battle");
            processes.Add(Process.Start(args));endpoints.Add(endpoint);
        }

        public IEnumerator Ready()
        {
            using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(2)};
            foreach (var endpoint in endpoints)
            {
                bool ready=false;
                double deadline=Time.realtimeSinceStartupAsDouble+15;
                while (!ready && Time.realtimeSinceStartupAsDouble<deadline)
                {
                    var fetch=http.GetAsync(endpoint+"/health");
                    while (!fetch.IsCompleted) yield return null;
                    if (fetch.Status==System.Threading.Tasks.TaskStatus.RanToCompletion)
                    {
                        using var result=fetch.Result;ready=result.IsSuccessStatusCode;
                    }
                    if (!ready) yield return new WaitForSecondsRealtime(.1f);
                }
                Assert.That(ready,Is.True,"本地测试服务启动失败");
            }
        }

        public IEnumerator Register(VisualElement root)
        {
            root.Q<TextField>("auth-id").value="LobbyTest"+Guid.NewGuid().ToString("N").Substring(0,8);
            root.Q<TextField>("auth-password").value=Guid.NewGuid().ToString("N");
            using (var click=NavigationSubmitEvent.GetPooled())
            {
                var button=root.Q<Button>("auth-register");click.target=button;button.SendEvent(click);
            }
            var auth=root.Q("auth");
            double deadline=Time.realtimeSinceStartupAsDouble+35;
            while (auth.resolvedStyle.display!=DisplayStyle.None && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.That(auth.resolvedStyle.display,Is.EqualTo(DisplayStyle.None),root.Q<Label>("auth-status").text);
        }

        public static IEnumerator Capture(string page)
        {
            string path=Environment.GetEnvironmentVariable("ANIMECS_LOBBY_CAPTURES");
            if (string.IsNullOrWhiteSpace(path)) yield break;
            Directory.CreateDirectory(path);
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(path,page+".png"));
            yield return null;
            yield return null;
        }

        public void Dispose()
        {
            foreach (var process in processes)
            {
                if (!process.HasExited) {process.Kill();process.WaitForExit(3000);}
                process.Dispose();
            }
            foreach(var pair in previous) Environment.SetEnvironmentVariable(pair.Key,pair.Value);
            if (hadPlayer) PlayerPrefs.SetString("AnimeCS.LastPlayerId",lastPlayer); else PlayerPrefs.DeleteKey("AnimeCS.LastPlayerId");
            if (Directory.Exists(directory)) Directory.Delete(directory,true);
        }
    }
}
