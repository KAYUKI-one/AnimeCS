using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeCS.Accounts;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        internal const int PollSeconds=2;
        static readonly Dictionary<string,string> PageTitles = new()
        {
            ["character"]="角色背包", ["inventory"]="库存一览", ["equipment"]="装备配置",
            ["shop"]="商店", ["tasks"]="任务手册", ["mode"]="模式选择", ["settings"]="设置"
        };
        string _dialogKey, _partySignature, _socialSignature, _joinedBattle, _headerRoom, _chatDraft = "", _friendDraft = "";
        bool _busy, _signedIn, _polling;
        IVisualElementScheduledItem _noticeTimer;
        AccountState State => _host.Account.State;

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        void BindNavigation()
        {
            var tabs = _screen.Q("top-tabs");
            void Nav(VisualElement host, string id, string title, Action action)
            {
                var button=new Button(action){name=id=="shop"?"shop-tab":id};
                var icon=new VisualElement{pickingMode=PickingMode.Ignore};
                icon.AddToClassList("glyph"); icon.AddToClassList("glyph-"+id); button.Add(icon);
                button.Add(new Label(title){pickingMode=PickingMode.Ignore}); host.Add(button);
            }
            Nav(tabs,"character","角色",()=>Open("character"));
            Nav(tabs,"inventory","库存",()=>Open("inventory"));
            Nav(tabs,"home","大厅",Close);
            Nav(tabs,"equipment","装备",()=>Open("equipment"));
            Nav(tabs,"shop","商店",()=>Open("shop"));
            var dock = _screen.Q("dock");
            Nav(dock,"world","世界",()=>Dialog("world"));
            Nav(dock,"friends","好友",()=>Dialog("friends"));
            Nav(dock,"tasks","任务",()=>Open("tasks"));
            Nav(dock,"settings","设置",()=>Open("settings"));
            Nav(dock,"config","配置",()=>Dialog("config"));
            _screen.Q<Button>("invite").clicked+=()=>Dialog("friends");
            _screen.Q<Button>("profile").clicked+=()=>Dialog("profile");
            _screen.Q<Button>("manual").clicked+=()=>Open("tasks");
            _screen.Q<Button>("chat-preview").clicked+=()=>Dialog("world");
            _screen.Q<Button>("mode").clicked+=()=>Open("mode");
            _screen.Q<Button>("start").clicked+=StartGame;
            _screen.Q<Button>("close").clicked+=Back;
            _screen.Q<Button>("dialog-close").clicked+=HideDialog;
            _screen.Q("overlay").RegisterCallback<ClickEvent>(e=>{if(e.target==_screen.Q("overlay")) HideDialog();});
            _screen.RegisterCallback<GeometryChangedEvent>(_=>PositionNames());
            SetVisible(_screen.Q("notice"),false);
        }

        void SelectTab(string key)
        {
            foreach(var tab in _screen.Q("top-tabs").Children()) tab.EnableInClassList("active",tab.name==(key=="shop"?"shop-tab":key));
        }

        void BindAccount()
        {
            var login=_screen.Q<Button>("auth-login");
            var register=_screen.Q<Button>("auth-register");
            var retry=_screen.Q<Button>("auth-retry");
            login.clicked+=()=>Authenticate(false);
            register.clicked+=()=>Authenticate(true);
            retry.clicked+=()=>Authenticate(false);
            SetVisible(retry,false);
            _screen.Q<TextField>("auth-password").RegisterCallback<KeyDownEvent>(e=>
            {
                if(e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter) Authenticate(false);
            });
            _screen.Q<TextField>("auth-id").value=_host.Account.LastPlayerId;
            _screen.schedule.Execute(Poll).Every(PollSeconds*1000);
        }

        async void Authenticate(bool register)
        {
            if (_busy || _signedIn) return;
            _busy=true;
            var auth=_screen.Q("auth");
            var status=auth.Q<Label>("auth-status");
            var id=auth.Q<TextField>("auth-id");
            var password=auth.Q<TextField>("auth-password");
            var login=auth.Q<Button>("auth-login");
            var signup=auth.Q<Button>("auth-register");
            var retry=auth.Q<Button>("auth-retry");
            login.SetEnabled(false); signup.SetEnabled(false); retry.SetEnabled(false);
            try
            {
                if (State==null)
                {
                    status.text=register?"正在注册唯一 ID…":"正在登录…";
                    await _host.Account.AuthenticateAsync(register,id.value.Trim(),password.value);
                    if (!_host.IsActive) return;
                    password.value="";
                }
                SetVisible(login,false); SetVisible(signup,false); SetVisible(id,false); SetVisible(password,false);
                auth.Q<Label>("auth-heading").text="检查资源更新";
                status.text="正在核对资源清单…";
                string result=await _host.Account.CheckUpdateAsync();
                if (!_host.IsActive) return;
                _signedIn=true;
                Show();
                Notice(result);
            }
            catch (Exception error)
            {
                if (_host.IsActive) {status.text=error.Message; SetVisible(retry,State!=null);}
            }
            finally
            {
                _busy=false;
                if (_host.IsActive) {login.SetEnabled(true); signup.SetEnabled(true); retry.SetEnabled(true);}
            }
        }

        void RunAction(LobbyAccountAction action, string argument = null, Action after = null) =>
            RunAction(() => _host.Account.RequestAsync(new LobbyAccountIntent(action, argument)), after);

        async void RunAction(Func<Task> request, Action after = null, bool requireSignIn = true)
        {
            if (_busy || requireSignIn && !_signedIn) return;
            _busy=true;
            try
            {
                await request();
                if (!_host.IsActive) return;
                RefreshHeader();
                after?.Invoke();
            }
            catch(Exception error) { if(_host.IsActive) Notice(error.Message); }
            finally { _busy=false; }
        }

        async void Poll()
        {
            if (!_signedIn || !_host.IsActive || !_host.InLobby || _polling || _busy) return;
            _polling=true;
            try
            {
                await _host.Account.RequestAsync(new LobbyAccountIntent(LobbyAccountAction.Refresh));
                if (!_host.IsActive || !_host.InLobby || _busy) return;
                RefreshHeader();
                EnterPartyBattle();
            }
            catch(Exception error) { if(_host.IsActive) _screen.Q<Button>("chat-preview").text="连接状态    "+error.Message; }
            finally { _polling=false; }
        }

        void StartGame()
        {
            if (!_signedIn || _busy) return;
            if (State.party is {Length:>1})
            {
                if (!string.IsNullOrEmpty(State.battleId) && State.battleFinished)
                {
                    if (State.party[0]!=State.id) { Notice("上一场比赛已结束，等待队长重新开赛"); return; }
                    string finishedRoom=State.battleId;
                    RunAction(LobbyAccountAction.RestartBattle,finishedRoom,EnterPartyBattle);
                    return;
                }
                if (!string.IsNullOrEmpty(State.battleId))
                {
                    // A returning party member may explicitly reconnect to the same room.
                    _joinedBattle=null;
                    EnterPartyBattle();
                    return;
                }
                if (State.party[0]!=State.id) { Notice("等待队长开始对局"); return; }
                RunAction(LobbyAccountAction.StartParty,after:EnterPartyBattle);
            }
            else { Close(); LocalMatchRequested?.Invoke(); }
        }

        internal void EnterPartyBattle()
        {
            if (!_host.InLobby || State.battleFinished || string.IsNullOrEmpty(State.battleId) || State.battleId==_joinedBattle) return;
            _joinedBattle=State.battleId;
            Close();
            PartyMatchRequested?.Invoke();
        }

        void RefreshHeader(bool force=false)
        {
            if (State==null) return;
            _screen.Q<Label>("profile-name").text=State.id;
            _screen.Q<Label>("profile-level").text=$"LV. {State.level:00} · AnimeCS";
            _screen.Q<Image>("profile-icon").image=_catalog.Find<CharacterDefinition>(State.avatarId)?.Icon;
            var party=State.party??Array.Empty<string>();
            _screen.Q<Label>("invite-count").text=$"{Math.Max(1,party.Length)}/{MatchFormat.TeamSize}";
            bool grouped=party.Length>1;
            bool leader=grouped && party[0]==State.id;
            bool currentRoom=!string.IsNullOrEmpty(State.battleId) && !State.battleFinished;
            var start=_screen.Q<Button>("start");
            start.text=!grouped ? "开始游戏     ↗" : currentRoom ? "进入当前对局" :
                !leader ? "等待队长开赛" : State.battleFinished ? "开始下一场" : "开始小队对局";
            start.SetEnabled(!grouped || currentRoom || leader);
            var latest=State.messages?.LastOrDefault();
            _screen.Q<Button>("chat-preview").text=latest==null?"世界信息    欢迎来到晴空中庭":"世界信息    "+latest.id+"   "+latest.text;
            var members=State.members??Array.Empty<PartyMemberState>();
            string partyKey=string.Join("|",members.Select(m=>m.id+" "+m.showCharacterId+" "+m.showSkinId));
            if(force || _headerRoom!=State.battleId || partyKey!=_partySignature) RefreshMap();
            _headerRoom=State.battleId;
            if ((force || partyKey!=_partySignature) && _section==null && _dialogKey!="config")
            {
                _partySignature=partyKey;
                _host.Preview.ShowTeammates(members.Where(member => member.id != State.id).ToArray());
                var tags=_screen.Q("nameplates");
                tags.Clear();
                var names=new[]{State.id}.Concat(members.Where(m=>m.id!=State.id).Select(m=>m.id));
                foreach(var name in names)
                {
                    var tag=new Label(name==State.id?"自己  |  "+name:name){pickingMode=PickingMode.Ignore};
                    tag.AddToClassList("nameplate");tags.Add(tag);
                }
                PositionNames();
            }
            string social=string.Join("|",State.invites??Array.Empty<string>())+";"+string.Join("|",State.friends??Array.Empty<string>())+";"+partyKey+";"+State.battleId+";"+State.battleFinished;
            if (_dialogKey=="friends" && social!=_socialSignature) DrawFriendsList();
            _socialSignature=social;
            if (_dialogKey=="world") RefreshMessages();
        }

        void PositionNames()
        {
            if (!_signedIn || _section!=null || !_host.InLobby) return;
            var names=_screen.Q("nameplates");
            for(int i=0;i<names.childCount;i++)
            {
                if (!_host.Preview.TryNamePosition(i,out var p)) continue;
                names[i].style.left=Length.Percent(p.x*100f);
                names[i].style.top=Length.Percent(Mathf.Clamp(1-p.y,.10f,.93f)*100f);
            }
        }

        internal void Notice(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var label=_screen.Q<Label>("notice");
            label.text=message; SetVisible(label,true);
            _noticeTimer?.Pause();
            _noticeTimer=label.schedule.Execute(()=>SetVisible(label,false)).StartingIn(3500);
        }
    }
}
