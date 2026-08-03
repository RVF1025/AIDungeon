using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>층마다 새 위치에 생성되는 방 한 개.</summary>
    public class Room
    {
        public GameObject root;
        public Vector2 center;
        public Vector2 half;      // 벽 안쪽 반경
        public Vector2 playerSpawn;
    }

    /// <summary>
    /// AI Director의 topology 결정을 실제 방 구조로 만든다(설계 문서 3.3 topology).
    ///   corridor → 길쭉한 통로,  open → 넓은 개활지,
    ///   cover → 기둥(엄폐물) 배치, encircle → 정사각(포위형)
    /// 층마다 center를 멀리 떨어뜨려 "새로운 위치"를 만든다(카메라는 스냅).
    /// </summary>
    public static class RoomBuilder
    {
        public static Room Build(DirectorDecision d, int floor)
        {
            var root = new GameObject($"Room_{floor}");
            Vector2 center = new Vector2(floor * 80f, 0f); // 층마다 먼 새 위치

            Vector2 half; Vector2 spawn;
            switch (d.topology)
            {
                case Topology.Corridor:
                    half = new Vector2(16f, 4f); spawn = center + new Vector2(-half.x + 2.5f, 0); break;
                case Topology.Open:
                    half = new Vector2(14f, 9f); spawn = center; break;
                case Topology.Cover:
                    half = new Vector2(11f, 7f); spawn = center; break;
                case Topology.Encircle:
                    half = new Vector2(9f, 9f); spawn = center; break;
                default:
                    half = new Vector2(12f, 7f); spawn = center; break;
            }

            BuildFloorAndWalls(root, center, half, floor);
            if (d.topology == Topology.Cover)
                BuildPillars(root, center, half, spawn);

            return new Room { root = root, center = center, half = half, playerSpawn = spawn };
        }

        private static void BuildFloorAndWalls(GameObject root, Vector2 center, Vector2 half, int floor)
        {
            // 바닥 (층마다 색조 살짝 변화 → 새 장소 느낌)
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(root.transform, false);
            floorGo.transform.position = center;
            floorGo.transform.localScale = new Vector3(half.x * 2f, half.y * 2f, 1f);
            var sr = floorGo.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = Color.HSVToRGB((floor * 0.08f) % 1f, 0.22f, 0.16f);
            sr.sortingOrder = -10;

            float t = 1f;
            Wall(root, center + new Vector2(0, half.y + t / 2f), new Vector2(half.x * 2f + t * 2f, t));
            Wall(root, center + new Vector2(0, -half.y - t / 2f), new Vector2(half.x * 2f + t * 2f, t));
            Wall(root, center + new Vector2(half.x + t / 2f, 0), new Vector2(t, half.y * 2f));
            Wall(root, center + new Vector2(-half.x - t / 2f, 0), new Vector2(t, half.y * 2f));
        }

        private static void Wall(GameObject root, Vector2 c, Vector2 size)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(root.transform, false);
            go.transform.position = c;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = new Color(0.25f, 0.25f, 0.32f);
            sr.sortingOrder = 0;
            go.AddComponent<BoxCollider2D>();
            go.AddComponent<Solid>();
        }

        private static void BuildPillars(GameObject root, Vector2 center, Vector2 half, Vector2 spawn)
        {
            int count = 6;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = Vector2.zero;
                for (int tries = 0; tries < 10; tries++)
                {
                    p = center + new Vector2(
                        Random.Range(-half.x + 2f, half.x - 2f),
                        Random.Range(-half.y + 2f, half.y - 2f));
                    if (Vector2.Distance(p, spawn) > 3.5f) break; // 스폰 지점은 비움
                }
                var go = new GameObject("Pillar");
                go.transform.SetParent(root.transform, false);
                go.transform.position = p;
                go.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.Square();
                sr.color = new Color(0.32f, 0.32f, 0.4f);
                sr.sortingOrder = 1;
                go.AddComponent<BoxCollider2D>();
                go.AddComponent<Solid>();
            }
        }
    }
}
