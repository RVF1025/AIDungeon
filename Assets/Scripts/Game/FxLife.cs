using TMPro;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>
    /// 이펙트 수명 관리: 정해진 시간 동안 이동/확대하며 알파를 페이드아웃하고 파괴한다.
    /// SpriteRenderer나 TMP_Text 어느 쪽이든 붙어 있으면 그 색을 페이드한다.
    /// </summary>
    public class FxLife : MonoBehaviour
    {
        public Vector3 velocity;
        public float scalePerSec;
        public float life = 0.5f;

        private SpriteRenderer _sr;
        private TMP_Text _tmp;
        private Color _c0;
        private float _t;

        public FxLife Bind()
        {
            _sr = GetComponent<SpriteRenderer>();
            _tmp = GetComponent<TMP_Text>();
            _c0 = _sr != null ? _sr.color : (_tmp != null ? _tmp.color : Color.white);
            return this;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - _t / life);

            transform.position += velocity * Time.deltaTime;
            if (scalePerSec != 0f) transform.localScale += Vector3.one * (scalePerSec * Time.deltaTime);

            var c = _c0; c.a = _c0.a * k;
            if (_sr != null) _sr.color = c;
            if (_tmp != null) _tmp.color = c;

            if (_t >= life) Destroy(gameObject);
        }
    }
}
