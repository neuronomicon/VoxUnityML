/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelRobotAgent.cs]
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
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies; // 🌟 BehaviorParameters 제어를 위해 추가!

using UnityEngine.InputSystem; // Keyboard for Heuristic()

public class VoxelRobotAgent : Agent // ML-Agents의 Agent 클래스 상속[cite: 1, 3, 11]
{
    [DllImport(VoxelDllConfig.DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Reset_Voxel_Unity(int robotIdx); // C++ 초기화 함수 연결[cite: 1, 3, 11]

    [Header("🤖 Robot ID")]
    public int robotIdx = 0;

    [HideInInspector] public VoxelRLManager rlManager;
    [HideInInspector] public VoxelPhysicsInfo PhysicsInfo { get; private set; }




    [Header("🧠 Training Logic Profile (Drag S.O. Asset!)")]
    public RobotTaskProfile taskProfile;

    [SerializeReference] 
    public RobotTaskState runtimeState;

    


    public override void Initialize()
    {
        rlManager = FindAnyObjectByType<VoxelRLManager>();
        PhysicsInfo = GetComponent<VoxelPhysicsInfo>();
        
        // 런타임 상태 동적 할당
        if (taskProfile != null && runtimeState == null)
        {
            runtimeState = taskProfile.CreateState();
        }

        // 🌟 게임(훈련)이 시작될 때 안전하게 한 번 더 적용
        ApplyProfileSettingsToComponents();
    }

    private void OnValidate()
    {
        if (taskProfile != null)
        {
            if (runtimeState == null || runtimeState.GetType() != taskProfile.GetStateType())
            {
                runtimeState = taskProfile.CreateState();
            }

            // 🌟 게임(훈련)이 시작될 때 안전하게 한 번 더 적용
            ApplyProfileSettingsToComponents();
        }
        else
        {
            runtimeState = null; 
        }
    }

    // =================================================================
    // 🌟 뇌(Profile)의 설정값을 내 몸(Components)에 강제로 맞추는 자동화 함수
    // =================================================================
    public void ApplyProfileSettingsToComponents()
    {
        if (taskProfile == null) return;

        // 1. Agent 컴포넌트의 Max Step 적용
        this.MaxStep = taskProfile.maxStep;

        // 2. Behavior Parameters 컴포넌트 세팅
        BehaviorParameters bp = GetComponent<BehaviorParameters>();
        if (bp != null)
        {
            // Behavior Name 적용
            bp.BehaviorName = taskProfile.behaviorName;
            
            // Space Size (Vector Observation) 적용
            bp.BrainParameters.VectorObservationSize = taskProfile.spaceSize;
            
            // Continuous Actions 적용 (ML-Agents 최신 API 방식)
            bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(taskProfile.continuousActions);
        }
    }

    // =================================================================
    // 🌟 ML-Agents 생명주기 통제 (모든 결정을 뇌에게 위임)
    // =================================================================
    
    public override void OnEpisodeBegin()
    {
        if (taskProfile != null) taskProfile.OnEpisodeBegin(this, runtimeState);
    }

    public override void CollectObservations(VectorSensor sensor) 
    {
        if (taskProfile != null) taskProfile.CollectObservations(this, sensor, runtimeState);
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        // 1. 프로필에게 행동 후 보상/판단 처리 일임
        if (taskProfile != null) taskProfile.OnActionReceived(this, actionBuffers, runtimeState);

        // 2. 모터 제어 신호를 매니저(전역 큐)로 제출[cite: 2, 3]
        var continuousActions = actionBuffers.ContinuousActions;
        float[] actionArray = new float[continuousActions.Length];
        for (int i = 0; i < continuousActions.Length; i++) actionArray[i] = continuousActions[i];

        if (rlManager != null) rlManager.SubmitAction(this, actionArray);


        int senseFreq = MaxStep / 4;    // 에피소드의 25% 씩 진행상황 표시
        if (StepCount % senseFreq == 0) {
            float pRate = 100.0f*(float)StepCount/(float)MaxStep;
            double currentSimTime = (PhysicsInfo != null) ? PhysicsInfo.currentRobotStep : 0f;
            Debug.Log($"[VoxelRobotAgent] Episode Progress:[{pRate}%] Agent step: {StepCount}/{MaxStep} | C++Time: {currentSimTime:F3}s");
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        //if (taskProfile != null) taskProfile.Heuristic(this, actionsOut, runtimeState);

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
    }

    // 매니저에서 1/4 주기마다 호출할 위상(Phase) 캡처 함수
    public void TriggerIntermediatePhase(int phaseIndex)
    {
        if (taskProfile != null) 
        {
            taskProfile.OnIntermediatePhase(this, runtimeState, phaseIndex);
        }
    }

    // =================================================================
    // 🌟 유틸리티 도구 모음
    // =================================================================

    // 길이가 0인 벡터 정규화 시 발생하는 NaN 오류를 막는 방어 함수[cite: 1, 3, 10]
    public Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        return v.sqrMagnitude > 1e-8f ? v.normalized : fallback;
    }

    //public float[] GetEgocentricVoxelState(int expectedVoxelCount, int centerIdx, int fwdA, int fwdB, int rightA, int rightB)
    public float[] GetEgocentricVoxelState(int expectedVoxelCount, int centerIdx, int fwdA, int fwdB, int rightA, int rightB, Transform currentTarget = null)
    {
        float[] stateArray = new float[296];
        int index = 0;

        if (PhysicsInfo == null || PhysicsInfo.lastStatePtr == IntPtr.Zero || PhysicsInfo.lastVoxelCount < expectedVoxelCount)
            return stateArray; 

        VoxelRealTimeState[] voxels = new VoxelRealTimeState[expectedVoxelCount];
        int structSize = Marshal.SizeOf(typeof(VoxelRealTimeState));
        IntPtr currentPtr = PhysicsInfo.lastStatePtr;

        for (int i = 0; i < expectedVoxelCount; i++)
        {
            voxels[i] = (VoxelRealTimeState)Marshal.PtrToStructure(currentPtr, typeof(VoxelRealTimeState));

            // NaN 유입 시 즉각 빈 배열 반환하여 신경망 붕괴 차단[cite: 1, 3, 10]
            if (float.IsNaN(voxels[i].pos.x) || float.IsNaN(voxels[i].vel.x))
                return new float[296]; 

            currentPtr = new IntPtr(currentPtr.ToInt64() + structSize);
        }

        Vector3 dirZ = voxels[fwdA].pos - voxels[fwdB].pos;
        Vector3 localZ = SafeNormalize(dirZ, Vector3.forward);

        Vector3 dirTempX = voxels[rightA].pos - voxels[rightB].pos;
        Vector3 tempX = SafeNormalize(dirTempX, Vector3.right);

        Vector3 dirY = Vector3.Cross(localZ, tempX);
        Vector3 localY = SafeNormalize(dirY, Vector3.up);

        Vector3 dirX = Vector3.Cross(localY, localZ);
        Vector3 localX = SafeNormalize(dirX, Vector3.right);

        
        // 🌟 taskObjective 대신 넘겨받은 currentTarget을 사용합니다.
        if (currentTarget != null)
        {
            Vector3 targetLocalPos = PhysicsInfo.transform.InverseTransformPoint(currentTarget.position);
            Vector3 localTargetDir = targetLocalPos - voxels[centerIdx].pos;

            float localTargetX = Vector3.Dot(localTargetDir, localX);
            float localTargetZ = Vector3.Dot(localTargetDir, localZ);

            float distanceXZ = new Vector2(localTargetX, localTargetZ).magnitude;
            float yawAngle = Mathf.Atan2(localTargetX, localTargetZ) / Mathf.PI; 

            stateArray[index++] = yawAngle;
            stateArray[index++] = distanceXZ;
        }
        else
        {
            stateArray[index++] = 0f;
            stateArray[index++] = 0f;
        }

        Vector3 centerVel = voxels[centerIdx].vel;
        Vector3 centerAngVel = voxels[centerIdx].angVel;
        
        Vector3 localCenterVel = new Vector3(Vector3.Dot(centerVel, localX), Vector3.Dot(centerVel, localY), Vector3.Dot(centerVel, localZ));
        Vector3 localCenterAngVel = new Vector3(Vector3.Dot(centerAngVel, localX), Vector3.Dot(centerAngVel, localY), Vector3.Dot(centerAngVel, localZ));

        stateArray[index++] = localCenterVel.x; stateArray[index++] = localCenterVel.y; stateArray[index++] = localCenterVel.z;
        stateArray[index++] = localCenterAngVel.x; stateArray[index++] = localCenterAngVel.y; stateArray[index++] = localCenterAngVel.z;

        for (int i = 0; i < expectedVoxelCount; i++)
        {
            if (i == centerIdx) continue; 

            Vector3 relPos = voxels[i].pos - voxels[centerIdx].pos;
            Vector3 relVel = voxels[i].vel - voxels[centerIdx].vel;
            Vector3 relAngVel = voxels[i].angVel - voxels[centerIdx].angVel;

            Vector3 localRelPos = new Vector3(Vector3.Dot(relPos, localX), Vector3.Dot(relPos, localY), Vector3.Dot(relPos, localZ));
            Vector3 localRelVel = new Vector3(Vector3.Dot(relVel, localX), Vector3.Dot(relVel, localY), Vector3.Dot(relVel, localZ));
            Vector3 localRelAngVel = new Vector3(Vector3.Dot(relAngVel, localX), Vector3.Dot(relAngVel, localY), Vector3.Dot(relAngVel, localZ));

            stateArray[index++] = localRelPos.x; stateArray[index++] = localRelPos.y; stateArray[index++] = localRelPos.z;
            stateArray[index++] = localRelVel.x; stateArray[index++] = localRelVel.y; stateArray[index++] = localRelVel.z;
            stateArray[index++] = localRelAngVel.x; stateArray[index++] = localRelAngVel.y; stateArray[index++] = localRelAngVel.z;
        }

        return stateArray;
    }

    public Vector3 GetRobotCenterOfMass()
    {
        if (PhysicsInfo != null && PhysicsInfo.lastStatePtr != IntPtr.Zero && PhysicsInfo.lastVoxelCount > 0)
        {   
            int totalVoxels = PhysicsInfo.lastVoxelCount; 
            if (totalVoxels == 0) return Vector3.zero;

            int structSize = Marshal.SizeOf(typeof(VoxelRealTimeState));
            IntPtr currentPtr = PhysicsInfo.lastStatePtr;
            
            Vector3 sum = Vector3.zero;

            for (int i = 0; i < totalVoxels; i++)
            {
                VoxelRealTimeState state = (VoxelRealTimeState)Marshal.PtrToStructure(currentPtr, typeof(VoxelRealTimeState));

                // 쓰레기값이나 폭발(NaN)을 감지하면 즉시 (0,0,0) 반환하여 유니티 다운 방지[cite: 1, 3, 10]
                if (float.IsNaN(state.pos.x) || float.IsNaN(state.pos.y) || float.IsNaN(state.pos.z))
                {
                    return Vector3.zero; 
                }

                sum += state.pos;
                currentPtr = new IntPtr(currentPtr.ToInt64() + structSize);
            }
            return sum / totalVoxels;
        }
        return Vector3.zero; 
    }
}