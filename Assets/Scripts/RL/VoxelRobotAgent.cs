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

using System; // IntPtr을 사용하기 위해 필수!
using System.Runtime.InteropServices; // Marshal(메모리 변환)을 사용하기 위해 필수!

using UnityEngine.InputSystem;

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class VoxelRobotAgent : Agent
{
    [DllImport(VoxelDllConfig.DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Reset_Voxel_Unity(int robotIdx);



    [Header("RL Environment Links")]
    public int robotIdx = 0;
    public Transform targetTransform; // 추적 대상 Target 오브젝트

    private VoxelRLManager rlManager;
    private VoxelPhysicsInfo physicsInfo;  // 기존 유니티 복셀 물리 상태 스크립트
    private float previousDistance;


    // [수정] 복셀의 진짜 회전 상태를 저장할 멤버 변수 (OnActionReceived에서 판정용)
    private Vector3 currentVoxelUpVector = Vector3.up;
    // [복구] 물리 엔진 공회전(Freeze) 감지용 변수
    private Vector3 lastCheckedPos = Vector3.zero;
    private int freezeCount = 0;


    
    public override void Initialize()
    {
        rlManager = FindAnyObjectByType<VoxelRLManager>();
        physicsInfo = GetComponent<VoxelPhysicsInfo>();
    }

    public override void OnEpisodeBegin()
    {
        Debug.Log($"Episode Begin!");

        // 1. Target 위치를 로봇 근처 일정 범위 내 랜덤 재배치
        if (targetTransform != null)
        {
            //targetTransform.localPosition = new Vector3( UnityEngine.Random.Range(-3f, 3f), 0.5f, UnityEngine.Random.Range(-3f, 3f) );

            // 1. 유니티 내장 함수를 이용해 랜덤한 2D 방향(길이가 1인 벡터)을 360도 무작위로 추출합니다.
            Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
            
            // 2. 타겟이 배치될 거리를 3.0 ~ 4.0 사이로 랜덤하게 뽑습니다.
            float randomDistance = UnityEngine.Random.Range(2.5f, 3.5f);
            
            // 3. 랜덤 방향에 랜덤 거리를 곱하여 3D 좌표(X, Z)로 변환해 줍니다. Y축은 0.5f로 고정합니다.
            targetTransform.localPosition = new Vector3( randomDirection.x * randomDistance - 0.25f , 0.5f, 
                                                         randomDirection.y * randomDistance - 0.25f         );
        }

        // 🌟 2. C++ 물리 엔진 속 로봇 완벽 초기화 호출!
        Reset_Voxel_Unity(robotIdx);
        
        // 3. 이전 거리 측정값 초기화
        Vector3 robotCoM = GetRobotCenterOfMass();
        if (targetTransform != null)
        {
            //previousDistance = Vector3.Distance(robotCoM, targetTransform.position);

            // 🌟 타겟의 좌표도 C++ 로컬 좌표로 똑같이 맞춰준 뒤 거리를 잽니다.
            Vector3 targetLocalPos = physicsInfo.transform.InverseTransformPoint(targetTransform.position);
            previousDistance = Vector3.Distance(robotCoM, targetLocalPos);
        }


        // 상태 초기화
        freezeCount = 0;
        lastCheckedPos = robotCoM;
        currentVoxelUpVector = Vector3.up;
    }

    

    // [추가] 0.5초 동안의 4단계 궤적을 저장할 내부 버퍼 (4단계 x 296개 값)
    private float[,] observationBuffer = new float[4, 296];

    // [추가] Manager가 1/4 지점마다 호출해 주는 저장 함수
    public void RecordIntermediateState(int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex > 3) return;

        // 현재 C++의 물리 상태를 296개의 float 배열로 가져옵니다.
        float[] currentState = GetEgocentricVoxelState();

        // 버퍼의 해당 위상(Phase)에 296개 값을 통째로 복사해 둡니다.
        for (int i = 0; i < 296; i++)
        {
            observationBuffer[phaseIndex, i] = currentState[i];
        }
    }

    public override void CollectObservations(VectorSensor sensor) 
    {
        
        // 4/4 지점에서 액션이 요청될 때, 버퍼에 쌓여있던 1/4 ~ 4/4의 
        // 1184개(296 x 4) 데이터를 순서대로 신경망에 모두 밀어 넣습니다!
        for (int phase = 0; phase < 4; phase++)
        {
            for (int i = 0; i < 296; i++)
            {
                sensor.AddObservation(observationBuffer[phase, i]);
            }
        }

        // 향후 관측 방식을 변경할 때 주석 처리만으로 쉽게 실험을 교체할 수 있습니다.
        //ObserveEgocentricVoxelState(sensor);
        
        // ObserveAlternativeState(sensor); // 다른 실험용 관측 함수 예시
    }



    // 🌟 [추가] 길이가 0인 벡터를 정규화할 때 NaN이 발생하는 것을 막는 안전 함수
    private Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        return v.sqrMagnitude > 1e-8f ? v.normalized : fallback;
    }
    
    /// 로컬 기저벡터(Egocentric)를 활용하여 33개 복셀의 상대 상태를 관측합니다.
    /// 총 관측 개수: 296개  
    // [수정] 이전의 ObserveEgocentricVoxelState 함수를 float[] 배열을 반환하도록 변경
    private float[] GetEgocentricVoxelState()
    {
        int expectedVoxelCount = 33;

        float[] stateArray = new float[296];
        int index = 0; // 배열 인덱스 카운터

        if (physicsInfo == null || physicsInfo.lastStatePtr == IntPtr.Zero || physicsInfo.lastVoxelCount < expectedVoxelCount)
        {
            return stateArray; // 오류 시 0으로 채워진 배열 반환
        }

        VoxelRealTimeState[] voxels = new VoxelRealTimeState[expectedVoxelCount];
        int structSize = Marshal.SizeOf(typeof(VoxelRealTimeState));
        IntPtr currentPtr = physicsInfo.lastStatePtr;

        for (int i = 0; i < expectedVoxelCount; i++)
        {
            voxels[i] = (VoxelRealTimeState)Marshal.PtrToStructure(currentPtr, typeof(VoxelRealTimeState));

            // 🌟 [추가 방어벽] C++에서 넘어온 데이터 자체가 이미 NaN으로 오염되었다면 즉시 중단 (현재 x만 검사)
            if (float.IsNaN(voxels[i].pos.x) || float.IsNaN(voxels[i].vel.x))
            {
                // 쓰레기값이 신경망에 들어가는 것을 막기 위해 0으로 채워진 안전한 배열 반환
                return new float[296]; 
            }

            currentPtr = new IntPtr(currentPtr.ToInt64() + structSize);
        }


        // 🌟 [수정] .normalized 대신 SafeNormalize 사용 (NaN 원천 차단)
        Vector3 dirZ = voxels[17].pos - voxels[15].pos;
        Vector3 localZ = SafeNormalize(dirZ, Vector3.forward);

        Vector3 dirTempX = voxels[21].pos - voxels[11].pos;
        Vector3 tempX = SafeNormalize(dirTempX, Vector3.right);

        Vector3 dirY = Vector3.Cross(localZ, tempX);
        Vector3 localY = SafeNormalize(dirY, Vector3.up);

        Vector3 dirX = Vector3.Cross(localY, localZ);
        Vector3 localX = SafeNormalize(dirX, Vector3.right);



        currentVoxelUpVector = localY;

        if (targetTransform != null)
        {
            // 🌟 타겟의 유니티 월드 좌표를 C++ 로컬 좌표계(스케일 포함)로 완벽히 역변환
            Vector3 targetLocalPos = physicsInfo.transform.InverseTransformPoint(targetTransform.position);
            Vector3 localTargetDir = targetLocalPos - voxels[16].pos;

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

        Vector3 localCenterVel = new Vector3(Vector3.Dot(voxels[16].vel, localX), Vector3.Dot(voxels[16].vel, localY), Vector3.Dot(voxels[16].vel, localZ));
        Vector3 localCenterAngVel = new Vector3(Vector3.Dot(voxels[16].angVel, localX), Vector3.Dot(voxels[16].angVel, localY), Vector3.Dot(voxels[16].angVel, localZ));

        stateArray[index++] = localCenterVel.x; stateArray[index++] = localCenterVel.y; stateArray[index++] = localCenterVel.z;
        stateArray[index++] = localCenterAngVel.x; stateArray[index++] = localCenterAngVel.y; stateArray[index++] = localCenterAngVel.z;

        for (int i = 0; i < expectedVoxelCount; i++)
        {
            if (i == 16) continue; 

            Vector3 relPos = voxels[i].pos - voxels[16].pos;
            Vector3 relVel = voxels[i].vel - voxels[16].vel;
            Vector3 relAngVel = voxels[i].angVel - voxels[16].angVel;

            Vector3 localRelPos = new Vector3(Vector3.Dot(relPos, localX), Vector3.Dot(relPos, localY), Vector3.Dot(relPos, localZ));
            Vector3 localRelVel = new Vector3(Vector3.Dot(relVel, localX), Vector3.Dot(relVel, localY), Vector3.Dot(relVel, localZ));
            Vector3 localRelAngVel = new Vector3(Vector3.Dot(relAngVel, localX), Vector3.Dot(relAngVel, localY), Vector3.Dot(relAngVel, localZ));

            stateArray[index++] = localRelPos.x; stateArray[index++] = localRelPos.y; stateArray[index++] = localRelPos.z;
            stateArray[index++] = localRelVel.x; stateArray[index++] = localRelVel.y; stateArray[index++] = localRelVel.z;
            stateArray[index++] = localRelAngVel.x; stateArray[index++] = localRelAngVel.y; stateArray[index++] = localRelAngVel.z;
        }

        return stateArray;
    }



    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        int senseFreq = MaxStep / 4;    // 에피소드의 25% 씩 진행상황 표시

        if (StepCount % senseFreq == 0) {
            float pRate = 100.0f*(float)StepCount/(float)MaxStep;
            double currentSimTime = (physicsInfo != null) ? physicsInfo.currentRobotStep : 0f;
            Debug.Log($"[VoxelRobotAgent] Episode Progress:[{pRate}%] Agent step: {StepCount}/{MaxStep} | C++Time: {currentSimTime:F3}s");
        }

        var continuousActions = actionBuffers.ContinuousActions;
        float[] actionArray = new float[continuousActions.Length];
        for (int i = 0; i < continuousActions.Length; i++) actionArray[i] = continuousActions[i];

        Vector3 currentCoM = GetRobotCenterOfMass();
        bool isDone = false; // 🌟 즉시 리턴하지 않고 플래그로 상태를 기록합니다.

        // [1. 공회전 체크]
        if (Vector3.Distance(currentCoM, lastCheckedPos) < 0.00001f) {
            freezeCount++;
            if (freezeCount > 5) {
                Debug.LogError($"[VoxelRobotAgent] Robot freeze not moving! Will reset.");
                AddReward(-1.0f); 
                isDone = true;
            }
        } else { freezeCount = 0; }
        lastCheckedPos = currentCoM; 

        // [2. 타겟 체크]
        if (!isDone && targetTransform != null) {
            Vector3 targetLocalPos = physicsInfo.transform.InverseTransformPoint(targetTransform.position);
            float currentDistance = Vector3.Distance(currentCoM, targetLocalPos);
            
            float rewardDelta = previousDistance - currentDistance;
            AddReward(rewardDelta * 2.0f);
            previousDistance = currentDistance;

            if (currentDistance < 0.2f) {
                Debug.Log($"[VoxelRobotAgent] [End Episode] Target reached! (current step: {StepCount})");
                AddReward(5.0f);
                isDone = true;
            }
        }

        // [3. 추락 체크]
        if (!isDone && (currentCoM.y < -2.0f || Vector3.Dot(currentVoxelUpVector, Vector3.up) < 0f)) {
            Debug.Log($"[VoxelRobotAgent] [End Episode] Fall down! (current step: {StepCount})");
            AddReward(-1.0f);
            isDone = true;
        }

        // [4. 에피소드 종료 처리]
        if (isDone) 
        {
            EndEpisode(); // 여기서 C++ 메모리가 리셋됨 (현재 C++ 스레드는 멈춰있으므로 100% 안전!)
        }
        else
        {
            // 🌟 시간 지연 벌점(Existential Penalty) 추가
            // 에피소드가 끝나지 않고 살아있는 매 스텝마다 아주 작은 마이너스 보상을 줍니다.
            // 이렇게 하면 가만히 서서 0점을 유지하려는 꼼수를 부리지 못하고, 
            // 살을 깎는 고통을 피하기 위해 어떻게든 타겟을 향해 미친 듯이 돌진하게 됩니다!
            if (MaxStep > 0)    AddReward(-1.0f / MaxStep); 
            else                AddReward(-0.001f);
        }

        // 🌟 [5. 크래시 방지 핵심] 
        // 리셋이 완전히 끝난 후, 가장 마지막에 액션을 제출하여 C++ 스레드를 가동시킵니다!
        rlManager.SubmitAction(this, actionArray);
    }



    public override void Heuristic(in ActionBuffers actionsOut)
    {
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

    private Vector3 GetRobotCenterOfMass()
    {
        // 🌟 physicsInfo.lastVoxelCount 를 그대로 사용하시면 됩니다!        
        if (physicsInfo != null && physicsInfo.lastStatePtr != IntPtr.Zero && physicsInfo.lastVoxelCount > 0)
        {   
            int totalVoxels = physicsInfo.lastVoxelCount; // 🌟 사용자님의 멤버 변수 사용
            // 🌟 방어벽 1: 0으로 나누기 방지
            if (totalVoxels == 0) return Vector3.zero;

            int structSize = Marshal.SizeOf(typeof(VoxelRealTimeState));
            IntPtr currentPtr = physicsInfo.lastStatePtr;

            
            Vector3 sum = Vector3.zero;

            for (int i = 0; i < totalVoxels; i++)
            {
                // 포인터에서 구조체를 읽어와 위치(pos)를 누적
                VoxelRealTimeState state = (VoxelRealTimeState)Marshal.PtrToStructure(currentPtr, typeof(VoxelRealTimeState));

                // 🌟 방어벽 2: C++에서 넘어온 개별 좌표가 NaN인지 검사
                if (float.IsNaN(state.pos.x) || float.IsNaN(state.pos.y) || float.IsNaN(state.pos.z))
                {
                    // 물리 폭발이나 쓰레기값 감지 시 안전한 기본값 반환
                    //return Vector3.zero;

                    Debug.LogError($"[VoxelRobotAgent] NaN Detected in Physics! Resetting Robot {robotIdx}.");
                    //EndEpisode(); // 🌟 쓰레기값 대신 즉시 에피소드 강제 리셋
                    return lastCheckedPos; // 🌟 0이 아닌 마지막 정상 위치 반환하여 튀는 현상 방지
                }

                sum += state.pos;

                // 다음 복셀 데이터 위치로 이동
                currentPtr = new IntPtr(currentPtr.ToInt64() + structSize);
            }

            // 전체 위치의 평균값(무게중심) 반환
            return sum / totalVoxels;
        }
        
        return lastCheckedPos; // 포인터가 없을 때 안전한 기본값
    }


    
}



/*   
    private void ObserveEgocentricVoxelState(VectorSensor sensor)
    {
        int expectedVoxelCount = 33;

        // 1. 메모리에서 33개 복셀 데이터 일괄 읽기 (최적화)
        if (physicsInfo == null || physicsInfo.lastStatePtr == IntPtr.Zero || physicsInfo.lastVoxelCount < expectedVoxelCount)
        {
            // 오류 또는 시뮬레이션 초기화 전일 경우 296개의 0으로 빈 공간 채우기
            for (int i = 0; i < 296; i++) sensor.AddObservation(0f);
            return;
        }

        VoxelRealTimeState[] voxels = new VoxelRealTimeState[expectedVoxelCount];
        int structSize = Marshal.SizeOf(typeof(VoxelRealTimeState));
        IntPtr currentPtr = physicsInfo.lastStatePtr;

        for (int i = 0; i < expectedVoxelCount; i++)
        {
            voxels[i] = (VoxelRealTimeState)Marshal.PtrToStructure(currentPtr, typeof(VoxelRealTimeState));
            currentPtr = new IntPtr(currentPtr.ToInt64() + structSize);
        }

        // 2. 로컬 기저벡터(Basis Vectors) 생성 및 직교화 보장
        // Z축 (Front): 17번과 15번의 차이 벡터
        Vector3 localZ = (voxels[17].pos - voxels[15].pos).normalized;
        
        // 임시 X축 (Right): 21번과 11번의 차이 벡터
        Vector3 tempX = (voxels[21].pos - voxels[11].pos).normalized;
        
        // Y축 (Up) 생성 및 완벽한 직교 X축 (Right) 재생성 (왼손 좌표계)
        Vector3 localY = Vector3.Cross(localZ, tempX).normalized;
        Vector3 localX = Vector3.Cross(localY, localZ).normalized;

        // [핵심] 보상 판정을 위해 계산된 진짜 로봇의 Up 벡터를 멤버 변수에 저장!
        currentVoxelUpVector = localY;

        // [Target 정보: 2개]
        if (targetTransform != null)
        {
            Vector3 worldTargetDir = targetTransform.position - voxels[16].pos;
            float localTargetX = Vector3.Dot(worldTargetDir, localX);
            float localTargetZ = Vector3.Dot(worldTargetDir, localZ);

            float distanceXZ = new Vector2(localTargetX, localTargetZ).magnitude;
            float yawAngle = Mathf.Atan2(localTargetX, localTargetZ) / Mathf.PI; // -1.0 ~ 1.0 정규화

            sensor.AddObservation(yawAngle);
            sensor.AddObservation(distanceXZ);
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }

        // [Center (16번) 정보: 6개] 
        // [개선] 16번 복셀의 속도와 각속도도 로컬 좌표계로 투영하여 "전진 속도/회전 속도"로 변환
        Vector3 localCenterVel = new Vector3(Vector3.Dot(voxels[16].vel, localX), Vector3.Dot(voxels[16].vel, localY), Vector3.Dot(voxels[16].vel, localZ));
        Vector3 localCenterAngVel = new Vector3(Vector3.Dot(voxels[16].angVel, localX), Vector3.Dot(voxels[16].angVel, localY), Vector3.Dot(voxels[16].angVel, localZ));

        sensor.AddObservation(localCenterVel);    
        sensor.AddObservation(localCenterAngVel); 


        // [나머지 32개 복셀의 상대 정보: 288개]
        for (int i = 0; i < expectedVoxelCount; i++)
        {
            if (i == 16) continue; // Center 복셀 건너뜀

            // 월드 좌표계에서의 Center 대비 상대(Relative) 벡터 계산
            Vector3 relPos = voxels[i].pos - voxels[16].pos;
            Vector3 relVel = voxels[i].vel - voxels[16].vel;
            Vector3 relAngVel = voxels[i].angVel - voxels[16].angVel;

            // 상대 벡터들을 로컬 기저벡터에 내적(Dot)하여 로컬 좌표계로 완벽히 변환
            Vector3 localRelPos = new Vector3(Vector3.Dot(relPos, localX), Vector3.Dot(relPos, localY), Vector3.Dot(relPos, localZ));
            Vector3 localRelVel = new Vector3(Vector3.Dot(relVel, localX), Vector3.Dot(relVel, localY), Vector3.Dot(relVel, localZ));
            Vector3 localRelAngVel = new Vector3(Vector3.Dot(relAngVel, localX), Vector3.Dot(relAngVel, localY), Vector3.Dot(relAngVel, localZ));

            sensor.AddObservation(localRelPos);    // 3
            sensor.AddObservation(localRelVel);    // 3
            sensor.AddObservation(localRelAngVel); // 3
        }
    }
*/


/*    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        // [추가] 10 스텝마다 유니티 스텝과 C++ 물리 시간을 비교하여 출력
        if (StepCount % 10 == 0) 
        {
            double currentSimTime = (physicsInfo != null) ? physicsInfo.currentRobotStep : 0f;
            Debug.Log($"[Step Check] Agent 스텝: {StepCount} / {MaxStep} | C++ 시뮬레이션 시간: {currentSimTime:F3}초");
        }


        // 1. ML-Agents Continuous Action 수신 및 제출
        var continuousActions = actionBuffers.ContinuousActions;
        float[] actionArray = new float[continuousActions.Length];
        for (int i = 0; i < continuousActions.Length; i++)
        {
            actionArray[i] = continuousActions[i];
        }

        // 2. 중앙 매니저에 이번 스텝 Action 제출 (LateUpdate 시점에 C++ 연산 가동)
        //rlManager.SubmitAction(robotIdx, actionArray);

        // 변경 전: rlManager.SubmitAction(robotIdx, actionArray);
        // 변경 후: 자기 자신(this)을 넘겨 매니저가 정확한 버퍼 위치를 찾게 합니다.
        rlManager.SubmitAction(this, actionArray);

        // 3. 보상(Reward) 및 에피소드 종료 조건 판정
        Vector3 currentCoM = GetRobotCenterOfMass();

        // [복구] 공회전(Freeze) 감지 레이더
        if (Vector3.Distance(currentCoM, lastCheckedPos) < 0.00001f)
        {
            freezeCount++;
            if (freezeCount > 5) 
            {
                Debug.LogError($"[RL 붕괴 감지] 물리 엔진 공회전 발생! 리셋합니다. (현재 시간: 약 {StepCount * 0.5f}초)");
                AddReward(-1.0f); 
                EndEpisode();
                return;
            }
        }
        else
        {
            freezeCount = 0; 
        }
        lastCheckedPos = currentCoM; 


        // 목표(Target) 보상 처리
        if (targetTransform != null)
        {
            //float currentDistance = Vector3.Distance(currentCoM, targetTransform.position);

            // 🌟 타겟의 좌표를 C++ 로컬 좌표로 변환하여 거리를 잽니다.
            Vector3 targetLocalPos = physicsInfo.transform.InverseTransformPoint(targetTransform.position);
            float currentDistance = Vector3.Distance(currentCoM, targetLocalPos);

            
            // 타겟과의 거리가 줄어들면 플러스 보상
            float rewardDelta = previousDistance - currentDistance;
            AddReward(rewardDelta * 2.0f);
            previousDistance = currentDistance;

            // 목표 도달 성공
            if (currentDistance < 0.2f)
            {
                Debug.Log($"[에피소드 종료] 타겟 도달! (현재 스텝: {StepCount})");
                AddReward(5.0f);
                EndEpisode();
                return; // 도달 후 아래 추락 체크 방지
            }
        }

        // [수정] 진짜 복셀의 상방향 벡터(currentVoxelUpVector)를 사용하여 뒤집힘 판정!
        if (currentCoM.y < -2.0f || Vector3.Dot(currentVoxelUpVector, Vector3.up) < 0f)
        {
            Debug.Log($"[에피소드 종료] 넘어지거나 추락함! (현재 스텝: {StepCount})");
            AddReward(-1.0f);
            EndEpisode();
        }
    }
*/




/*
    public override void CollectObservations(VectorSensor sensor) 
    {
        Vector3 robotCoM = GetRobotCenterOfMass();

        // 1. Target 방향 및 거리 (4)
        if (targetTransform != null)
        {
            Vector3 dirToTarget = (targetTransform.position - robotCoM).normalized;
            sensor.AddObservation(dirToTarget); // (3)
            sensor.AddObservation(Vector3.Distance(robotCoM, targetTransform.position)); // (1)
        }
        else
        {
            // 타겟이 할당되지 않았을 때도 빈 값 4개를 채워넣어 배열 크기(19)를 무조건 유지합니다.
            sensor.AddObservation(Vector3.zero); // (3)
            sensor.AddObservation(0f);           // (1)
        }

        // 2. 로봇 상방(Up) 벡터 (3)
        sensor.AddObservation(transform.up); 

        // 3. 개별 센서/복셀 상태 관측 (C++ 메모리 포인터인 lastStatePtr 직접 읽기)
        if (physicsInfo != null && physicsInfo.lastStatePtr != IntPtr.Zero)
        {
            // 구조체 하나의 크기를 계산
            int structSize = Marshal.SizeOf(typeof(VoxelRealTimeState));
            IntPtr currentPtr = physicsInfo.lastStatePtr;

            // RL 학습 속도를 위해 앞의 주요 복셀 4개 정도만 샘플링하여 관측 (원하는 개수로 조절)
            int checkCount = 4; 
            
            for (int i = 0; i < checkCount; i++)
            {
                // 메모리 포인터 위치의 데이터를 C# VoxelRealTimeState 구조체로 변환
                VoxelRealTimeState state = (VoxelRealTimeState)Marshal.PtrToStructure(currentPtr, typeof(VoxelRealTimeState));
                

                // [추가] ML-Agents가 조용히 리셋하기 전에 우리가 먼저 에러 로그를 낚아챕니다!
                if (float.IsNaN(state.vel.x) || float.IsNaN(state.pos.x))
                {
                    Debug.LogError($"[치명적 오류] 복셀 물리 엔진 붕괴! NaN 관측됨. (Agent Step: {StepCount}, 생존 시간: 약 {StepCount * 0.5f}초)");
                }
                

                // 인공지능에게 해당 복셀의 현재 속도를 알려줌
                sensor.AddObservation(state.vel); // (3 values * 4 = 12)

                // 다음 복셀 데이터 위치로 포인터 이동
                currentPtr = new IntPtr(currentPtr.ToInt64() + structSize);
            }
        }
        else
        {
            // 포인터가 비어있을 경우 (시뮬레이션 초기화 전) 0으로 채움 (Behavior Parameters 사이즈 오류 방지)
            for (int i = 0; i < 4; i++) 
            {
                sensor.AddObservation(Vector3.zero);
            }
        }
    }
    */


    
    