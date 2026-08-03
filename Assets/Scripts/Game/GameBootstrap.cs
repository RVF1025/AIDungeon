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
        public Vector2 roomHalf = new Vector2(11f, 7f);

        private void Start()
        {
            SetupCamera(out var cam);
            BuildRoom();
            var player = BuildPlayer(out var playerHealth);

            var follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.target = player.transform;

            var logger = gameObject.AddComponent<BehaviorLogger>();
            logger.player = playerHealth;

            var spawner = gameObject.AddComponent<EnemySpawner>();
            spawner.roomCenter = Vector2.zero;
            spawner.roomHalf = roomHalf;
            spawner.Setup(player.transform, playerHealth);

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

        private void BuildRoom()
        {
            // 바닥
            var floor = new GameObject("Floor");
            var sr = floor.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = new Color(0.13f, 0.13f, 0.18f);
            sr.sortingOrder = -10;
            floor.transform.localScale = new Vector3(roomHalf.x * 2f, roomHalf.y * 2f, 1f);

            // 벽 4개 (정적 콜라이더)
            float t = 1f;
            Wall(new Vector2(0, roomHalf.y + t / 2f), new Vector2(roomHalf.x * 2f + t * 2f, t));
            Wall(new Vector2(0, -roomHalf.y - t / 2f), new Vector2(roomHalf.x * 2f + t * 2f, t));
            Wall(new Vector2(roomHalf.x + t / 2f, 0), new Vector2(t, roomHalf.y * 2f));
            Wall(new Vector2(-roomHalf.x - t / 2f, 0), new Vector2(t, roomHalf.y * 2f));
        }

        private void Wall(Vector2 center, Vector2 size)
        {
            var go = new GameObject("Wall");
            go.transform.position = center;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = new Color(0.25f, 0.25f, 0.32f);
            sr.sortingOrder = 0;
            go.AddComponent<BoxCollider2D>(); // localScale에 맞춰 1x1 박스가 늘어남
        }

        private GameObject BuildPlayer(out Health health)
        {
            var go = new GameObject("Player");
            go.transform.position = Vector3.zero;
            go.transform.localScale = Vector3.one * 0.8f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = new Color(0.4f, 0.9f, 1f);
            sr.sortingOrder = 3;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            health = go.AddComponent<Health>();
            health.Init(Team.Player, 100f);
            go.AddComponent<PlayerController>();
            return go;
        }
    }
}
