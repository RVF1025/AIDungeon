using TMPro;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>구운 한글 폰트(Resources/Fonts/NanumKR)를 캐시 로드. 없으면 null(TMP 기본 폰트).</summary>
    public static class UiFont
    {
        private static TMP_FontAsset _kr;
        private static bool _tried;

        public static TMP_FontAsset Korean()
        {
            if (_tried) return _kr;
            _tried = true;
            _kr = Resources.Load<TMP_FontAsset>("Fonts/NanumKR");
            if (_kr == null)
                Debug.LogWarning("[UiFont] Resources/Fonts/NanumKR 없음 — Font Asset Creator로 생성 필요.");
            return _kr;
        }
    }
}
