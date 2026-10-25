using System;
using UnityEngine;
using Unity.MLAgents;                  // ← 추가 (Academy.Instance.StatsRecorder)
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators; 

using UnityEngine.InputSystem; // Keyboard for Heuristic()

// ==============================================================================
// [TargetTrackingState] 타겟 추적 전용 런타임 상태 변수 묶음 (VoxelRobotAgent.cs 에서 조작 가능하도록)
// ==============================================================================
[Serializable]
public class TargetTrackingState : RobotTaskState
{
    [Header("🎯 Target Object (Drag Your Object!)")]
    public Transform targetTransform; // 🌟 씬 오브젝트 연결 슬롯이 이쪽으로 이동!

    [Header("🎯 Task Specific Realtime Variables")]
    [ReadOnly] public float previousDistance;    
    [ReadOnly] public Vector3 lastCheckedPos = Vector3.zero;
    [ReadOnly] public int freezeCount = 0;
    

    // 2026-10-22
    // ───── 에피소드 결과 기록용 ─────
    [ReadOnly] public int   lastResult   = -2;    // ← -1 이 아니라 -2 (기록 완료 표시) // -1 미결(=타임아웃), 1 성공, 0 실패
    [ReadOnly] public int   lastFailCode = 0;     // 1 공회전, 2 추락, 3 뒤집힘
    [ReadOnly] public float lastDistance = -1f;   // 종료 시점 타깃까지 거리
}


// ==============================================================================
// [TargetTrackingProfile] 타겟 추적 훈련의 "규칙서(Rules)"
// ==============================================================================
[CreateAssetMenu(fileName = "NewTargetTrackingTask", menuName = "RL Tasks/Target Tracking")]
public class TargetTrackingProfile : RobotTaskProfile
{
    [Header("🎯 Task Parameters")]
    public float minSpawnDist = 2.5f; 
    public float maxSpawnDist = 3.5f; 

    public float successReward = 5.0f; 
    public float failPenalty = -1.0f; 
    public float distanceRewardMultiplier = 20.0f; 
    public float targetReachThreshold = 0.07f; 

    
    /*
    [Header("🦴 Robot Anatomy Parameters")]    
    public int centerVoxelIdx = 16; 
    public int forwardVoxelA = 17; 
    public int forwardVoxelB = 15; 
    public int rightVoxelA = 21; 
    public int rightVoxelB = 11; 
    */
    
    [Header("🧊 Freeze Detection")]    
    [Tooltip("Consecutive steps to trigger stationary detection. (125 = 2.5s @ 50Hz)")]
    public int freezeStepLimit = 125;
    
    
    public override Type GetStateType() => typeof(TargetTrackingState);
    public override RobotTaskState CreateState() => new TargetTrackingState();

    public override void OnEpisodeBegin(VoxelRobotAgent agent, RobotTaskState state)
    {
        base.OnEpisodeBegin(agent, state);

        var tState = state as TargetTrackingState;


        // 2026-10-22 직전 에피소드가 어느 분기도 타지 않고 끝났다면 = MaxStep 타임아웃
        if (agent.CompletedEpisodes > 0 && tState.lastResult == -1)
            RecordEpisodeStats(agent, tState);         // ← 추가

        tState.lastResult = -1;        // ← 새 에피소드 시작: 미결 상태로


        /*// 에이전트가 계산해둔 버퍼 크기를 가져와서 4개(Phase)의 슬롯을 동적 생성
        if (tState.observationBuffer == null || tState.observationBuffer.GetLength(1) != agent.StateBufferSize)
        {
            tState.observationBuffer = new float[4, agent.StateBufferSize];
        }
        */
        if (tState.targetTransform == null)
        {
            Debug.LogError($"[{agent.name}] targetTransform 이 비어 있습니다! " +
                           $"거리 보상과 도달 판정이 전혀 동작하지 않고 생존 페널티만 쌓입니다. " +
                           $"Agent 의 Runtime State > Target Object 슬롯을 확인하세요.");
        #if UNITY_SERVER
            Application.Quit(1);          // 헤드리스에서는 즉시 중단
        #endif
        }


        // 2026-10-24 스케일 검증
        // VoxelPhysicsInfo.Awake() 가 transform.localScale 을 simScale(10) 로 설정한다.
        // 이게 적용되지 않으면 InverseTransformPoint 가 10 으로 나누지 않아
        // targetLocalPos 는 유니티 미터, currentCoM 은 sim 단위가 되어 거리가 10배 어긋난다.
        // (보상이 이론 상한의 5배로 뜨고, 도달 임계값이 10배 엄격해진다.)
        if (agent.CompletedEpisodes == 0)
        {
            float sc = agent.PhysicsInfo.transform.lossyScale.x;
            if (Mathf.Abs(sc - 10f) > 0.01f)
            {
                Debug.LogError($"[{agent.name}] simScale 미적용! lossyScale={sc} (기대값 10) " +
                    $"— 거리 계산이 10배 어긋나므로 학습 결과를 신뢰할 수 없습니다.");
            #if UNITY_SERVER
                Application.Quit(1);
            #endif
            }
        }


        // 목표물(Target)을 로봇 근처 일정 범위 내 랜덤 재배치[cite: 1, 3]
        if (tState.targetTransform != null)
        {
            Vector2 randomDir = UnityEngine.Random.insideUnitCircle.normalized;
            float randomDist = UnityEngine.Random.Range(minSpawnDist, maxSpawnDist);
            tState.targetTransform.localPosition = new Vector3(randomDir.x * randomDist - 0.25f, 0.5f, randomDir.y * randomDist - 0.25f);
        }


    /*  // 2026-10-22 수정
        Vector3 robotCoM = agent.GetRobotCenterOfMass(body.expectedVoxelCount);
        
        if (tState.targetTransform != null)
        {
            //Vector3 targetLocalPos = agent.PhysicsInfo.transform.InverseTransformPoint(tState.targetTransform.position);
            //tState.previousDistance = Vector3.Distance(robotCoM, targetLocalPos);

            Vector3 targetLocalPos = agent.PhysicsInfo.transform.InverseTransformPoint(tState.targetTransform.position);
            Vector3 delta = targetLocalPos - robotCoM;
            tState.previousDistance = new Vector2(delta.x, delta.z).magnitude;   // ← XZ
        }

        tState.freezeCount = 0;
        tState.lastCheckedPos = robotCoM;
    */

        // C++ 리셋은 지연 실행된다 — CPP_Reset_Voxel_Unity() 는 플래그만 세우고 리턴하고,
        // 실제 g_sim->Zero() 는 다음 사이클에 워커 스레드에서 일어난다.
        // 따라서 이 시점의 CoM 은 직전 에피소드의 마지막 자세이며 기준값으로 쓸 수 없다.
        // 거리 기준은 리셋이 끝난 뒤 첫 OnActionReceived 에서 잡는다.
        tState.previousDistance = -1f;                    // 센티넬: 아직 기준 없음
        tState.lastCheckedPos   = Vector3.positiveInfinity;
        tState.freezeCount      = 0;
        
    }

    public override void OnIntermediatePhase(VoxelRobotAgent agent, RobotTaskState state, int phaseIndex, int cycleCount)
    {
    /*    if (phaseIndex < 0 || phaseIndex > 3) return;
        var tState = state as TargetTrackingState;

        float[] currentState = agent.GetEgocentricVoxelState(expectedVoxelCount, 
                                                             centerVoxelIdx, 
                                                             forwardVoxelA, forwardVoxelB, 
                                                             rightVoxelA, rightVoxelB, 
                                                             tState.targetTransform );

        // 버퍼의 해당 위상(Phase)에 296개 값을 통째로 복사해 둠[cite: 1, 3]
        for (int i = 0; i < agent.StateBufferSize; i++)
        {
            tState.observationBuffer[phaseIndex, i] = currentState[i];
        }
    */
    }


    
    public override void CollectObservations(VoxelRobotAgent agent, VectorSensor sensor, RobotTaskState state)
    {
        var tState = state as TargetTrackingState;
        if (tState == null) return;

        /*// 1/4 ~ 4/4의 1184개(296 x 4) 데이터를 순서대로 신경망에 모두 밀어 넣음[cite: 1, 3]
        for (int phase = 0; phase < 4; phase++)
        {
            for (int i = 0; i < agent.StateBufferSize; i++)
            {
                sensor.AddObservation(tState.observationBuffer[phase, i]);
            }
        }
        */

        float[] s = agent.GetEgocentricVoxelState(body, tState.targetTransform);
        for (int i = 0; i < agent.StateBufferSize; i++) sensor.AddObservation(s[i]);

        // 액추에이터 1차 지연 보정 — 반드시 마지막에
        AddActuatorObservations(agent, sensor);        
    }

    public override void OnActionReceived(VoxelRobotAgent agent, ActionBuffers actionBuffers, RobotTaskState state)
    {
        if (body == null) return;

        var tState = state as TargetTrackingState;
        Vector3 currentCoM = agent.GetRobotCenterOfMass(body.expectedVoxelCount);
        bool isDone = false; 

        
        bool isFirstStep = (tState.previousDistance < 0f);   // ← 추가
        float currentDistance = -1f; 


        // [1. 공회전 체크] 로봇이 움직이지 않고 굳었을 때 마이너스 보상[cite: 1, 3]
        //if (Vector3.Distance(currentCoM, tState.lastCheckedPos) < 0.00001f) {
        if (!isFirstStep && Vector3.Distance(currentCoM, tState.lastCheckedPos) < 0.00001f) 
        {
            tState.freezeCount++;
            
            if (tState.freezeCount > freezeStepLimit) 
            {
                Debug.LogError($"[{agent.name}] Robot freeze not moving! Will reset.");
                agent.AddReward(failPenalty); 

                tState.lastResult   = 0;        // ← 추가
                tState.lastFailCode = 1;        // ← 추가

                isDone = true;
            }
        } 
        else
        { 
            tState.freezeCount = 0; 
        }

        tState.lastCheckedPos = currentCoM; 

        // [2. 타겟 체크] 타겟 도달 시 플러스 보상[cite: 1, 3]
        if (!isDone && tState.targetTransform != null) 
        {
            Vector3 targetLocalPos = agent.PhysicsInfo.transform.InverseTransformPoint(tState.targetTransform.position);
            
            //float currentDistance = Vector3.Distance(currentCoM, targetLocalPos);
            Vector3 delta = targetLocalPos - currentCoM;
            currentDistance = new Vector2(delta.x, delta.z).magnitude;   // ← XZ 로 통일

            tState.lastDistance = currentDistance;    // ★ 추가: 매 스텝 갱신

        /*  // 2026-10-22
            float rewardDelta = tState.previousDistance - currentDistance;
            agent.AddReward(rewardDelta * distanceRewardMultiplier);
            tState.previousDistance = currentDistance;
        */

            if (isFirstStep)
            {
                // 리셋이 끝난 진짜 위치에서 기준만 잡는다. 이 스텝의 거리 보상은 0.
                tState.previousDistance = currentDistance;
            }
            else
            {
                float rewardDelta = tState.previousDistance - currentDistance;
                agent.AddReward(rewardDelta * distanceRewardMultiplier);
                tState.previousDistance = currentDistance;
            }



            if (currentDistance < targetReachThreshold) 
            {
            #if !UNITY_SERVER
                Debug.Log($"[{agent.name}] Target reached!");
            #endif

                agent.AddReward(successReward);

                tState.lastResult = 1;        // ← 추가

                isDone = true;
            }

        }


        // [3. 추락 및 뒤집힘 체크][cite: 1, 3] c++ 단위로 -0.2 
        if (!isDone && (currentCoM.y < -0.2f || Vector3.Dot(agent.LastUpVector, Vector3.up) < 0f)) {
            agent.AddReward(failPenalty);

            tState.lastResult   = 0;                                 // ← 추가
            tState.lastFailCode = (currentCoM.y < -0.2f) ? 2 : 3;     // ← 추가

            isDone = true;
        }

        // [4. 에피소드 종료 처리 및 생존 페널티][cite: 1, 3]
        if (isDone) 
        {
            //tState.lastDistance = currentDistance;     // ← 추가
            RecordEpisodeStats(agent, tState);         // ← 추가

            agent.EndEpisode(); 
        }
        else
        {
            //if (agent.MaxStep > 0) agent.AddReward(-1.0f / agent.MaxStep); 

            float timePenaltyScale = 0.2f;
            if (agent.MaxStep > 0) agent.AddReward(-timePenaltyScale / agent.MaxStep); 
            else                   agent.AddReward(-0.001f);
        }
    }


    private void RecordEpisodeStats(VoxelRobotAgent agent, TargetTrackingState tState)
    {
        var rec = Academy.Instance.StatsRecorder;
        bool success = (tState.lastResult == 1);

        rec.Add("Task/Success",      success ? 1.0f : 0.0f);
        rec.Add("Task/Fail_Freeze",  tState.lastFailCode == 1  ? 1.0f : 0.0f);
        rec.Add("Task/Fail_Fall",    tState.lastFailCode == 2  ? 1.0f : 0.0f);
        rec.Add("Task/Fail_Flip",    tState.lastFailCode == 3  ? 1.0f : 0.0f);
        rec.Add("Task/Fail_Timeout", tState.lastResult   == -1 ? 1.0f : 0.0f);

        if (tState.lastDistance >= 0f) rec.Add("Task/FinalDistance",  tState.lastDistance);
        if (success)                   rec.Add("Task/StepsToTarget",  agent.StepCount);

        tState.lastResult   = -2;
        tState.lastFailCode = 0;
        tState.lastDistance = -1f;
    }


    public override void Heuristic(VoxelRobotAgent agent, in ActionBuffers actionsOut, RobotTaskState state)
    {
        var ca = actionsOut.ContinuousActions;

        float amp = 0f;

    #if UNITY_SERVER
        // 서버(헤드리스) 빌드: 키보드가 없다. 벤치마크에서 로봇이 실제로 보행해야
        // 충돌 리스트 재생성 비용까지 포함된 현실적인 작업량이 측정된다.
        amp = 1.0f;
    #else

        if (Keyboard.current != null)
        {
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)     amp =  1.0f;
            else if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) amp = -1.0f;
        }

    #endif

        // 근육마다 위상을 어긋나게 -> 진행파가 생겨 실제로 이동/회전함
        //float t = Time.time;

        // 벽시계가 아니라 시뮬레이션 시간으로 위상을 구동한다.
        // 구성마다 결정 주기가 달라져도 보행 주파수(와 작업량)가 동일하게 유지된다.
        float t = (agent.PhysicsInfo != null) ? (float)agent.PhysicsInfo.currentRobotStep : Time.time;

        for (int i = 0; i < ca.Length; i++)
            ca[i] = amp * Mathf.Sin(2f * Mathf.PI * 2f * t + i * 0.5f);


/*        // 휴리스틱 (키보드 조작) 로직 필요 시 구현
        var continuousActionsOut = actionsOut.ContinuousActions;
        
        float horizontalInput = 0f;

        // 새로운 Input System을 사용한 키보드 입력 처리
        if (Keyboard.current != null)
        {
            // D키나 오른쪽 화살표를 누르면 +1
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            {
                horizontalInput = 1.0f;
            }
            // A키나 왼쪽 화살표를 누르면 -1
            else if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            {
                horizontalInput = -1.0f;
            }
        }

        // 결정된 입력값을 로봇의 모든 모터에 전달 (테스트용)
        for (int i = 0; i < continuousActionsOut.Length; i++)
        {
            continuousActionsOut[i] = horizontalInput;
        }
        */
    }
}