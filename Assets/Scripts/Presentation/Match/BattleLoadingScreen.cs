using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    // Stays above both lobby and HUD until the first rendered frame and network snapshot are ready.
    internal sealed class BattleLoadingScreen : VisualElement
    {
        private readonly Label _map, _mode, _brief, _objectives, _status, _tip, _percent;
        private readonly VisualElement _art, _radar, _fill, _teams, _briefing;
        private readonly VisualElement[,] _portraits = new VisualElement[2, LocalMatch.TeamSize];
        private readonly Label[,] _names = new Label[2, LocalMatch.TeamSize];
        private readonly Label[,] _accounts = new Label[2, LocalMatch.TeamSize];
        private readonly Label[] _teamTitles = new Label[2];
        private readonly ContentCatalog _catalog;
        private float _progress;
        private bool _battle;

        internal BattleLoadingScreen(ContentCatalog catalog, StyleSheet styles)
        {
            _catalog = catalog;
            name = "match-loading";
            if (styles) styleSheets.Add(styles);
            _art = Block(this, "load-art");
            var shading = Block(this, "load-overlay");
            var top = Block(shading, "load-brand-row");
            Text(top, "ANIMECS", "load-brand");
            _status = Text(top, "", "load-status");
            var heading = Block(shading, "load-heading");
            _map = Text(heading, "", "load-title");
            _mode = Text(heading, "", "load-mode");
            _teams = Block(shading, "load-teams");
            SidePanel(Side.Defend);
            Text(_teams, "VS", "load-vs");
            SidePanel(Side.Attack);
            _briefing = Block(shading, "load-briefing");
            _radar = Block(_briefing, "load-radar");
            var mapInfo = Block(_briefing, "load-copy");
            Text(mapInfo, "地图简介", "load-subhead");
            _brief = Text(mapInfo, "", "load-description");
            var objective = Block(_briefing, "load-copy");
            Text(objective, "行动目标", "load-subhead");
            _objectives = Text(objective, "", "load-description");
            var bottom = Block(shading, "load-bottom");
            _tip = Text(bottom, "", "load-tip");
            _fill = Block(Block(bottom, "load-track"), "load-fill");
            _percent = Text(bottom, "", "load-percent");
            style.display = DisplayStyle.None;
        }

        internal void Begin(MapDefinition map, bool online, AnimeCS.Accounts.PartyMemberState[] party = null)
        {
            _battle = true;
            _progress = 0;
            _teams.style.display = _briefing.style.display = DisplayStyle.Flex;
            _map.text = map.DisplayName;
            _mode.text = map.Practice ? "自由训练   /   AIM PRACTICE" : (online ? "联机爆破" : "人机爆破") + "   /   5 v 5";
            _brief.text = map.LoadingBrief;
            _objectives.text = map.Practice ? "自由补给，调整瞄准与射击节奏。" : "攻方在目标区域安置装置。守方阻止安置或完成拆除。";
            _tip.text = map.Practice ? "训练提示   /   打开装备菜单可更换武器。" : "战术提示   /   脚步会暴露方位，靠近目标区时留意队友。";
            _art.style.backgroundImage = map.LoadingArt ? new StyleBackground(map.LoadingArt) : StyleKeyword.None;
            _radar.style.backgroundImage = map.Radar ? new StyleBackground(map.Radar) : StyleKeyword.None;
            for (int group=0; group<2; group++)
                for (int slot=0; slot<LocalMatch.TeamSize; slot++)
                    SetCard((Side)group, slot, null, online ? "等待同步" : "准备中");
            TeamTitles(!online);
            if (online && party != null)
                for (int i=0;i<Mathf.Min(party.Length,LocalMatch.TeamSize);i++)
                {
                    var actor=_catalog.Find<CharacterDefinition>(party[i].showCharacterId);
                    DrawCard(Side.Defend,i,actor,party[i].id);
                }
            Progress(0, "正在读取地图");
            style.display = DisplayStyle.Flex;
        }

        internal void Boot()
        {
            _battle = false;
            _progress = 0;
            _teams.style.display = _briefing.style.display = DisplayStyle.None;
            _map.text = "晴空中庭"; _mode.text = "ANIMECS   /   LOBBY";
            _tip.text = "首次启动正在准备界面资源";
            Progress(0, "正在启动");
            style.display = DisplayStyle.Flex;
        }

        internal void ShaderProgress(int done, int total)
        {
            float fraction = total > 0 ? (float)done/total : 1;
            Progress(_battle ? .43f + .18f*fraction : fraction, "正在准备着色器  " + done + " / " + total);
        }

        internal void SetCard(Side side, int slot, string outfitId, string label)
        {
            if ((uint)slot >= LocalMatch.TeamSize) return;
            var fit = _catalog.Find<OutfitDefinition>(outfitId);
            DrawCard(side,slot,fit ? fit.Character : null,label);
        }

        internal void TeamTitles(bool assigned)
        {
            _teamTitles[(int)Side.Defend].text=assigned ? "守方   DEFENDERS" : "我的小队   PARTY";
            _teamTitles[(int)Side.Attack].text=assigned ? "攻方   ATTACKERS" : "等待阵容   SYNC";
        }

        private void DrawCard(Side side,int slot,CharacterDefinition actor,string label)
        {
            var image = actor ? actor.BattlePortrait : null;
            _portraits[(int)side,slot].style.backgroundImage = image ? new StyleBackground(image) : StyleKeyword.None;
            _names[(int)side,slot].text = actor ? actor.DisplayName : label;
            var account=_accounts[(int)side,slot];
            account.text = actor ? label : "";
            account.style.display = actor && !string.IsNullOrEmpty(label) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        internal void Progress(float fraction, string message)
        {
            _progress = Mathf.Max(_progress, Mathf.Clamp01(fraction));
            _fill.style.width = Length.Percent(_progress*100);
            _percent.text = Mathf.RoundToInt(_progress*100) + "%";
            _status.text = message;
        }

        private void SidePanel(Side side)
        {
            var panel = Block(_teams, "load-team");
            panel.AddToClassList(side == Side.Defend ? "load-defend" : "load-attack");
            _teamTitles[(int)side] = Text(panel, side == Side.Defend ? "守方   DEFENDERS" : "攻方   ATTACKERS", "load-team-title");
            var row = Block(panel, "load-row");
            for (int i = 0; i < LocalMatch.TeamSize; i++)
            {
                var card = Block(row, "load-card");
                _portraits[(int)side,i] = Block(card, "load-portrait");
                _names[(int)side,i] = Text(card, "", "load-name");
                _accounts[(int)side,i] = Text(card, "", "load-player");
            }
        }
        private static VisualElement Block(VisualElement parent, string css)
        {
            var node = new VisualElement(); node.AddToClassList(css); parent.Add(node); return node;
        }
        private static Label Text(VisualElement parent, string value, string css)
        {
            var node = new Label(value); node.AddToClassList(css); parent.Add(node); return node;
        }
    }
}
