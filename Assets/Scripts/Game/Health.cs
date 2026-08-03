using System;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>체력 + 피아 팀. 투사체/근접이 반대 팀만 때린다.</summary>
    public class Health : MonoBehaviour
    {
        public Team team;
        public float maxHp = 100f;
        public float CurrentHp { get; private set; }
        public float Fraction => maxHp <= 0 ? 0 : Mathf.Clamp01(CurrentHp / maxHp);
        public bool IsDead { get; private set; }

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

        /// <summary>데미지 적용. 죽으면 true 반환.</summary>
        public bool TakeDamage(float amount)
        {
            if (IsDead) return false;
            CurrentHp -= amount;
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
