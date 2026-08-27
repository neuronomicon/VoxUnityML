
using UnityEngine;
using UnityEditor;

public class ReplacePrefabTool : EditorWindow
{
    public GameObject newPrefab;
    
    // 🌟 일괄 변경할 기본 이름 설정
    public string baseName = "TrainingArea"; 

    
    [MenuItem("VoxUnityML/Prefab Restore Tool")]
    public static void ShowWindow()
    {
        GetWindow<ReplacePrefabTool>("Prefab Restore Tool");
    }

    void OnGUI()
    {
        GUILayout.Label("1. Drop the valid prefab from the Project window into the slot below.", EditorStyles.wordWrappedLabel);
        newPrefab = (GameObject)EditorGUILayout.ObjectField("Valid Prefab", newPrefab, typeof(GameObject), false);

        EditorGUILayout.Space();
        
        // 🌟 이름 설정 UI 추가
        GUILayout.Label("2. Set the base name for the generated objects.", EditorStyles.wordWrappedLabel);
        baseName = EditorGUILayout.TextField("New Name (Base Name)", baseName);

        EditorGUILayout.Space();
        GUILayout.Label("3. Select all the red objects in the Hierarchy window.", EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space();
        if (GUILayout.Button("Replace Selected Objects & Rename All!", GUILayout.Height(40)))
        {
            if (newPrefab == null)
            {
                EditorUtility.DisplayDialog("Warning", "Please drag and drop the source prefab first!", "OK");
                return;
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Warning", "Please select the objects to replace in the Hierarchy window first!", "OK");
                return;
            }

            // 🌟 하이어라키 창의 배치 순서(위에서 아래)대로 오브젝트들을 정렬하여 번호가 꼬이지 않게 합니다.
            System.Array.Sort(selectedObjects, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject oldGo = selectedObjects[i];

                GameObject newGo = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);
                
                newGo.transform.SetParent(oldGo.transform.parent);
                newGo.transform.localPosition = oldGo.transform.localPosition;
                newGo.transform.localRotation = oldGo.transform.localRotation;
                newGo.transform.localScale = oldGo.transform.localScale;
                newGo.transform.SetSiblingIndex(oldGo.transform.GetSiblingIndex());

                // 🌟 핵심 변경: 예쁜 이름과 함께 00, 01, 02 순서로 번호를 매겨줍니다!
                newGo.name = $"{baseName}_{i:D2}";

                Undo.RegisterCreatedObjectUndo(newGo, "Replace Prefab");
                Undo.DestroyObjectImmediate(oldGo);
            }
            
            Debug.Log($"A total of {selectedObjects.Length} prefabs have been successfully restored and renamed!");
        }
    }
}