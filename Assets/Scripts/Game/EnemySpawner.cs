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

        public void SpawnWave(DirectorDecision d, int count)
        {
            var types = RollTypes(d.composition, d.topology, count);

            if (d.topology == Topology.Corridor)
            {
                // 통로: 근접 없음. 좌측(플레이어 쪽)부터 탱커 → 원거리 순 진형
                types.Sort((a, b) => RoleOrder(a).CompareTo(RoleOrder(b)));
                float x0 = roomCenter.x + roomHalf.x * 0.2f;
                float x1 = roomCenter.x + roomHalf.x - 2f;
                for (int i = 0; i < types.Count; i++)
                {
                    float t = types.Count <= 1 ? 0.5f : (float)i / (types.Count - 1);
                    float x = Mathf.Lerp(x0, x1, t);
                    float y = roomCenter.y + ((i % 2 == 0) ? 1f : -1f) * Random.Range(0f, roomHalf.y - 0.8f);
                    Spawn(types[i], Clamp(new Vector2(x, y)), d.difficultyModifier);
                }
                return;
            }

            for (int i = 0; i < types.Count; i++)
                Spawn(types[i], SpawnPos(d.topology, i, count), d.difficultyModifier);
        }

        // topology별 배치 규칙: corridor=근접 제외, encircle=탱커 제외
        private List<EnemyType> RollTypes(string composition, string topology, int count)
        {
            var (m, r, t) = Weights(composition);
            if (topology == Topology.Corridor) m = 0f; // 근접 없음
            if (topology == Topology.Encircle) t = 0f; // 탱커 없음
            if (m + r + t <= 0f) { m = r = t = 1f; }   // 안전장치

            var list = new List<EnemyType>(count);
            for (int i = 0; i < count; i++) list.Add(PickType(m, r, t));
            return list;
        }

        // 통로 정렬용: 탱커가 앞(왼쪽), 원거리가 뒤(오른쪽)
        private static int RoleOrder(EnemyType t) => t switch
        {
            EnemyType.Tank => 0,
            EnemyType.Ranged => 1,
            _ => 2,
        };

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

        private void Spawn(EnemyType type, Vector2 pos, float diff)
        {
            var go = new GameObject($"Enemy_{type}");
            go.transform.position = pos;

            int tile; float baseHp;
            switch (type)
            {
                case EnemyType.Melee: tile = 122; baseHp = 40f; break; // 거미
                case EnemyType.Ranged: tile = 84; baseHp = 30f; break; // 마법사
                default: tile = 96; baseHp = 60f; break;               // 기사(탱커, 방패로 버팀 → 본체 HP 낮음)
            }

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(tile);
            sr.color = Color.white;
            sr.sortingOrder = 2;
            float scale = SpriteFactory.ScaleFor(sr.sprite, 1.0f);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var col2d = go.AddComponent<CircleCollider2D>();
            col2d.radius = 0.35f / scale;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 지터 방지

            go.AddComponent<Health>();
            go.AddComponent<EnemyController>().Init(type, baseHp * diff, diff, _player, _playerHealth);
            go.AddComponent<HitReaction>(); // 색/컴포넌트 세팅 후 부착
        }
    }
}
