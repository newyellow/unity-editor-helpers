#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using System;
using System.Reflection;

[AttributeUsage(AttributeTargets.Method)]
public class ButtonAttribute : Attribute
{
    public string Label { get; private set; }

    public ButtonAttribute(string label = null)
    {
        Label = label;
    }
}

#if UNITY_EDITOR
static class ButtonGUI
{
    public static void DrawButtonsForObject(UnityEngine.Object targetObj)
    {
        if (targetObj == null) return;

        var type = targetObj.GetType();
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var m in methods)
        {
            var attr = m.GetCustomAttribute<ButtonAttribute>();
            if (attr == null) continue;

            // Only methods with no parameters (you can extend if you wish)
            if (m.GetParameters().Length != 0) continue;

            string label = string.IsNullOrEmpty(attr.Label) ? m.Name : attr.Label;

            EditorGUILayout.Space(4);
            {
                var buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.padding = new RectOffset(12, 12, 8, 8); // Add padding to the button
                var style = new GUIStyle(GUI.skin.button) { padding = new RectOffset(12, 12, 8, 8) };
                style.stretchWidth = true;

                // EditorGUILayout.BeginHorizontal();
                // GUILayout.FlexibleSpace();
                if (GUILayout.Button(label, style, GUILayout.ExpandWidth(true)))
                {
                    try { m.Invoke(m.IsStatic ? null : targetObj, null); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                // GUILayout.FlexibleSpace();
                // EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space(2);
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(MonoBehaviour), true)]
public class ButtonEditor_MonoBehaviour : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        // Draw buttons for the target MB
        if (!serializedObject.isEditingMultipleObjects)
            ButtonGUI.DrawButtonsForObject(target);
        else
            foreach (var t in targets) ButtonGUI.DrawButtonsForObject(t);
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(ScriptableObject), true)]
public class ButtonEditor_ScriptableObject : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        // Draw buttons for the selected ScriptableObject
        if (!serializedObject.isEditingMultipleObjects)
            ButtonGUI.DrawButtonsForObject(target);
        else
            foreach (var t in targets) ButtonGUI.DrawButtonsForObject(t);
    }
}
#endif