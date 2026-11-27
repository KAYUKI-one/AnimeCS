using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    public sealed partial class LocalGame
    {
        private sealed class ReplicaPort : ISeatPort
        {
            internal SeatView State;
            internal SeatDatagramClient Input;
            public SeatView Read() => State;
            public SeatView WatchTeam(int direction=0) => Input!=null ? Input.WatchTeam(direction) : State;
            public bool Submit(int tick,SeatIntent intent) => Input?.Submit(intent) == true;
        }

#if ANIMECS_RENDER_PROBE
        internal KeyboardSeat OnlineProbeKeyboard => _keyboard;
        internal Transform OnlineProbeActor(int seat) => _actors[seat].transform;
        internal SeatDatagramClient OnlineProbeClient => _online;
#endif
        private bool _onlineRequested;
        private SeatDatagramClient _online;
        private readonly ReplicaPort[] _replicas = new ReplicaPort[LocalMatch.Seats];
        private readonly string[] _onlineNames = new string[LocalMatch.Seats];
        private readonly bool[] _shown = new bool[LocalMatch.Seats];
        private readonly bool[] _onlineAllyTags = new bool[LocalMatch.Seats];
        private bool _onlineMenu, _roomActionPending, _checkingRoom, _finishedSeen, _showingReconnect;
        private double _nextRoomCheck;
        private const int ActiveRoomPollSeconds=10;
        private string _onlineRoom;
        private string _eventRun;
        private int _completedRounds;
        internal string BattleConnectionError { get; private set; }

        public void StartAccountBattle()
        {
            var state=Account?.State;
            if (!InLobby || _preparing || _onlineRequested || state == null || state.battleFinished || string.IsNullOrEmpty(state.battleTicket)) return;
            var map=_catalog.Find<MapDefinition>(state.mapId);
            if (!map || map.Archived || map.Practice) { ShowConnectionError("对局地图与客户端目录不匹配"); return; }
            if (state.contentVersion!=_catalog.ContentVersion) { ShowConnectionError("战斗内容版本不一致，请更新客户端与服务器后重试"); return; }
            _onlineRoom=state.battleId;
            _onlineRequested=true; BattleConnectionError=null;
            if (SelectedMap != map) SelectMap(map);
            Restart();
        }

        private IEnumerator EnterOnline()
        {
            _match=null;
            var join=Account.State;
            if (join.battleId!=_onlineRoom || join.battleFinished || join.mapId!=_catalog.Map.Id || join.contentVersion!=_catalog.ContentVersion)
            { OnlineFailure("载入的地图或内容版本与当前房间不一致"); yield break; }
            _loading.Progress(.72f,"正在连接战斗服务器");
            var addresses=Dns.GetHostAddressesAsync(join.battleHost);
            double deadline=Time.realtimeSinceStartupAsDouble+20;
            while (!addresses.IsCompleted && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            if (addresses.Status != TaskStatus.RanToCompletion || addresses.Result.Length==0)
            { OnlineFailure("无法解析战斗服务器地址"); yield break; }
            try
            {
                var ip=Array.Find(addresses.Result,a=>a.AddressFamily==AddressFamily.InterNetwork) ?? addresses.Result[0];
                _online=new SeatDatagramClient(new IPEndPoint(ip,join.battlePort),_rules.movement,join.battleTicket);
                _online.BindCatalog(_catalog);
            }
            catch (Exception e) { OnlineFailure("连接失败："+e.GetBaseException().Message); yield break; }
            _loading.Progress(.85f,"等待服务器首个快照");
            while (Time.realtimeSinceStartupAsDouble<deadline)
            {
                _online.Poll();
                if (_online.Connected && _online.HasRoster) break;
                yield return null;
            }
            if (!_online.Connected || !_online.HasRoster) { OnlineFailure("等待战斗同步超时，请检查房间、地址及 UDP 连接后重试"); yield break; }

            _loading.TeamTitles(true);
            var self=_online.PlayView;
            for (int i=0;i<_replicas.Length;i++)
            {
                _replicas[i] ??= new ReplicaPort();
                _replicas[i].Input=i==self.Seat ? _online : null;
                _ports[i]=_replicas[i];
                var member=_online.Member(i,out bool visible);
                _onlineNames[i]=join.party!=null && i<join.party.Length ? join.party[i] : $"BOT {i+1:00}";
                ShowLoadingOutfit(member.Side,i%LocalMatch.TeamSize,member.OutfitId,_onlineNames[i]);
                _loading.Progress(.86f+.10f*(i+1)/_replicas.Length,"正在绘制角色");
                if (visible && member.Health>0)
                {
                    _actors[i].PresentReplica(member.Position,member.Yaw,member.Pitch,member.Motion.Crouch);
                    _avatars[i].Present(member,i==self.Seat);
                    _shown[i]=true;
                    yield return null;
                    _online.Poll();
                }
            }
            _world.WarmFeedback(); _preview.Hide();
            InLobby=false; _lobbyHost.style.display=DisplayStyle.None; _hudHost.style.display=DisplayStyle.Flex;
            _hud.StartMatch(_online.WorldView,ReadWardrobe().current,false,IsPartyLeader,IsPartyLeader);
            _keyboard.SyncRound(self,true); _keyboard.ResetPointer(); MapLight(true); ApplyAspect(); _time=0;
            RenderOnline();
            yield return null;
            _loading.Progress(1,"同步完成");
            yield return null;
            _loading.style.display=DisplayStyle.None; _entry=null; _preparing=false;
            _nextRoomCheck=Time.realtimeSinceStartupAsDouble+ActiveRoomPollSeconds;
            UpdatePointer();
        }

        private void UpdateOnline(bool pause,bool shop)
        {
            _online.Poll();
            if (_showingReconnect!=_online.Reconnecting)
            {
                _showingReconnect=_online.Reconnecting;
                if (_showingReconnect) _nextRoomCheck=0;
                _time=0;
                _keyboard.ResetPointer();
                _hud.ShowReconnecting(_showingReconnect);
                UpdatePointer();
            }
            if (_online.Finished && !_finishedSeen)
            {
                _finishedSeen=true;
                _nextRoomCheck=0;
            }
            if (Time.realtimeSinceStartupAsDouble>=_nextRoomCheck && !_checkingRoom && !_roomActionPending)
                RefreshOnlineRoom();
            if (_online==null) return;
            if (_online.Finished)
            {
                _onlineMenu=false;
                RenderOnline(); UpdatePointer();
                return;
            }
            if (_online.Reconnecting) return;
            if (!_online.Connected) { OnlineFailure("重连战斗服务器超时，已返回大厅"); return; }
            var self=_online.PlayView;
            ReadPlayerInput(self,pause,shop);
            _time=Math.Min(_time+Time.unscaledDeltaTime,4d/MatchRules.TickRate);
            while (_time>=1d/MatchRules.TickRate)
            {
                _time-=1d/MatchRules.TickRate;
                _online.SubmitFrom(_keyboard,self);
            }
            RenderOnline();
            UpdatePointer();
        }

        private void RenderOnline()
        {
            var self=_online.PlayView;
            self.Position=_online.PredictedPosition;
            var watched=self.Health>0 ? self : _online.WatchTeam();
            bool mouseLook=watched.Seat==self.Seat && !_online.Finished;
            bool orbit=Orbiting(watched);
            var eye=watched.Position+Vector3.up*SeatMovement.EyeHeight(watched.Motion.Crouch);
            PositionCamera(watched,eye,mouseLook,1);
            for (int i=0;i<_replicas.Length;i++)
            {
                var member=_online.Member(i,out bool visible);
                if (i==watched.Seat)
                {
                    var outfit=member.OutfitId; var weapon=member.WeaponId; var skin=member.SkinId;
                    member=watched; member.OutfitId??=outfit;
                    member.WeaponId??=weapon; member.SkinId??=skin;
                    visible=true;
                }
                else if (i==self.Seat) member=self;
                member.Match=self.Match;member.DisplayId=_onlineNames[i];
                _onlineAllyTags[i]=_online.CanIdentifyAlly(i);
                _replicas[i].State=member;
                _hudSeats[i]=member;
                if (visible && member.Health>0 && member.OutfitId!=null)
                {
                    _avatars[i].BeforeAnimation();
                    _actors[i].PresentReplica(member.Position,member.Yaw,member.Pitch,member.Motion.Crouch);
                    _avatars[i].Present(member,i==watched.Seat && !orbit);
                    _shown[i]=true;
                }
                else if (_shown[i]) { _avatars[i].Hide(); _shown[i]=false; }
            }
            self=_replicas[self.Seat].State;
            if(watched.Seat>=0) watched=_replicas[watched.Seat].State;
            DrawWorld(_online.WorldView,watched.Seat<0,self.Match.Tick);
            _hud.Present(self,watched,_hudSeats,_onlineMenu);
            _audio.Present(watched);
        }

        private void OnlineFailure(string reason)
        {
            _entry=null;
            ReturnToLobby();
            ShowConnectionError(reason);
        }
        private void ShowConnectionError(string reason)
        {
            BattleConnectionError=reason;
            _lobby.Notice(reason);
        }
        private void StopOnline()
        {
            if (!_onlineRequested) return;
            _onlineRequested=false; _onlineMenu=false; _checkingRoom=false;
            _roomActionPending=false; _finishedSeen=false; _showingReconnect=false; _onlineRoom=null; _nextRoomCheck=0;
            _online?.Dispose(); _online=null;
            Array.Clear(_ports,0,_ports.Length); Array.Clear(_shown,0,_shown.Length);
            Array.Clear(_onlineAllyTags,0,_onlineAllyTags.Length);
            foreach(var replica in _replicas) if(replica!=null) replica.Input=null;
        }

        private bool IsPartyLeader => Account?.State?.party is {Length:>0} party && party[0]==Account.State.id;
        private void RestartOnlineRoom()
        {
            if (_online?.Finished==true) ChangeRoom(true);
        }
        private void EndOnlineRoom() => ChangeRoom(false);

        private async void ChangeRoom(bool restart)
        {
            if (_roomActionPending || _online==null || !IsPartyLeader) return;
            string previous=_onlineRoom;
            _roomActionPending=true;
            _hud.SetRoomAction(true,restart?"正在创建新对局…":"正在结束房间…");
            try
            {
                if (restart) await Account.RestartBattleAsync(previous);
                else await Account.EndBattleAsync(previous);
                if (!this || _onlineRoom!=previous) return;
                if (restart && (string.IsNullOrEmpty(Account.State.battleId) || Account.State.battleId==previous))
                    throw new InvalidOperationException("服务器尚未创建新对局，请重试");
                ReturnToLobby();
                if(restart) _lobby.EnterPartyBattle();
            }
            catch(Exception error) { if (this && _onlineRoom==previous) _hud.SetRoomAction(false,error.Message); }
            finally { if (this && _onlineRoom==previous) _roomActionPending=false; }
        }

        private async void RefreshOnlineRoom()
        {
            string previous=_onlineRoom;
            _checkingRoom=true;
            _nextRoomCheck=Time.realtimeSinceStartupAsDouble+(_online.Finished ? LobbyScreen.PollSeconds : ActiveRoomPollSeconds);
            try
            {
                await Account.RefreshAsync();
                if (!this || _roomActionPending || _onlineRoom!=previous) return;
                var state=Account.State;
                if (string.IsNullOrEmpty(state.battleId) && !_online.Finished)
                {
                    ReturnToLobby();
                    _lobby.Notice("小队房间已结束");
                    return;
                }
                if (state.battleId==previous)
                {
                    if (!state.battleFinished) _online.RenewTicket(state.battleTicket);
                    return;
                }
                if (string.IsNullOrEmpty(state.battleId) || state.battleFinished) return;
                ReturnToLobby();
                _lobby.EnterPartyBattle();
            }
            catch(Exception error)
            {
                if (this && !_roomActionPending && _onlineRoom==previous && _online.Finished)
                    _hud.SetRoomAction(false,"房间状态暂不可用："+error.Message);
            }
            finally { if (this && _onlineRoom==previous) _checkingRoom=false; }
        }

        private void RecordEntry(MatchMode mode)
        {
            _eventRun=mode!=MatchMode.Online && Account?.State?.localPracticeProgress==true ? "local-"+Guid.NewGuid().ToString("N") : null;
            var view=_match.Public;
            _completedRounds=view.Score0+view.Score1;
            SendProgress("entry",mode==MatchMode.AimPractice ? "practice.enter" : "match.enter");
        }
        private void ObserveRound(in PublicMatchView view)
        {
            int total=view.Score0+view.Score1;
            if (total<=_completedRounds || view.Phase==RoundPhase.Practice) return;
            _completedRounds=total;
            SendProgress("round."+total,"round.complete");
        }
        private async void SendProgress(string operation,string kind)
        {
            if (Account?.State==null || string.IsNullOrEmpty(_eventRun)) return;
            try { await Account.ProgressAsync(_eventRun+"."+operation,kind); }
            catch (Exception e) { Debug.LogWarning("任务事件同步失败："+e.Message); }
        }
    }
}
