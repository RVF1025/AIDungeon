using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>
    /// 피격/사망 연출: 히트 플래시 · 넉백+경직(적) · 데미지 숫자 · 스파크 · 사망 파열 · 화면 흔들림(플레이어).
    /// Health.OnDamaged/OnDeath에 반응한다. SpriteRenderer 색이 정해진 뒤에 추가할 것.
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
        private Color _base;
        private float _flash;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _enemy = GetComponent<EnemyController>();
            // 적은 스폰 텔레그래프로 색이 회색이므로 EnemyController의 실제 색을 기준으로 잡는다.
            if (_sr != null) _base = _enemy != null ? _enemy.RealColor : _sr.color;
            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;
        }

        private void OnDamaged(Health h, float amount, Vector2 dir)
        {
            bool isPlayer = _health.team == Team.Player;

            // 히트 플래시
            _flash = 0.06f;
            if (_sr != null) _sr.color = Color.white;

            Vfx.DamageNumber(transform.position, amount,
                isPlayer ? new Color(1f, 0.45f, 0.45f) : Color.white);
            Vfx.Spark(transform.position,
                isPlayer ? new Color(1f, 0.5f, 0.5f) : new Color(1f, 1f, 0.75f));

            // 넉백+경직은 적에게만 (플레이어는 조작감 방해되니 제외)
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
            Vfx.Burst(transform.position, _base, isPlayer ? 14 : 8, 6f);
            CameraFollow.Shake(isPlayer ? 0.5f : 0.1f);
        }

        private void Update()
        {
            if (StunTimer > 0f) StunTimer -= Time.deltaTime;
            if (_flash > 0f)
            {
                _flash -= Time.deltaTime;
                if (_flash <= 0f && _sr != null) _sr.color = _base;
            }
        }
    }
}
