using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>직선으로 날아가며 반대 팀 첫 대상에게 데미지. 물리 레이어 설정 없이 OverlapCircle로 판정.</summary>
    public class Projectile : MonoBehaviour
    {
        private Team _owner;
        private float _damage;
        private DamageType _playerDamageType; // owner가 Player일 때만 로깅에 사용
        private Vector2 _velocity;
        private float _life;
        private const float Radius = 0.2f;

        public void Launch(Team owner, Vector2 dir, float speed, float damage,
                           DamageType playerDamageType, Color color, float life = 3f)
        {
            _owner = owner;
            _velocity = dir.normalized * speed;
            _damage = damage;
            _playerDamageType = playerDamageType;
            _life = life;

            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle();
            sr.color = color;
            sr.sortingOrder = 5;
            transform.localScale = Vector3.one * 0.35f;
        }

        private void Update()
        {
            transform.position += (Vector3)(_velocity * Time.deltaTime);

            var hits = Physics2D.OverlapCircleAll(transform.position, Radius);
            foreach (var h in hits)
            {
                var health = h.GetComponentInParent<Health>();
                if (health == null || health.IsDead || health.team == _owner || health.invulnerable) continue;

                health.TakeDamage(_damage, _velocity.normalized);
                if (_owner == Team.Player && BehaviorLogger.Instance != null)
                    BehaviorLogger.Instance.RecordDamage(_playerDamageType, _damage);

                Destroy(gameObject);
                return;
            }
            // 벽/기둥에 막힘 (엄폐물)
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<Solid>() != null) { Destroy(gameObject); return; }
            }

            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
