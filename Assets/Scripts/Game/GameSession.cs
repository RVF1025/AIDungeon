namespace AIDungeon.Game
{
    /// <summary>씬 간 공유 상태 + 씬 이름 상수. (정적이라 씬 전환에도 유지)</summary>
    public static class GameSession
    {
        public const string SceneTitle = "Title";
        public const string SceneGame = "Game";
        public const string SceneGameOver = "GameOver";

        /// <summary>게임오버 시 도달한 층 (GameOver 씬에서 표시).</summary>
        public static int FloorsReached = 1;
    }
}
