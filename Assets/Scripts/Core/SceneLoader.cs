using KernelPanic.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KernelPanic.Core
{
    /// <summary>
    /// Provides scene navigation entry points for the runtime shell.
    /// </summary>
    public static class SceneLoader
    {
        public const string MainMenuSceneName = "MainMenuScene";
        public const string GameSceneName = "GameScene";

        private const string CurtainClassName = "scene-fade-curtain";
        private const string CurtainVisibleClassName = "scene-fade-visible";
        private const int FadeOutMs = 200;

        public static void LoadMainMenu(VisualElement fadeRoot)
        {
            FadeThenLoad(fadeRoot, MainMenuSceneName);
        }

        public static void LoadGame(VisualElement fadeRoot)
        {
            FadeThenLoad(fadeRoot, GameSceneName);
        }

        private static void FadeThenLoad(VisualElement fadeRoot, string sceneName)
        {
            if (fadeRoot == null)
            {
                LoadScene(sceneName);
                return;
            }

            VisualElement curtain = new() { pickingMode = PickingMode.Ignore };
            curtain.AddToClassList(CurtainClassName);
            fadeRoot.Add(curtain);

            // Two scheduled steps: add the class one tick late so the initial opacity:0 is
            // actually rendered first and the transition has something to animate from.
            curtain.schedule.Execute(() => curtain.AddToClassList(CurtainVisibleClassName)).StartingIn(0);

            int delay = UIPreferences.ReducedMotion ? 0 : FadeOutMs;
            curtain.schedule.Execute(() => LoadScene(sceneName)).StartingIn(delay);
        }

        // Static for now because menu navigation has no instance state; a loading-screen service can replace this hook later.
        private static void LoadScene(string sceneName)
        {
            // TODO: Route async progress into a loading screen once the UI shell has one.
            SceneManager.LoadSceneAsync(sceneName);
        }
    }
}
