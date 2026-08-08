using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>
    /// 씬/프리팹 없이 Play 시 전체 게임을 코드로 구성한다.
    /// 사용법: 빈 씬에 빈 GameObject 하나 만들고 이 컴포넌트만 붙이면 끝.
    /// (입력은 Input System 저수준 API 사용 — 별도 프로젝트 설정 불필요)
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private void Start()
        {
            SetupCamera(out var cam);
            var player = BuildPlayer(out var playerHealth);

            var follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.target = player.transform;

            var logger = gameObject.AddComponent<BehaviorLogger>();
            logger.player = playerHealth;

            var spawner = gameObject.AddComponent<EnemySpawner>();
            spawner.Setup(player.transform, playerHealth); // 방 범위는 RoomManager가 층마다 Configure

            var client = gameObject.AddComponent<GeminiDirectorClient>();

            var hud = new GameObject("DirectorHud").AddComponent<DirectorHud>();

            var room = gameObject.AddComponent<RoomManager>();
            room.Begin(spawner, client, logger, playerHealth, hud);
        }

        private void SetupCamera(out Camera cam)
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.09f);
        }

        private GameObject BuildPlayer(out Health health)
        {
            var go = new GameObject("Player");
            go.transform.position = Vector3.zero;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(98); // 모험가
            sr.color = Color.white;
            sr.sortingOrder = 3;
            float scale = SpriteFactory.ScaleFor(sr.sprite, 1.1f);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.35f / scale; // 월드 반경 ~0.35
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 물리 스텝 사이 보간(지터 방지)

            health = go.AddComponent<Health>();
            health.Init(Team.Player, 100f);
            go.AddComponent<PlayerController>();
            go.AddComponent<HitReaction>();
            return go;
        }
    }
}
