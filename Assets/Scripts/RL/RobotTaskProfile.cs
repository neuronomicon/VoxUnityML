
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
/*    
    [Header("Robot Model (Body) Settings")]
    [HideInInspector] public string selectedVoxFileName;

    [Header("로봇 공통 제원")]    
    public int expectedVoxelCount = 33;
    
    [Tooltip("Total number of sensor observations")]
    public int spaceSize = 1184;
    
    [Tooltip("Total number of motor control actions")]
    public int continuousActions = 66;
*/
    [Header("🦴 Robot Body (Drag Body Asset!)")]
    public RobotBodyProfile body;

    // 관측·액션 크기를 유도값으로. 필요하면 파생 프로필에서 오버라이드
    //public virtual int GetObservationSize() => body != null ? body.EgocentricStateSize : 0;
    public virtual int GetObservationSize() => body != null ? body.EgocentricStateSize + body.muscleCount : 0;

    public virtual int GetActionSize()      => body != null ? body.muscleCount        : 0;


    /// 파생 프로필의 CollectObservations 마지막에 반드시 호출.
    /// 액추에이터 1차 지연(EMA, tau~50ms) 때문에 이전 액션이 있어야 MDP 가 마르코프가 됨.
    protected void AddActuatorObservations(VoxelRobotAgent agent, VectorSensor sensor)
    {
        var prev = agent.PrevAction;
        int n = (body != null) ? body.muscleCount : 0;

        // 개수는 GetObservationSize() 와 항상 일치해야 하므로 무조건 n 개를 쓴다
        for (int i = 0; i < n; i++)
            sensor.AddObservation((prev != null && i < prev.Length) ? prev[i] : 0f);
    }


    [Header("🤖 ML-Agents Auto Settings (Behavior Parameters)")]
    [Tooltip("Training Behavior Name")]
    public string behaviorName = "VoxBot33";
    
    [Tooltip("Max RL-NN steps per episode (0=unlimited)")]
    public int maxStep = 40;


    public abstract Type GetStateType();
    public abstract RobotTaskState CreateState();

    public virtual void OnEpisodeBegin(VoxelRobotAgent agent, RobotTaskState state)
    {
        Debug.Log($"[{agent.name}] Episode Begin!");
                
        VoxelRobotAgent.CPP_Reset_Voxel_Unity(agent.robotIdx);
    }

    public virtual void OnIntermediatePhase(VoxelRobotAgent agent, RobotTaskState state, int phaseIndex, int cycleCount) { }
    public abstract void CollectObservations(VoxelRobotAgent agent, VectorSensor sensor, RobotTaskState state);
    public abstract void OnActionReceived(VoxelRobotAgent agent, ActionBuffers actionBuffers, RobotTaskState state);
    public abstract void Heuristic(VoxelRobotAgent agent, in ActionBuffers actionsOut, RobotTaskState state);

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