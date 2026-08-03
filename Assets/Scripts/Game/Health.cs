using System;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>체력 + 피아 팀. 피격/사망 이벤트로 연출(HitReaction)을 구동한다.</summary>
    public class Health : MonoBehaviour
    {
        public Team team;
        public float maxHp = 100f;
        public bool invulnerable; // 방패병 본체: 방패 살아있는 동안 true
        public float CurrentHp { get; private set; }
        public float Fraction => maxHp <= 0 ? 0 : Mathf.Clamp01(CurrentHp / maxHp);
        public bool IsDead { get; private set; }

        /// <summary>(피격 대상, 데미지량, 피격 방향)</summary>
        public event Action<Health, float, Vector2> OnDamaged;
        public event Action<Health> OnDeath;

        public void Init(Team team, float maxHp)
        {
            this.team = team;
            this.maxHp = maxHp;
            CurrentHp = maxHp;
            IsDead = false;
        }

        private void Awake()
        {
            if (CurrentHp <= 0 && !IsDead) CurrentHp = maxHp;
        }

        /// <summary>데미지 적용. 죽으면 true. hitDir은 넉백/이펙트 방향(공격자→대상).</summary>
        public bool TakeDamage(float amount, Vector2 hitDir = default)
        {
            if (IsDead || invulnerable) return false;
            CurrentHp -= amount;
            OnDamaged?.Invoke(this, amount, hitDir);
            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                IsDead = true;
                OnDeath?.Invoke(this);
                return true;
            }
            return false;
        }
    }
}
