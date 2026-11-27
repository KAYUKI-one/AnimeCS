using System;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        string _messageKey;
        ScrollView DialogBody => _screen.Q<ScrollView>("dialog-content");

        static Label SectionLabel(VisualElement parent,string title)
        {
            var label=new Label(title);label.AddToClassList("subhead");parent.Add(label);return label;
        }

        static Button ActionButton(VisualElement parent,string title,string id,Action action,bool accent=false)
        {
            var button=new Button(action){name=id,text=title};
            button.EnableInClassList("accent",accent); parent.Add(button);return button;
        }

        void HideDialog()
        {
            bool wasDisplay = _dialogKey == "config";
            _dialogKey=null;
            SetVisible(_screen.Q("overlay"),false);
            if (wasDisplay) _game.EndInspection();
        }

        void Dialog(string kind)
        {
            if (!_signedIn) return;
            _dialogKey=kind;
            var overlay=_screen.Q("overlay");
            overlay.EnableInClassList("drawer",kind=="world");
            overlay.EnableInClassList("display-config",kind=="config");
            SetVisible(overlay,true);
            var body=DialogBody;body.Clear();
            var title=_screen.Q<Label>("dialog-title");
            switch (kind)
            {
                case "world": title.text="世界频道"; Chat(); break;
                case "friends": title.text="好友与队伍"; Friends(); break;
                case "profile": title.text="个人资料"; Profile(); break;
                case "config": title.text="大厅展示配置"; DisplayConfig(); break;
                case "portrait":
                    title.text=_character.DisplayName+"  ·  角色立绘";
                    var art=new Image{image=_character.Portrait,scaleMode=ScaleMode.ScaleToFit};
                    art.style.height=Mathf.Max(185,_screen.resolvedStyle.height*.45f);
                    body.Add(art);break;
            }
        }

        void Chat()
        {
            var body=DialogBody;
            var lines=new ScrollView{name="messages"};
            lines.style.flexGrow=1; lines.style.minHeight=120;
            lines.style.maxHeight=Mathf.Max(150,_screen.resolvedStyle.height*.56f);
            body.Add(lines);
            var input=new TextField{name="world-message",label="消息",value=_chatDraft,maxLength=200};
            input.RegisterValueChangedCallback(e=>_chatDraft=e.newValue);body.Add(input);
            void Send()
            {
                string message=input.value.Trim();
                if (message.Length==0) return;
                RunAction(()=>_game.Account.SendChatAsync(message),()=>{
                    if (input.value==message) input.value="";
                    _chatDraft=input.value;
                    RefreshMessages();
                });
            }
            input.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Return) Send();});
            ActionButton(body,"发送消息","world-send",Send,true);
            _messageKey=null;RefreshMessages();
        }

        void RefreshMessages()
        {
            var panel=DialogBody.Q<ScrollView>("messages");
            if (panel==null) return;
            var messages=State.messages??Array.Empty<AnimeCS.Accounts.ChatLine>();
            string key=string.Join("\n",messages.Select(m=>m.id+"\t"+m.text));
            if (key==_messageKey) return;
            _messageKey=key;panel.Clear();
            if (messages.Length==0) panel.Add(new Label("频道暂无消息"));
            foreach(var entry in messages)
            {
                var line=new Label(entry.id+"    "+entry.text);
                line.style.whiteSpace=WhiteSpace.Normal;panel.Add(line);
            }
            if (panel.childCount>0) panel.schedule.Execute(()=>{if(panel.childCount>0) panel.ScrollTo(panel[panel.childCount-1]);});
        }

        void Friends()
        {
            var body=DialogBody;
            var input=new TextField("玩家 ID"){name="add-friend-id",value=_friendDraft,maxLength=32};
            input.RegisterValueChangedCallback(e=>_friendDraft=e.newValue);body.Add(input);
            ActionButton(body,"通过 ID 添加好友","add-friend",()=>
                RunAction(()=>_game.Account.AddFriendAsync(input.value.Trim()),()=>{input.value="";Notice("已添加好友");DrawFriendsList();}),true);
            body.Add(new VisualElement{name="friend-list"});DrawFriendsList();
        }

        void DrawFriendsList()
        {
            var list=DialogBody.Q("friend-list");
            if (list==null) return;
            list.Clear();
            void Line(string name,string title,string key,Action action)
            {
                var row=new VisualElement();row.AddToClassList("member-row");list.Add(row);
                row.Add(new Label(name));
                if (action!=null) ActionButton(row,title,key,action);
            }
            var invites=State.invites??Array.Empty<string>();
            if (invites.Length>0) SectionLabel(list,"队伍邀请");
            foreach(string leader in invites)
                Line(leader,"接受邀请","accept-"+leader,()=>RunAction(()=>_game.Account.AcceptInviteAsync(leader),()=>{Notice("已加入队伍");DrawFriendsList();}));
            var party=State.party??Array.Empty<string>();
            bool isLeader=party.Length>0 && party[0]==State.id;
            bool activeRoom=!string.IsNullOrEmpty(State.battleId) && !State.battleFinished;
            SectionLabel(list,$"当前队伍  {Math.Max(1,party.Length)}/{MatchFormat.TeamSize}");
            if (party.Length==0) Line(State.id,null,null,null);
            foreach(string member in party) Line(member+(member==party[0]?"  ·  队长":""),null,null,null);
            if (party.Length>1 && isLeader && !string.IsNullOrEmpty(State.battleId))
            {
                string room=State.battleId;
                ActionButton(list,"结束小队房间","end-party-battle",()=>
                    RunAction(()=>_game.Account.EndBattleAsync(room),()=>{Notice("小队房间已结束");DrawFriendsList();}));
            }
            if (party.Length>1)
            {
                var leave=ActionButton(list,isLeader?"解散队伍":"离开队伍","leave-party",()=>RunAction(_game.Account.LeavePartyAsync,DrawFriendsList));
                leave.SetEnabled(!activeRoom);
                if(activeRoom) leave.tooltip="当前对局结束后可用";
            }
            var friends=State.friends??Array.Empty<string>();
            SectionLabel(list,"好友  "+friends.Length);
            if (friends.Length==0) list.Add(new Label("输入玩家 ID 添加第一位好友"));
            foreach (string friend in friends)
            {
                bool canInvite=!activeRoom && !party.Contains(friend) && (party.Length==0 || isLeader) && party.Length<MatchFormat.TeamSize;
                Line(friend,canInvite?"邀请入队":null,"invite-"+friend,canInvite?()=>RunAction(()=>_game.Account.InviteAsync(friend),()=>Notice("已向 "+friend+" 发送邀请")):null);
            }
        }

        string DisplayName(string id) => _catalog.Find<ContentDefinition>(id)?.DisplayName ?? "未设置";

        void Profile()
        {
            var body=DialogBody;
            var image=new Image{image=_catalog.Find<CharacterDefinition>(State.avatarId)?.Icon,scaleMode=ScaleMode.ScaleToFit};
            image.style.width=66; image.style.height=66;body.Add(image);
            SectionLabel(body,State.id);
            body.Add(new Label($"等级  {State.level}     经验值  {State.xp}"));
            body.Add(new Label($"段位  {State.rank}     段位分  {State.rankPoints}     胜利场次  {State.victories}"));
            body.Add(new Label("展示角色    "+DisplayName(State.showCharacterId)));
            body.Add(new Label("展示枪皮    "+DisplayName(State.showSkinId)));
            var signature=new TextField("个性签名"){value=State.signature??"",maxLength=80,name="profile-signature"};
            body.Add(signature);
            var avatars=OwnedCharacters();
            var avatar=SelectItem(body,"头像","profile-avatar",avatars,State.avatarId);
            ActionButton(body,"保存资料","save-profile",()=>RunAction(()=>_game.Account.SaveProfileAsync(signature.value,
                avatar.index>=0?avatars[avatar.index].Id:State.avatarId,State.showCharacterId,State.showSkinId),()=>{HideDialog();Notice("个人资料已保存");}),true);
        }

        ContentDefinition[] OwnedCharacters() => _catalog.Entries.OfType<CharacterDefinition>().Where(Owned).Cast<ContentDefinition>().ToArray();

        static DropdownField SelectItem(VisualElement host,string label,string key,ContentDefinition[] options,string current)
        {
            var names=options.Select(c=>c.DisplayName).ToList();
            var field=new DropdownField(label,names,options.Length==0?-1:Math.Max(0,Array.FindIndex(options,i=>i.Id==current))){name=key};
            host.Add(field);return field;
        }

        void DisplayConfig()
        {
            var body=DialogBody;
            var chars=OwnedCharacters();
            var finishes=_catalog.Entries.Where(i=>Owned(i) && (i is WeaponSkinDefinition || i is WeaponDefinition)).ToArray();
            var character=SelectItem(body,"展示角色","display-character",chars,State.showCharacterId);
            var weapon=SelectItem(body,"展示武器","display-skin",finishes,State.showSkinId);
            var thumb=new Image{scaleMode=ScaleMode.ScaleToFit}; thumb.AddToClassList("config-art");
            body.Add(thumb);
            void UpdateArt()
            {
                if (character.index < 0 || weapon.index < 0) return;
                var selected = finishes[weapon.index];
                thumb.image = Art(selected);
                var fit = _catalog.Entries.OfType<OutfitDefinition>()
                    .FirstOrDefault(o => Owned(o) && o.Character == chars[character.index]);
                if (!fit) return;
                var draft = _game.ReadWardrobe().current;
                draft.Select(fit);
                var skin = selected as WeaponSkinDefinition;
                var gun = skin ? skin.Weapon : selected as WeaponDefinition;
                if (!gun) return;
                _game.PreviewWardrobe(draft, gun.Id, false, previewWorn: draft.WornFor(fit.Character.Faction),
                    previewSkin: skin ? skin.Id : "");
                _game.CenterPreview(.37f);
            }
            weapon.RegisterValueChangedCallback(_=>UpdateArt());
            character.RegisterValueChangedCallback(_=>UpdateArt());
            UpdateArt();
            var save=ActionButton(body,"应用到大厅","save-display",()=>RunAction(()=>_game.Account.SaveProfileAsync(State.signature,State.avatarId,
                chars[character.index].Id,finishes[weapon.index].Id),()=>{HideDialog();RefreshHeader(true);Notice("大厅展示已更新");}),true);
            save.SetEnabled(chars.Length>0 && finishes.Length>0);
        }

        void Tasks()
        {
            Header("每日任务");
            var tasks=State.tasks??Array.Empty<AnimeCS.Accounts.TaskState>();
            if (tasks.Length==0) _content.Add(new Label("今天暂无任务"));
            foreach(var task in tasks)
            {
                var card=new VisualElement(); card.AddToClassList("task-row"); _content.Add(card);
                SectionLabel(card,task.title);
                card.Add(new Label(task.description));
                var meter=new ProgressBar{lowValue=0,highValue=Math.Max(1,task.goal),value=task.progress,title=task.progress+" / "+task.goal};
                card.Add(meter);
                card.Add(new Label($"经验 +{task.experience}     代币 +{task.credits}"));
                var button=ActionButton(card,task.claimed?"已领取":task.progress>=task.goal?"领取奖励":"进行中","claim-"+task.id,
                    ()=>RunAction(()=>_game.Account.ClaimAsync(task.id),()=>{if (_section=="tasks") Render();Notice("任务奖励已到账");}),true);
                button.SetEnabled(!task.claimed && task.progress>=task.goal);
            }
        }
    }
}
