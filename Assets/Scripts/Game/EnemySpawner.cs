using System.Collections.Generic;
using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>
    /// AI Director 결정을 실제 스폰으로 변환하는 결정론 로직(설계 문서 3.3).
    ///   composition → 몬스터 3종 스폰 가중치
    ///   topology    → 스폰 위치 전략
    ///   difficultyModifier → HP/데미지 승수
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        public Vector2 roomCenter = Vector2.zero;
        public Vector2 roomHalf = new Vector2(11f, 7f); // 벽 안쪽 반경

        private Transform _player;
        private Health _playerHealth;

        public void Setup(Transform player, Health playerHealth)
        {
            _player = player;
            _playerHealth = playerHealth;
        }

        /// <summary>현재 층 방의 범위로 스폰 영역 갱신.</summary>
        public void Configure(Vector2 center, Vector2 half)
        {
            roomCenter = center;
            roomHalf = half;
        }

        // eliteCount: 이 방에서 정예로 만들 마릿수(정예 전투 방에서만 > 0). 호출자가 결정.
        public void SpawnWave(DirectorDecision d, int count, int eliteCount)
        {
            var types = RollTypes(d.composition, d.topology, count);

            if (d.topology == Topology.Corridor)
            {
                // 가로 통로: 역할별로 '세로 열'을 이뤄 근접(앞)→탱커(중)→원거리(뒤) 순으로 배치.
                // 같은 역할을 세로로 묶어 각개격파를 막고 진형처럼 몰려오게 한다.
                var order = new[] { EnemyType.Melee, EnemyType.Tank, EnemyType.Ranged };
                var xFrac = new[] { -0.2f, 0.15f, 0.5f }; // 플레이어(좌측)에서 앞→뒤
                var placed = new List<Vector2>(types.Count);
                var placedTypes = new List<EnemyType>(types.Count);
                for (int r = 0; r < order.Length; r++)
                {
                    int c = types.FindAll(t => t == order[r]).Count;
                    if (c == 0) continue;
                    float x = roomCenter.x + roomHalf.x * xFrac[r];
                    for (int k = 0; k < c; k++)
                    {
                        float t = c <= 1 ? 0.5f : (float)k / (c - 1);
                        float y = Mathf.Lerp(roomCenter.y - (roomHalf.y - 0.8f), roomCenter.y + (roomHalf.y - 0.8f), t);
                        placedTypes.Add(order[r]);
                        placed.Add(Clamp(new Vector2(x, y)));
                    }
                }
                var eliteSetC = PickEliteIndices(placed.Count, eliteCount);
                for (int i = 0; i < placed.Count; i++)
                    Spawn(placedTypes[i], placed[i], d.difficultyModifier, eliteSetC.Contains(i));
                return;
            }

            var eliteSet = PickEliteIndices(types.Count, eliteCount);
            for (int i = 0; i < types.Count; i++)
                Spawn(types[i], SpawnPos(d.topology, i, count), d.difficultyModifier, eliteSet.Contains(i));
        }

        private static HashSet<int> PickEliteIndices(int total, int eliteCount)
        {
            var set = new HashSet<int>();
            if (eliteCount <= 0 || total <= 0) return set;
            var pool = new List<int>(total);
            for (int i = 0; i < total; i++) pool.Add(i);
            for (int k = 0; k < eliteCount && pool.Count > 0; k++)
            {
                int j = Random.Range(0, pool.Count);
                set.Add(pool[j]);
                pool.RemoveAt(j);
            }
            return set;
        }

        // topology별 배치 규칙: encircle=탱커 제외(포위). corridor는 근접~원거리 모두 등장(진형 형성).
        private List<EnemyType> RollTypes(string composition, string topology, int count)
        {
            var (m, r, t) = Weights(composition);
            if (topology == Topology.Encircle) t = 0f; // 탱커 없음
            if (m + r + t <= 0f) { m = r = t = 1f; }   // 안전장치

            var list = new List<EnemyType>(count);
            for (int i = 0; i < count; i++) list.Add(PickType(m, r, t));
            return list;
        }

        private static (float, float, float) Weights(string composition) => composition switch
        {
            Composition.KiterPack => (0.10f, 0.60f, 0.30f),
            Composition.RusherPack => (0.70f, 0.10f, 0.20f),
            Composition.TankBait => (0.20f, 0.50f, 0.30f),
            _ => (0.34f, 0.33f, 0.33f), // balanced
        };

        private static EnemyType PickType(float m, float r, float t)
        {
            float roll = Random.value * (m + r + t);
            if (roll < m) return EnemyType.Melee;
            if (roll < m + r) return EnemyType.Ranged;
            return EnemyType.Tank;
        }

        private Vector2 SpawnPos(string topology, int i, int count)
        {
            Vector2 p = _player != null ? (Vector2)_player.position : roomCenter;
            switch (topology)
            {
                case Topology.Encircle: // 플레이어 주위 링 (도망갈 곳 없음)
                    float ang = (360f / Mathf.Max(1, count)) * i * Mathf.Deg2Rad;
                    return Clamp(p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 5.5f);
                case Topology.Open: // 멀리 넓게 분산
                    return Clamp(roomCenter + new Vector2(
                        Random.Range(-(roomHalf.x - 1f), roomHalf.x - 1f),
                        Random.Range(-(roomHalf.y - 1f), roomHalf.y - 1f)));
                case Topology.Corridor: // 통로 오른쪽 구간에 길이 방향으로 분산(겹침 방지)
                {
                    float t = count <= 1 ? 0.5f : (float)i / (count - 1);
                    float x = roomCenter.x + Mathf.Lerp(roomHalf.x * 0.35f, roomHalf.x - 2f, t);
                    float y = roomCenter.y + Random.Range(-(roomHalf.y - 0.8f), roomHalf.y - 0.8f);
                    return Clamp(new Vector2(x, y));
                }
                default: // cover 등: 플레이어에서 적당히 떨어진 랜덤
                    return FarFromPlayer(p);
            }
        }

        private Vector2 FarFromPlayer(Vector2 p)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                Vector2 c = Clamp(roomCenter + new Vector2(
                    Random.Range(-(roomHalf.x - 1f), roomHalf.x - 1f),
                    Random.Range(-(roomHalf.y - 1f), roomHalf.y - 1f)));
                if (Vector2.Distance(c, p) >= 4f) return c;
            }
            return Clamp(p + Vector2.right * 4f);
        }

        private Vector2 Clamp(Vector2 v) => new(
            Mathf.Clamp(v.x, roomCenter.x - roomHalf.x, roomCenter.x + roomHalf.x),
            Mathf.Clamp(v.y, roomCenter.y - roomHalf.y, roomCenter.y + roomHalf.y));

        // 정예(챔피언) 강화 배수/외형.
        private const float EliteHpMul = 2.6f, EliteDmgMul = 1.6f, EliteWorldSize = 1.5f;
        private static readonly Color EliteTint = new(1f, 0.78f, 0.32f); // 금빛

        private void Spawn(EnemyType type, Vector2 pos, float diff, bool elite)
        {
            var go = new GameObject(elite ? $"Elite_{type}" : $"Enemy_{type}");
            go.transform.position = pos;

            int tile; float baseHp;
            switch (type)
            {
                case EnemyType.Melee: tile = 122; baseHp = 40f; break; // 거미
                case EnemyType.Ranged: tile = 84; baseHp = 30f; break; // 마법사
                default: tile = 96; baseHp = 60f; break;               // 기사(탱커, 방패로 버팀 → 본체 HP 낮음)
            }

            // 정예: HP·데미지 상승, 금빛 틴트로 식별. 크기 확대는 근접 정예만.
            float hp = baseHp * diff * (elite ? EliteHpMul : 1f);
            float dmgScale = diff * (elite ? EliteDmgMul : 1f);
            bool bigger = elite && type == EnemyType.Melee;
            float worldSize = bigger ? EliteWorldSize : 1.0f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(tile);
            sr.color = elite ? EliteTint : Color.white; // Init이 이 색을 RealColor로 저장
            sr.sortingOrder = 2;
            float scale = SpriteFactory.ScaleFor(sr.sprite, worldSize);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var col2d = go.AddComponent<CircleCollider2D>();
            col2d.radius = (bigger ? 0.5f : 0.35f) / scale;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 지터 방지

            go.AddComponent<Health>();
            go.AddComponent<EnemyController>().Init(type, hp, dmgScale, _player, _playerHealth, elite);
            go.AddComponent<HitReaction>(); // 색/컴포넌트 세팅 후 부착
            go.AddComponent<HealthBar>(); // 적: 피해 입었을 때만 표시
        }
    }
}
