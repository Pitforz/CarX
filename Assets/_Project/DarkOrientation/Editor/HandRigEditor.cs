using UnityEditor;
using UnityEngine;

namespace CarX.DarkOrientation.EditorTools
{
    [CustomEditor(typeof(HandRig))]
    sealed class HandRigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var rig = (HandRig)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Заполнение", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Из Humanoid"))
                {
                    var animator = rig.GetComponentInParent<Animator>();
                    Undo.RecordObject(rig, "Fill Hand Rig");
                    if (!HandRigAutoSetup.FromHumanoid(rig, animator, rig.side))
                        Debug.LogWarning("CarX DarkOrientation: no Humanoid Animator above " + rig.name, rig);
                    FillRenderers(rig);
                }
                if (GUILayout.Button("По именам костей"))
                {
                    Undo.RecordObject(rig, "Fill Hand Rig");
                    var root = rig.transform.root;
                    if (!HandRigAutoSetup.FromNamePatterns(rig, root, rig.side))
                        Debug.LogWarning("CarX DarkOrientation: no hand bone found under " + root.name, rig);
                    FillRenderers(rig);
                }
            }
            if (GUILayout.Button("Проверить"))
            {
                bool ok = rig.Bind();
                Debug.Log(ok
                    ? $"CarX DarkOrientation: {rig.name} ok, arm {(rig.HasArm ? "yes" : "no")}, hand length {rig.HandLength:0.000} m, reach {rig.Reach:0.000} m, fingers "
                      + $"{Count(rig)}/5"
                    : $"CarX DarkOrientation: {rig.name} has no hand bone", rig);
            }
        }

        static int Count(HandRig rig)
        {
            int n = 0;
            for (int i = 0; i < 5; i++) if (rig.HasFinger((Finger)i)) n++;
            return n;
        }

        static void FillRenderers(HandRig rig)
        {
            var root = rig.GetComponentInParent<Animator>() != null ? rig.GetComponentInParent<Animator>().transform : rig.transform.root;
            rig.renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            EditorUtility.SetDirty(rig);
        }
    }
}
