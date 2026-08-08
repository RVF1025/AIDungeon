using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>엔티티 머리 위 체력바. 플레이어는 상시, 적은 피해 입었을 때만 표시.</summary>
    [RequireComponent(typeof(Health))]
    public class HealthBar : MonoBehaviour
    {
        public bool alwaysShow = false;
        public float yOffset = 0.85f, width = 0.9f, height = 0.14f;

        private Health _health;
        private GameObject _root;
        private Transform _bg, _fill;
        private SpriteRenderer _fillSr;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _root = new GameObject("HealthBar");
            _bg = MakePart(new Color(0f, 0f, 0f, 0.75f), 15, out _);
            _fill = MakePart(Color.green, 16, out _fillSr);
            _health.OnDeath += _ => { if (_root != null) Destroy(_root); };
        }

        private Transform MakePart(Color c, int order, out SpriteRenderer sr)
        {
            var go = new GameObject("part");
            go.transform.SetParent(_root.transform, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = c;
            sr.sortingOrder = order;
            return go.transform;
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            float frac = _health.Fraction;
            bool show = !_health.IsDead && (alwaysShow || frac < 0.999f);
            if (_root.activeSelf != show) _root.SetActive(show);
            if (!show) return;

            _root.transform.position = transform.position + Vector3.up * yOffset;
            _bg.localScale = new Vector3(width, height, 1f);

            float w = width * Mathf.Clamp01(frac);
            _fill.localScale = new Vector3(Mathf.Max(w, 0.001f), height * 0.7f, 1f);
            _fill.localPosition = new Vector3(-(width - w) * 0.5f, 0f, 0f);
            _fillSr.color = frac > 0.5f
                ? Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.4f, 0.9f, 0.3f), (frac - 0.5f) * 2f)
                : Color.Lerp(new Color(0.9f, 0.25f, 0.25f), new Color(1f, 0.85f, 0.2f), frac * 2f);
        }

        private void OnDestroy() { if (_root != null) Destroy(_root); }
    }
}
