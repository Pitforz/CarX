using Unity.Cinemachine;
using UnityEngine;

namespace CarX.Flow
{
    /// <summary>
    /// Loops the menu camera along a spline the user can reshape in the Scene view.
    /// Timing is the curve on this component: 0 is the start of the path, 1 is the end.
    /// Look Wide, Look Board, and Look Desk are scene empties — move them to reframe.
    /// </summary>
    [AddComponentMenu("CarX/Menu desk camera fly")]
    public class MenuDeskCameraFly : MonoBehaviour
    {
        public CinemachineSplineDolly dolly;
        public Transform lookFocus;
        public Transform lookWide;
        public Transform lookBoard;
        public Transform lookDesk;

        [Tooltip("Position along Menu Fly Path over one loop, in spline knot units. 0 is the wide shot. Edit the keys here, or move the knots on Menu Fly Path in the Scene view.")]
        public AnimationCurve position = DefaultPosition();

        [Tooltip("Which look empty to use over the same 0–1 loop. 0 = Look Wide, 1 = Look Board, 2 = Look Desk.")]
        public AnimationCurve look = DefaultLook();

        [Tooltip("Seconds for one full loop.")]
        public float loopSeconds = 18f;

        void Reset()
        {
            dolly = GetComponent<CinemachineSplineDolly>();
        }

        void Update()
        {
            if (dolly == null || loopSeconds <= 0.05f) return;

            float u = Mathf.Repeat(Time.time, loopSeconds) / loopSeconds;
            dolly.CameraPosition = position.Evaluate(u);
            PlaceLook(Mathf.Clamp(look.Evaluate(u), 0f, 2f));
        }

        void PlaceLook(float index)
        {
            if (lookFocus == null) return;
            Transform a;
            Transform b;
            float t;
            if (index <= 1f)
            {
                a = lookWide;
                b = lookBoard;
                t = Smooth(index);
            }
            else
            {
                a = lookBoard;
                b = lookDesk;
                t = Smooth(index - 1f);
            }

            if (a == null && b == null) return;
            if (a == null) lookFocus.position = b.position;
            else if (b == null) lookFocus.position = a.position;
            else lookFocus.position = Vector3.Lerp(a.position, b.position, t);
        }

        static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        void OnDrawGizmos()
        {
            DrawLook(lookWide, new Color(0.95f, 0.85f, 0.45f));
            DrawLook(lookBoard, new Color(0.45f, 0.75f, 0.95f));
            DrawLook(lookDesk, new Color(0.95f, 0.55f, 0.35f));
        }

        static void DrawLook(Transform target, Color color)
        {
            if (target == null) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(target.position, 0.05f);
        }

        static AnimationCurve DefaultPosition()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.16f, 0f),
                new Keyframe(0.42f, 2f),
                new Keyframe(0.62f, 3f),
                new Keyframe(0.78f, 3f),
                new Keyframe(1f, 0f));
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0.4f);
            return curve;
        }

        static AnimationCurve DefaultLook()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.16f, 0f),
                new Keyframe(0.40f, 1f),
                new Keyframe(0.58f, 2f),
                new Keyframe(0.74f, 2f),
                new Keyframe(1f, 0f));
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0.35f);
            return curve;
        }
    }
}
