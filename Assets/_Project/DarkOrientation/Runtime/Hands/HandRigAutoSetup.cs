using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.DarkOrientation
{
    /// <summary>Fills a <see cref="HandRig"/> from a Humanoid avatar or from common bone naming schemes.</summary>
    public static class HandRigAutoSetup
    {
        public static bool FromHumanoid(HandRig rig, Animator animator, HandSide side)
        {
            if (rig == null || animator == null || !animator.isHuman) return false;
            bool left = side == HandSide.Left;
            rig.side = side;
            rig.upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            rig.forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            rig.hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            rig.thumb = Chain(animator, left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            rig.index = Chain(animator, left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            rig.middle = Chain(animator, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            rig.ring = Chain(animator, left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal);
            rig.little = Chain(animator, left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            return rig.hand != null;
        }

        // Proximal, Intermediate, Distal follow each other in HumanBodyBones.
        static FingerChain Chain(Animator animator, HumanBodyBones proximal)
        {
            var joints = new List<Transform>(3);
            for (int i = 0; i < 3; i++)
            {
                var t = animator.GetBoneTransform(proximal + i);
                if (t == null) break;
                joints.Add(t);
            }
            var chain = new FingerChain { joints = joints.ToArray() };
            if (joints.Count > 0 && joints[^1].childCount > 0) chain.tip = joints[^1].GetChild(0);
            return chain;
        }

        static readonly string[] ThumbNames = { "thumb" };
        static readonly string[] IndexNames = { "index", "pointer" };
        static readonly string[] MiddleNames = { "middle" };
        static readonly string[] RingNames = { "ring" };
        static readonly string[] LittleNames = { "pinky", "little", "small" };

        /// <summary>
        /// Generic rigs: finds bones by name under <paramref name="root"/> (Mixamo "mixamorig:LeftHandIndex1",
        /// Unreal "index_01_l", Rigify "f_index.01.L", or anything with side + finger name + number).
        /// </summary>
        public static bool FromNamePatterns(HandRig rig, Transform root, HandSide side)
        {
            if (rig == null || root == null) return false;
            rig.side = side;
            var all = root.GetComponentsInChildren<Transform>(true);
            var hand = FindHand(all, side);
            if (hand == null) return false;
            rig.hand = hand;
            rig.forearm = hand.parent;
            rig.upperArm = rig.forearm != null ? rig.forearm.parent : null;
            if (rig.forearm != null && Has(rig.forearm.name, "twist") && rig.forearm.parent != null)
            {
                rig.forearm = rig.forearm.parent;
                rig.upperArm = rig.forearm.parent;
            }
            var under = hand.GetComponentsInChildren<Transform>(true);
            rig.thumb = NamedChain(under, ThumbNames);
            rig.index = NamedChain(under, IndexNames);
            rig.middle = NamedChain(under, MiddleNames);
            rig.ring = NamedChain(under, RingNames);
            rig.little = NamedChain(under, LittleNames);
            return true;
        }

        static Transform FindHand(Transform[] all, HandSide side)
        {
            Transform best = null;
            int bestScore = 0;
            foreach (var t in all)
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("hand") || n.Contains("index") || n.Contains("thumb") || n.Contains("middle")
                    || n.Contains("ring") || n.Contains("pinky") || n.Contains("little")) continue;
                if (!IsSide(n, side)) continue;
                int score = 1 + (n.EndsWith("hand") || n.EndsWith("hand_l") || n.EndsWith("hand_r") || n.EndsWith("hand.l") || n.EndsWith("hand.r") ? 2 : 0);
                if (score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        static bool IsSide(string n, HandSide side)
        {
            if (side == HandSide.Left)
                return n.Contains("left") || n.EndsWith("_l") || n.EndsWith(".l") || n.StartsWith("l_") || n.Contains("_l_");
            return n.Contains("right") || n.EndsWith("_r") || n.EndsWith(".r") || n.StartsWith("r_") || n.Contains("_r_");
        }

        static FingerChain NamedChain(Transform[] candidates, string[] names)
        {
            Transform first = null;
            foreach (var t in candidates)
            {
                string n = t.name.ToLowerInvariant();
                if (Array.Exists(names, x => n.Contains(x)) && (first == null || Depth(t) < Depth(first)))
                    first = t;
            }
            var joints = new List<Transform>();
            for (var t = first; t != null && joints.Count < 4;)
            {
                joints.Add(t);
                Transform next = null;
                foreach (Transform c in t)
                {
                    string n = c.name.ToLowerInvariant();
                    if (Array.Exists(names, x => n.Contains(x))) { next = c; break; }
                }
                t = next;
            }
            var chain = new FingerChain();
            // A trailing "end" / "4" bone is the tip, not a joint.
            if (joints.Count >= 4 || (joints.Count > 1 && Has(joints[^1].name, "end")))
            {
                chain.tip = joints[^1];
                joints.RemoveAt(joints.Count - 1);
            }
            chain.joints = joints.ToArray();
            return chain;
        }

        static bool Has(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        static int Depth(Transform t)
        {
            int d = 0;
            for (; t != null; t = t.parent) d++;
            return d;
        }
    }
}
