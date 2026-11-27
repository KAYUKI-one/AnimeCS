using UnityEngine;

namespace AnimeCS.Match
{
    [System.Serializable]
    public sealed class BotTuning
    {
        [Min(.02f), InspectorName("发现敌人后最短反应秒数")] public float minReactionSeconds = .24f;
        [Min(.02f), InspectorName("发现敌人后最长反应秒数")] public float maxReactionSeconds = .48f;
        [Min(0), InspectorName("每秒最大转向角度")] public float turnDegreesPerSecond = 240f;
        [Min(0), InspectorName("每秒视角回平角度")] public float levelPitchDegreesPerSecond = 150f;
        [Min(.02f), InspectorName("听声转向持续秒数")] public float listenSeconds = .30f;
        [Min(.02f), InspectorName("基础寻路刷新秒数")] public float repathSeconds = .70f;
    }

    [CreateAssetMenu(menuName = "AnimeCS/比赛规则")]
    public sealed class MatchRules : ScriptableObject
    {
        // A tick is simulation time; pausing never advances it.
        public const int TickRate = 50;
        [InspectorName("移动与跳跃手感")] public MovementTuning movement = new MovementTuning();
        [InspectorName("机器人反应与导航")] public BotTuning bot = new BotTuning();
        [Min(1), InspectorName("获胜局数")] public int winsRequired = 13;
        [Min(0.02f), InspectorName("冻结秒数")] public float buySeconds = 15;
        [Min(0.02f), InspectorName("争夺秒数")] public float contestSeconds = 115;
        [Min(0.02f), InspectorName("装置倒计时")] public float plantedSeconds = 40;
        [Min(0.02f), InspectorName("结算秒数")] public float settlementSeconds = 7;
        [Min(0.02f), InspectorName("安装秒数")] public float plantSeconds = 3.2f;
        [Min(0.02f), InspectorName("拆除秒数")] public float defuseSeconds = 10;
        [Min(.02f), InspectorName("带拆除套件秒数")] public float kitDefuseSeconds = 5;
        [Min(0), InspectorName("解冻后可购买秒数")] public float liveBuySeconds = 20;
        [Min(0), InspectorName("拆除套件价格")] public int kitPrice = 400;
        [Min(0), InspectorName("装置结算影响半径（游戏米）")] public float deviceRadius = 24;
        [Min(0), InspectorName("半场预算")] public int openingCredits = 900;
        [Min(0), InspectorName("决胜预算")] public int deciderCredits = 4000;
        [Min(0), InspectorName("钱包上限")] public int walletCap = 10000;
        [Min(0), InspectorName("胜局收入")] public int winIncome = 2700;
        [Min(0), InspectorName("首败收入")] public int firstLossIncome = 1900;
        [Min(0), InspectorName("连败收入")] public int lossIncome = 2400;
        [Min(0), InspectorName("装成后败局补贴")] public int plantIncome = 300;
        [Min(0), InspectorName("胸甲价格")] public int armorPrice = 400;
        [Min(0), InspectorName("头盔价格")] public int helmetPrice = 200;
        [Range(.05f,1f), InspectorName("命中瞬间保留速度比例")] public float hitSpeedFactor = .48f;
        [Min(0), InspectorName("受击减速持续秒数")] public float hitHoldSeconds = .10f;
        [Min(0), InspectorName("受击后速度恢复秒数")] public float hitRecoverySeconds = .27f;
        public static int Ticks(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * TickRate));
    }
}
