using UnityEngine;
using UnityEngine.UIElements;
using Side = AnimeCS.Content.Side;

namespace AnimeCS.Play
{
    // Stays above both lobby and HUD until the first rendered frame and network snapshot are ready.
    internal sealed class BattleLoadingScreen : VisualElement
    {
        private readonly Label _map, _mode, _brief, _objectives, _status, _tip, _percent;
        private readonly VisualElement _art, _radar, _fill, _teams, _briefing;
        private readonly VisualElement[,] _portraits;
        private readonly Label[,] _names, _accounts;
        private readonly Label[] _teamTitles = new Label[2];
        private readonly int _teamSize;
        private float _progress;
        private bool _battle;

        internal BattleLoadingScreen(int teamSize, StyleSheet styles)
        {
            _teamSize = teamSize;
            _portraits = new VisualElement[2, teamSize];
            _names = new Label[2, teamSize];
            _accounts = new Label[2, teamSize];
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

        internal void Begin(string mapName, string brief, Texture2D art, Texture2D radar, bool practice, bool online)
        {
            _battle = true;
            _progress = 0;
            _teams.style.display = _briefing.style.display = DisplayStyle.Flex;
            _map.text = mapName;
            _mode.text = practice ? "自由训练   /   AIM PRACTICE" : $"{(online ? "联机爆破" : "人机爆破")}   /   {_teamSize} v {_teamSize}";
            _brief.text = brief;
            _objectives.text = practice ? "自由补给，调整瞄准与射击节奏。" : "攻方在目标区域安置装置。守方阻止安置或完成拆除。";
            _tip.text = practice ? "训练提示   /   打开装备菜单可更换武器。" : "战术提示   /   脚步会暴露方位，靠近目标区时留意队友。";
            _art.style.backgroundImage = art ? new StyleBackground(art) : StyleKeyword.None;
            _radar.style.backgroundImage = radar ? new StyleBackground(radar) : StyleKeyword.None;
            for (int group=0; group<2; group++)
                for (int slot=0; slot<_teamSize; slot++)
                    SetCard((Side)group, slot, null, null, online ? "等待同步" : "准备中");
            TeamTitles(!online);
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

        internal void TeamTitles(bool assigned)
        {
            _teamTitles[(int)Side.Defend].text=assigned ? "守方   DEFENDERS" : "我的小队   PARTY";
            _teamTitles[(int)Side.Attack].text=assigned ? "攻方   ATTACKERS" : "等待阵容   SYNC";
        }

        internal void SetCard(Side side, int slot, string actorName, Texture2D portrait, string label)
        {
            if ((uint)slot >= _teamSize) return;
            _portraits[(int)side,slot].style.backgroundImage = portrait ? new StyleBackground(portrait) : StyleKeyword.None;
            _names[(int)side,slot].text = actorName ?? label;
            var account=_accounts[(int)side,slot];
            account.text = actorName != null ? label : "";
            account.style.display = actorName != null && !string.IsNullOrEmpty(label) ? DisplayStyle.Flex : DisplayStyle.None;
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
            for (int i = 0; i < _teamSize; i++)
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
