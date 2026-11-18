using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.DarkOrientation
{
    /// <summary>How the hands treat a collider.</summary>
    public enum TouchKind
    {
        /// <summary>Small highlight; the hand rests on it for a moment, then takes it again. People, props, moving things.</summary>
        Object,
        /// <summary>Wide highlight; the hand slides along it, it holds the player and turns the view at corners.</summary>
        Wall,
        /// <summary>Not felt at all (e.g. a visible wall that has a smooth invisible band in front of it).</summary>
        Ignore,
    }

    /// <summary>Sorts touchable colliders into walls, objects and ignored ones, with a per-collider cache.</summary>
    internal sealed class TouchClassifier
    {
        readonly Dictionary<Collider, TouchKind> _cache = new();

        /// <summary>Project rule (set by the integration). Without it, large colliders without a rigidbody count as walls.</summary>
        public Func<Collider, TouchKind> KindQuery;

        public TouchKind Kind(Collider c)
        {
            if (c == null) return TouchKind.Ignore;
            if (_cache.TryGetValue(c, out var kind)) return kind;
            kind = Classify(c);
            if (_cache.Count > 4096) _cache.Clear();
            _cache[c] = kind;
            return kind;
        }

        public bool IsWall(Collider c) => Kind(c) == TouchKind.Wall;

        public void Clear() => _cache.Clear();

        TouchKind Classify(Collider c)
        {
            if (c.GetComponentInParent<DarkTouchIgnore>() != null) return TouchKind.Ignore;
            if (c.isTrigger || c is CharacterController || c.attachedRigidbody != null) return TouchKind.Object;
            if (c.GetComponentInParent<DarkTouchProxy>() != null || c.GetComponentInParent<Animator>() != null) return TouchKind.Object;
            if (c.GetComponentInParent<DarkTouchWall>() != null) return TouchKind.Wall;
            if (KindQuery != null) return KindQuery(c);
            Vector3 size = c.bounds.size;
            return size.y >= 1.35f && Mathf.Max(size.x, size.z) >= 0.85f ? TouchKind.Wall : TouchKind.Object;
        }
    }
}
