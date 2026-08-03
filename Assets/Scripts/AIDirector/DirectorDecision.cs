using System;

namespace AIDungeon.Director
{
    // 문자열 enum과 1:1 매칭되는 상수 모음. (JsonUtility는 문자열로 주고받고,
    // 게임 로직은 아래 상수로 분기한다.)
    public static class Composition
    {
        public const string KiterPack = "kiter_pack";   // 원거리로 근접 플레이어 농락
        public const string RusherPack = "rusher_pack"; // 빠른 근접으로 원거리 플레이어 압박
        public const string TankBait = "tank_bait";     // 탱커 앞세우고 후방 딜 (저돌형 카운터)
        public const string Balanced = "balanced";
    }

    public static class Topology
    {
        public const string Encircle = "encircle"; // 사방·배후 스폰, 안전 코너 제거 (회피 봉쇄)
        public const string Cover = "cover";       // 엄폐물·시야 차단 (원거리 라인 차단)
        public const string Open = "open";         // 개활지 (저돌형 유인 후 노출 처벌)
        public const string Corridor = "corridor"; // 좁은 통로 (1:1 강제)
    }

    public static class Tone
    {
        public const string Taunt = "taunt";         // 도발 (약점 확실)
        public const string Impressed = "impressed"; // 감탄 (카운터를 뚫음)
        public const string Concern = "concern";     // 자비 (고전 중)
        public const string Neutral = "neutral";     // 관찰
    }

    /// <summary>
    /// AI Director 출력. AI는 analysis(대사)와 tone(태도)을 담당하고,
    /// composition/topology/difficultyModifier는 <see cref="DirectorPolicy"/>가
    /// 결정론적으로 계산·검증한다(신뢰성 100%). fromFallback은 클라이언트가 세팅.
    /// </summary>
    [Serializable]
    public class DirectorDecision
    {
        public string analysis;              // 캐릭터 대사 한 문장 (composition/topology 암시)
        public string composition;           // Composition.*
        public string topology;              // Topology.*
        public float difficultyModifier;     // 0.8 ~ 1.3
        public string tone;                  // Tone.*

        [NonSerialized] public bool fromFallback; // API 실패/타임아웃으로 프리셋 대체됐는지

        public override string ToString()
        {
            return $"[{(fromFallback ? "FALLBACK" : "AI")}] comp={composition} topo={topology} " +
                   $"diff={difficultyModifier:0.00} tone={tone}\n  \"{analysis}\"";
        }
    }
}
