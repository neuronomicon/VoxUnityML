/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [AutoBuilder.cs]
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
using System.IO;
using System.Collections.Generic;

public class AutoBuilder : EditorWindow
{
    private string[] scenePaths;
    private string[] sceneNames;
    private int selectedSceneIndex = 0;

    [MenuItem("VoxUnityML/Auto Build (Client & Server)")]
    public static void ShowWindow()
    {
        GetWindow<AutoBuilder>("Auto Builder");
    }

    private void OnEnable()
    {
        RefreshScenes();
    }

    private void RefreshScenes()
    {
        List<string> paths = new List<string>();
        List<string> names = new List<string>();

        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (File.Exists(scene.path))
            {
                paths.Add(scene.path);
                names.Add(Path.GetFileNameWithoutExtension(scene.path));
            }
        }

        scenePaths = paths.ToArray();
        sceneNames = names.ToArray();
    }

    private void OnGUI()
    {
        GUILayout.Label("Select the training scene to build", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (sceneNames == null || sceneNames.Length == 0)
        {
            EditorGUILayout.HelpBox("No scenes registered in Build Settings. Please drag and drop scenes into the [File -> Build Settings] window.", MessageType.Warning);
            if (GUILayout.Button("Refresh")) RefreshScenes();
            return;
        }

        selectedSceneIndex = EditorGUILayout.Popup("Target Scene", selectedSceneIndex, sceneNames);

        EditorGUILayout.Space();

        if (GUILayout.Button("Dual Build Selected Scene (Graphic + Server)", GUILayout.Height(40)))
        {
            BuildSelectedScene(scenePaths[selectedSceneIndex], sceneNames[selectedSceneIndex]);
        }
    }

    private void BuildSelectedScene(string scenePath, string sceneName)
    {
        string basePath = Path.GetFullPath(Application.dataPath + "/../../VoxUnityML_Auto_Builds/" + sceneName);

        if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);

        string[] scenesToBuild = new string[] { scenePath };
        Debug.Log($"🚀 [AutoBuilder] Starting dual build for scene '{sceneName}'...");

        // ==========================================
        // 1. 그래픽(Window) 모드 빌드
        // ==========================================
        string graphicPath = basePath + "/GraphicMode/VoxelSim_Graphics.exe";
        Debug.Log("⏳ [AutoBuilder] 1/2: Building Graphic (Window) mode...");
        BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenesToBuild,
            locationPathName = graphicPath,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player 
        });
        
        // 🌟 빌드 직후 StreamingAssets 복사
        CopyStreamingAssetsToBuild(graphicPath);

        // ==========================================
        // 2. 서버(Server) 모드 빌드
        // ==========================================
        string serverPath = basePath + "/ServerMode/VoxelSim_Server.exe";
        Debug.Log("⏳ [AutoBuilder] 2/2: Building Server mode...");
        BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenesToBuild,
            locationPathName = serverPath,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Server 
        });
        
        // 🌟 빌드 직후 StreamingAssets 복사
        CopyStreamingAssetsToBuild(serverPath);
        
        Debug.Log($"✅ [AutoBuilder] Build and asset copying for scene '{sceneName}' completed!");


        // 1. 서브타겟을 Server에서 일반 Player 모드로 확실하게 되돌림
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;        
        // 2. 플랫폼 타겟 복구
        UnityEditor.EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.BuildTargetGroup.Standalone, 
                                                                    UnityEditor.BuildTarget.StandaloneWindows64);


        EditorUtility.RevealInFinder(basePath); 
    }

    // ==========================================
    // 📁 에셋 자동 복사 유틸리티 함수들
    // ==========================================
    
    // 대상 실행파일(.exe) 위치를 기준으로 Assets/StreamingAssets 폴더를 생성하고 원본을 복사합니다.
    private void CopyStreamingAssetsToBuild(string exePath)
    {
        string exeDirectory = Path.GetDirectoryName(exePath);
        string targetStreamingAssetsDir = Path.Combine(exeDirectory, "Assets", "StreamingAssets");
        string sourceStreamingAssetsDir = Application.streamingAssetsPath;

        if (Directory.Exists(sourceStreamingAssetsDir))
        {
            CopyDirectory(sourceStreamingAssetsDir, targetStreamingAssetsDir);
            Debug.Log($"📁 Copy completed: {targetStreamingAssetsDir}");
        }
        else
        {
            Debug.LogWarning("⚠️ Source StreamingAssets folder does not exist. Skipping copy.");
        }
    }

    // 디렉토리 내부의 모든 파일과 하위 폴더를 재귀적으로 복사합니다 (.meta 파일 제외).
    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        DirectoryInfo dirInfo = new DirectoryInfo(sourceDir);

        // 파일 복사 (.meta 제외)
        foreach (FileInfo file in dirInfo.GetFiles())
        {
            if (file.Extension.ToLower() == ".meta") continue;
            
            string targetFilePath = Path.Combine(destDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        // 하위 폴더 복사
        foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
        {
            string newDestDir = Path.Combine(destDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestDir);
        }
    }
}