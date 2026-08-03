namespace AIDungeon.Game
{
    /// <summary>피아 구분. 투사체/근접이 반대 팀만 때린다.</summary>
    public enum Team { Player, Enemy }

    /// <summary>플레이어가 준 데미지의 종류(행동 로거가 meleeRatio 계산에 사용).</summary>
    public enum DamageType { Melee, Ranged }

    /// <summary>몬스터 3종 (설계 문서 2장).</summary>
    public enum EnemyType
    {
        Melee,  // 근접형: 플레이어에게 돌진, 접촉 데미지
        Ranged, // 원거리형: 거리 유지하며 투사체 발사 (kiter)
        Tank    // 탱커형: 느리지만 체력 높음
    }
}
