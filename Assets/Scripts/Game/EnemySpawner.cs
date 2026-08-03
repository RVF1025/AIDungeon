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
            var (wMelee, wRanged, wTank) = Weights(d.composition);
            for (int i = 0; i < count; i++)
            {
                EnemyType type = PickType(wMelee, wRanged, wTank);
                Vector2 pos = SpawnPos(d.topology, i, count);
                Spawn(type, pos, d.difficultyModifier);
            }
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

        private void Spawn(EnemyType type, Vector2 pos, float diff)
        {
            var go = new GameObject($"Enemy_{type}");
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle();
            sr.sortingOrder = 2;
            float scale; float baseHp; Color col;
            switch (type)
            {
                case EnemyType.Melee: col = new Color(0.9f, 0.3f, 0.3f); scale = 0.8f; baseHp = 40f; break;
                case EnemyType.Ranged: col = new Color(1f, 0.75f, 0.2f); scale = 0.7f; baseHp = 30f; break;
                default: col = new Color(0.6f, 0.2f, 0.5f); scale = 1.3f; baseHp = 140f; break; // Tank
            }
            sr.color = col;
            go.transform.localScale = Vector3.one * scale;

            var col2d = go.AddComponent<CircleCollider2D>();
            col2d.radius = 0.5f;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            go.AddComponent<Health>();
            go.AddComponent<EnemyController>().Init(type, baseHp * diff, diff, _player, _playerHealth);
            go.AddComponent<HitReaction>(); // 색/컴포넌트 세팅 후 부착
        }
    }
}
