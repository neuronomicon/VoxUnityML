using System;
using UnityEngine;
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
    
    // 0.5초 동안 4단계 궤적을 저장할 내부 버퍼 (4단계 x 296개 값)[cite: 1, 3]
    //public float[,] observationBuffer = new float[4, 296];

    // [수정] 크기를 고정하지 않고 선언만 해둠
    //public float[,] observationBuffer;
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
    public float distanceRewardMultiplier = 2.0f; 
    public float targetReachThreshold = 0.2f; 

    
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
        }

        // 목표물(Target)을 로봇 근처 일정 범위 내 랜덤 재배치[cite: 1, 3]
        if (tState.targetTransform != null)
        {
            Vector2 randomDir = UnityEngine.Random.insideUnitCircle.normalized;
            float randomDist = UnityEngine.Random.Range(minSpawnDist, maxSpawnDist);
            tState.targetTransform.localPosition = new Vector3(randomDir.x * randomDist - 0.25f, 0.5f, randomDir.y * randomDist - 0.25f);
        }

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
        //tState.currentVoxelUpVector = Vector3.up;
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

        // [1. 공회전 체크] 로봇이 움직이지 않고 굳었을 때 마이너스 보상[cite: 1, 3]
        if (Vector3.Distance(currentCoM, tState.lastCheckedPos) < 0.00001f) {
            tState.freezeCount++;
            if (tState.freezeCount > freezeStepLimit) {
                Debug.LogError($"[{agent.name}] Robot freeze not moving! Will reset.");
                agent.AddReward(failPenalty); 
                isDone = true;
            }
        } else { 
            tState.freezeCount = 0; 
        }
        tState.lastCheckedPos = currentCoM; 

        // [2. 타겟 체크] 타겟 도달 시 플러스 보상[cite: 1, 3]
        if (!isDone && tState.targetTransform != null) {
            Vector3 targetLocalPos = agent.PhysicsInfo.transform.InverseTransformPoint(tState.targetTransform.position);
            
            //float currentDistance = Vector3.Distance(currentCoM, targetLocalPos);
            Vector3 delta = targetLocalPos - currentCoM;
            float currentDistance = new Vector2(delta.x, delta.z).magnitude;   // ← XZ 로 통일
            
            float rewardDelta = tState.previousDistance - currentDistance;
            agent.AddReward(rewardDelta * distanceRewardMultiplier);
            tState.previousDistance = currentDistance;

            if (currentDistance < targetReachThreshold) {
                Debug.Log($"[{agent.name}] Target reached!");
                agent.AddReward(successReward);
                isDone = true;
            }
        }

        // [3. 추락 및 뒤집힘 체크][cite: 1, 3] c++ 단위로 -0.2 
        if (!isDone && (currentCoM.y < -0.2f || Vector3.Dot(agent.LastUpVector, Vector3.up) < 0f)) {
            agent.AddReward(failPenalty);
            isDone = true;
        }

        // [4. 에피소드 종료 처리 및 생존 페널티][cite: 1, 3]
        if (isDone) 
        {
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

    public override void Heuristic(VoxelRobotAgent agent, in ActionBuffers actionsOut, RobotTaskState state)
    {
        var ca = actionsOut.ContinuousActions;

        float amp = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)     amp =  1.0f;
            else if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) amp = -1.0f;
        }

        // 근육마다 위상을 어긋나게 -> 진행파가 생겨 실제로 이동/회전함
        float t = Time.time;
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