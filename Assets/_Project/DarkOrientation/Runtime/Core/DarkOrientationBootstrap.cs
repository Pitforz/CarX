using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.DarkOrientation
{
    /// <summary>
    /// Adds the mode to every loaded scene that has a player (Player tag + CharacterController), so no shared scene
    /// or prefab has to change. Skipped when the scene already has a controller or a <see cref="DarkOrientationOptOut"/>.
    /// </summary>
    public static class DarkOrientationBootstrap
    {
        static bool s_Hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            s_Hooked = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (!s_Hooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                s_Hooked = true;
            }
            TryInstall();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInstall();

        public static DarkOrientationController TryInstall()
        {
            var settings = DarkOrientationSettings.Load();
            if (!settings.autoInstall) return null;
            var existing = Object.FindAnyObjectByType<DarkOrientationController>();
            if (existing != null) return existing;
            if (Object.FindAnyObjectByType<DarkOrientationOptOut>() != null) return null;
            var player = FindPlayer();
            if (player == null) return null;

            var go = new GameObject("_CarX_DarkOrientation");
            if (go.scene != player.gameObject.scene) SceneManager.MoveGameObjectToScene(go, player.gameObject.scene);
            var controller = go.AddComponent<DarkOrientationController>();
            controller.Bind(player, Camera.main);
            return controller;
        }

        public static Transform FindPlayer()
        {
            foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
                if (go.TryGetComponent(out CharacterController _)) return go.transform;
            var any = Object.FindAnyObjectByType<CharacterController>();
            return any != null ? any.transform : null;
        }
    }
}
