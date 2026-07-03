using UnityEngine.SceneManagement;

namespace KernelPanic.Core
{
    /// <summary>
    /// Provides scene navigation entry points for the runtime shell.
    /// </summary>
    public static class SceneLoader
    {
        public const string MainMenuSceneName = "MainMenuScene";
        public const string GameSceneName = "GameScene";

        public static void LoadMainMenu()
        {
            LoadScene(MainMenuSceneName);
        }

        public static void LoadGame()
        {
            LoadScene(GameSceneName);
        }

        // Static for now because menu navigation has no instance state; a loading-screen service can replace this hook later.
        private static void LoadScene(string sceneName)
        {
            // TODO: Route async progress into a loading screen once the UI shell has one.
            SceneManager.LoadSceneAsync(sceneName);
        }
    }
}
