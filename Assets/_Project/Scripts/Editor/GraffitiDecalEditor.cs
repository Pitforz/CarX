using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    [CustomEditor(typeof(GraffitiDecal)), CanEditMultipleObjects]
    public class GraffitiDecalEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("image"), new GUIContent("Картинка"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("size"), new GUIContent("Ширина, м"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("opacity"), new GUIContent("Прозрачность"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wear"), new GUIContent("Потёртость"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flip"), new GUIContent("Отразить"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("depth"), new GUIContent("Толщина слоя, м"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("drawDistance"), new GUIContent("Дальность видимости, м"));
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) foreach (var t in targets) ((GraffitiDecal)t).Refresh();

            var g = (GraffitiDecal)target;
            if (g.image == null)
                EditorGUILayout.HelpBox("Нет картинки: файл удалён или не выбран. Выберите картинку в окне CarX → Граффити и нажмите «Поставить выбранную картинку».", MessageType.Warning);
            else if (!AssetDatabase.GetAssetPath(g.image).StartsWith(GraffitiLibrary.Root + "/"))
                EditorGUILayout.HelpBox("Картинка не из папки " + GraffitiLibrary.Root + ": положите её туда, иначе материал не создастся.", MessageType.Warning);

            string where = g.GetComponent<TunnelAnchor>() != null
                ? "Привязано к тоннелю: двигается вместе с ним (см. блок «Tunnel Anchor» ниже)."
                : "Двигается вместе с родителем: " + (g.transform.parent != null ? g.transform.parent.name : "нет родителя") + ".";
            EditorGUILayout.HelpBox("Поворот и положение — обычными инструментами Unity (W/E). Синяя стрелка (ось Z) смотрит в стену.\n" + where, MessageType.None);
            if (GUILayout.Button("Открыть окно «Граффити»")) GraffitiWindow.Open();
        }
    }
}
