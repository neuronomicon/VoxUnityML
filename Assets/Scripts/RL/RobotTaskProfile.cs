
using System;
using System.Runtime.InteropServices; 

using UnityEngine;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators; 

// 🌟 모든 태스크의 상태 변수 묶음이 상속받을 뼈대
[Serializable]
public abstract class RobotTaskState { }

public abstract class RobotTaskProfile : ScriptableObject
{      
    
    [Header("Robot Model (Body) Settings")]
    [HideInInspector] public string selectedVoxFileName;

    // 🌟 [이곳에 추가!] 모든 태스크가 공통으로 알아야 할 로봇의 총 복셀 개수
    [Header("로봇 공통 제원")]    
    public int expectedVoxelCount = 33;

    [Header("🤖 ML-Agents Auto Settings (Behavior Parameters)")]
    [Tooltip("Training Behavior Name")]
    public string behaviorName = "VoxBot33";
    
    [Tooltip("Total number of sensor observations")]
    public int spaceSize = 1184;
    
    [Tooltip("Total number of motor control actions")]
    public int continuousActions = 66;
    
    [Tooltip("Max RL-NN steps per episode")]
    public int maxStep = 40;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 🌟 [수정] 유니티 에디터 안전장치: 지연 호출(Delay Call)을 사용하여 락(Lock) 충돌 방지
        UnityEditor.EditorApplication.delayCall += () =>
        {
            // delayCall 내부에서는 오브젝트가 삭제되었을 수도 있으므로 안전 검사 필수
            if (this == null) return; 

            VoxelRobotAgent[] allAgents = FindObjectsByType<VoxelRobotAgent>(FindObjectsSortMode.None);
            foreach (var agent in allAgents)
            {
                if (agent != null && agent.taskProfile == this)
                {
                    agent.ApplyProfileSettingsToComponents();
                    
                    UnityEditor.EditorUtility.SetDirty(agent);
                    var bp = agent.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
                    if (bp != null) UnityEditor.EditorUtility.SetDirty(bp);
                }
            }
        };
    }
#endif

/*    // ▼▼▼ 여기에 실시간 동기화 코드를 추가합니다 ▼▼▼
#if UNITY_EDITOR
    private void OnValidate()
    {
        // 씬(Scene)에 있는 모든 VoxelRobotAgent를 찾습니다.
        VoxelRobotAgent[] allAgents = FindObjectsByType<VoxelRobotAgent>(FindObjectsSortMode.None);
        
        foreach (var agent in allAgents)
        {
            // 이 에셋(나 자신)을 뇌로 장착하고 있는 로봇을 발견하면
            if (agent.taskProfile == this)
            {
                // 로봇에게 즉시 컴포넌트를 강제 동기화하라고 명령합니다!
                agent.ApplyProfileSettingsToComponents();
                
                // 유니티 에디터에게 "이 로봇 정보가 갱신되었으니 화면에 새로 그려줘!" 라고 알려줍니다.
                UnityEditor.EditorUtility.SetDirty(agent);
                var bp = agent.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
                if (bp != null) UnityEditor.EditorUtility.SetDirty(bp);
            }
        }
    }
#endif
*/

    public abstract Type GetStateType();
    public abstract RobotTaskState CreateState();

    public virtual void OnEpisodeBegin(VoxelRobotAgent agent, RobotTaskState state)
    {
        Debug.Log($"[{agent.name}] Episode Begin!");
                
        VoxelRobotAgent.Reset_Voxel_Unity(agent.robotIdx);
    }

    public virtual void OnIntermediatePhase(VoxelRobotAgent agent, RobotTaskState state, int phaseIndex) { }
    public abstract void CollectObservations(VoxelRobotAgent agent, VectorSensor sensor, RobotTaskState state);
    public abstract void OnActionReceived(VoxelRobotAgent agent, ActionBuffers actionBuffers, RobotTaskState state);
    public abstract void Heuristic(VoxelRobotAgent agent, in ActionBuffers actionsOut, RobotTaskState state);
}


/*
[Header("로봇 모델(Body) 설정")]
    [HideInInspector] public string selectedVoxFileName;


    [Header("🤖 ML-Agents 자동 설정 (Behavior Parameters)")]
    [Tooltip("훈련 이름 (예: VoxBot33)")]
    public string behaviorName = "VoxBot33";
    
    [Tooltip("센서 관측값의 총 개수 (예: 1184)")]
    public int spaceSize = 1184;
    
    [Tooltip("모터 제어 액션의 총 개수 (예: 66)")]
    public int continuousActions = 66;
    
    [Tooltip("에피소드 최대 스텝 (물리 프레임 기준, 예: 500)")]
    public int maxStep = 40;
*/