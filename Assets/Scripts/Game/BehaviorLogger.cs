using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>
    /// 플레이어 전투 행동을 관측해 <see cref="PlayerProfile"/>를 만든다. 각 층 시작 시 Reset,
    /// 클리어 시 BuildProfile로 AI Director 입력을 뽑는다.
    ///   meleeRatio  = 근접 데미지 / 총 데미지
    ///   aggression  = 적 근처에 머문 시간 / 전투 시간
    ///   avgHpPct    = 층 동안 평균 HP 비율
    /// </summary>
    public class BehaviorLogger : MonoBehaviour
    {
        public static BehaviorLogger Instance { get; private set; }

        [Tooltip("이 반경 안에 적이 있으면 '교전 중'으로 간주")]
        public float engageRadius = 3.5f;

        public Health player;

        private float _meleeDmg, _rangedDmg;
        private float _combatTime, _nearTime;
        private float _hpSum; private int _hpSamples;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void ResetFloor()
        {
            _meleeDmg = _rangedDmg = 0f;
            _combatTime = _nearTime = 0f;
            _hpSum = 0f; _hpSamples = 0;
        }

        /// <summary>플레이어가 적에게 데미지를 줄 때 호출.</summary>
        public void RecordDamage(DamageType type, float amount)
        {
            if (type == DamageType.Melee) _meleeDmg += amount;
            else _rangedDmg += amount;
        }

        private void Update()
        {
            if (player == null || player.IsDead) return;

            // HP 샘플링
            _hpSum += player.Fraction;
            _hpSamples++;

            // 교전/근접 시간 샘플링 (적이 있을 때만 전투로 카운트)
            float nearest = NearestEnemyDistance(player.transform.position);
            if (nearest < float.MaxValue)
            {
                _combatTime += Time.deltaTime;
                if (nearest <= engageRadius) _nearTime += Time.deltaTime;
            }
        }

        private static float NearestEnemyDistance(Vector3 from)
        {
            float best = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (e == null) continue;
                float d = Vector2.Distance(from, e.transform.position);
                if (d < best) best = d;
            }
            return best;
        }

        public PlayerProfile BuildProfile()
        {
            float totalDmg = _meleeDmg + _rangedDmg;
            float meleeRatio = totalDmg > 0f ? _meleeDmg / totalDmg : 0.5f;
            float aggression = _combatTime > 0f ? _nearTime / _combatTime : 0.5f;
            float avgHp = _hpSamples > 0 ? _hpSum / _hpSamples : 1f;
            return new PlayerProfile(meleeRatio, aggression, avgHp);
        }
    }
}
