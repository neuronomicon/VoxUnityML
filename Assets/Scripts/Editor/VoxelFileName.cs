/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelFileName.cs]
 * Author       : [Y.S.Shim]
 * Date Created : 2026-08-15
 * 
 * [WARNING] 
 * The code in this file may not be copied, modified, distributed, or used for 
 * commercial purposes without prior authorization. Plagiarism or intentional 
 * removal of copyright notices may result in legal consequences.
 * ==============================================================================
 */

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

[CustomEditor(typeof(VoxelRobotInstance))]
public class VoxelRobotInstanceEditor : Editor {
    private string[] voxFiles;
    private int selectedIndex = 0;

    private void OnEnable() {
        // StreamingAssets 폴더 경로
        string path = Application.streamingAssetsPath;

        // 디렉토리가 없으면 빈 배열 처리
        if (!Directory.Exists(path)) {
            voxFiles = new string[0];
            return;
        }

        // 폴더 내의 모든 .vox 파일 이름만 가져오기
        voxFiles = Directory.GetFiles(path, "*.vox")
                            .Select(Path.GetFileName)
                            .ToArray();
    }

    public override void OnInspectorGUI() {
        // 기존 필드들(robotIndex 등) 그리기
        DrawDefaultInspector();

        VoxelRobotInstance script = (VoxelRobotInstance)target;

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Voxel File Settings", EditorStyles.boldLabel);

        if (voxFiles == null || voxFiles.Length == 0) {
            EditorGUILayout.HelpBox("No *.vox in StreamingAssets folder", MessageType.Warning);
        } else {
            // 현재 선택된 파일의 인덱스 찾기
            selectedIndex = Mathf.Max(0, System.Array.IndexOf(voxFiles, script.selectedVoxFileName));

            // 팝업(드롭다운)으로 파일 목록 표시
            selectedIndex = EditorGUILayout.Popup("Voxel File", selectedIndex, voxFiles);

            // 선택된 파일명 저장
            string newSelectedFile = voxFiles[selectedIndex];
            if (script.selectedVoxFileName != newSelectedFile) {
                script.selectedVoxFileName = newSelectedFile;
                EditorUtility.SetDirty(script); // 씬에 변경사항 저장 알림
            }
        }
    }
}
#endif