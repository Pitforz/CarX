using NUnit.Framework;
using UnityEngine;

namespace CarX.DarkOrientation.Tests
{
    public class HandRigTests
    {
        [Test]
        public void TwoBoneIK_ReachableTarget_KeepsBoneLengths()
        {
            var root = new Vector3(0.2f, 1.2f, 0f);
            var target = root + new Vector3(0.1f, -0.1f, 0.4f);
            var elbow = TwoBoneIK.SolveElbow(root, target, 0.30f, 0.26f, root + Vector3.down);
            Assert.That(Vector3.Distance(root, elbow), Is.EqualTo(0.30f).Within(1e-4f));
            Assert.That(Vector3.Distance(elbow, target), Is.EqualTo(0.26f).Within(1e-4f));
            Assert.That(elbow.y, Is.LessThan(Vector3.Lerp(root, target, 0.5f).y), "elbow bends towards the pole");
        }

        [Test]
        public void TwoBoneIK_FarTarget_IsClampedToReach()
        {
            var root = Vector3.zero;
            var clamped = TwoBoneIK.ClampTarget(root, new Vector3(0f, 0f, 2f), 0.30f, 0.26f);
            Assert.That(clamped.magnitude, Is.LessThanOrEqualTo(0.56f));
            Assert.That(clamped.magnitude, Is.GreaterThan(0.55f));
        }

        [Test]
        public void ProceduralHand_PutsWristOnTarget_AndPalmFacesSurface()
        {
            var rig = ProceduralHandBuilder.Build(HandSide.Right, 1f, null);
            try
            {
                Assert.That(rig.IsBound, Is.True);
                Assert.That(rig.HasArm, Is.True);
                for (int i = 0; i < 5; i++) Assert.That(rig.HasFinger((Finger)i), Is.True, ((Finger)i).ToString());

                rig.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, 0f), Quaternion.identity);
                rig.ResetPose();
                var wallNormal = Vector3.back;
                var rotation = rig.HandRotationFor(Vector3.up, -wallNormal);
                var palmTarget = new Vector3(0.05f, 1.35f, 0.45f);
                var wrist = palmTarget - rotation * rig.PalmPointLocal;
                rig.SolvePose(wrist, new Vector3(0.4f, 0.6f, 0f), rotation);

                Assert.That(Vector3.Distance(rig.hand.position, wrist), Is.LessThan(1e-3f));
                var palmPoint = rig.hand.position + rig.hand.rotation * rig.PalmPointLocal;
                Assert.That(Vector3.Distance(palmPoint, palmTarget), Is.LessThan(1e-3f));
                // Fingers point up the wall.
                Assert.That(rig.FingerBase(Finger.Middle).y, Is.GreaterThan(rig.hand.position.y));
            }
            finally
            {
                ProceduralHandBuilder.DestroyMeshesImmediate(rig);
                Object.DestroyImmediate(rig.gameObject);
            }
        }

        [Test]
        public void SetCurl_BendsFingerTowardsPalm()
        {
            var rig = ProceduralHandBuilder.Build(HandSide.Left, 1f, null);
            try
            {
                rig.ResetPose();
                var tip = rig.index.tip;
                var palmNormal = rig.hand.rotation * Vector3.down;
                float straightReach = Vector3.Distance(tip.position, rig.hand.position);
                rig.SetCurl(Finger.Index, 0.5f);
                Assert.That(Vector3.Dot(tip.position - rig.FingerBase(Finger.Index), palmNormal), Is.GreaterThan(0.03f), "half curl lifts the tip to the palm side");
                rig.SetCurl(Finger.Index, 1f);
                Assert.That(Vector3.Distance(tip.position, rig.hand.position), Is.LessThan(straightReach - 0.05f), "a fist pulls the tip back");
            }
            finally
            {
                ProceduralHandBuilder.DestroyMeshesImmediate(rig);
                Object.DestroyImmediate(rig.gameObject);
            }
        }
    }
}
