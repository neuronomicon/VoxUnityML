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



public class VoxelRobotAgent : Agent // ML-Agents의 Agent 클래스 상속[cite: 1, 3, 11]
{
    [DllImport(VoxelDllConfig.DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void CPP_Reset_Voxel_Unity(int robotIdx); // C++ 초기화 함수 연결[cite: 1, 3, 11]

    [Header("🤖 Robot ID")]
    public int robotIdx = 0;

    [HideInInspector] public VoxelRLManager rlManager;
    [HideInInspector] public VoxelPhysicsInfo PhysicsInfo { get; private set; }


    
    [HideInInspector] public int BufferIndex = -1;   // Manager 가 주입
    [Header("Debug")]
    public bool logEpisodeProgress = false;



    [Header("🧠 Training Logic Profile (Drag S.O. Asset!)")]
    public RobotTaskProfile taskProfile;

    [SerializeReference] 
    public RobotTaskState runtimeState;

    
    // 로컬 프레임의 up 축. 뒤집힘 감지 + 프레임 퇴화 시 폴백에 사용
    public Vector3 LastUpVector { get; private set; } = Vector3.up;
    // 직전 스텝의 액션 (관측 시점 기준). 마르코프 복구용
    public float[] PrevAction => actionBuffer;



    // 타겟 정보 3개 + 내 속도/각속도 6개 + 복셀 정보 6 * 33 ==> 207개    
    public int StateBufferSize { get; private set; } 
    private float[] egocentricStateBuffer;

    // 1. 최상단 변수 선언부에 액션 버퍼 추가
    private float[] actionBuffer;

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

    /*  // 🌟 배열 크기 동적 계산 (Y축이 빠져 타겟 정보가 2개로 유지됨)
        if (taskProfile != null)
        {
            StateBufferSize = 2 + 6 + 9 * (taskProfile.expectedVoxelCount - 1);
            egocentricStateBuffer = new float[StateBufferSize];

            // 🌟 액션 배열도 한 번만 메모리에 할당해 둡니다.
            actionBuffer = new float[taskProfile.continuousActions];
        }
    */
        if (taskProfile != null && taskProfile.body != null)
        {
            StateBufferSize = taskProfile.body.EgocentricStateSize;
            egocentricStateBuffer = new float[StateBufferSize];
            actionBuffer = new float[Mathf.Max(1, taskProfile.GetActionSize())];

            // ── 관측 서브셋 검증 ─────────────────────────────────────
            var b  = taskProfile.body;
            var oi = b.observedVoxelIndices;
            if (oi != null && oi.Length > 0)
            {
                int bad = 0, dup = 0;
                var seen = new System.Collections.Generic.HashSet<int>();
                foreach (int v in oi)
                {
                    if (v < 0 || v >= b.expectedVoxelCount) bad++;
                    if (!seen.Add(v)) dup++;
                }
                
                Debug.Log($"[VoxelRobotAgent] [{name}] body={b.name}  {oi.Length} obs subsets -> obs={taskProfile.GetObservationSize()}, act={taskProfile.GetActionSize()}"
                        + (bad > 0 ? $"   ⚠ Out of range: {bad}" : "")
                        + (dup > 0 ? $"   ⚠ Duplicates: {dup}"   : ""));
            }
            else
            {                
                Debug.Log($"[VoxelRobotAgent] [{name}] body={b.name}  observing all {b.expectedVoxelCount} voxels -> obs={taskProfile.GetObservationSize()}, act={taskProfile.GetActionSize()}");
            }
            // ─────────────────────────────────────────────────────
        }
        else
        {
            Debug.LogError($"[VoxelRobotAgent] [{name}] taskProfile or body is null.");
        }

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
            
            //bp.BrainParameters.VectorObservationSize = taskProfile.spaceSize;            
            //bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(taskProfile.continuousActions);

            bp.BrainParameters.VectorObservationSize = taskProfile.GetObservationSize();
            bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(taskProfile.GetActionSize());
        }
    }

    // =================================================================
    // 🌟 ML-Agents 생명주기 통제 (모든 결정을 뇌에게 위임)
    // =================================================================
    
    public override void OnEpisodeBegin()
    {
        LastUpVector = Vector3.up;
        if (actionBuffer != null) Array.Clear(actionBuffer, 0, actionBuffer.Length);

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
        
        
        if (actionBuffer == null) return;   // ① 방어
        int n = Mathf.Min(continuousActions.Length, actionBuffer.Length);   // ② 방어
        for (int i = 0; i < n; i++) actionBuffer[i] = continuousActions[i];

        if (rlManager != null) rlManager.SubmitAction(this, actionBuffer); 


/*        int senseFreq = MaxStep / 4;    // 에피소드의 25% 씩 진행상황 표시
        if (StepCount % senseFreq == 0) {
            float pRate = 100.0f*(float)StepCount/(float)MaxStep;
            double currentSimTime = (PhysicsInfo != null) ? PhysicsInfo.currentRobotStep : 0f;
            Debug.Log($"[VoxelRobotAgent] Episode Progress:[{pRate}%] Agent step: {StepCount}/{MaxStep} | C++Time: {currentSimTime:F3}s");
        }
*/
        if (logEpisodeProgress && MaxStep >= 4 && StepCount % (MaxStep / 4) == 0)
        {
            float pRate = 100.0f * (float)StepCount / (float)MaxStep;
            double t = (PhysicsInfo != null) ? PhysicsInfo.currentRobotStep : 0f;
            Debug.Log($"[VoxelRobotAgent] [{pRate}%] {StepCount}/{MaxStep} | C++Time: {t:F3}s");
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        if (taskProfile != null) taskProfile.Heuristic(this, actionsOut, runtimeState);
    }

    // 매니저에서 1/4 주기마다 호출할 위상(Phase) 캡처 함수
    public void TriggerIntermediatePhase(int phaseIndex, int cycleCount)
    {
        if (taskProfile != null) 
        {
            taskProfile.OnIntermediatePhase(this, runtimeState, phaseIndex, cycleCount);
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


    public unsafe float[] GetEgocentricVoxelState(RobotBodyProfile body, Transform currentTarget = null)
    {
        if (body == null || egocentricStateBuffer == null) return egocentricStateBuffer;

        int  N      = body.expectedVoxelCount;
        int  fwdA   = body.forwardVoxelA, fwdB   = body.forwardVoxelB;
        int  rightA = body.rightVoxelA,   rightB = body.rightVoxelB;
        bool useAng = body.includeVoxelAngVel;

        if (egocentricStateBuffer.Length < body.EgocentricStateSize)
        {
            StateBufferSize = body.EgocentricStateSize;
            egocentricStateBuffer = new float[StateBufferSize];
        }


        Array.Clear(egocentricStateBuffer, 0, egocentricStateBuffer.Length);

        if (PhysicsInfo == null || PhysicsInfo.lastStatePtr == IntPtr.Zero || PhysicsInfo.lastVoxelCount < N)
            return egocentricStateBuffer;

        VoxelRealTimeState* voxels = (VoxelRealTimeState*)PhysicsInfo.lastStatePtr.ToPointer();

        // NaN 차단 — 신경망 붕괴 방지
        for (int i = 0; i < N; i++)
            if (float.IsNaN(voxels[i].pos.x) || float.IsNaN(voxels[i].vel.x))
                return egocentricStateBuffer;

        // ── CoM 기준값 (보상과 동일한 기준) ──
        Vector3 com = Vector3.zero, comVel = Vector3.zero, comAngVel = Vector3.zero;
        for (int i = 0; i < N; i++)
        {
            com       += voxels[i].pos;
            comVel    += voxels[i].vel;
            comAngVel += voxels[i].angVel;
        }
        float invN = 1f / N;
        com *= invN;  comVel *= invN;  comAngVel *= invN;

        // ── 로컬 프레임 (퇴화 방어) ──
        Vector3 localZ = SafeNormalize(voxels[fwdA].pos   - voxels[fwdB].pos,   Vector3.forward);
        Vector3 tempX  = SafeNormalize(voxels[rightA].pos - voxels[rightB].pos, Vector3.right);

        // 둘 다 단위벡터이므로 |cross| = sin(사잇각)
        Vector3 dirY = Vector3.Cross(localZ, tempX);
        Vector3 localY;
        if (dirY.sqrMagnitude < 0.01f)        // sin < 0.1 (약 5.7도) -> 퇴화
        {
            localY = LastUpVector;              // 직전 프레임 유지 -> 관측이 튀지 않음
        }
        else
        {
            localY = dirY.normalized;
            LastUpVector = localY;              // 뒤집힘 감지용
        }

        Vector3 localX = SafeNormalize(Vector3.Cross(localY, localZ), Vector3.right);

        int index = 0;

        // ── [0-2] 타겟: 방향 단위벡터 + 정규화 거리 (불연속 없음) ──
        if (currentTarget != null)
        {
            Vector3 targetLocalPos = PhysicsInfo.transform.InverseTransformPoint(currentTarget.position);
            Vector3 d = targetLocalPos - com;

            float tx   = Vector3.Dot(d, localX);
            float tz   = Vector3.Dot(d, localZ);
            float dist = Mathf.Sqrt(tx * tx + tz * tz);
            float inv  = dist > 1e-6f ? 1f / dist : 0f;

            egocentricStateBuffer[index++] = tx * inv;                                  // sin
            egocentricStateBuffer[index++] = tz * inv;                                  // cos
            egocentricStateBuffer[index++] = Mathf.Min(dist / body.targetDistScale, 2f);
        }
        else { index += 3; }   // 이미 0 으로 clear 됨

        // ── [3-8] CoM 속도 / 평균 각속도 ──
        egocentricStateBuffer[index++] = Vector3.Dot(comVel,    localX);
        egocentricStateBuffer[index++] = Vector3.Dot(comVel,    localY);
        egocentricStateBuffer[index++] = Vector3.Dot(comVel,    localZ);
        egocentricStateBuffer[index++] = Vector3.Dot(comAngVel, localX);
        egocentricStateBuffer[index++] = Vector3.Dot(comAngVel, localY);
        egocentricStateBuffer[index++] = Vector3.Dot(comAngVel, localZ);


        // ── [9~] 복셀별 CoM 상대 상태 (관측 서브셋) ──
        var obsIdx = body.observedVoxelIndices;
        bool useSubset = (obsIdx != null && obsIdx.Length > 0);
        int  M = useSubset ? obsIdx.Length : N;
        
        for (int k = 0; k < M; k++)
        {
            int i = useSubset ? obsIdx[k] : k;
            if (i < 0 || i >= N) { index += body.PerVoxelSize; continue; }   // 잘못된 인덱스는 0으로

            Vector3 relPos = voxels[i].pos - com;
            Vector3 relVel = voxels[i].vel - comVel;

            egocentricStateBuffer[index++] = Vector3.Dot(relPos, localX);
            egocentricStateBuffer[index++] = Vector3.Dot(relPos, localY);
            egocentricStateBuffer[index++] = Vector3.Dot(relPos, localZ);

            egocentricStateBuffer[index++] = Vector3.Dot(relVel, localX);
            egocentricStateBuffer[index++] = Vector3.Dot(relVel, localY);
            egocentricStateBuffer[index++] = Vector3.Dot(relVel, localZ);

            if (useAng)
            {
                Vector3 relAng = voxels[i].angVel - comAngVel;
                egocentricStateBuffer[index++] = Vector3.Dot(relAng, localX);
                egocentricStateBuffer[index++] = Vector3.Dot(relAng, localY);
                egocentricStateBuffer[index++] = Vector3.Dot(relAng, localZ);
            }
        }

        return egocentricStateBuffer;
    }

    // 🌟 unsafe 키워드 추가 및 포인터 직접 참조로 변경 (GC 완전 제거)
    public unsafe Vector3 GetRobotCenterOfMass(int voxelCount = 0)
    {
        if (PhysicsInfo == null || PhysicsInfo.lastStatePtr == IntPtr.Zero || PhysicsInfo.lastVoxelCount <= 0)
            return Vector3.zero;

        int totalVoxels = (voxelCount > 0 && voxelCount <= PhysicsInfo.lastVoxelCount)
                        ? voxelCount : PhysicsInfo.lastVoxelCount;

        VoxelRealTimeState* statePtr = (VoxelRealTimeState*)PhysicsInfo.lastStatePtr.ToPointer();
        Vector3 sum = Vector3.zero;

        for (int i = 0; i < totalVoxels; i++)
        {
            if (float.IsNaN(statePtr[i].pos.x) || float.IsNaN(statePtr[i].pos.y) || float.IsNaN(statePtr[i].pos.z))
                return Vector3.zero;

            sum += statePtr[i].pos;
        }
        return sum / totalVoxels;
    }


/*
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
*/

}