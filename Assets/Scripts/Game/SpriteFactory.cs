using System.Collections.Generic;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>
    /// 에셋 임포트 없이 런타임에 스프라이트를 생성한다. 사각형/원 텍스처를 한 번 만들어 캐시하고
    /// SpriteRenderer의 color로 색만 바꿔 쓴다(프로토타입용 저비용 방식).
    /// </summary>
    public static class SpriteFactory
    {
        private static readonly Dictionary<string, Sprite> _cache = new();

        public static Sprite Square()
        {
            if (_cache.TryGetValue("sq", out var s)) return s;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            _cache["sq"] = s;
            return s;
        }

        /// <summary>Kenney Tiny Dungeon 타일 스프라이트 로드(Resources). index → tile_XXXX.png</summary>
        public static Sprite Tile(int index)
        {
            string key = "t" + index;
            if (_cache.TryGetValue(key, out var s)) return s;
            s = Resources.Load<Sprite>($"kenney_tiny-dungeon/Tiles/tile_{index:0000}");
            if (s == null) Debug.LogWarning($"[SpriteFactory] tile_{index:0000} 로드 실패");
            _cache[key] = s;
            return s;
        }

        /// <summary>
        /// 타일을 FullRect 메시 스프라이트로 생성(Sprite.Create는 기본이 FullRect).
        /// drawMode=Tiled가 임포트 설정(Full Rect) 없이도 제대로 반복 렌더되게 한다.
        /// </summary>
        public static Sprite TileFullRect(int index)
        {
            string key = "tf" + index;
            if (_cache.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>($"kenney_tiny-dungeon/Tiles/tile_{index:0000}");
            if (tex == null) { Debug.LogWarning($"[SpriteFactory] tile_{index:0000} 텍스처 로드 실패"); return Tile(index); }
            tex.filterMode = FilterMode.Point;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _cache[key] = s;
            return s;
        }

        /// <summary>스프라이트 실제 크기 기준으로 원하는 월드 높이에 맞춰 스케일(PPU 무관).</summary>
        public static float ScaleFor(Sprite sprite, float worldHeight)
        {
            if (sprite == null) return 1f;
            float h = sprite.bounds.size.y;
            return h > 0.0001f ? worldHeight / h : 1f;
        }

        /// <summary>근접 스윙용 초승달(아크) 스프라이트. +x 쪽으로 열린 호.</summary>
        public static Sprite Slash()
        {
            if (_cache.TryGetValue("slash", out var s)) return s;
            const int R = 32; float c = (R - 1) / 2f;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
            var px = new Color[R * R];
            for (int y = 0; y < R; y++)
                for (int x = 0; x < R; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    bool ring = d <= 14f && d >= 9f;
                    bool arc = ring && Mathf.Abs(Mathf.DeltaAngle(ang, 0f)) < 70f; // +x쪽 호
                    px[y * R + x] = arc ? Color.white : Color.clear;
                }
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);
            _cache["slash"] = s;
            return s;
        }

        public static Sprite Circle()
        {
            if (_cache.TryGetValue("ci", out var s)) return s;
            const int R = 32;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
            float c = (R - 1) / 2f, rad = R / 2f;
            var px = new Color[R * R];
            for (int y = 0; y < R; y++)
                for (int x = 0; x < R; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * R + x] = d <= rad ? Color.white : Color.clear;
                }
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);
            _cache["ci"] = s;
            return s;
        }
    }
}
