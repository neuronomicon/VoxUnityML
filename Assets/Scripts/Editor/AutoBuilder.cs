using UnityEngine;
using UnityEditor;
using System.IO;

public class AutoBuilder
{
    // 유니티 상단 메뉴에 'Voxel Engine > Build Both (Client & Server)' 버튼을 생성합니다.
    [MenuItem("Vox-AutoBuild/Build Both (Client & Server)")]
    public static void BuildBoth()
    {
        // 1. 빌드 결과물이 저장될 최상위 폴더 경로 설정 (프로젝트 폴더 바로 바깥에 생성됨)
        string basePath = Path.GetFullPath(Application.dataPath + "/../../VoxUnityML_Auto_Builds");
        
        // 폴더가 없으면 생성
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        string[] scenes = GetScenePaths();
        Debug.Log("🚀 [AutoBuilder] 듀얼 빌드 자동화 프로세스 시작...");



        // ==========================================
        // 1. 그래픽이 보이는 Window 모드 빌드
        // ==========================================
        string graphicPath = basePath + "/GraphicMode/VoxelSim_Graphics.exe";
        Debug.Log("⏳ [AutoBuilder] 1/2: 그래픽(Window) 모드 빌드 중...");
        
        BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = graphicPath,
            target = BuildTarget.StandaloneWindows64,
            // 그래픽이 렌더링되는 일반 플레이어 모드로 명시
            subtarget = (int)StandaloneBuildSubtarget.Player 
        });


        // ==========================================
        // 2. 화면 없는 Server 모드 (Headless) 빌드
        // ==========================================
        string serverPath = basePath + "/ServerMode/VoxelSim_Server.exe";
        Debug.Log("⏳ [AutoBuilder] 2/2: 서버(Server) 모드 빌드 중...");
        
        BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = serverPath,
            target = BuildTarget.StandaloneWindows64,
            // 최신 유니티 문법: 서버(화면 없음) 모드로 명시
            subtarget = (int)StandaloneBuildSubtarget.Server 
        });



        // ==========================================
        // 🌟 마무리: 에디터 상태를 완벽하게 그래픽 모드로 강제 고정
        // ==========================================
        
        // 1. 서브타겟을 Server에서 일반 Player 모드로 확실하게 되돌림
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        
        // 2. 플랫폼 타겟 복구
        UnityEditor.EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.BuildTargetGroup.Standalone, 
                                                                    UnityEditor.BuildTarget.StandaloneWindows64);



        Debug.Log("✅ [AutoBuilder] 그래픽 빌드와 서버 빌드가 모두 완료되었습니다!");
        
        // 빌드가 완료된 폴더를 자동으로 열어줌
        EditorUtility.RevealInFinder(basePath); 
    }

    // Build Settings에 등록된 씬 경로들을 가져오는 헬퍼 함수
    private static string[] GetScenePaths()
    {
        var scenes = new System.Collections.Generic.List<string>();
        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
        {
            if (EditorBuildSettings.scenes[i].enabled)
            {
                scenes.Add(EditorBuildSettings.scenes[i].path);
            }
        }
        return scenes.ToArray();
    }
}