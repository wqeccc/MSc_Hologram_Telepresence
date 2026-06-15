using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(KinectController))]
public class KinectControllerEditor : Editor
{
    string[] options = { "WFOV_2x2Binned", "NFOV_2x2Binned", "WFOV_Unbinned", "NFOV_Unbinned" };
    SerializedProperty depthModeProp;
    // SerializedProperty usePlaybackProp;

    void OnEnable()
    {
        // bind variables with SerializedProperty
        depthModeProp = serializedObject.FindProperty("depthMode");
        // usePlaybackProp = serializedObject.FindProperty("usePlayback");
    }

    public override void OnInspectorGUI()
    {
        // call Update
        serializedObject.Update();

        // Popup
        int selected = Array.IndexOf(options, depthModeProp.stringValue);
        if (selected < 0) selected = 0;

        selected = EditorGUILayout.Popup("Depth Mode", selected, options);
        depthModeProp.stringValue = options[selected];

        // Toggle
        // EditorGUILayout.PropertyField(usePlaybackProp);

        // apply properties
        serializedObject.ApplyModifiedProperties();
    }
}
