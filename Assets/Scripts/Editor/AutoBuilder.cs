/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 *
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [AutoBuilder.cs]
 * Author       : [Y.S.Shim]
 * Date Created : 2026-08-15
 * Revised      : 2026-09-14  (build-report check / clean output / state restore)
 *
 * [WARNING]
 * The code in this file may not be copied, modified, distributed, or used for
 * commercial purposes without prior authorization. Plagiarism or intentional
 * removal of copyright notices may result in legal consequences.
 * ==============================================================================
 *
 * 선택한 씬 하나를 Graphic(Player) / Server 두 모드로 연속 빌드합니다.
 * 모든 빌드 설정은 현재 전역 설정을 그대로 사용합니다.
 *   - Player Settings        : Project Settings > Player
 *   - Development Build 등   : Build Profiles(또는 Build Settings) 창의 체크박스
 *   - 씬 목록                : 이 창에서 고른 씬 1개만 빌드
 */

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class AutoBuilder : EditorWindow
{
    // ─────────────────────────────────────────────────────────────
    // 상수
    // ─────────────────────────────────────────────────────────────
    private const string OUTPUT_ROOT_NAME = "VoxUnityML_Auto_Builds";   // 삭제 안전장치에 사용
    private const string GRAPHIC_DIR = "GraphicMode";
    private const string SERVER_DIR = "ServerMode";
    private const string GRAPHIC_EXE = "VoxelSim_Graphics.exe";
    private const string SERVER_EXE = "VoxelSim_Server.exe";

    private const string PREF_SCENE = "VoxUnityML.AutoBuilder.SceneIndex";

    // ─────────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────────
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
        selectedSceneIndex = EditorPrefs.GetInt(PREF_SCENE, 0);
        if (scenePaths != null && selectedSceneIndex >= scenePaths.Length) selectedSceneIndex = 0;
    }

    private void OnDisable()
    {
        EditorPrefs.SetInt(PREF_SCENE, selectedSceneIndex);
    }

    // ─────────────────────────────────────────────────────────────
    // 씬 목록
    // ─────────────────────────────────────────────────────────────
    private void RefreshScenes()
    {
        var paths = new List<string>();
        var names = new List<string>();

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

        if (selectedSceneIndex >= scenePaths.Length) selectedSceneIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        GUILayout.Label("Select the training scene to build", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (sceneNames == null || sceneNames.Length == 0)
        {            
            EditorGUILayout.HelpBox("No scenes are registered in Build Settings.\n" 
                                    + "Add scenes to the Scene List via File > Build Profiles (or Build Settings).",
                                        MessageType.Warning);

            if (GUILayout.Button("Refresh")) RefreshScenes();
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            selectedSceneIndex = EditorGUILayout.Popup("Target Scene", selectedSceneIndex, sceneNames);
            if (GUILayout.Button("↻", GUILayout.Width(24))) RefreshScenes();
        }

        EditorGUILayout.Space();

        // 현재 전역 설정을 그대로 쓴다는 것을 명시적으로 보여줌
        EditorGUILayout.HelpBox("Building with current global settings:\n" 
                                + $"  • Development Build : {(EditorUserBuildSettings.development ? "ON" : "OFF")}\n" 
                                + $"  • Run In Background : {(PlayerSettings.runInBackground ? "ON" : "OFF (Training will pause if the window loses focus)")}\n" 
                                +  "  • Other player settings are loaded from Project Settings > Player\n\n" 
                                +  "The output folder will be cleared automatically before building.",
                                PlayerSettings.runInBackground ? MessageType.Info : MessageType.Warning);

        EditorGUILayout.Space();

        if (GUILayout.Button("Dual Build Selected Scene (Graphic + Server)", GUILayout.Height(40)))
        {
            BuildSelectedScene(scenePaths[selectedSceneIndex], sceneNames[selectedSceneIndex]);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 빌드 오케스트레이션
    // ─────────────────────────────────────────────────────────────
    private void BuildSelectedScene(string scenePath, string sceneName)
    {
        EditorPrefs.SetInt(PREF_SCENE, selectedSceneIndex);

        string basePath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", OUTPUT_ROOT_NAME, sceneName));

        // Build Profiles / Build Settings 창의 체크박스를 그대로 반영
        BuildOptions opts = BuildOptions.None;
        if (EditorUserBuildSettings.development) opts |= BuildOptions.Development;
        if (EditorUserBuildSettings.allowDebugging) opts |= BuildOptions.AllowDebugging;
        if (EditorUserBuildSettings.connectProfiler) opts |= BuildOptions.ConnectWithProfiler;
        if (EditorUserBuildSettings.buildWithDeepProfilingSupport) opts |= BuildOptions.EnableDeepProfilingSupport;
        if (EditorUserBuildSettings.waitForPlayerConnection) opts |= BuildOptions.WaitForPlayerConnection;

        // 빌드 중 변경되는 에디터 상태 백업
        var savedSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

        Debug.Log($"🚀 [AutoBuilder] Starting dual build for '{sceneName}' (options={opts})");

        try
        {
            // ── 1/2 그래픽(Window) 모드 ──
            if (!BuildOne(scenePath, Path.Combine(basePath, GRAPHIC_DIR), GRAPHIC_EXE,
                          StandaloneBuildSubtarget.Player, opts, "1/2 Graphic"))
            {
                return;     // 실패하면 서버 빌드로 넘어가지 않음
            }

            // ── 2/2 서버(Server) 모드 ──
            if (!BuildOne(scenePath, Path.Combine(basePath, SERVER_DIR), SERVER_EXE,
                          StandaloneBuildSubtarget.Server, opts, "2/2 Server"))
            {
                return;
            }
            
            Debug.Log($"✅ [AutoBuilder] Dual build completed for '{sceneName}' -> {basePath}");
            EditorUtility.RevealInFinder(basePath);
        }
        finally
        {
            // 예외가 나도 에디터가 Server 서브타겟에 갇히지 않도록 반드시 원복
            EditorUserBuildSettings.standaloneBuildSubtarget = savedSubtarget;            
            Debug.Log($"↩ [AutoBuilder] Restored subtarget: {savedSubtarget}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 개별 빌드 1회
    // ─────────────────────────────────────────────────────────────
    private bool BuildOne(string scenePath, string outDir, string exeName,
                          StandaloneBuildSubtarget subtarget,
                          BuildOptions opts, string label)
    {
        // ── 출력 폴더 정리 ──────────────────────────────────────
        // Unity 는 출력 디렉터리를 청소하지 않습니다. 이전 빌드의 잔여 파일이 남으면
        // UnityPlayer.dll 과 globalgamemanagers 의 버전이 어긋나
        // "Failed to load PlayerSettings (internal index #0)" 오류가 발생합니다.
        if (Directory.Exists(outDir))
        {
            if (!IsSafeToDelete(outDir))
            {
                Debug.LogError($"❌ [AutoBuilder] Aborted due to unsafe deletion path: {outDir}");
                return false;
            }
            Directory.Delete(outDir, true);
        }
        Directory.CreateDirectory(outDir);

        string exePath = Path.Combine(outDir, exeName);
        Debug.Log($"⏳ [AutoBuilder] Building {label}... -> {exePath}");

        // 일부 버전은 EditorUserBuildSettings 쪽도 참조하므로 함께 지정 (finally 에서 원복)
        EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)subtarget,
            options = opts
        });

        // ── 결과 확인 ───────────────────────────────────────────
        var s = report.summary;

        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError(
                $"❌ [AutoBuilder] {label} Failed — result={s.result}, " +
                $"errors={s.totalErrors}, warnings={s.totalWarnings}");

            // 어느 단계에서 터졌는지 남겨둠
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"   ↳ [{step.name}] {msg.content}");
                }
            }
            return false;
        }

        Debug.Log($"✔ [AutoBuilder] {label} Succeeded — " +
                  $"{s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:F1}sec, " +
                  $"warnings={s.totalWarnings}");

        CopyStreamingAssetsToBuild(exePath);
        return true;
    }

    // 출력 루트 이름이 경로에 포함될 때만 재귀 삭제를 허용하는 안전장치
    private static bool IsSafeToDelete(string dir)
    {
        string full = Path.GetFullPath(dir).Replace('\\', '/');
        return full.Contains("/" + OUTPUT_ROOT_NAME + "/");
    }

    // ─────────────────────────────────────────────────────────────
    // StreamingAssets 복사
    //   C++ DLL 이 exe 기준 상대경로 "Assets/StreamingAssets/..." 로 읽으므로 유지.
    //   (Unity 도 <Product>_Data/StreamingAssets 에 자동 복사하지만 그건 별개 경로)
    // ─────────────────────────────────────────────────────────────
    private void CopyStreamingAssetsToBuild(string exePath)
    {
        string exeDirectory = Path.GetDirectoryName(exePath);
        string targetDir = Path.Combine(exeDirectory, "Assets", "StreamingAssets");
        string sourceDir = Application.streamingAssetsPath;

        if (!Directory.Exists(sourceDir))
        {
            Debug.LogWarning("⚠️ [AutoBuilder] StreamingAssets source folder does not exist, skipping copy.");
            return;
        }

        CopyDirectory(sourceDir, targetDir);
        Debug.Log($"📁 [AutoBuilder] StreamingAssets copy completed → {targetDir}");
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var dirInfo = new DirectoryInfo(sourceDir);

        foreach (FileInfo file in dirInfo.GetFiles())
        {
            if (file.Extension.ToLower() == ".meta") continue;
            file.CopyTo(Path.Combine(destDir, file.Name), true);
        }

        foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
        {
            CopyDirectory(subDir.FullName, Path.Combine(destDir, subDir.Name));
        }
    }
}
