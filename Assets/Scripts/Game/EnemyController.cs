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
        private Transform _shield; // 탱커가 플레이어를 향해 드는 방패(부술 수 있음)
        private Transform _weapon; // 원거리몹이 드는 지팡이(시각)
        private float _diff = 1f;
        private bool _elite;
        private const float ShieldHp = 70f;

        // 근접 러시: 예비동작(멈칫) 후 빠른 돌진
        private float _lungeCd, _windupTimer, _lungeTimer;
        private Vector2 _lungeDir;
        private const float LungeCooldown = 3f, LungeRange = 5f, WindupTime = 0.3f, LungeTime = 0.22f, LungeSpeed = 12f;

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

        public void Init(EnemyType type, float hp, float dmgScale, Transform player, Health playerHealth, bool elite = false)
        {
            this.type = type;
            _player = player;
            _playerHealth = playerHealth;
            _diff = dmgScale;
            _elite = elite;

            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _sr = GetComponent<SpriteRenderer>();
            _health.Init(Team.Enemy, hp);
            _health.OnDeath += _ =>
            {
                if (_shield != null) Destroy(_shield.gameObject); // 본체 죽으면 방패도 제거
                if (_weapon != null) Destroy(_weapon.gameObject);
                Destroy(gameObject);
            };

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
            if (type == EnemyType.Ranged) CreateWeapon();
        }

        private void CreateWeapon()
        {
            var go = new GameObject("Weapon");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(130); // 지팡이
            sr.color = Color.white;
            sr.sortingOrder = 3; // 몸통 위
            float s = SpriteFactory.ScaleFor(sr.sprite, 0.6f);
            go.transform.localScale = new Vector3(s, s, 1f);
            _weapon = go.transform;
        }

        private void CreateShield()
        {
            // 독립 오브젝트(자식 X: 중첩 리지드바디 방지). LateUpdate가 플레이어 향해 배치.
            var go = new GameObject("Shield");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(102); // Kenney 방패
            sr.color = Color.white;
            sr.sortingOrder = 3; // 몸통(2) 위
            float ss = SpriteFactory.ScaleFor(sr.sprite, 0.9f);
            go.transform.localScale = new Vector3(ss, ss, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = sr.sprite.bounds.size; // 스프라이트 크기에 맞춤(로컬)
            col.isTrigger = true; // 히트박스(공격 판정용), 물리 밀침 없음
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic; // 매 프레임 이동하는 콜라이더
            rb.gravityScale = 0f;

            var sh = go.AddComponent<Health>();
            sh.Init(Team.Enemy, ShieldHp * _diff);
            go.AddComponent<HitReaction>(); // 피격 플래시/데미지 숫자 + 파괴 시 파열

            // 방패가 살아있는 동안 본체 무적 → 깨지면 노출
            _health.invulnerable = true;
            sh.OnDeath += _ =>
            {
                _health.invulnerable = false;
                _shield = null;
                Destroy(go);
            };

            _shield = go.transform;
        }

        // 방패·지팡이를 플레이어 쪽에 배치(회전 없이 항상 세워둠 → 아래는 아래).
        private void LateUpdate()
        {
            if (_player == null) return;
            Vector2 toP = (Vector2)(_player.position - transform.position);
            if (toP.sqrMagnitude < 0.0001f) return;
            Vector2 dir = toP.normalized;

            if (_shield != null)
            {
                _shield.position = transform.position + (Vector3)(dir * 0.6f);
                _shield.rotation = Quaternion.identity; // 세워둠
            }
            if (_weapon != null)
            {
                float side = toP.x >= 0f ? 1f : -1f; // 플레이어 쪽 손에
                _weapon.position = transform.position + new Vector3(side * 0.35f, -0.05f, 0f);
            }
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
            else if (type == EnemyType.Tank)
            {
                desired = dir * _moveSpeed; // 탱커: 돌진
            }
            else
            {
                desired = MeleeMove(dir); // 근접: 러시 상태 반영
            }

            _rb.linearVelocity = AvoidObstacles(desired, toPlayer);
        }

        // 근접 러시 이동: 돌진 중이면 확정 방향으로 고속, 예비동작이면 멈칫, 아니면 평상시 추격.
        private Vector2 MeleeMove(Vector2 dir)
        {
            if (_lungeTimer > 0f) return _lungeDir * LungeSpeed;
            if (_windupTimer > 0f) return Vector2.zero;
            return dir * _moveSpeed;
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

            Vector2 toP = (Vector2)(_player.position - transform.position);
            float dist = toP.magnitude;
            Vector2 dir = dist > 0.001f ? toP / dist : Vector2.right;

            if (_sr != null) _sr.flipX = toP.x < 0f; // 플레이어 향해 좌우

            if (type == EnemyType.Ranged)
            {
                if (dist <= _shootRange && _shootTimer <= 0f)
                {
                    if (_elite && Random.value < 0.35f) // 3발 산탄은 정예만
                    {
                        _shootTimer = _shootCooldown * 1.5f;
                        FireProjectile(Rotate(dir, -15f));
                        FireProjectile(dir);
                        FireProjectile(Rotate(dir, 15f));
                    }
                    else
                    {
                        _shootTimer = _shootCooldown;
                        FireProjectile(dir);
                    }
                }
            }
            else
            {
                if (type == EnemyType.Melee) UpdateLunge(dist, dir);

                if (dist <= _contactRange && _contactTimer <= 0f)
                {
                    _contactTimer = _contactCooldown;
                    _playerHealth.TakeDamage(_contactDamage, dir);
                    Vfx.Spark(transform.position + (Vector3)(dir * 0.6f), new Color(1f, 0.85f, 0.5f)); // 근접 스윙
                }
            }
        }

        private void FireProjectile(Vector2 dir)
        {
            var go = new GameObject("EnemyProjectile");
            go.transform.position = transform.position;
            go.AddComponent<Projectile>()
              .Launch(Team.Enemy, dir, _projSpeed, _projDamage, DamageType.Ranged, new Color(1f, 0.55f, 0.2f));
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // 근접 러시 상태기계: 쿨 지나면 예비동작 → 방향 확정 후 돌진.
        private void UpdateLunge(float dist, Vector2 dir)
        {
            _lungeCd -= Time.deltaTime;
            if (_lungeTimer > 0f) { _lungeTimer -= Time.deltaTime; return; } // 돌진 중
            if (_windupTimer > 0f)
            {
                _windupTimer -= Time.deltaTime;
                if (_windupTimer <= 0f)
                {
                    _lungeTimer = LungeTime;
                    _lungeDir = dir; // 예비동작 끝 시점 방향으로 확정(사이드스텝으로 회피 가능)
                    Vfx.Spark(transform.position, new Color(1f, 0.9f, 0.3f));
                }
                return;
            }
            if (_lungeCd <= 0f && dist > _contactRange && dist < LungeRange)
            {
                _windupTimer = WindupTime;
                _lungeCd = LungeCooldown;
                Vfx.Spark(transform.position, new Color(1f, 0.5f, 0.2f)); // 예비동작 신호
            }
        }
    }
}
