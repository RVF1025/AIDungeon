using System.Collections.Generic;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>
    /// 몬스터 3종 행동 (설계 문서 2장):
    ///   Melee : 플레이어에게 돌진, 접촉 데미지
    ///   Ranged: preferredRange 유지하며 투사체 발사(kiter)
    ///   Tank  : 느리고 체력 높음, 접촉 데미지 큼
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Health))]
    public class EnemyController : MonoBehaviour
    {
        /// <summary>살아있는 적 전역 목록(웨이브 클리어 판정 + 로거 거리계산).</summary>
        public static readonly HashSet<EnemyController> Active = new();

        public EnemyType type;
        private float _moveSpeed, _contactDamage, _contactCooldown, _contactRange = 1f;
        private float _preferredRange, _shootRange, _projDamage, _shootCooldown, _projSpeed;
        private float _contactTimer, _shootTimer;
        private Transform _shield; // 탱커가 플레이어를 향해 드는 방패(시각)

        private Rigidbody2D _rb;
        private Health _health;
        private HitReaction _hit;
        private SpriteRenderer _sr;
        private Transform _player;
        private Health _playerHealth;

        // 스폰 텔레그래프: 회색 반투명으로 나타났다가 진해진 뒤 활성화(플레이어 대응 시간).
        private const float SpawnDelay = 1.1f;
        private float _spawnTimer;
        private bool _active;
        public Color RealColor { get; private set; }

        public void Init(EnemyType type, float hp, float dmgScale, Transform player, Health playerHealth)
        {
            this.type = type;
            _player = player;
            _playerHealth = playerHealth;

            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _sr = GetComponent<SpriteRenderer>();
            _health.Init(Team.Enemy, hp);
            _health.OnDeath += _ => Destroy(gameObject);

            // 텔레그래프 시작: 실제 색 보관, 회색 반투명으로 표시
            RealColor = _sr != null ? _sr.color : Color.white;
            _spawnTimer = SpawnDelay;
            _active = false;
            if (_sr != null) _sr.color = new Color(0.75f, 0.75f, 0.8f, 0.12f);

            switch (type)
            {
                case EnemyType.Melee:
                    _moveSpeed = 4.2f; _contactDamage = 8f * dmgScale; _contactCooldown = 0.6f;
                    _contactRange = 1.0f;
                    break;
                case EnemyType.Ranged:
                    _moveSpeed = 3.2f; _preferredRange = 6f; _shootRange = 11f;
                    _projDamage = 7f * dmgScale; _shootCooldown = 1.3f; _projSpeed = 8f;
                    break;
                case EnemyType.Tank:
                    _moveSpeed = 2.7f; _contactDamage = 8f * dmgScale; _contactCooldown = 0.8f;
                    _contactRange = 1.0f; // 근접몹과 동일 크기, 약한 데미지(방패로 식별)
                    break;
            }
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void Activate()
        {
            _active = true;
            if (_sr != null) _sr.color = RealColor;
            Vfx.Spark(transform.position, RealColor); // 등장 순간 팟
            if (type == EnemyType.Tank) CreateShield();
        }

        private void CreateShield()
        {
            var go = new GameObject("Shield");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(1.6f, 0.35f, 1f); // 넓고 얇은 직사각형
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = new Color(0.78f, 0.8f, 0.88f); // 강철색
            sr.sortingOrder = 3; // 몸통(2) 위
            _shield = go.transform;
        }

        // 방패가 항상 플레이어를 향하도록(넓은 면이 플레이어 쪽) 정렬.
        private void LateUpdate()
        {
            if (_shield == null || _player == null) return;
            Vector2 toP = (Vector2)(_player.position - transform.position);
            if (toP.sqrMagnitude < 0.0001f) return;
            toP.Normalize();
            _shield.position = transform.position + (Vector3)(toP * 0.6f);
            float ang = Mathf.Atan2(toP.y, toP.x) * Mathf.Rad2Deg;
            _shield.rotation = Quaternion.Euler(0, 0, ang - 90f); // local up(+y, 넓은 면)이 플레이어를 향함
        }

        private void FixedUpdate()
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            if (!_active) return; // 스폰 중엔 AI 정지(속도 강제 X → 겹친 스폰이 물리로 자연 분리)

            if (_hit == null) _hit = GetComponent<HitReaction>();
            if (_hit != null && _hit.IsStunned) return; // 넉백 중엔 물리에 맡김

            Vector2 toPlayer = (Vector2)(_player.position - transform.position);
            float dist = toPlayer.magnitude;
            Vector2 dir = dist > 0.001f ? toPlayer / dist : Vector2.zero;

            Vector2 desired;
            if (type == EnemyType.Ranged)
            {
                // 거리 유지: 너무 가까우면 물러나고, 멀면 다가감 (카이팅)
                if (dist < _preferredRange - 0.6f) desired = -dir * _moveSpeed;
                else if (dist > _preferredRange + 0.6f) desired = dir * _moveSpeed;
                else desired = Vector2.Perpendicular(dir) * (_moveSpeed * 0.6f); // 스트레이핑
            }
            else
            {
                desired = dir * _moveSpeed; // Melee/Tank: 돌진
            }

            _rb.linearVelocity = AvoidObstacles(desired, toPlayer);
        }

        /// <summary>진행 방향에 벽/기둥(Solid)이 있으면 플레이어 쪽으로 비껴가게 조향(단순 회피).</summary>
        private Vector2 AvoidObstacles(Vector2 vel, Vector2 toPlayer)
        {
            if (vel.sqrMagnitude < 0.0001f) return vel;
            float speed = vel.magnitude;
            Vector2 dir = vel / speed;

            var hits = Physics2D.CircleCastAll(transform.position, 0.45f, dir, 1.3f);
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.gameObject == gameObject) continue;
                if (h.collider.GetComponent<Solid>() == null) continue;

                Vector2 perp = Vector2.Perpendicular(dir);
                if (Vector2.Dot(perp, toPlayer) < 0f) perp = -perp; // 플레이어 방향으로 우회
                dir = (dir * 0.4f + perp).normalized;
                break;
            }
            return dir * speed;
        }

        private void Update()
        {
            if (!_active)
            {
                _spawnTimer -= Time.deltaTime;
                if (_sr != null)
                {
                    float p = Mathf.Clamp01(1f - _spawnTimer / SpawnDelay);
                    _sr.color = new Color(0.75f, 0.75f, 0.8f, Mathf.Lerp(0.12f, 0.7f, p));
                }
                if (_spawnTimer <= 0f) Activate();
                return;
            }

            if (_player == null || _playerHealth == null || _playerHealth.IsDead) return;
            _contactTimer -= Time.deltaTime;
            _shootTimer -= Time.deltaTime;

            float dist = Vector2.Distance(_player.position, transform.position);

            if (type == EnemyType.Ranged)
            {
                if (dist <= _shootRange && _shootTimer <= 0f)
                {
                    _shootTimer = _shootCooldown;
                    Vector2 dir = ((Vector2)(_player.position - transform.position)).normalized;
                    var go = new GameObject("EnemyProjectile");
                    go.transform.position = transform.position;
                    go.AddComponent<Projectile>()
                      .Launch(Team.Enemy, dir, _projSpeed, _projDamage, DamageType.Ranged,
                              new Color(1f, 0.55f, 0.2f));
                }
            }
            else
            {
                if (dist <= _contactRange && _contactTimer <= 0f)
                {
                    _contactTimer = _contactCooldown;
                    Vector2 d = ((Vector2)(_player.position - transform.position)).normalized;
                    _playerHealth.TakeDamage(_contactDamage, d);
                    Vfx.Spark(transform.position + (Vector3)(d * 0.6f), new Color(1f, 0.85f, 0.5f)); // 근접 스윙
                }
            }
        }
    }
}
