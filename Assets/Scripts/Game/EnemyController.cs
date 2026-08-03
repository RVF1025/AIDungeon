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
        private float _moveSpeed, _contactDamage, _contactCooldown;
        private float _preferredRange, _shootRange, _projDamage, _shootCooldown, _projSpeed;
        private float _contactTimer, _shootTimer;

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
                    break;
                case EnemyType.Ranged:
                    _moveSpeed = 3.2f; _preferredRange = 5f; _shootRange = 7.5f;
                    _projDamage = 7f * dmgScale; _shootCooldown = 1.3f; _projSpeed = 7f;
                    break;
                case EnemyType.Tank:
                    _moveSpeed = 1.8f; _contactDamage = 14f * dmgScale; _contactCooldown = 0.9f;
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
        }

        private void FixedUpdate()
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            if (!_active) { _rb.linearVelocity = Vector2.zero; return; } // 스폰 중엔 정지

            if (_hit == null) _hit = GetComponent<HitReaction>();
            if (_hit != null && _hit.IsStunned) return; // 넉백 중엔 물리에 맡김

            Vector2 toPlayer = (Vector2)(_player.position - transform.position);
            float dist = toPlayer.magnitude;
            Vector2 dir = toPlayer.normalized;

            if (type == EnemyType.Ranged)
            {
                // 거리 유지: 너무 가까우면 물러나고, 멀면 다가감 (카이팅)
                if (dist < _preferredRange - 0.6f) _rb.linearVelocity = -dir * _moveSpeed;
                else if (dist > _preferredRange + 0.6f) _rb.linearVelocity = dir * _moveSpeed;
                else _rb.linearVelocity = Vector2.Perpendicular(dir) * (_moveSpeed * 0.6f); // 스트레이핑
            }
            else
            {
                // Melee/Tank: 돌진
                _rb.linearVelocity = dir * _moveSpeed;
            }
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
                if (dist <= 1.0f && _contactTimer <= 0f)
                {
                    _contactTimer = _contactCooldown;
                    _playerHealth.TakeDamage(_contactDamage,
                        ((Vector2)(_player.position - transform.position)).normalized);
                }
            }
        }
    }
}
