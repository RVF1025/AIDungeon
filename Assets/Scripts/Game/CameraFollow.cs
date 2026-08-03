using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>플레이어를 부드럽게 따라가는 탑다운 카메라 + 화면 흔들림.</summary>
    public class CameraFollow : MonoBehaviour
    {
        public static CameraFollow Instance { get; private set; }

        public Transform target;
        public float smooth = 8f;

        private float _shake;

        private void Awake() => Instance = this;

        /// <summary>강도 a로 화면 흔들기(누적이 아니라 최댓값 유지).</summary>
        public void AddShake(float a) => _shake = Mathf.Max(_shake, a);
        public static void Shake(float a) { if (Instance != null) Instance.AddShake(a); }

        private void LateUpdate()
        {
            if (target == null) return;
            var p = target.position;
            p.z = transform.position.z;
            Vector3 follow = Vector3.Lerp(transform.position, p, smooth * Time.deltaTime);

            if (_shake > 0.0001f)
            {
                follow += (Vector3)(Random.insideUnitCircle * _shake);
                _shake = Mathf.MoveTowards(_shake, 0f, 1.5f * Time.deltaTime);
            }
            transform.position = follow;
        }
    }
}
