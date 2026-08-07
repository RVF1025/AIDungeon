using UnityEngine;
using UnityEngine.InputSystem;

namespace AIDungeon.Game
{
    /// <summary>
    /// 탑다운 조작: WASD 이동, 좌클릭 근접(짧은 사거리), 우클릭 원거리(쿨타임 투사체).
    /// 근접/원거리 선택이 명확히 갈리도록 두 공격의 성격을 분리(설계 문서 2장).
    /// 입력은 Input System 저수준 API(Keyboard/Mouse.current) 사용 — .inputactions 에셋/씬 배선
    /// 불필요, Active Input Handling 설정도 필요 없음.
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
        private SpriteRenderer _sr;
        private Transform _crosshair;
        private float _meleeTimer, _rangedTimer;
        private Camera _cam;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _sr = GetComponent<SpriteRenderer>();
            _cam = Camera.main;
            Cursor.visible = false; // 크로스헤어로 대체
            CreateCrosshair();
        }

        private void CreateCrosshair()
        {
            var go = new GameObject("Crosshair");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(60); // 조준점
            sr.color = new Color(1f, 1f, 1f, 0.9f);
            sr.sortingOrder = 20;
            float s = SpriteFactory.ScaleFor(sr.sprite, 0.7f);
            go.transform.localScale = new Vector3(s, s, 1f);
            _crosshair = go.transform;
        }

        private void Update()
        {
            _meleeTimer -= Time.deltaTime;
            _rangedTimer -= Time.deltaTime;

            var mouse = Mouse.current;
            if (mouse != null && _crosshair != null)
            {
                if (_cam == null) _cam = Camera.main;
                Vector2 sp = mouse.position.ReadValue();
                Vector3 m = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));
                m.z = 0f;
                _crosshair.position = m;
            }

            if (_health.IsDead) return;

            Vector2 aim = AimDir();
            if (_sr != null && Mathf.Abs(aim.x) > 0.05f) _sr.flipX = aim.x < 0f; // 조준 방향으로 좌우

            if (mouse != null)
            {
                if (mouse.leftButton.isPressed && _meleeTimer <= 0f) DoMelee();
                if (mouse.rightButton.isPressed && _rangedTimer <= 0f) DoRanged();
            }
        }

        private void FixedUpdate()
        {
            if (_health.IsDead) { _rb.linearVelocity = Vector2.zero; return; }
            var kb = Keyboard.current;
            if (kb == null) { _rb.linearVelocity = Vector2.zero; return; }

            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            _rb.linearVelocity = new Vector2(x, y).normalized * moveSpeed;
        }

        private Vector2 AimDir()
        {
            if (_cam == null) _cam = Camera.main;
            Vector2 screen = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Vector3 m = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
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
                if (hp == null || hp.IsDead || hp.team != Team.Enemy || hp.invulnerable) continue;
                Vector2 dir = ((Vector2)(hp.transform.position - transform.position)).normalized;
                hp.TakeDamage(meleeDamage, dir);
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
