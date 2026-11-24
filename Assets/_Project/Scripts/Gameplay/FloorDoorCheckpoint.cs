using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.Gameplay
{
    /// <summary>
    /// Запоминает игрока в момент открытия двери на другой этаж.
    /// Смерть после этого возвращает к последней такой двери, без экрана «начать заново».
    /// </summary>
    static class FloorDoorCheckpoint
    {
        static bool _hasSave;
        static bool _restoreOnLoad;
        static Vector3 _position;
        static Quaternion _rotation;
        static float _pitch;
        static int _health = PlayerHealth.MaxHealth;
        static int _bleeding;
        static int _flashlight = 100;
        static int _loaded = PlayerPistol.MagazineSize;
        static int _reserve = PlayerPistol.MagazineSize;
        static bool _hasKey;
        static readonly List<string> _openDoors = new();

        internal static bool HasSave => _hasSave;

        internal static void Remember(LowerFloorDoor door, PlayerInventory inventory)
        {
            if (door == null || inventory == null || !IsFirstDoor(door))
                return;

            var player = inventory.transform;
            var body = player.GetComponent<FirstPersonController>();
            var health = player.GetComponent<PlayerHealth>();
            var pistol = player.GetComponent<PlayerPistol>();
            _position = player.position;
            _rotation = player.rotation;
            _pitch = body != null ? body.LookPitch : 0f;
            _health = health != null ? health.CurrentHealth : PlayerHealth.MaxHealth;
            _bleeding = health != null ? health.Bleeding : 0;
            _flashlight = health != null ? health.FlashlightConditionPoints : 100;
            _loaded = pistol != null ? pistol.LoadedRounds : PlayerPistol.MagazineSize;
            _reserve = inventory.ReserveRounds;
            _hasKey = inventory.HasLowerFloorKey;
            if (!_openDoors.Contains(door.name))
                _openDoors.Add(door.name);
            _hasSave = true;
        }

        internal static bool TryReload()
        {
            if (!_hasSave)
                return false;

            _restoreOnLoad = true;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            if (scene.buildIndex < 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    scene.path, new LoadSceneParameters(LoadSceneMode.Single));
                return true;
            }
#endif
            SceneManager.LoadScene(scene.buildIndex, LoadSceneMode.Single);
            return true;
        }

        internal static void Apply(PlayerInventory inventory)
        {
            if (!_restoreOnLoad || inventory == null)
                return;

            _restoreOnLoad = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;

            foreach (var door in Object.FindObjectsByType<LowerFloorDoor>(FindObjectsInactive.Exclude))
                if (_openDoors.Contains(door.name))
                    door.RestoreOpened();

            inventory.GetComponent<FirstPersonController>()?.RestoreCheckpointPose(_position, _rotation, _pitch);
            inventory.GetComponent<PlayerHealth>()?.RestoreCheckpoint(_health, _bleeding, _flashlight);
            inventory.GetComponent<PlayerPistol>()?.RestoreLoadedRounds(_loaded);
            inventory.RestoreCheckpoint(_hasKey, _reserve);
        }

        static bool IsFirstDoor(LowerFloorDoor door)
        {
            foreach (var part in door.GetComponentsInChildren<Transform>(true))
            {
                var name = part.name;
                if (name.IndexOf("Old Metal Door Leaf", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("first", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
