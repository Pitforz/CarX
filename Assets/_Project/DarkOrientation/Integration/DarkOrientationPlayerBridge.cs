using System.Collections.Generic;
using CarX.Gameplay;
using CarX.Level;
using UnityEngine;

namespace CarX.DarkOrientation.Integration
{
    /// <summary>
    /// The only file that knows this project's player. It lives outside the module's assembly because
    /// CarX.Gameplay is compiled into Assembly-CSharp. If the player scripts change, fix or delete just this file.
    /// Rules: asking for the mode puts the flashlight out, and the flashlight coming on ends the mode;
    /// a pistol in the right hand leaves the search to the left hand; inventory and menus block the key;
    /// walls are the tunnel graph's geometry, everything else is a small object.
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
            var flashlights = new List<Flashlight>();
            CollectFlashlights(body, flashlights);
            if (mode.ViewCamera != null) CollectFlashlights(mode.ViewCamera.transform, flashlights);

            mode.InputBlockedQuery = () => inventory != null && inventory.BlocksWeaponInput;
            mode.ForceOffQuery = () => AnyOn(flashlights);
            mode.ClearForceOff = () =>
            {
                foreach (var flashlight in flashlights)
                    if (flashlight != null) flashlight.SwitchOff();
            };
            // TunnelTouchShell builds its bands and collision shells on scene load; the hands feel the bands instead of the
            // joints of the visible mesh, and ignore the collision shells.
            mode.KindQuery = KindOf;
            // The hands feel the band instead of the visible wall, so its layer is always in the touch mask.
            DarkOrientationSettings.Load().extraTouchLayers |= 1 << TunnelTouchShell.Layer;
            // Both hands feel the way: the pistol and its arms are put away while the mode is on.
            mode.HandOccupiedQuery = null;
            if (pistol != null)
            {
                pistol.HideQuery = () => mode != null && mode.HidesWeapon;
                pistol.BlockQuery = () => mode != null && mode.BlocksWeapon;
                // The mode widens the view so the hand on the wall stays in sight when the player looks along it.
                pistol.FovBonusQuery = () => mode != null ? mode.FovBonus : 0f;
            }

            // The pistol view model is built at runtime under the camera; keep it sharp and outlined like the hands.
            var weapon = new List<Renderer>();
            mode.ExtraSilhouettes = into =>
            {
                if (pistol == null || !pistol.IsEquipped || pistol.ViewCamera == null || pistol.IsHiddenExternally) return;
                if (weapon.Count == 0 || weapon[0] == null)
                {
                    weapon.Clear();
                    foreach (var r in pistol.ViewCamera.GetComponentsInChildren<Renderer>(true))
                        if ((r.renderingLayerMask & PlayerPistol.WeaponRenderingLayer) != 0) weapon.Add(r);
                }
                into.AddRange(weapon);
            };
        }

        static void CollectFlashlights(Transform root, List<Flashlight> into)
        {
            foreach (var flashlight in root.GetComponentsInChildren<Flashlight>(true))
                if (!into.Contains(flashlight)) into.Add(flashlight);
        }

        static bool AnyOn(List<Flashlight> flashlights)
        {
            foreach (var flashlight in flashlights)
                if (flashlight != null && flashlight.IsSwitchedOn && flashlight.isActiveAndEnabled) return true;
            return false;
        }

        /// <summary>
        /// Every wall of the level comes from the tunnel graph. Where a piece has its touch shell, the hands feel the shell
        /// and ignore the visible mesh behind it. Dressing, mannequins and anything else are objects.
        /// </summary>
        static TouchKind KindOf(Collider c)
        {
            if (TunnelTouchShell.IsShell(c)) return TouchKind.Wall;
            if (TunnelTouchShell.IsCollisionShell(c)) return TouchKind.Ignore;
            if (c.GetComponent<TunnelColumn>() != null)
                return TunnelTouchShell.HasShell(c.transform) ? TouchKind.Ignore : TouchKind.Wall;
            var t = c.transform;
            if (t.name != TunnelPath.GeneratedName || t.parent == null) return TouchKind.Object;
            var owner = t.parent;
            bool graph = owner.GetComponent<TunnelPath>() != null || owner.GetComponent<TunnelJunction>() != null
                || owner.GetComponent<TunnelRoom>() != null;
            if (!graph) return TouchKind.Object;
            return TunnelTouchShell.HasShell(owner) ? TouchKind.Ignore : TouchKind.Wall;
        }
    }
}
