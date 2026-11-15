using System.Collections.Generic;
using CarX.Gameplay;
using UnityEngine;

namespace CarX.DarkOrientation.Integration
{
    /// <summary>
    /// The only file that knows this project's player. It lives outside the module's assembly because
    /// CarX.Gameplay is compiled into Assembly-CSharp. If the player scripts change, fix or delete just this file.
    /// Rules: the mode turns on only while the flashlight is off and turns off when the flashlight comes on;
    /// a pistol in the right hand leaves the search to the left hand; inventory and menus block the key.
    /// </summary>
    static class DarkOrientationPlayerBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            DarkOrientationController.Created -= Attach;
            DarkOrientationController.Created += Attach;
        }

        static void Attach(DarkOrientationController mode)
        {
            var body = mode.Body;
            if (body == null) return;
            var inventory = body.GetComponentInParent<PlayerInventory>();
            var pistol = body.GetComponentInParent<PlayerPistol>();
            var flashlights = new List<Light>();
            CollectFlashlights(body, flashlights);
            if (mode.ViewCamera != null) CollectFlashlights(mode.ViewCamera.transform, flashlights);

            mode.InputBlockedQuery = () => inventory != null && inventory.BlocksWeaponInput;
            mode.ForceOffQuery = () => AnyOn(flashlights);
            mode.HandOccupiedQuery = side => side == HandSide.Right && pistol != null && pistol.IsEquipped;

            // The pistol view model is built at runtime under the camera; keep it sharp and outlined like the hands.
            var weapon = new List<Renderer>();
            mode.ExtraSilhouettes = into =>
            {
                if (pistol == null || !pistol.IsEquipped || pistol.ViewCamera == null) return;
                if (weapon.Count == 0 || weapon[0] == null)
                {
                    weapon.Clear();
                    foreach (var r in pistol.ViewCamera.GetComponentsInChildren<Renderer>(true))
                        if ((r.renderingLayerMask & PlayerPistol.WeaponRenderingLayer) != 0) weapon.Add(r);
                }
                into.AddRange(weapon);
            };
        }

        static void CollectFlashlights(Transform root, List<Light> into)
        {
            foreach (var flashlight in root.GetComponentsInChildren<Flashlight>(true))
                if (flashlight.TryGetComponent(out Light light) && !into.Contains(light)) into.Add(light);
        }

        // Flashlight disables its Light both when switched off and when the battery is empty.
        static bool AnyOn(List<Light> lights)
        {
            foreach (var light in lights)
                if (light != null && light.enabled && light.gameObject.activeInHierarchy) return true;
            return false;
        }
    }
}
