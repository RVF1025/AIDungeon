using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>
    /// 피격/사망 연출: 스케일 펀치(순간 커짐) · 넉백+경직(적) · 데미지 숫자 · 스파크 · 사망 파열 ·
    /// 화면 흔들림(플레이어). 스프라이트는 흰색 틴트로 플래시가 안 되므로 스케일 펀치로 피격감을 준다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HitReaction : MonoBehaviour
    {
        public float StunTimer { get; private set; }
        public bool IsStunned => StunTimer > 0f;

        private SpriteRenderer _sr;
        private Rigidbody2D _rb;
        private Health _health;
        private EnemyController _enemy;
        private Vector3 _baseScale;
        private float _punch;
        private const float PunchTime = 0.1f;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _enemy = GetComponent<EnemyController>();
            _baseScale = transform.localScale;
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        private void OnDamaged(Health h, float amount, Vector2 dir)
        {
            bool isPlayer = _health.team == Team.Player;

            _punch = PunchTime; // 스케일 펀치

            Vfx.DamageNumber(transform.position, amount,
                isPlayer ? new Color(1f, 0.45f, 0.45f) : Color.white);
            Vfx.Spark(transform.position,
                isPlayer ? new Color(1f, 0.5f, 0.5f) : new Color(1f, 1f, 0.75f));

            // 넉백+경직은 적에게만
            if (!isPlayer && _rb != null && dir != Vector2.zero)
            {
                float force = (_enemy != null && _enemy.type == EnemyType.Tank) ? 2.5f : 6f;
                _rb.linearVelocity = dir.normalized * force;
                StunTimer = 0.12f;
            }

            if (isPlayer) CameraFollow.Shake(0.25f);
        }

        private void OnDeath(Health h)
        {
            bool isPlayer = _health.team == Team.Player;
            Color debris = isPlayer ? new Color(1f, 0.5f, 0.5f) : new Color(0.9f, 0.9f, 0.95f);
            Vfx.Burst(transform.position, debris, isPlayer ? 14 : 8, 6f);
            CameraFollow.Shake(isPlayer ? 0.5f : 0.1f);
        }

        private void Update()
        {
            if (StunTimer > 0f) StunTimer -= Time.deltaTime;

            if (_punch > 0f)
            {
                _punch -= Time.deltaTime;
                float k = Mathf.Clamp01(_punch / PunchTime);
                transform.localScale = _baseScale * (1f + 0.18f * k);
                if (_punch <= 0f) transform.localScale = _baseScale;
            }
        }
    }
}
