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
    /// AI Director의 topology를 방 구조로 만든다. 바닥/벽은 Kenney Tiny Dungeon 타일로 깔고,
    /// 벽 타일이 곧 콜라이더(Solid)다. cover면 기둥 배치.
    /// </summary>
    public static class RoomBuilder
    {
        private const int FloorTile = 0, WallTile = 40, PillarTile = 28;
        private const float TileSize = 1.0f;

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

            BuildFloor(root, center, half);
            BuildWalls(root, center, half);
            if (d.topology == Topology.Cover) BuildPillars(root, center, half, spawn);

            return new Room { root = root, center = center, half = half, playerSpawn = spawn };
        }

        private static GameObject MakeTile(GameObject root, int tile, Vector2 pos, int sorting)
        {
            var go = new GameObject("Tile");
            go.transform.SetParent(root.transform, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Tile(tile);
            sr.sortingOrder = sorting;
            float s = SpriteFactory.ScaleFor(sr.sprite, TileSize);
            go.transform.localScale = new Vector3(s, s, 1f);
            return go;
        }

        private static Vector2 Origin(Vector2 center, Vector2 half) =>
            center - half + Vector2.one * (TileSize * 0.5f);

        private static void BuildFloor(GameObject root, Vector2 center, Vector2 half)
        {
            int nx = Mathf.CeilToInt(half.x * 2f / TileSize);
            int ny = Mathf.CeilToInt(half.y * 2f / TileSize);
            Vector2 o = Origin(center, half);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    Vector2 pos = o + new Vector2(i * TileSize, j * TileSize);
                    int t = Random.value < 0.07f ? (Random.value < 0.5f ? 12 : 24) : FloorTile; // 가끔 잔해
                    MakeTile(root, t, pos, -10);
                }
        }

        private static void BuildWalls(GameObject root, Vector2 center, Vector2 half)
        {
            int nx = Mathf.CeilToInt(half.x * 2f / TileSize);
            int ny = Mathf.CeilToInt(half.y * 2f / TileSize);
            for (int i = -1; i <= nx; i++) { PlaceWall(root, center, half, i, -1); PlaceWall(root, center, half, i, ny); }
            for (int j = 0; j < ny; j++) { PlaceWall(root, center, half, -1, j); PlaceWall(root, center, half, nx, j); }
        }

        private static void PlaceWall(GameObject root, Vector2 center, Vector2 half, int i, int j)
        {
            Vector2 pos = Origin(center, half) + new Vector2(i * TileSize, j * TileSize);
            var go = MakeTile(root, WallTile, pos, 0);
            var sr = go.GetComponent<SpriteRenderer>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = sr.sprite.bounds.size; // 로컬(스케일 적용 전) 크기
            go.AddComponent<Solid>();
        }

        private static void BuildPillars(GameObject root, Vector2 center, Vector2 half, Vector2 spawn)
        {
            int count = 5;
            for (int k = 0; k < count; k++)
            {
                Vector2 p = Vector2.zero;
                for (int tries = 0; tries < 10; tries++)
                {
                    p = center + new Vector2(
                        Random.Range(-half.x + 2.5f, half.x - 2.5f),
                        Random.Range(-half.y + 2.5f, half.y - 2.5f));
                    if (Vector2.Distance(p, spawn) > 4f) break;
                }
                var go = MakeTile(root, PillarTile, p, 1);
                var sr = go.GetComponent<SpriteRenderer>();
                var col = go.AddComponent<BoxCollider2D>();
                col.size = sr.sprite.bounds.size;
                go.AddComponent<Solid>();
            }
        }
    }
}
