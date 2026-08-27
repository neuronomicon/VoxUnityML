using System;
using UnityEngine;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators; 



// ==============================================================================
// [TargetTrackingState] 타겟 추적 전용 런타임 상태 변수 묶음 (VoxelRobotAgent.cs 에서 조작 가능하도록)
// ==============================================================================
[Serializable]
public class TargetTrackingState : RobotTaskState
{
    [Header("🎯 Target Object (Drag Your Object!)")]
    public Transform targetTransform; // 🌟 씬 오브젝트 연결 슬롯이 이쪽으로 이동!

    [Header("🎯 Task Specific Realtime Variables")]
    public float previousDistance;
    public Vector3 currentVoxelUpVector = Vector3.up;
    public Vector3 lastCheckedPos = Vector3.zero;
    public int freezeCount = 0;
    
    // 0.5초 동안 4단계 궤적을 저장할 내부 버퍼 (4단계 x 296개 값)[cite: 1, 3]
    //public float[,] observationBuffer = new float[4, 296];

    // [수정] 크기를 고정하지 않고 선언만 해둠
    public float[,] observationBuffer;
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

    [Header("🦴 Robot Anatomy Parameters")]
    //public int expectedVoxelCount = 33; 
    public int centerVoxelIdx = 16; 
    public int forwardVoxelA = 17; 
    public int forwardVoxelB = 15; 
    public int rightVoxelA = 21; 
    public int rightVoxelB = 11; 

    public override Type GetStateType() => typeof(TargetTrackingState);
    public override RobotTaskState CreateState() => new TargetTrackingState();

    public override void OnEpisodeBegin(VoxelRobotAgent agent, RobotTaskState state)
    {
        base.OnEpisodeBegin(agent, state);

        var tState = state as TargetTrackingState;

        // 에이전트가 계산해둔 버퍼 크기를 가져와서 4개(Phase)의 슬롯을 동적 생성
        if (tState.observationBuffer == null || tState.observationBuffer.GetLength(1) != agent.StateBufferSize)
        {
            tState.observationBuffer = new float[4, agent.StateBufferSize];
        }

        // 목표물(Target)을 로봇 근처 일정 범위 내 랜덤 재배치[cite: 1, 3]
        if (tState.targetTransform != null)
        {
            Vector2 randomDir = UnityEngine.Random.insideUnitCircle.normalized;
            float randomDist = UnityEngine.Random.Range(minSpawnDist, maxSpawnDist);
            tState.targetTransform.localPosition = new Vector3(randomDir.x * randomDist - 0.25f, 0.5f, randomDir.y * randomDist - 0.25f);
        }

        Vector3 robotCoM = agent.GetRobotCenterOfMass();
        
        if (tState.targetTransform != null)
        {
            Vector3 targetLocalPos = agent.PhysicsInfo.transform.InverseTransformPoint(tState.targetTransform.position);
            tState.previousDistance = Vector3.Distance(robotCoM, targetLocalPos);
        }

        tState.freezeCount = 0;
        tState.lastCheckedPos = robotCoM;
        tState.currentVoxelUpVector = Vector3.up;
    }

    public override void OnIntermediatePhase(VoxelRobotAgent agent, RobotTaskState state, int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex > 3) return;
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
    }

    public override void CollectObservations(VoxelRobotAgent agent, VectorSensor sensor, RobotTaskState state)
    {
        var tState = state as TargetTrackingState;
        if (tState == null) return;

        // 1/4 ~ 4/4의 1184개(296 x 4) 데이터를 순서대로 신경망에 모두 밀어 넣음[cite: 1, 3]
        for (int phase = 0; phase < 4; phase++)
        {
            for (int i = 0; i < agent.StateBufferSize; i++)
            {
                sensor.AddObservation(tState.observationBuffer[phase, i]);
            }
        }
    }

    public override void OnActionReceived(VoxelRobotAgent agent, ActionBuffers actionBuffers, RobotTaskState state)
    {
        var tState = state as TargetTrackingState;
        Vector3 currentCoM = agent.GetRobotCenterOfMass();
        bool isDone = false; 

        // [1. 공회전 체크] 로봇이 움직이지 않고 굳었을 때 마이너스 보상[cite: 1, 3]
        if (Vector3.Distance(currentCoM, tState.lastCheckedPos) < 0.00001f) {
            tState.freezeCount++;
            if (tState.freezeCount > 5) {
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
            float currentDistance = Vector3.Distance(currentCoM, targetLocalPos);
            
            float rewardDelta = tState.previousDistance - currentDistance;
            agent.AddReward(rewardDelta * distanceRewardMultiplier);
            tState.previousDistance = currentDistance;

            if (currentDistance < targetReachThreshold) {
                Debug.Log($"[{agent.name}] Target reached!");
                agent.AddReward(successReward);
                isDone = true;
            }
        }

        // [3. 추락 및 뒤집힘 체크][cite: 1, 3]
        if (!isDone && (currentCoM.y < -2.0f || Vector3.Dot(tState.currentVoxelUpVector, Vector3.up) < 0f)) {
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
            if (agent.MaxStep > 0) agent.AddReward(-1.0f / agent.MaxStep); 
            else                   agent.AddReward(-0.001f);
        }
    }

    public override void Heuristic(VoxelRobotAgent agent, in ActionBuffers actionsOut, RobotTaskState state)
    {
        // 휴리스틱 (키보드 조작) 로직 필요 시 구현
    }
}