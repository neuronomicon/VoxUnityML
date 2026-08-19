/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelRLManager.cs]
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
using System.Runtime.InteropServices;

using Unity.MLAgents;

public class VoxelRLManager : MonoBehaviour
{
    const string DLL_NAME = VoxelDllConfig.DLL_NAME;

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Step_All_Simulations_For_RL(int microSteps, float[] allActions, int[] rlRobotIndices, int numRlRobots, int actionSizePerRobot);
    //public static extern void Step_All_Simulations_For_RL(int numRobots, int microSteps, float[] allActions, int actionSizePerRobot);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Initialize_for_Unity_RL();

    // [추가] C++의 상태를 확인하는 DLL 함수 임포트
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Check_Simulation_Ready();


    [Header("RL Synchronous Control")]
    public int actionSizePerRobot = 34; // 로봇 1대당 모터 복셀 수

    [Header("강화학습 타이밍 및 동기화 (100Hz 기준)")]
    [Tooltip("매 FixedUpdate마다 C++에서 진행할 물리 스텝")]
    public int stepsPerSimulationCycle = 125;
    
    [Tooltip("모든 로봇에 일괄 적용할 Decision Period")]
    public int customDecisionPeriod = 4;

    

    [Header("자동 탐색된 RL 로봇 (Read Only)")]
    [ReadOnly] public int numRlRobots = 0; // 5대가 탐색될 예정

    private VoxelRobotAgent[] activeAgents; // RL 로봇 5대를 담을 배열
    private int[] rlRobotIndices;           // C++에 보낼 로봇 번호표 배열 (예: 0, 2, 4, 7, 9)
    private float[] globalActionBuffer;
    
    private int receivedActionCount = 0;    // 몇 대의 로봇이 액션을 제출했는지 카운트
    private bool isWaitingForCpp = false;

    

    private VoxelEngineCore engineCore; // 🌟 VoxelEngineCore 참조
    // ML-Agent 사용 여부를 VoxelEngineCore의 is_ml_agent 변수에서 직접 확인
    private bool IsMLAgentEnabled => engineCore != null && engineCore.is_ml_agent;


    private VoxelPhysicsManager[] allPhysicsAreas;
    
    // 🌟 [변경] Awake, Start 삭제하고 InitializeRL()로 통합
    public void InitializeRL()
    {        
        engineCore = GetComponent<VoxelEngineCore>();
        if (engineCore == null) engineCore = FindAnyObjectByType<VoxelEngineCore>();

        // ML-Agents 자동 스텝 차단
        Academy.Instance.AutomaticSteppingEnabled = false;

        // 🌟 씬에 존재하는 모든 훈련장(VoxelPhysicsManager)을 찾아 일제히 동기화를 지시합니다.
        allPhysicsAreas = FindObjectsByType<VoxelPhysicsManager>(FindObjectsSortMode.None);

        activeAgents = FindObjectsByType<VoxelRobotAgent>(FindObjectsSortMode.None);
        
        // 에이전트들을 부여받은 인덱스(0, 1, 2...) 순서대로 정렬하여 버퍼 꼬임 완벽 방지
        System.Array.Sort(activeAgents, (a, b) => a.robotIdx.CompareTo(b.robotIdx));
        
        numRlRobots = activeAgents.Length;

        if (numRlRobots > 0)
        {
            rlRobotIndices = new int[numRlRobots];
            globalActionBuffer = new float[numRlRobots * actionSizePerRobot];

            for (int i = 0; i < numRlRobots; i++)
            {
                rlRobotIndices[i] = activeAgents[i].robotIdx;
            }
            
            Debug.Log($"[VoxelRLManager] Total {numRlRobots} RL robots initialized in precise order.");
        }        
    }


    

    // [핵심] 이제 로봇 번호 대신 에이전트 자기 자신(this)을 인자로 받습니다.
    public void SubmitAction(VoxelRobotAgent agent, float[] robotActions)
    {
        // 이 에이전트가 배열의 몇 번째 위치에 있는지 확인
        int bufferIndex = System.Array.IndexOf(activeAgents, agent);
        if (bufferIndex == -1) return;

        // 버퍼 갱신
        int offset = bufferIndex * actionSizePerRobot;
        for (int i = 0; i < actionSizePerRobot; i++)
        {
            globalActionBuffer[offset + i] = robotActions[i];
        }

        receivedActionCount++;

        // 5대의 에이전트가 모두 액션을 제출했다면 C++ 가동!
        if (receivedActionCount >= numRlRobots)
        {
            receivedActionCount = 0; // 다음 턴을 위해 초기화

            // C++로 액션 버퍼와 타겟 로봇 번호표를 함께 넘깁니다.
            Step_All_Simulations_For_RL(stepsPerSimulationCycle, globalActionBuffer, rlRobotIndices, numRlRobots, actionSizePerRobot);
            isWaitingForCpp = true;
        }
    }


    // [추가] C++가 연산을 몇 번 완료했는지 세는 카운터
    [Header("C++가 연산을 몇 번 완료했는지 세는 카운터")]
    [ReadOnly] public int currentDecisionStep = 0;


    // [추가] 시동 타이머 변수
    private bool isFirstStepStarted = false;
    private float startupTimer = 0f;
    
    private void Update()
    {
        if (!IsMLAgentEnabled || numRlRobots == 0) return;


        // 🌟 [안전한 첫 시동] 파이썬과 소켓 통신이 완벽히 안정화되도록 '현실 시간'으로 1.5초 대기합니다.
        // (ML-Agents는 훈련 시 게임 속도를 100배로 올리므로, 반드시 unscaledDeltaTime을 써야 합니다)
        if (!isFirstStepStarted)
        {
            startupTimer += Time.unscaledDeltaTime; 
            if (startupTimer > 1.5f)
            {
                isFirstStepStarted = true;
                foreach (var agent in activeAgents) agent.RequestDecision();
                Academy.Instance.EnvironmentStep();
                Debug.Log("[VoxelRLManager] Python connection stabilized ready! Begin the first training cycle.");
            }
            return; // 1.5초가 지나기 전까지는 아래 시뮬레이션 코드를 실행하지 않고 방어합니다.
        }


        // C++ 백그라운드 스레드가 연산을 마쳤다면
        if (isWaitingForCpp && Check_Simulation_Ready() == 1)
        {
            isWaitingForCpp = false;
                        

            // 🌟 씬에 존재하는 모든 훈련장(VoxelPhysicsManager)을 찾아 일제히 동기화를 지시합니다.
            //VoxelPhysicsManager[] allPhysicsAreas = FindObjectsByType<VoxelPhysicsManager>(FindObjectsSortMode.None);
            foreach (var area in allPhysicsAreas)
            {                
                area.SyncPhysicsWithCpp();
                //Debug.Log("[slog] [VoxelRLManager] 씬에 존재하는 모든 훈련장(VoxelPhysicsManager)을 찾아 일제히 동기화를 지시");
            }

            currentDecisionStep++;


            // 🌟 [복구된 핵심 로직] 1/4, 2/4, 3/4, 4/4 지점(루프 5, 10, 15, 20)에서 관측값 기록
            int partialPeriod = customDecisionPeriod / 4;

            if (currentDecisionStep % partialPeriod == 0)
            {
                int phaseIndex = (currentDecisionStep / partialPeriod) - 1; // 0, 1, 2, 3 인덱스

                foreach (var agent in activeAgents)
                {
                    var physicsInfo = agent.GetComponent<VoxelPhysicsInfo>();
                    if (physicsInfo != null) physicsInfo.ForceMonitorVoxelState();
                    
                    // 에이전트 내부 버퍼에 현재 위상의 상태(296개) 저장
                    agent.RecordIntermediateState(phaseIndex); 
                }
            }


            // 목표한 주기 (예: customDecisionPeriod(회) = 0.5초)에 도달했는가?
            if (currentDecisionStep >= customDecisionPeriod)
            {
                currentDecisionStep = 0;

                // [추가] 25번 주기(0.5초)가 제대로 돌고 있는지 콘솔에서 직접 확인!
                //Debug.Log($"[RL Manager] {customDecisionPeriod}회 루프 도달 완료! 에이전트에게 새로운 Action을 요청합니다.");

                // 1. 관측값 강제 최신화
                //foreach (var agent in activeAgents)
                //{
                //    var physicsInfo = agent.GetComponent<VoxelPhysicsInfo>();
                //    if (physicsInfo != null) physicsInfo.ForceMonitorVoxelState();
                //}

                // 2. 에이전트 행동 지시 -> 이 함수가 위 SubmitAction()을 호출하면서 다시 사이클이 시작됩니다!
                foreach (var agent in activeAgents)
                {
                    agent.RequestDecision(); 
                }

                // 🚨 [CRITICAL FIX 2] 방금 깃발을 올린 에이전트들의 행동을 '지금 당장' 실행시킵니다!
                // 이 함수가 호출되는 찰나의 순간에 OnActionReceived가 동기적으로 실행됩니다.
                Academy.Instance.EnvironmentStep();

            }
            else
            {
                // 🌟 [추가] 아직 25번을 못 채웠다면? 에이전트를 부르지 않고 기존 액션 그대로 C++ 시뮬레이션만 계속 돌립니다!
                Step_All_Simulations_For_RL(stepsPerSimulationCycle, globalActionBuffer, rlRobotIndices, numRlRobots, actionSizePerRobot);
                isWaitingForCpp = true;
            }
        }
    }


}


/*
private void Start_OLD()
    {        
        // 🌟 같은 오브젝트(또는 씬)에서 VoxelEngineCore 스크립트 가져오기
        engineCore = GetComponent<VoxelEngineCore>();
        if (engineCore == null) engineCore = FindAnyObjectByType<VoxelEngineCore>();

        // 🚨 [CRITICAL FIX 1] ML-Agents의 자동 FixedUpdate 스텝(Stepping) 기능을 강제로 꺼버립니다!
        // 이제 ML-Agents는 매니저가 허락할 때까지 절대 혼자서 스텝을 올리지 않습니다.
        Academy.Instance.AutomaticSteppingEnabled = false;


        // 시스템 시작 시 에이전트에게 최초 1회 행동을 요구하여 사이클을 가동합니다.
        //if (IsMLAgentEnabled && agent != null)  agent.RequestDecision();

        // 1. 씬에 있는 모든 RL 에이전트를 자동으로 찾습니다.
        activeAgents = FindObjectsByType<VoxelRobotAgent>(FindObjectsSortMode.None);
        numRlRobots = activeAgents.Length;

        if (numRlRobots > 0)
        {
            rlRobotIndices = new int[numRlRobots];
            globalActionBuffer = new float[numRlRobots * actionSizePerRobot];

            // 2. 찾아낸 에이전트들의 C++ 인덱스를 배열에 저장합니다.
            // (인스펙터에서 설정한 agent.robotIdx 값을 그대로 활용)
            for (int i = 0; i < numRlRobots; i++)
            {
                rlRobotIndices[i] = activeAgents[i].robotIdx;
            }
            
            Debug.Log($"[VoxelRLManager] Total {numRlRobots} RL robots initialized.");

            // 3. 최초 1회 행동 요구
            //if (IsMLAgentEnabled)
            //{
            //    foreach (var agent in activeAgents) agent.RequestDecision();

                // 🌟 [추가된 코드] 수동으로 첫 번째 스텝을 즉시 가동하여 C++로 첫 액션을 쏘아 보냅니다!
            //    Academy.Instance.EnvironmentStep();
            //}
        }        
    }
*/


/// 개별 VoxelRobotAgent가 OnActionReceived에서 호출    
/*    public void SubmitAction(int robotIdx, float[] robotActions)
    {
        if (robotIdx < 0 || robotIdx >= totalRobots) return;

        int offset = robotIdx * actionSizePerRobot;
        for (int i = 0; i < actionSizePerRobot; i++)
        {
            globalActionBuffer[offset + i] = robotActions[i];
        }

        // C++ 백그라운드 스레드에 목표 스텝 연산을 지시하고 즉시 빠져나옵니다 (논블로킹).
        Step_All_Simulations_For_RL(totalRobots, stepsPerSimulationCycle, globalActionBuffer, actionSizePerRobot);        
        isWaitingForCpp = true;
    }

    // 더 이상 FixedUpdate를 사용하지 않습니다.
    // private void FixedUpdate() { }
    private void Update()
    {
        if (!IsMLAgentEnabled) return;

        // C++ 스레드가 이전 사이클 연산을 끝마쳤다면 (완벽한 타이밍 포착)
        if (isWaitingForCpp && Check_Simulation_Ready() == 1)
        {
            isWaitingForCpp = false;

            // 1. 에이전트에게 최신 C++ 데이터를 강제 동기화합니다.
            var physicsInfo = agent.GetComponent<VoxelPhysicsInfo>();
            if (physicsInfo != null)
            {
                physicsInfo.ForceMonitorVoxelState(); 
            }

            // 2. 에이전트에게 "가장 신선한 데이터가 세팅됐으니 관측하고 다음 액션을 줘!" 명령
            // 이 호출 즉시 CollectObservations -> OnActionReceived 순으로 실행됩니다.
            agent.RequestDecision(); 
        }
    }
*/


/*
    private void Update()
    {
        if (!IsMLAgentEnabled || numRlRobots == 0) return;

        if (isWaitingForCpp && Check_Simulation_Ready() == 1)
        {
            isWaitingForCpp = false;

            // 1. 모든 RL 에이전트 물리 데이터 강제 최신화
            foreach (var agent in activeAgents)
            {
                var physicsInfo = agent.GetComponent<VoxelPhysicsInfo>();
                if (physicsInfo != null) physicsInfo.ForceMonitorVoxelState();
            }

            // 2. 동기화가 끝나면 일제히 다음 행동 지시
            foreach (var agent in activeAgents)
            {
                agent.RequestDecision(); 
            }
        }
    }
*/    




    /// <summary>
    /// 씬 안의 모든 DecisionRequester를 찾아 customDecisionPeriod 값으로 바꿉니다.
    /// </summary>
/*    public void ApplyDecisionPeriodToAllRobots()
    {
        var requesters = FindObjectsByType<DecisionRequester>(FindObjectsSortMode.None);
        foreach (var req in requesters)
        {
            req.DecisionPeriod = customDecisionPeriod;
        }
        Debug.Log($"[VoxelRLManager] 총 {requesters.Length}대 로봇의 Decision Period가 {customDecisionPeriod}로 일괄 설정되었습니다.");
    }
*/
    
    
    /*
    private void FixedUpdate()
    {
        // 🌟 VoxelEngineCore.cs의 is_ml_agent가 false이면 C++ 동기화 스텝을 부르지 않고 리턴
        if (!IsMLAgentEnabled) return;

        // ML-Agent 모드일 때만 매 FixedUpdate마다 C++ 물리 연산 진행
        Step_All_Simulations_For_RL(totalRobots, stepsPerFixedUpdate, globalActionBuffer, actionSizePerRobot);
    }
    */

/*
    private void Awake()
    {
        globalActionBuffer = new float[totalRobots * actionSizePerRobot];
    }

    /// 개별 VoxelRobotAgent가 OnActionReceived에서 호출    
    public void SubmitAction(int robotIdx, float[] robotActions)
    {
        if (robotIdx < 0 || robotIdx >= totalRobots) return;

        int offset = robotIdx * actionSizePerRobot;
        for (int i = 0; i < actionSizePerRobot; i++)
        {
            globalActionBuffer[offset + i] = robotActions[i];
        }

        hasNewActionsThisFrame = true;
    }


    private void LateUpdate()
    {
        // 이번 프레임에 에이전트 액션 제출이 수집되었다면 C++ 동기화 진행
        if (hasNewActionsThisFrame)
        {
            Step_All_Simulations_For_RL(totalRobots, stepsPerFixedUpdate, globalActionBuffer, actionSizePerRobot);
            hasNewActionsThisFrame = false;

            
        }
    }
*/

