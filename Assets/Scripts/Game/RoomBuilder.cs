using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>층마다 새 위치에 생성되는 방 한 개.</summary>
    public class Room
    {
        public GameObject root;
        public Vector2 center;
        public Vector2 half;      // 벽 안쪽 반경(바닥 영역)
        public Vector2 playerSpawn;
    }

    /// <summary>
    /// AI Director의 topology를 방 구조로 만든다. 바닥/벽은 Kenney 타일을 SpriteRenderer의
    /// Tiled 모드로 "반복" 렌더 → 방당 오브젝트 몇 개면 됨(수백 개 생성 시 WebGL 메모리 폭발 방지).
    /// 벽 4스트립이 콜라이더(Solid). cover면 기둥.
    /// 전제: 타일 스프라이트 Mesh Type = Full Rect (Tiled 모드 요구; 아니어도 크래시는 안 남).
    /// </summary>
    public static class RoomBuilder
    {
        private const int FloorTile = 0, WallTile = 40, PillarTile = 28;

        public static Room Build(DirectorDecision d, int floor)
        {
            var root = new GameObject($"Room_{floor}");
            Vector2 center = new Vector2(floor * 80f, 0f);

            Vector2 half; Vector2 spawn;
            switch (d.topology)
            {
                case Topology.Corridor:
                    half = new Vector2(19f, 2.6f); spawn = center + new Vector2(-half.x + 2.5f, 0); break;
                case Topology.Open:
                    half = new Vector2(14f, 9f); spawn = center; break;
                case Topology.Cover:
                    half = new Vector2(11f, 7f); spawn = center; break;
                case Topology.Encircle:
                    half = new Vector2(9f, 9f); spawn = center; break;
                default:
                    half = new Vector2(12f, 7f); spawn = center; break;
            }

            // 바닥(반복 렌더 1개)
            TiledQuad(root, FloorTile, center, half.x * 2f, half.y * 2f, -10, false);

            // 벽 4스트립(콜라이더 포함, 코너까지 겹침)
            float t = 1f;
            TiledQuad(root, WallTile, center + new Vector2(0, half.y + t * 0.5f), half.x * 2f + t * 2f, t, 0, true);
            TiledQuad(root, WallTile, center + new Vector2(0, -half.y - t * 0.5f), half.x * 2f + t * 2f, t, 0, true);
            TiledQuad(root, WallTile, center + new Vector2(half.x + t * 0.5f, 0), t, half.y * 2f, 0, true);
            TiledQuad(root, WallTile, center + new Vector2(-half.x - t * 0.5f, 0), t, half.y * 2f, 0, true);

            DecorateFloor(root, center, half, spawn);
            if (d.topology == Topology.Cover) BuildPillars(root, center, half, spawn);

            return new Room { root = root, center = center, half = half, playerSpawn = spawn };
        }

        /// <summary>타일 스프라이트를 Tiled 모드로 (worldW × worldH) 만큼 반복 렌더. solid면 콜라이더.</summary>
        private static GameObject TiledQuad(GameObject root, int tile, Vector2 center,
                                            float worldW, float worldH, int sorting, bool solid)
        {
            var go = new GameObject(solid ? "Wall" : "Floor");
            go.transform.SetParent(root.transform, false);
            go.transform.position = center;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.TileFullRect(tile); // FullRect → Tiled 정상 동작(임포트 설정 불필요)
            sr.sortingOrder = sorting;
            sr.drawMode = SpriteDrawMode.Tiled;

            float unit = sr.sprite.bounds.size.y; // 타일 1개의 월드 크기(스케일1 기준)
            float s = unit > 0.0001f ? 1f / unit : 1f; // 타일 1개 = 1 월드유닛
            go.transform.localScale = new Vector3(s, s, 1f);
            sr.size = new Vector2(worldW * unit, worldH * unit); // 로컬 크기(스케일 후 worldW×worldH)

            if (solid)
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.size = sr.size; // 로컬
                go.AddComponent<Solid>();
            }
            return go;
        }

        // 바닥 위에 잔해/돌 장식을 흩뿌려 단조로움 완화(콜라이더 없음, 소량).
        private static readonly int[] Decor = { 12, 24 }; // 돌 / 잔해
        private static void DecorateFloor(GameObject root, Vector2 center, Vector2 half, Vector2 spawn)
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(half.x * half.y * 4f / 22f), 4, 40);
            for (int k = 0; k < count; k++)
            {
                Vector2 p = center + new Vector2(
                    Random.Range(-half.x + 0.6f, half.x - 0.6f),
                    Random.Range(-half.y + 0.6f, half.y - 0.6f));
                if (Vector2.Distance(p, spawn) < 1.5f) continue; // 스폰 발밑은 비움

                var go = new GameObject("Decor");
                go.transform.SetParent(root.transform, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0, 0, 90f * Random.Range(0, 4)); // 회전 변화
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.Tile(Decor[Random.Range(0, Decor.Length)]);
                sr.sortingOrder = -9; // 바닥(-10) 위, 캐릭터 아래
                float s = SpriteFactory.ScaleFor(sr.sprite, Random.Range(0.6f, 0.9f));
                go.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        private static void BuildPillars(GameObject root, Vector2 center, Vector2 half, Vector2 spawn)
        {
            for (int k = 0; k < 5; k++)
            {
                Vector2 p = Vector2.zero;
                for (int tries = 0; tries < 10; tries++)
                {
                    p = center + new Vector2(
                        Random.Range(-half.x + 2.5f, half.x - 2.5f),
                        Random.Range(-half.y + 2.5f, half.y - 2.5f));
                    if (Vector2.Distance(p, spawn) > 4f) break;
                }
                var go = new GameObject("Pillar");
                go.transform.SetParent(root.transform, false);
                go.transform.position = p;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.Tile(PillarTile);
                sr.sortingOrder = 1;
                float s = SpriteFactory.ScaleFor(sr.sprite, 1f);
                go.transform.localScale = new Vector3(s, s, 1f);
                var col = go.AddComponent<BoxCollider2D>();
                col.size = sr.sprite.bounds.size;
                go.AddComponent<Solid>();
            }
        }
    }
}
