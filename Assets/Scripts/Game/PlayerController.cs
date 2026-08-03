using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>
    /// 탑다운 조작: WASD 이동, 좌클릭 근접(짧은 사거리), 우클릭 원거리(쿨타임 투사체).
    /// 근접/원거리 선택이 명확히 갈리도록 두 공격의 성격을 분리(설계 문서 2장).
    /// 입력은 구형 Input API 사용 → Project Settings > Player > Active Input Handling = Both(또는 Old).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Health))]
    public class PlayerController : MonoBehaviour
    {
        [Header("이동")] public float moveSpeed = 6f;

        [Header("근접 (좌클릭)")]
        public float meleeDamage = 34f;
        public float meleeRange = 1.4f;
        public float meleeCooldown = 0.35f;

        [Header("원거리 (우클릭)")]
        public float rangedDamage = 20f;
        public float rangedCooldown = 0.5f;
        public float projectileSpeed = 13f;

        private Rigidbody2D _rb;
        private Health _health;
        private float _meleeTimer, _rangedTimer;
        private Camera _cam;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _cam = Camera.main;
        }

        private void Update()
        {
            _meleeTimer -= Time.deltaTime;
            _rangedTimer -= Time.deltaTime;
            if (_health.IsDead) return;

            if (Input.GetMouseButton(0) && _meleeTimer <= 0f) DoMelee();
            if (Input.GetMouseButton(1) && _rangedTimer <= 0f) DoRanged();
        }

        private void FixedUpdate()
        {
            if (_health.IsDead) { _rb.linearVelocity = Vector2.zero; return; }
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            _rb.linearVelocity = new Vector2(x, y).normalized * moveSpeed;
        }

        private Vector2 AimDir()
        {
            if (_cam == null) _cam = Camera.main;
            Vector3 m = _cam.ScreenToWorldPoint(Input.mousePosition);
            return ((Vector2)(m - transform.position)).normalized;
        }

        private void DoMelee()
        {
            _meleeTimer = meleeCooldown;
            // 조준 방향으로 약간 치우친 근접 판정(플레이어 중심 반경 내 적 타격)
            Vector2 center = (Vector2)transform.position + AimDir() * 0.6f;
            var hits = Physics2D.OverlapCircleAll(center, meleeRange);
            foreach (var h in hits)
            {
                var hp = h.GetComponentInParent<Health>();
                if (hp == null || hp.IsDead || hp.team != Team.Enemy) continue;
                hp.TakeDamage(meleeDamage);
                BehaviorLogger.Instance?.RecordDamage(DamageType.Melee, meleeDamage);
            }
            SlashFx(center);
        }

        private void DoRanged()
        {
            _rangedTimer = rangedCooldown;
            var go = new GameObject("PlayerProjectile");
            go.transform.position = transform.position;
            go.AddComponent<Projectile>()
              .Launch(Team.Player, AimDir(), projectileSpeed, rangedDamage,
                      DamageType.Ranged, new Color(0.4f, 0.9f, 1f));
        }

        // 근접 타격 위치에 잠깐 나타나는 표식(피드백용).
        private void SlashFx(Vector2 pos)
        {
            var go = new GameObject("SlashFx");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * meleeRange;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle();
            sr.color = new Color(1f, 1f, 1f, 0.25f);
            sr.sortingOrder = 4;
            Destroy(go, 0.08f);
        }
    }
}
