
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
        GetWindow<ReplacePrefabTool>("프리팹 복구 툴");
    }

    void OnGUI()
    {
        GUILayout.Label("1. 프로젝트 창의 정상 프리팹을 아래 빈칸에 넣으세요.", EditorStyles.wordWrappedLabel);
        newPrefab = (GameObject)EditorGUILayout.ObjectField("정상 프리팹", newPrefab, typeof(GameObject), false);

        EditorGUILayout.Space();
        
        // 🌟 이름 설정 UI 추가
        GUILayout.Label("2. 생성될 오브젝트들의 기본 이름을 정해주세요.", EditorStyles.wordWrappedLabel);
        baseName = EditorGUILayout.TextField("새 이름 (Base Name)", baseName);

        EditorGUILayout.Space();
        GUILayout.Label("3. 하이어라키 창에서 붉은색 오브젝트들을 모두 선택하세요.", EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space();
        if (GUILayout.Button("선택한 오브젝트 교체 및 이름 일괄 변경!", GUILayout.Height(40)))
        {
            if (newPrefab == null)
            {
                EditorUtility.DisplayDialog("경고", "먼저 교체할 원본 프리팹을 드래그해서 넣어주세요!", "확인");
                return;
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("경고", "하이어라키 창에서 교체할 오브젝트들을 먼저 선택해주세요!", "확인");
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
            
            Debug.Log($"총 {selectedObjects.Length}개의 프리팹 복구 및 이름 정리가 완료되었습니다!");
        }
    }
}