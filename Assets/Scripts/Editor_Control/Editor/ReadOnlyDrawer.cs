/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [ReadOnlyDrawer.cs]
 * Author       : [Y.S.Shim]
 * Date Created : 2026-08-15
 * 
 * [WARNING] 
 * The code in this file may not be copied, modified, distributed, or used for 
 * commercial purposes without prior authorization. Plagiarism or intentional 
 * removal of copyright notices may result in legal consequences.
 * ==============================================================================
 */

using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 1. 유니티의 원래 색상을 기억해 둡니다. (매우 중요)
        Color originalColor = GUI.contentColor;

        // 2. 원하는 색상으로 변경합니다. 
        // 비활성화(회색화) 되면서 약간 어두워지기 때문에 가급적 밝은 색을 추천합니다.
        GUI.contentColor = Color.cyan; // 청록색 (Color.yellow, Color.green 등 사용 가능)

        // 3. 변수를 비활성화 상태로 화면에 그립니다.
        GUI.enabled = false;
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = true;

        // 4. 이 밑에 있는 다른 멀쩡한 변수들까지 색이 바뀌는 것을 막기 위해 원래 색으로 복구합니다.
        GUI.contentColor = originalColor;
    }
}