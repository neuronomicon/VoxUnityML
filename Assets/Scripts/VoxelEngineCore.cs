/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelEngineCore.cs]
 * Author       : [Y.S.Shim]
 * Date Created : 2026-08-15
 * 
 * [WARNING] 
 * The code in this file may not be copied, modified, distributed, or used for 
 * commercial purposes without prior authorization. Plagiarism or intentional 
 * removal of copyright notices may result in legal consequences.
 * ==============================================================================
 */

using System;
using System.IO;
using UnityEngine;
using System.Runtime.InteropServices;

using Unity.MLAgents;          // 🌟 추가: DecisionRequester, Agent를 인식하기 위해 필수!
using Unity.MLAgents.Policies; // 🌟 추가: BehaviorParameters를 인식하기 위해 필수!

using TMPro; // Button 텍스트 변경용

public class VoxelEngineCore : MonoBehaviour
{
    // 1. SetDllDirectory 함수 임포트 (Kernel32)
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    // DLL 함수 임포트
    const string DLL_NAME = VoxelDllConfig.DLL_NAME;

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    //public static extern void Init_Voxel_Unity(int nRobot, int[] nRobotThr, int VOXEL_PARSE_KIND, int MAX_NUM_OSC, bool IS_RL );
    public static extern void Init_Voxel_Unity(int nRobot, IntPtr nRobotThr, int VOXEL_PARSE_KIND, int MAX_NUM_OSC, int IS_RL );
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void End_Voxel_Unity();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetSimulationPlayState(int is_Play);

    // 🌟 [추가] C++로 Headless 모드 여부를 전송하는 함수
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Set_Headless_Mode(int isHeadless);

    // C++ DLL에서 만든 파이프 연결 함수를 가져옵니다.
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ConnectConsoleOutput();
    

    [Header("CPPN/CE & Other properties")]    
    public bool is_ml_agent = true;

    
    
    //[ReadOnly] public string dllFolderPath = "C:/Z_SHIM/PROG_MAJOR/PROJECT/NeuronomicoN_VS2022/ProjectDLL";
    [ReadOnly] public string dllFolderPath = "C:/Z_SHIM";

    [Tooltip("Currently loaded DLL file. (Change in VoxelDllConfig.cs)")]
    [ReadOnly] public string activeDllName;


    [Header("CPPN/CE & Other properties")]    
    [ReadOnly] public int num_robots = 0;           // nRobotThr    
    [ReadOnly] public int voxelParseKind = 0;         // VOXEL_PARSE_KIND    
    [ReadOnly] public int maxNumOscillators = 2;    // MAX_NUM_OSC



    // [추가] 중복 해제 방지용 플래그
    private bool isDllCleanedUp = false;

    // R2: C++이 스레드 배열 포인터를 보관해도 안전하도록 영구 고정
    private GCHandle threadArrayHandle;
    private int[] threadsArrayKeepAlive;


    // 🌟 [추가] 로봇 데이터를 전송할 빌더 스크립트 참조
    [Header("Robot Builder Reference")]
    public VoxelRobotBuilder robotBuilder;


    /// 만약 RL 사용하지 않으면 (EA 등 다른 시스템) 관련 컴포넌트를 체크해제
    /// ML-Agents 관련 모든 컴포넌트의 체크박스를 일괄 활성화/비활성화하는 함수
    public void ApplyMLAgentComponentsState(bool active)
    {
        // 1. 같은 오브젝트(VoxelEngineManager)에 붙은 VoxelRLManager 켜고 끄기
        var rlManager = GetComponent<VoxelRLManager>();
        if (rlManager != null) rlManager.enabled = active;

        // 2. 씬 내의 모든 로봇에 붙은 BehaviorParameters 켜고 끄기
        var behaviors = FindObjectsByType<BehaviorParameters>(FindObjectsSortMode.None);
        foreach (var b in behaviors) b.enabled = active;

        // 3. 씬 내의 모든 에이전트(VoxelRobotAgent 포함) 켜고 끄기
        var agents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        foreach (var a in agents) a.enabled = active;

        // 4. 씬 내의 모든 DecisionRequester 켜고 끄기
        var requesters = FindObjectsByType<DecisionRequester>(FindObjectsSortMode.None);
        foreach (var r in requesters) r.enabled = active;

        Debug.Log($"[VoxelEngineCore] All ML-Agents checkboxes set: {(active ? "Enabled(ON)" : "Disabled(OFF)")}");
    } 

    // 유니티 에디터에서 스크립트가 로드되거나 값이 바뀔 때 자동으로 호출되는 함수
    void OnValidate()
    {
        // VoxelDllConfig에 적힌 상수 값을 인스펙터 변수에 자동으로 채워줍니다.
        activeDllName = VoxelDllConfig.DLL_NAME;
        if (robotBuilder == null) robotBuilder = GetComponent<VoxelRobotBuilder>();

        ApplyMLAgentComponentsState(is_ml_agent);
    }


    void Awake()
    {
        // [2026-09-21 PATCH]
        //   vSync 가 켜져 있으면 targetFrameRate 가 무시되고 모니터 주사율에 묶인다.
        //   머신마다 시뮬 배속이 달라지므로 어떤 모드에서도 0 으로 고정한다.
        QualitySettings.vSyncCount = 0;

        // 여기서는 일단 풀어 둔다. 실제 캡은 Start() 의 ApplyFrameRatePolicy() 에서
        // (헤드리스 여부 / 파이썬 연결 여부를 확인한 뒤) 결정한다.
        Application.targetFrameRate = -1;


                
        // 디버그 로그(일반 메시지) 출력 시 스택 트레이스(호출 경로)를 표시하지 않음
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);        
        // (선택) 경고나 에러도 깔끔하게 메시지만 보고 싶다면 아래 것도 추가
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);

        
        // 1. 유니티 C#이 윈도우 터미널 창을 가장 먼저 띄웁니다.
        WindowsConsole.ShowConsole();        
        
       
        // 2. DLL 경로 설정
        if (Directory.Exists(dllFolderPath))
        {
            if (SetDllDirectory(dllFolderPath)) Debug.Log($"[VoxelEngineCore] Set DLL path: {dllFolderPath}");
            else                                Debug.LogError("[VoxelEngineCore] DLL path failed!");
        }
        else
        {
            Debug.LogError($"[VoxelEngineCore] DLL folder does not exist: {dllFolderPath}");
        }


        // 2. 창이 성공적으로 띄워진 직후, C++에게 파이프를 연결하라고 명령합니다.
    #if !UNITY_SERVER
        ConnectConsoleOutput();
    #endif

        // ==========================================================
        // 2. 씬 내의 훈련장(총괄: VoxelPhysicsManager)들을 찾아서 인덱스 체계적 발급
        // ==========================================================
        VoxelPhysicsManager[] allAreas = FindObjectsByType<VoxelPhysicsManager>(FindObjectsSortMode.None);

        // 🌟 [추가된 마법의 한 줄] 하이어라키 창에 배치된 순서(위에서 아래)대로 배열을 정렬합니다!
        System.Array.Sort(allAreas, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        
        int globalRobotCounter = 0;

        foreach (var area in allAreas)
        {
            // 각 훈련장이 자신의 로봇들에게 번호를 매기도록 지시합니다.
            area.InitializeArea(ref globalRobotCounter); 
        }
        num_robots = globalRobotCounter;
        Debug.Log($"[VoxelEngineCore] Total {num_robots} robots indexed sequentially across {allAreas.Length} training areas.");


        // ==========================================================
        // 🌟 [추가된 핵심 흐름] 엔진 초기화 전, 로봇 설계도부터 C++로 전송하여 캐싱!
        // ==========================================================

        if (robotBuilder == null) robotBuilder = GetComponent<VoxelRobotBuilder>();

        if (robotBuilder != null)
        {
            Debug.Log("[VoxelEngineCore] Robot data transfer before C++ engine core startup..");            
            robotBuilder.GatherRobotsAndSendToCpp();
        }
        else
        {
            Debug.LogWarning("[VoxelEngineCore] ⚠️ VoxelRobotBuilder not connected! Confirm Unity Inspector Window.");
        }
        
        // ==========================================================
        // 4. C++ 코어 엔진 기동!
        // ==========================================================
        
        //num_robots = robotBuilder != null ? robotBuilder.finalRobotCount : 1;

    /*    // 🌟 수정됨: VoxelRobotBuilder 스크립트에 정의된 리스트 데이터에 직접 접근하여 변환
        int[] threadsArray = (robotBuilder != null) ? robotBuilder.finalThreadArray : new int[] { 10 };

        // 🌟 수정할 로그 코드
        Debug.Log($"[VoxelEngineCore] Loaded DLL Name: {VoxelDllConfig.DLL_NAME}");
        Debug.Log($"[VoxelEngineCore] Num Robots: {num_robots}, Vox Parsing Mode: {voxelParseKind}");
    
        Init_Voxel_Unity(num_robots, threadsArray, voxelParseKind, maxNumOscillators, is_ml_agent);
    */


        threadsArrayKeepAlive = (robotBuilder != null) ? robotBuilder.finalThreadArray : new int[] { 10 };

        // R2: 호출 반환 후에도 C++이 포인터를 계속 참조할 수 있으므로 영구 핀 고정
        if (threadArrayHandle.IsAllocated) threadArrayHandle.Free();
        threadArrayHandle = GCHandle.Alloc(threadsArrayKeepAlive, GCHandleType.Pinned);

        Debug.Log($"[VoxelEngineCore] Loaded DLL Name: {VoxelDllConfig.DLL_NAME}");
        Debug.Log($"[VoxelEngineCore] Num Robots: {num_robots}, Vox Parsing Mode: {voxelParseKind}");

        // R10: C++ 워커 스레드가 시작되기 전에 headless 플래그부터 확정
        SetSimulationPlayState(0);


        // [2026-09-21 PATCH] 헤드리스 판정을 런타임으로.
        //   UNITY_SERVER 는 Dedicated Server 빌드 타겟에서만 정의된다.
        //   ML-Agents 의 no_graphics:true 는 일반 빌드를 -batchmode -nographics 로
        //   실행할 뿐이므로, 컴파일 심볼만 보면 화면 없이 도는데도 렌더 패킹을 계속한다.
        bool isHeadless = IsHeadlessRuntime();
        Set_Headless_Mode(isHeadless ? 1 : 0);
        Debug.Log(isHeadless
            ? "[VoxelEngineCore] Headless detected: C++ Pack_Render_Data() OFF"
            : "[VoxelEngineCore] Graphics detected: C++ Pack_Render_Data() ON");



        // R7: bool → int (Win32 BOOL 4바이트 vs MSVC bool 1바이트 불일치 제거)
        Init_Voxel_Unity(num_robots, threadArrayHandle.AddrOfPinnedObject(),
                        voxelParseKind, maxNumOscillators, is_ml_agent ? 1 : 0);

#if UNITY_SERVER
    ToggleSimulationButton();   // 엔진 기동 후에 실행되어야 함
#endif

        Debug.Log("[VoxelEngineCore] DLL Engine Initialization Complete.");

    
        


    // ==========================================================
        // 5. C++ 기동 후 데이터 역로드 (기존 Builder의 Start 역할)
        // ==========================================================
        if (robotBuilder != null) robotBuilder.LoadDataFromCpp();

        // ==========================================================
        // 6. RL 매니저 초기화 (기존 RLManager의 Start 역할)
        // ==========================================================
        var rlManager = GetComponent<VoxelRLManager>();
        if (rlManager != null) rlManager.InitializeRL();

    

    }


    // [2026-09-21 PATCH]
    public static bool IsHeadlessRuntime()
    {
    #if UNITY_SERVER
        return true;
    #else
        // -batchmode / -nographics 로 실행된 일반 빌드도 잡아낸다.
        return Application.isBatchMode
            || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
    #endif
    }


    [Header("Frame Rate Policy")]
    [Tooltip("파이썬 없이 추론을 눈으로 볼 때의 fps. 1000 / (stepsPerSimulationCycle x 1ms) 이 실시간.")]
    public int inferenceWatchFps = 50;
    [Tooltip("is_ml_agent = false 자유 주행에서의 렌더 fps. 물리 속도와 무관하므로 캡은 공짜.")]
    public int freeRunRenderFps = 60;

    public void ApplyFrameRatePolicy()
    {
        QualitySettings.vSyncCount = 0;

        bool headless = IsHeadlessRuntime();
        bool python   = is_ml_agent
                        && Academy.IsInitialized
                        && Academy.Instance.IsCommunicatorOn;

        int fps;

        if (python)
        {
            // [A][B] 훈련/통신 추론.
            //   ML 모드에서는 1 프레임 = 1 결정 사이클이므로 캡 = 훈련 속도 캡.
            //   yaml 의 target_frame_rate:-1 과 같은 값을 쓴다(충돌 없음).
            fps = -1;
        }
        else if (headless)
        {
            // [D][F] 화면이 없으므로 캡할 이유가 없다.
            fps = -1;
        }
        else if (is_ml_agent)
        {
            // [C] 파이썬 없이 ONNX 추론을 눈으로 보는 경우. 여기만 실시간 캡이 의미 있다.
            fps = inferenceWatchFps;
        }
        else
        {
            // [E] 자유 주행. 물리는 C++ 워커가 Unity 와 무관하게 자유 주행하므로
            //     여기서의 캡은 물리 속도에 영향이 없다. 렌더/GPU 만 아낀다.
            fps = freeRunRenderFps;
        }

        Application.targetFrameRate = fps;

        Debug.Log($"[VoxelEngineCore] FrameRate policy -> targetFrameRate={fps} " +
                  $"(ml={is_ml_agent}, python={python}, headless={headless})");
    }


    // [추가] DLL 메모리를 안전하게 닫는 전용 함수
    private void CleanupDLL()
    {
     /*   if (!isDllCleanedUp)
        {
            Debug.Log("[VoxelEngineCore] DLL thread termination & Freeing memory in progress...");
            
            // C++ 쪽으로 스레드 개수를 넘겨주어 루프 탈출 및 메모리 delete를 지시합니다.
            End_Voxel_Unity(); 
            
            Debug.Log("[VoxelEngineCore] DLL safely terminated.");
            isDllCleanedUp = true;
        }
*/
        if (!isDllCleanedUp)
        {                                // R12
            Debug.Log("[VoxelEngineCore] DLL thread termination & Freeing memory in progress...");
            End_Voxel_Unity();

            // R2: C++이 포인터 사용을 끝낸 뒤에 핀 해제 (순서 중요)
            if (threadArrayHandle.IsAllocated) threadArrayHandle.Free();

            Debug.Log("[VoxelEngineCore] DLL safely terminated.");
            isDllCleanedUp = true;
        }
    }

    // 오브젝트가 파괴되거나 다른 씬으로 넘어갈 때 호출됨
    void OnDestroy()
    {       
        // DLL 해제
        CleanupDLL();

        // 유니티가 꺼질 때 터미널 창도 깔끔하게 같이 닫아줍니다.
        WindowsConsole.HideConsole();
    }

    // 유니티 플레이 모드를 정지(▶)하거나, 빌드된 게임이 꺼질 때 호출됨
    void OnApplicationQuit()
    {
        // DLL 해제 (OnDestroy보다 먼저 불릴 수도 있으므로 양쪽에 방어적으로 작성)
        CleanupDLL();
    }



    // 기존의 public 변수 선언을 지우고, 인스펙터에 보이지 않는 private으로 변경합니다.
    private TextMeshProUGUI toggleButtonText;

    void Start() // 만약 이미 Start()나 Awake()가 있다면 그 안의 맨 윗부분에 아래 코드를 추가하세요.
    {
        ApplyFrameRatePolicy();

        


        // 1. 씬 전체에서 "GO/STOP" 이라는 이름을 가진 오브젝트를 찾습니다.
        GameObject textObj = GameObject.Find("GO/STOP");
        
        // 2. 오브젝트를 성공적으로 찾았다면, 그 안에 있는 텍스트 컴포넌트를 가져와 연결합니다.
        if (textObj != null)
        {
            toggleButtonText = textObj.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            Debug.LogError("[VoxelEngineCore] Can't find 'GO/STOP' Text Object!");
        }
        
        ShowButtonText();
    }


    // 2. 유니티 버튼에 연결할 함수 (반드시 public이어야 함!)
    // 유니티에서 관리할 현재 재생 상태
    private int isPlaying = 0;

    public void ToggleSimulationButton()
    {
        isPlaying = 1 - isPlaying;  // 상태 뒤집기 (true -> false, false -> true)

        ShowButtonText();
    }

    public void ShowButtonText()
    {
        if( is_ml_agent )
        {
            // 🌟 3. 버튼 텍스트 업데이트 로직 추가
            if (toggleButtonText != null)
            {
                if (isPlaying > 0)  toggleButtonText.text = "Phase";
                else                toggleButtonText.text = "Amplitude";
            }

            SetSimulationPlayState(isPlaying+3);  // C++ 로 바뀐 상태 전송

            if (isPlaying > 0)  Debug.Log("[VoxelEngineCore] Voxel Color: Action Phase");
            else                Debug.Log("[VoxelEngineCore] Voxel Color: Action Amplitude");
        }
        else
        {               
            SetSimulationPlayState(isPlaying);  // C++ 로 바뀐 상태 전송

            // RL 안할 때 스타트 버튼
            if (isPlaying > 0)  Debug.Log("[VoxelEngineCore] Simulation Start!");
            else                Debug.Log("[VoxelEngineCore] Simulation Stop!");
        }
    }
    
    

}