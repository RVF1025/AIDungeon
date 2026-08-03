using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>플레이어를 부드럽게 따라가는 탑다운 카메라.</summary>
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public float smooth = 8f;

        private void LateUpdate()
        {
            if (target == null) return;
            var p = target.position;
            p.z = transform.position.z; // 카메라 z 유지
            transform.position = Vector3.Lerp(transform.position, p, smooth * Time.deltaTime);
        }
    }
}
