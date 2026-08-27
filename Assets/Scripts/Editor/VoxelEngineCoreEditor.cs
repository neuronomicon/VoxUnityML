
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// VoxelEngineCore 스크립트의 인스펙터 창을 커스터마이징합니다.
[CustomEditor(typeof(VoxelEngineCore))]
public class VoxelEngineCoreEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 최신 데이터 갱신
        serializedObject.Update();

        SerializedProperty dllPathProp = serializedObject.FindProperty("dllFolderPath");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("⚙️ C++ DLL Folder Setting", EditorStyles.boldLabel);

        // 2. 가로로 텍스트 칸과 버튼을 나란히 배치합니다.
        EditorGUILayout.BeginHorizontal();
        
        // 경로 텍스트 표시 (여전히 수동 입력도 가능)
        EditorGUILayout.PropertyField(dllPathProp, new GUIContent("DLL Path"));

        // '폴더 찾기' 버튼 생성
        if (GUILayout.Button("DLL-Dir", GUILayout.Width(50)))
        {
            // 🌟 1. 팝업을 띄우기 전, 현재 유니티 프로젝트의 작업 폴더(Working Directory) 위치를 기억합니다.
            string originalDirectory = System.IO.Directory.GetCurrentDirectory();

            string defaultPath = dllPathProp.stringValue;
            if (string.IsNullOrEmpty(defaultPath)) 
            {
                defaultPath = Application.dataPath;
            }

            // 윈도우 폴더 선택 팝업창 띄우기
            string selectedPath = EditorUtility.OpenFolderPanel("Select C++ DLL Folder", defaultPath, "");

            // 🌟 2. 팝업이 닫힌 직후, 작업 폴더 위치를 원래 유니티 경로로 강제 복구합니다! (핵심)
            System.IO.Directory.SetCurrentDirectory(originalDirectory);

            // 사용자가 취소(X)를 누르지 않고 폴더를 선택했다면 경로 업데이트
            if (!string.IsNullOrEmpty(selectedPath))
            {
                dllPathProp.stringValue = selectedPath;
            }
        }

        EditorGUILayout.EndHorizontal();

        // 3. dllFolderPath 외에 VoxelEngineCore에 있는 나머지 변수(is_ml_agent 등)들을 정상적으로 그려줍니다.
        DrawPropertiesExcluding(serializedObject, "dllFolderPath", "m_Script");

        // 4. 변경 사항 저장
        serializedObject.ApplyModifiedProperties();
    }
}
#endif