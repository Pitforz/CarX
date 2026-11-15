using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.DarkOrientation
{
    public enum HandSide { Left, Right }
    public enum Finger { Thumb, Index, Middle, Ring, Little }

    [Serializable]
    public sealed class FingerChain
    {
        [Tooltip("Суставы от основания к кончику")]
        public Transform[] joints = new Transform[0];
        [Tooltip("Кончик пальца (необязательно)")]
        public Transform tip;

        public bool IsValid => joints != null && joints.Length > 0 && joints[0] != null;
    }

    /// <summary>
    /// Describes one arm of any skeleton: optional upper arm and forearm, a hand and up to five fingers.
    /// Palm and bend axes are measured from the bind pose, so bone axis conventions do not matter.
    /// Fill it from a Humanoid avatar, from bone names (HandRigAutoSetup) or by hand.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CarX/Dark Orientation/Hand Rig")]
    public sealed class HandRig : MonoBehaviour
    {
        static readonly float[] FingerMaxAngles = { 70f, 95f, 70f, 60f };
        static readonly float[] ThumbMaxAngles = { 30f, 45f, 65f, 50f };

        public HandSide side = HandSide.Right;
        [Tooltip("Плечевая кость. Без неё и предплечья кисть ставится в цель напрямую")]
        public Transform upperArm;
        public Transform forearm;
        public Transform hand;
        public FingerChain thumb = new();
        public FingerChain index = new();
        public FingerChain middle = new();
        public FingerChain ring = new();
        public FingerChain little = new();
        [Tooltip("Рендереры руки: по ним рисуется силуэт и контур")]
        public Renderer[] renderers = new Renderer[0];

        sealed class FingerData
        {
            public Transform[] joints;
            public Vector3[] bendAxes;
            public float[] maxAngles;
            public float length;
        }

        readonly List<Transform> _poseBones = new();
        readonly List<Quaternion> _restLocal = new();
        readonly FingerData[] _fingers = new FingerData[5];
        Vector3 _fingerDirLocal = Vector3.forward;
        Vector3 _palmNormalLocal = Vector3.down;
        Quaternion _palmFrameInverse = Quaternion.identity;
        float _upperLength;
        float _foreLength;

        public bool IsBound { get; private set; }
        public bool HasArm => upperArm != null && forearm != null;
        public float HandLength { get; private set; } = 0.19f;
        /// <summary>Hand-local vector from the wrist to the centre of the palm surface.</summary>
        public Vector3 PalmPointLocal { get; private set; }
        public float Reach => (HasArm ? _upperLength + _foreLength : 0f) + PalmPointLocal.magnitude;

        public FingerChain GetFinger(Finger f) => f switch
        {
            Finger.Thumb => thumb,
            Finger.Index => index,
            Finger.Middle => middle,
            Finger.Ring => ring,
            _ => little,
        };

        /// <summary>Measures the rig. Call while it is in its rest pose (arm straight, fingers open).</summary>
        public bool Bind()
        {
            IsBound = false;
            if (hand == null) return false;

            _poseBones.Clear();
            _restLocal.Clear();
            AddPoseBone(upperArm);
            AddPoseBone(forearm);
            AddPoseBone(hand);

            Vector3 wrist = hand.position;
            Vector3 fingerDir = middle.IsValid ? middle.joints[0].position - wrist
                : index.IsValid ? index.joints[0].position - wrist
                : FirstChildDirection(hand);
            if (fingerDir.sqrMagnitude < 1e-8f) fingerDir = hand.forward;
            fingerDir.Normalize();

            Vector3 across = index.IsValid && little.IsValid ? little.joints[0].position - index.joints[0].position
                : index.IsValid && ring.IsValid ? ring.joints[0].position - index.joints[0].position
                : hand.right * (side == HandSide.Right ? 1f : -1f);
            Vector3 palmNormal = Vector3.Cross(across, fingerDir) * (side == HandSide.Right ? 1f : -1f);
            palmNormal = Vector3.ProjectOnPlane(palmNormal, fingerDir);
            if (palmNormal.sqrMagnitude < 1e-8f) palmNormal = -hand.up;
            palmNormal.Normalize();

            _fingerDirLocal = hand.InverseTransformDirection(fingerDir);
            _palmNormalLocal = hand.InverseTransformDirection(palmNormal);
            _palmFrameInverse = Quaternion.Inverse(Quaternion.LookRotation(_fingerDirLocal, _palmNormalLocal));

            float knuckle = middle.IsValid ? Vector3.Distance(wrist, middle.joints[0].position) : 0.1f;
            float middleLength = 0f;
            for (int f = 0; f < 5; f++)
            {
                _fingers[f] = BuildFinger((Finger)f, palmNormal, fingerDir);
                if ((Finger)f == Finger.Middle && _fingers[f] != null) middleLength = _fingers[f].length;
            }
            HandLength = knuckle + (middleLength > 0f ? middleLength : knuckle * 0.9f);

            Vector3 palmCentre = wrist;
            int count = 1;
            if (index.IsValid) { palmCentre += index.joints[0].position; count++; }
            if (little.IsValid) { palmCentre += little.joints[0].position; count++; }
            else if (ring.IsValid) { palmCentre += ring.joints[0].position; count++; }
            if (count == 1) { palmCentre += wrist + fingerDir * knuckle; count++; }
            palmCentre /= count;
            float thickness = HandLength * 0.14f;
            // Rotation only: the offset stays in metres even on scaled skeletons.
            PalmPointLocal = Quaternion.Inverse(hand.rotation) * (palmCentre - wrist + palmNormal * (thickness * 0.5f));

            if (HasArm)
            {
                _upperLength = Vector3.Distance(upperArm.position, forearm.position);
                _foreLength = Vector3.Distance(forearm.position, hand.position);
            }

            IsBound = true;
            return true;
        }

        void AddPoseBone(Transform t)
        {
            if (t == null || _poseBones.Contains(t)) return;
            _poseBones.Add(t);
            _restLocal.Add(t.localRotation);
        }

        static Vector3 FirstChildDirection(Transform t)
        {
            return t.childCount > 0 ? t.GetChild(0).position - t.position : t.forward;
        }

        FingerData BuildFinger(Finger which, Vector3 palmNormal, Vector3 fingerDir)
        {
            var chain = GetFinger(which);
            if (!chain.IsValid) return null;
            int n = 0;
            while (n < chain.joints.Length && chain.joints[n] != null) n++;
            var data = new FingerData
            {
                joints = new Transform[n],
                bendAxes = new Vector3[n],
                maxAngles = new float[n],
            };
            var limits = which == Finger.Thumb ? ThumbMaxAngles : FingerMaxAngles;
            Vector3 curlNormal = palmNormal;
            if (which == Finger.Thumb && middle.IsValid)
                curlNormal = (palmNormal + (middle.joints[0].position - chain.joints[0].position).normalized * 0.8f).normalized;

            Vector3 lastDir = fingerDir * 0.02f;
            for (int i = 0; i < n; i++)
            {
                var joint = chain.joints[i];
                Vector3 next = i + 1 < n ? chain.joints[i + 1].position
                    : chain.tip != null ? chain.tip.position
                    : joint.position + lastDir * 0.8f;
                Vector3 seg = next - joint.position;
                if (seg.sqrMagnitude < 1e-10f) seg = lastDir;
                lastDir = seg;
                data.length += seg.magnitude;
                Vector3 axis = Vector3.Cross(seg, curlNormal);
                if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(fingerDir, palmNormal);
                data.joints[i] = joint;
                data.bendAxes[i] = joint.InverseTransformDirection(axis.normalized);
                data.maxAngles[i] = limits[Mathf.Min(i, limits.Length - 1)];
                AddPoseBone(joint);
            }
            return data;
        }

        public void ResetPose()
        {
            for (int i = 0; i < _poseBones.Count; i++)
                if (_poseBones[i] != null) _poseBones[i].localRotation = _restLocal[i];
        }

        /// <summary>World rotation of the hand bone that points the fingers along <paramref name="fingerDir"/> with the palm facing <paramref name="palmNormal"/>.</summary>
        public Quaternion HandRotationFor(Vector3 fingerDir, Vector3 palmNormal)
        {
            if (Vector3.Cross(fingerDir, palmNormal).sqrMagnitude < 1e-8f)
                palmNormal = Vector3.Cross(fingerDir, Vector3.right);
            return Quaternion.LookRotation(fingerDir, palmNormal) * _palmFrameInverse;
        }

        /// <summary>Places the wrist at <paramref name="wristTarget"/> (two-bone IK when the rig has an arm) and turns the hand.</summary>
        public void SolvePose(Vector3 wristTarget, Vector3 pole, Quaternion handRotation)
        {
            if (!IsBound) return;
            if (HasArm)
            {
                Vector3 shoulder = upperArm.position;
                Vector3 target = TwoBoneIK.ClampTarget(shoulder, wristTarget, _upperLength, _foreLength);
                Vector3 elbow = TwoBoneIK.SolveElbow(shoulder, target, _upperLength, _foreLength, pole);
                upperArm.rotation = Quaternion.FromToRotation(forearm.position - shoulder, elbow - shoulder) * upperArm.rotation;
                forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, target - forearm.position) * forearm.rotation;
            }
            else
            {
                hand.position = wristTarget;
            }
            hand.rotation = handRotation;
        }

        /// <summary>0 = straight finger, 1 = fist. Absolute: relative to the rest pose.</summary>
        public void SetCurl(Finger finger, float curl)
        {
            var data = _fingers[(int)finger];
            if (data == null) return;
            for (int i = 0; i < data.joints.Length; i++)
            {
                var joint = data.joints[i];
                int idx = _poseBones.IndexOf(joint);
                Quaternion rest = idx >= 0 ? _restLocal[idx] : joint.localRotation;
                joint.localRotation = rest * Quaternion.AngleAxis(curl * data.maxAngles[i], data.bendAxes[i]);
            }
        }

        public bool HasFinger(Finger finger) => _fingers[(int)finger] != null;
        public float FingerLength(Finger finger) => _fingers[(int)finger]?.length ?? 0f;
        public Vector3 FingerBase(Finger finger)
        {
            var data = _fingers[(int)finger];
            return data != null ? data.joints[0].position : hand.position;
        }

        public void CollectRenderers(List<Renderer> into)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
                if (r != null) into.Add(r);
        }
    }

    /// <summary>Analytic two-bone IK.</summary>
    public static class TwoBoneIK
    {
        public static Vector3 ClampTarget(Vector3 root, Vector3 target, float upper, float lower)
        {
            Vector3 d = target - root;
            float len = d.magnitude;
            float max = (upper + lower) * 0.999f;
            float min = Mathf.Abs(upper - lower) + 1e-3f;
            if (len < 1e-6f) return root + Vector3.forward * min;
            return root + d / len * Mathf.Clamp(len, min, max);
        }

        /// <summary>Elbow position for a reachable target; the elbow bends towards <paramref name="pole"/>.</summary>
        public static Vector3 SolveElbow(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole)
        {
            Vector3 toTarget = target - root;
            float d = toTarget.magnitude;
            if (d < 1e-6f) return root + Vector3.down * upper;
            Vector3 dir = toTarget / d;
            Vector3 bend = Vector3.ProjectOnPlane(pole - root, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.right, dir);
            bend.Normalize();
            float cos = Mathf.Clamp((upper * upper + d * d - lower * lower) / (2f * upper * d), -1f, 1f);
            float sin = Mathf.Sqrt(1f - cos * cos);
            return root + dir * (upper * cos) + bend * (upper * sin);
        }
    }
}
