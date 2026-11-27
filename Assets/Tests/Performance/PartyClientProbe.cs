using System;
using System.IO;
using System.Linq;
using AnimeCS.Match;
using AnimeCS.Play;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Tests.Performance
{
    // Built only by AnimeCS.RenderProbe. Every lobby operation uses the player's UI.
    public sealed class PartyClientProbe : MonoBehaviour
    {
        [Serializable] sealed class Sample
        {
            public int tick, rosterTick, acceptedInput;
            public float yaw,pitch,teammateYaw,teammatePitch;
            public Vector3 position, teammatePosition;
        }
        [Serializable] sealed class Report
        {
            public string player, role, room, map, device, api;
            public bool registered, loggedIn, friends, invited, joined, loadingCaptured;
            public int seat, teammateSeat, activeSleeveRenderers;
            public Vector3 leftWristViewport, rightWristViewport;
            public Sample first, second;
            public double downKiBPerSecond, upKiBPerSecond, trafficSeconds, finalMenuSeconds;
            public int finalTick, finalOurScore, finalTheirScore, roundsPlayed, nextSeat;
            public bool startAgainVisible, stableFinal, nextInputAcknowledged;
            public bool pairedPurchasesConfirmed;
            public int purchaseRound, creditsBeforePurchase, creditsAfterPurchase;
            public string finishTitle, nextRoom;
            public Facing facing;
        }

        [Serializable] sealed class Facing
        {
            public int round,firstTick,lastTick,firstAcknowledged,lastAcknowledged,samples;
            public float targetYaw,targetPitch,maxCameraError,maxAuthorityError,maxPeerError,maxActorError;
            public double heldSeconds;
        }

        string output, role, phase, player, other, peerRole, previousPlayer;
        bool hadPlayer, host, second, finished;
        bool testRematch,verifyFacing,manualPlay;
        long firstReceivedBytes, firstSentBytes;
        double finalAt;
        double limit, next, combatEntered;
        float wantedYaw;
        double facingSince;
        LocalGame game;
        VisualElement root;
        readonly Report report=new Report();
        int step;
        int lookRound;
        int purchaseCount;
        string message;
        string Own(string suffix) => Path.Combine(output,role+"-"+suffix);
        bool Peer(string suffix) => File.Exists(Path.Combine(output,peerRole+"-"+suffix));
        float Expected(bool captain,bool later) => captain ? (later ? -61 : 24) : (later ? 84 : -37);
        float ExpectedPitch(bool captain,bool later) => captain ? (later ? -7 : 9) : (later ? 16 : -11);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-party-probe");
            if(index<0 || index+1>=args.Length) return;
            var go=new GameObject("Graphical party verification");
            DontDestroyOnLoad(go);
            var check=go.AddComponent<PartyClientProbe>();
            check.output=Path.GetFullPath(args[index+1]);
            check.role=Environment.GetEnvironmentVariable("ANIMECS_PROBE_ROLE");
            check.phase=Environment.GetEnvironmentVariable("ANIMECS_PROBE_PHASE");
            check.testRematch=Array.IndexOf(args,"-party-rematch")>=0;
            check.verifyFacing=Array.IndexOf(args,"-party-facing")>=0;
            check.manualPlay=Array.IndexOf(args,"-party-manual")>=0;
            check.player=Environment.GetEnvironmentVariable("ANIMECS_PROBE_ID");
            check.other=Environment.GetEnvironmentVariable("ANIMECS_PROBE_PEER");
        }

        void Start()
        {
            Directory.CreateDirectory(output);
            host=role=="captain";peerRole=host?"guest":"captain";
            Application.runInBackground=true;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=40;
            hadPlayer=PlayerPrefs.HasKey("AnimeCS.LastPlayerId");
            previousPlayer=PlayerPrefs.GetString("AnimeCS.LastPlayerId","");
            report.role=role;report.player=player;
            report.device=SystemInfo.graphicsDeviceName;report.api=SystemInfo.graphicsDeviceType.ToString();
            Advance("waiting for authentication form");
        }

        void Advance(string stage)
        {
            ++step;message=stage;limit=Time.realtimeSinceStartupAsDouble+115;
            File.WriteAllText(Own(phase+"-stage.txt"),stage);
        }

        void Update()
        {
            if(finished || Time.realtimeSinceStartupAsDouble<next) return;
            next=Time.realtimeSinceStartupAsDouble+.1;
            try
            {
                if(Time.realtimeSinceStartupAsDouble>limit)
                    throw new TimeoutException(message+"  "+root?.Q<Label>("auth-status")?.text+"  "+game?.BattleConnectionError);
                if(game && game.OnlineProbeClient?.Reconnecting==true)
                {
                    var status=root?.Q<Label>("action");
                    if(status!=null && OnScreen("action") && !string.IsNullOrEmpty(status.text) &&
                        !File.Exists(Own("reconnecting.txt")))
                    {
                        File.WriteAllText(Own("reconnecting.txt"),status.text);
                        ScreenCapture.CaptureScreenshot(Own("reconnecting.png"));
                    }
                    return;
                }
                bool awaitingBothAngles=step>=8 && step<=10 || step==11 && !Peer("report.json");
                if(awaitingBothAngles && game && game.Ready && game.OnlineProbeClient!=null &&
                    game.OnlineProbeClient.View.Match.Round!=lookRound)
                    TurnOnce(wantedYaw,ExpectedPitch(host,second));
                Drive();
                if(testRematch && !report.pairedPurchasesConfirmed && step>=8 && step<=12 &&
                    game && game.Ready && game.OnlineProbeClient!=null)
                    CheckPurchases(game.OnlineProbeClient.PlayView);
            }
            catch(Exception e)
            {
                File.WriteAllText(Own(phase+"-error.txt"),e.ToString());
                Quit(3);
            }
        }

        void Drive()
        {
            if (!game) game=FindAnyObjectByType<LocalGame>();
            if (!game || game.Account==null) return;
            if (root==null) root=FindAnyObjectByType<UIDocument>()?.rootVisualElement;
            if (root==null) return;
            var state=game.Account.State;
            switch(step)
            {
                case 1:
                    if(!game.InLobby || !Visible("auth"))return;
                    root.Q<TextField>("auth-id").value=player;
                    var password=root.Q<TextField>("auth-password");
                    if (phase=="auth-register")
                    {
                        if (!password.isPasswordField) throw new InvalidOperationException("Password field is unmasked");
                        password.value="1234";
                        foreach (var button in new[]{"auth-register","auth-login"})
                        {
                            Press(button);
                            if (!root.Q<Label>("auth-status").text.Contains("8 至 128") ||
                                !root.Q<Button>("auth-login").enabledSelf || !root.Q<Button>("auth-register").enabledSelf || game.Account.State!=null)
                                throw new InvalidOperationException("Short password did not leave the form ready to retry");
                        }
                    }
                    password.value=Environment.GetEnvironmentVariable("ANIMECS_PROBE_PASSWORD");
                    Press(phase=="register" || phase=="auth-register" ? "auth-register":"auth-login");
                    Advance("waiting for account and update");break;
                case 2:
                    if(state?.id!=player || Visible("auth") || !game.InLobby) return;
                    if(phase=="auth-login")
                    {
                        File.WriteAllText(Own("logged-in"),player);
                        Quit(0);break;
                    }
                    if(phase=="register" || phase=="auth-register")
                    {
                        File.WriteAllText(Own("registered"),player);
                        Quit(0);break;
                    }
                    report.registered=File.Exists(Own("registered"));
                    report.loggedIn=true;
                    Press("friends");
                    if(host)
                    {
                        root.Q<TextField>("add-friend-id").value=other;
                        Press("add-friend");
                    }
                    Advance("waiting for reciprocal friend");break;
                case 3:
                    if(!state.friends.Contains(other))return;
                    report.friends=true;
                    if(host)Press("invite-"+other);
                    Advance("waiting for party invitation and acceptance");break;
                case 4:
                    if(!host)
                    {
                        if(!state.invites.Contains(other) || root.Q<Button>("accept-"+other)==null) return;
                        Press("accept-"+other);
                    }
                    report.invited=true;
                    Advance("waiting for server party membership");break;
                case 5:
                    if(state.party.Length!=2 || !state.party.Contains(other) || state.party[0]!=(host?player:other))return;
                    report.joined=true;
                    Press("dialog-close");
                    ScreenCapture.CaptureScreenshot(Own("party.png"));
                    File.WriteAllText(Own("party-ready"),player);
                    Advance("waiting for both lobby views");break;
                case 6:
                    if(!Peer("party-ready"))return;
                    if(host) Press("start");
                    Advance("waiting for battle scene and network seat");break;
                case 7:
                    if(!game.Ready || game.OnlineProbeClient==null || !game.OnlineProbeClient.Connected) return;
                    combatEntered=Time.realtimeSinceStartupAsDouble;
                    report.room=state.battleId;report.map=game.SelectedMap.Id;
                    report.seat=game.OnlineProbeClient.View.Seat;
                    firstSentBytes=game.OnlineProbeClient.SentPayloadBytes;
                    firstReceivedBytes=game.OnlineProbeClient.ReceivedPayloadBytes;
                    report.teammateSeat=host?1:0;
                    if(report.seat!=(host?0:1)) throw new InvalidOperationException("Wrong authoritative seat "+report.seat);
                    if(manualPlay)
                    {
                        File.WriteAllText(Own(phase+"-stage.txt"),"manual keyboard and mouse control");
                        enabled=false;
                        break;
                    }
                    wantedYaw=Expected(host,false);
                    TurnOnce(wantedYaw,ExpectedPitch(host,false));
                    Advance("verifying first authoritative and teammate input");break;
                case 8:
                    var first=Observation();
                    if(first==null) return;
                    report.first=first;
                    File.WriteAllText(Own("first-input"),first.tick.ToString());
                    Advance("waiting for both first input observations");break;
                case 9:
                    if(!Peer("first-input"))return;
                    second=true;wantedYaw=Expected(host,true);
                    TurnOnce(wantedYaw,ExpectedPitch(host,true));
                    Advance("verifying second authoritative and teammate input");break;
                case 10:
                    var later=Observation();
                    if(later==null || later.tick<=report.first.tick || later.rosterTick<=report.first.rosterTick || later.acceptedInput<=report.first.acceptedInput) return;
                    if(Time.realtimeSinceStartupAsDouble-combatEntered<2.5) return;
                    report.second=later;
                    RecordView();
                    ScreenCapture.CaptureScreenshot(Own("match.png"));
                    File.WriteAllText(Own("report.json"),JsonUtility.ToJson(report,true));
                    Advance("waiting for both reports and screenshots");break;
                case 11:
                    if(!Peer("report.json") || !File.Exists(Own("match.png"))) return;
                    if(!report.loadingCaptured || !File.Exists(Own("loading.png")))throw new InvalidOperationException("Missing rendered match loading capture");
                    if(verifyFacing && !CheckFacing()) return;
                    if(!testRematch) {Quit(0);break;}
                    Advance("waiting for the authoritative match result");
                    limit=Time.realtimeSinceStartupAsDouble+75*60;break;
                case 12:
                    var current=game.OnlineProbeClient;
                    if(current==null || !game.Ready || game.InLobby) throw new InvalidOperationException("Left battle before the authoritative result");
                    double elapsed=Time.realtimeSinceStartupAsDouble-combatEntered;
                    if(report.trafficSeconds==0 && elapsed>=6)
                    {
                        report.trafficSeconds=elapsed;
                        report.downKiBPerSecond=(current.ReceivedPayloadBytes-firstReceivedBytes)/1024d/elapsed;
                        report.upKiBPerSecond=(current.SentPayloadBytes-firstSentBytes)/1024d/elapsed;
                    }
                    if(!current.Finished || !OnScreen("menu"))return;
                    var result=current.PlayView.Match;
                    report.finalOurScore=result.Score0;report.finalTheirScore=result.Score1;
                    report.roundsPlayed=result.Round;report.finalTick=result.Tick;
                    report.finishTitle=root.Q<Label>("pause-title").text;
                    report.startAgainVisible=OnScreen("restart");
                    if(report.startAgainVisible!=host || !OnScreen("to-lobby"))throw new InvalidOperationException("Incorrect result actions for this role");
                    ScreenCapture.CaptureScreenshot(Own("finished.png"));
                    finalAt=Time.realtimeSinceStartupAsDouble;
                    Advance("checking final menu through the disconnect timeout");break;
                case 13:
                    if(game.InLobby || game.OnlineProbeClient==null || !game.OnlineProbeClient.Finished ||
                        !OnScreen("menu") || !string.IsNullOrEmpty(game.BattleConnectionError))
                        throw new InvalidOperationException("Final view disappeared: "+game.BattleConnectionError);
                    report.finalMenuSeconds=Time.realtimeSinceStartupAsDouble-finalAt;
                    if(report.finalMenuSeconds<7 || !File.Exists(Own("finished.png")))return;
                    report.stableFinal=true;
                    ScreenCapture.CaptureScreenshot(Own("finished-later.png"));
                    File.WriteAllText(Own("final-ready"),"ready");
                    Advance("waiting for both result screens");break;
                case 14:
                    if(!Peer("final-ready"))return;
                    if(host) Press("restart");
                    Advance("waiting for a new authoritative room");break;
                case 15:
                    var nextClient=game.OnlineProbeClient;
                    if(game.InLobby || !game.Ready || nextClient==null || !nextClient.Connected || nextClient.Finished ||
                        !nextClient.HasRoster || string.IsNullOrEmpty(state.battleId) || state.battleId==report.room)return;
                    if(nextClient.View.Seat!=report.seat) throw new InvalidOperationException("Seats changed between party matches");
                    if(nextClient.AcceptedInputSequence<1)return;
                    report.nextSeat=nextClient.View.Seat;report.nextRoom=state.battleId;report.nextInputAcknowledged=true;
                    ScreenCapture.CaptureScreenshot(Own("rematch.png"));
                    File.WriteAllText(Own("report.json"),JsonUtility.ToJson(report,true));
                    File.WriteAllText(Own("rematch-ready"),"ready");
                    Advance("waiting for both rematch clients");break;
                case 16:
                    if(!Peer("rematch-ready") || !File.Exists(Own("rematch.png")))return;
                    Quit(0);break;
            }
        }

        bool Visible(string name)
        {
            var element=root.Q(name);
            return element!=null && element.resolvedStyle.display!=DisplayStyle.None;
        }

        bool OnScreen(string name)
        {
            var element=root.Q(name);
            if(element==null || element.panel==null) return false;
            for(var parent=element;parent!=null;parent=parent.parent)
                if(parent.resolvedStyle.display==DisplayStyle.None)return false;
            return true;
        }

        void Press(string id)
        {
            var button=root.Q<Button>(id);
            if(button==null || !button.enabledInHierarchy || button.resolvedStyle.display==DisplayStyle.None)
                throw new InvalidOperationException("Unavailable UI control "+id);
            using var submit=NavigationSubmitEvent.GetPooled();
            submit.target=button;button.SendEvent(submit);
        }

        Sample Observation()
        {
            var net=game.OnlineProbeClient;
            if(net==null || !game.Ready || game.InLobby || net.Finished)
                throw new InvalidOperationException($"Battle ended before angle observations completed. Lobby={game.InLobby}, ready={game.Ready}, finished={net?.Finished}, connection={game.BattleConnectionError}");
            var self=net.View;
            var peer=net.ReceivedMember(report.teammateSeat);
            net.Member(report.teammateSeat,out bool visible);
            if(!visible || net.AcceptedInputSequence<1 || Mathf.Abs(Mathf.DeltaAngle(self.Yaw,wantedYaw))>.15f ||
                Mathf.Abs(self.Pitch-ExpectedPitch(host,second))>.15f ||
                Mathf.Abs(Mathf.DeltaAngle(peer.Yaw,Expected(!host,second)))>.15f ||
                Mathf.Abs(peer.Pitch-ExpectedPitch(!host,second))>.15f) return null;
            return new Sample{tick=self.Match.Tick,rosterTick=net.ReceivedRosterTick,acceptedInput=net.AcceptedInputSequence,
                yaw=self.Yaw,pitch=self.Pitch,teammateYaw=peer.Yaw,teammatePitch=peer.Pitch,
                position=self.Position,teammatePosition=peer.Position};
        }

        // One relative mouse delta. The production keyboard remains the only network input driver.
        void TurnOnce(float yaw,float pitch)
        {
            var keyboard=game.OnlineProbeKeyboard;
            lookRound=game.OnlineProbeClient.View.Match.Round;
            keyboard.SyncRound(game.OnlineProbeClient.View);
            var zoom=Mathf.Max(1f,game.OnlineProbeClient.PlayView.Magnification);
            var degrees=new Vector2(Mathf.DeltaAngle(keyboard.ViewYaw,yaw),pitch-keyboard.ViewPitch);
            keyboard.ApplyLookDelta(degrees*(zoom/keyboard.Sensitivity),zoom);
        }

        bool CheckFacing()
        {
            var net=game.OnlineProbeClient;
            var self=net.View;
            var peer=net.ReceivedMember(report.teammateSeat);
            if(self.Health<=0 || peer.Health<=0 || !net.HasRoster) return false;
            Vector3 eye=self.Position+Vector3.up*SeatMovement.EyeHeight(self.Motion.Crouch);
            Vector3 otherEye=peer.Position+Vector3.up*SeatMovement.EyeHeight(peer.Motion.Crouch);
            Vector3 direction=otherEye-eye;
            if(direction.sqrMagnitude<.01f) return false;
            float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            float pitch=Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
            var f=report.facing;
            if(f==null || f.round!=self.Match.Round)
            {
                f=report.facing=new Facing{round=self.Match.Round,targetYaw=yaw,targetPitch=pitch};
                facingSince=0;
                TurnOnce(yaw,pitch);
                File.WriteAllText(Own("facing-"+f.round),"ready");
                return false;
            }
            if(!Peer("facing-"+f.round))return false;
            float camera=Vector3.Angle(Camera.main.transform.forward,direction);
            float authority=Vector3.Angle(Quaternion.Euler(-self.Pitch,self.Yaw,0)*Vector3.forward,direction);
            float remote=Vector3.Angle(Quaternion.Euler(-peer.Pitch,peer.Yaw,0)*Vector3.forward,-direction);
            float actor=Mathf.Abs(Mathf.DeltaAngle(game.OnlineProbeActor(peer.Seat).eulerAngles.y,yaw+180));
            const float tolerance=2f;
            if(Mathf.Max(Mathf.Max(camera,authority),Mathf.Max(remote,actor))>tolerance)
            {
                if(f.samples>0)throw new InvalidOperationException($"Facing changed after release at tick {self.Match.Tick}: camera={camera:F3}, authority={authority:F3}, peer={remote:F3}, actor={actor:F3}");
                return false;
            }
            if(f.samples==0)
            {
                f.firstTick=self.Match.Tick;f.firstAcknowledged=net.AcceptedInputSequence;
                facingSince=Time.realtimeSinceStartupAsDouble;
            }
            f.samples++;
            f.maxCameraError=Mathf.Max(f.maxCameraError,camera);f.maxAuthorityError=Mathf.Max(f.maxAuthorityError,authority);
            f.maxPeerError=Mathf.Max(f.maxPeerError,remote);f.maxActorError=Mathf.Max(f.maxActorError,actor);
            f.lastTick=self.Match.Tick;f.lastAcknowledged=net.AcceptedInputSequence;
            f.heldSeconds=Time.realtimeSinceStartupAsDouble-facingSince;
            if(f.heldSeconds<3 || f.lastAcknowledged<=f.firstAcknowledged) return false;
            if(!File.Exists(Own("facing-ready")))
            {
                ScreenCapture.CaptureScreenshot(Own("facing.png"));
                File.WriteAllText(Own("facing-ready"),"ready");
                File.WriteAllText(Own("report.json"),JsonUtility.ToJson(report,true));
            }
            return Peer("facing-ready") && File.Exists(Own("facing.png"));
        }

        void RecordView()
        {
            var camera=Camera.main;
            if(!camera || game.OnlineProbeClient==null) return;
            var body=game.OnlineProbeActor(game.OnlineProbeClient.View.Seat);
            report.activeSleeveRenderers=body.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r=>r.enabled && r.name=="First-person body");
            var animator=body.GetComponentInChildren<Animator>();
            if(!animator || !animator.isHuman) return;
            var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
            if(left) report.leftWristViewport=camera.WorldToViewportPoint(left.position);
            if(right) report.rightWristViewport=camera.WorldToViewportPoint(right.position);
        }

        void CheckPurchases(in SeatView view)
        {
            if(purchaseCount==2 && report.purchaseRound==view.Match.Round &&
                (view.Draft.Armor || view.Armor) && (view.Draft.Helmet || view.Helmet))
            {
                report.creditsAfterPurchase=view.Credits;
                report.pairedPurchasesConfirmed=report.creditsAfterPurchase<report.creditsBeforePurchase;
                return;
            }
            if(!view.CanBuy || view.Match.Phase!=RoundPhase.Buy) return;
            if(report.purchaseRound!=view.Match.Round)
            {
                if(view.Armor || view.Helmet || view.Draft.Armor || view.Draft.Helmet) return;
                report.purchaseRound=view.Match.Round;
                report.creditsBeforePurchase=view.Credits;
                game.OnlineProbeKeyboard.Order(BuySlot.Armor,null,false);
                game.OnlineProbeKeyboard.Order(BuySlot.Helmet,null,false);
                purchaseCount=2;
            }
        }

        void LateUpdate()
        {
            if(report.loadingCaptured || finished || step<6 || step>7 || root==null || !Visible("match-loading")) return;
            var percent=root.Q<Label>(className:"load-percent");
            if(percent==null || !int.TryParse(percent.text.Trim().TrimEnd('%'),out int value) || value<96) return;
            ScreenCapture.CaptureScreenshot(Own("loading.png"));
            report.loadingCaptured=true;
        }

        void Quit(int code)
        {
            finished=true;
            RestoreLastPlayer();
            Application.Quit(code);
        }

        void OnApplicationQuit()
        {
            if(manualPlay && !finished) RestoreLastPlayer();
        }

        void RestoreLastPlayer()
        {
            if(hadPlayer) PlayerPrefs.SetString("AnimeCS.LastPlayerId",previousPlayer); else PlayerPrefs.DeleteKey("AnimeCS.LastPlayerId");
            PlayerPrefs.Save();
        }
    }
}
