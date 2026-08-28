/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelPhysicsInfo.cs]
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

using Unity.MLAgents; // 🌟 Training 모드인지 Inference 모드인지 구분하기 위해서

using System.Collections.Generic; // [추가] 복셀-->링크 Dictionary 사용을 위해 필요

// C++ 구조체 매핑
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VoxelRealTimeState
{
    public int index;
    public Vector3 pos;
    public Vector3 vel;
    public Quaternion rot;
    public Vector3 angVel;
    public Vector3 appliedForce;
    public float pressure;
}

// [추가] C++ 구조체 매핑 (링크 용)
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LinkRealTimeState
{
    public int voxelIndex1;
    public int voxelIndex2;
    public float stress;
}

public class VoxelPhysicsInfo : MonoBehaviour
{
    const string DLL_NAME = VoxelDllConfig.DLL_NAME;
    
/*    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Get_Voxel_RealTime_States(int robotIdx, out IntPtr stateData, 
                                                        out int voxelCount, out double robotStep);
*/
    // [수정] DllImport 서명에 링크 인자 추가
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Get_Voxel_RealTime_States(int robotIdx, 
                                                        out IntPtr stateData, out int voxelCount, 
                                                        out IntPtr linkData, out int linkCount, // [추가]
                                                        out double robotStep);

    
    [Header("Realtime Voxel Status")]
    [ReadOnly] public int robotIndex = 0;
    [ReadOnly] public int numTotalVoxel = 0;
    [ReadOnly] public int numMotorVoxel = 0;
    [ReadOnly] public double currentRobotStep = 0.0;
    

    
    public int inspectVoxelIndex = 0; 
    [ReadOnly] public Vector3 currentPos;
    [ReadOnly] public Vector3 currentVel;
    [ReadOnly] public Vector3 currentAngVel;
    [ReadOnly] public Vector3 currentAppliedForce;
    [ReadOnly] public float currentPressure;

    // ▼▼▼ [추가] 링크 모니터링 변수 ▼▼▼
    public int inspectLinkIndex = 0;
    [ReadOnly] public int currentLinkVoxel1 = -1;
    [ReadOnly] public int currentLinkVoxel2 = -1;
    [ReadOnly] public float currentLinkStress = 0f;



    // 외부에서 포인터를 읽을 수 있도록 저장
    public IntPtr lastStatePtr = IntPtr.Zero;
    [ReadOnly] public int lastVoxelCount = 0;

    // [추가] 링크 포인터 및 빠른 검색용 딕셔너리
    public IntPtr lastLinkStatePtr = IntPtr.Zero;
    [ReadOnly] public int lastLinkCount = 0;

    //private Dictionary<(int, int), float> linkStressMap = new Dictionary<(int, int), float>();

    // 🌟 변경: 응력(stress) 값을 캐싱하는 것이 아니라, C++ 배열 상의 '인덱스(위치)'를 캐싱합니다.
    private Dictionary<(int, int), int> linkIndexCache = new Dictionary<(int, int), int>();
    private int cachedLinkCount = -1; // 링크 개수 변화 감지용


    // 🌟 1. VoxelEngineCore 참조 변수 추가
    private VoxelEngineCore engineCore;

    public void Fill_Robot_Param( int num_voxel, int num_motor )
    {
        numTotalVoxel = num_voxel;
        numMotorVoxel = num_motor;

        // 🌟 2. 시작 시 씬에 있는 VoxelEngineCore를 찾아 연결합니다.
        engineCore = FindAnyObjectByType<VoxelEngineCore>();
    }


    
    private void Update()
    {
        // 🌟 파이썬과 통신 중(훈련 모드)이 아닐 때(= Inference 모드일 때)만 모니터링 실행
        //if (!Academy.Instance.IsCommunicatorOn) MonitorVoxelState();

        // 🌟 3. Academy 대신 VoxelEngineCore의 is_ml_agent 값을 직접 읽어옵니다.
        // RL 모드가 꺼져있을 때(false)만 매 프레임 관측(모니터링)을 실행합니다.
        if (engineCore != null && !engineCore.is_ml_agent) ForceMonitorVoxelState();
    }

    // 기존의 private unsafe void MonitorVoxelState()를 다음과 같이 변경합니다.
    public unsafe void ForceMonitorVoxelState()
    {
        // [수정] 함수 호출부
        Get_Voxel_RealTime_States(robotIndex, out lastStatePtr, out lastVoxelCount, 
                                  out lastLinkStatePtr, out lastLinkCount, out currentRobotStep);

    
        BuildLinkLookupMap(); // [추가] 링크 맵 구축

    #if UNITY_EDITOR    
        UpdateEditorInspector();
    #endif
    }

    // [추가] 매 프레임 데이터를 딕셔너리에 담아 복셀-->링크 O(1) 검색을 가능케 하는 내부 함수
    private unsafe void BuildLinkLookupMap()
    {
    /*    if (lastLinkCount > 0 && lastLinkStatePtr != IntPtr.Zero)
        {
            linkStressMap.Clear();
            LinkRealTimeState* links = (LinkRealTimeState*)lastLinkStatePtr;

            for (int i = 0; i < lastLinkCount; i++)
            {
                int v1 = links[i].voxelIndex1;
                int v2 = links[i].voxelIndex2;
                float stress = links[i].stress;

                // 양방향 모두 캐싱 (순서에 상관없이 검색할 수 있도록)
                linkStressMap[(v1, v2)] = stress;
                linkStressMap[(v2, v1)] = stress;
            }
        }
    */
        // 🌟 핵심 최적화: 링크 개수가 변했을 때(최초 로딩 또는 링크 파손 시) 딱 한 번만 딕셔너리를 만듭니다!
        if (lastLinkCount > 0 && lastLinkStatePtr != IntPtr.Zero && lastLinkCount != cachedLinkCount)
        {
            linkIndexCache.Clear();
            LinkRealTimeState* links = (LinkRealTimeState*)lastLinkStatePtr;

            for (int i = 0; i < lastLinkCount; i++)
            {
                int v1 = links[i].voxelIndex1;
                int v2 = links[i].voxelIndex2;

                // 응력 값이 아닌, 이 링크가 배열의 몇 번째(i)에 있는지를 저장합니다.
                linkIndexCache[(v1, v2)] = i;
                linkIndexCache[(v2, v1)] = i;
            }
            cachedLinkCount = lastLinkCount; // 캐싱 완료 기록
        }
    }

    private unsafe void UpdateEditorInspector()
    {
        if (lastVoxelCount > 0 && lastStatePtr != IntPtr.Zero)
        {
            VoxelRealTimeState* states = (VoxelRealTimeState*)lastStatePtr;
            
            if (inspectVoxelIndex >= 0 && inspectVoxelIndex < lastVoxelCount)
            {
                currentPos = states[inspectVoxelIndex].pos;
                currentVel = states[inspectVoxelIndex].vel;
                currentAngVel = states[inspectVoxelIndex].angVel;
                currentAppliedForce = states[inspectVoxelIndex].appliedForce;
                currentPressure = states[inspectVoxelIndex].pressure;
            }
        }

        // ▼▼▼ [추가] 링크 데이터 읽기 ▼▼▼
        if (lastLinkCount > 0 && lastLinkStatePtr != IntPtr.Zero)
        {
            LinkRealTimeState* links = (LinkRealTimeState*)lastLinkStatePtr;
            
            // 입력한 링크 인덱스가 유효한 범위인지 확인
            if (inspectLinkIndex >= 0 && inspectLinkIndex < lastLinkCount)
            {
                currentLinkVoxel1 = links[inspectLinkIndex].voxelIndex1;
                currentLinkVoxel2 = links[inspectLinkIndex].voxelIndex2;
                currentLinkStress = links[inspectLinkIndex].stress;
            }
        }
    }


// 주의: 기존 Update() 안에 있던 MonitorVoxelState() 호출은 삭제하거나 주석 처리하세요.
// 이제 관측은 에이전트가 필요할 때만 딱 한 번 강제로 수행합니다.

    // =========================================================
    // [추가] 특정 두 복셀의 인덱스를 주었을 때 그 사이 링크의 물리량(Stress) 꺼내기
    // =========================================================
    public unsafe float GetLinkStress(int voxelIdxA, int voxelIdxB)
    {
        // Voxel A와 Voxel B의 연결을 확인 (방향 무관하게 찾아짐)
        //if (linkStressMap.TryGetValue((voxelIdxA, voxelIdxB), out float stressVal)) return stressVal;

        // 🌟 요청받은 복셀 쌍이 캐시(배열 인덱스)에 있는지 확인
        if (lastLinkStatePtr != IntPtr.Zero && linkIndexCache.TryGetValue((voxelIdxA, voxelIdxB), out int arrayIndex))
        {
            // C++의 메모리 포인터에서 직접 실시간 응력(Stress) 값만 읽어서 즉시 반환! (매 프레임 딕셔너리 연산 0)
            return ((LinkRealTimeState*)lastLinkStatePtr)[arrayIndex].stress;
        }
        
        // 두 복셀 사이에 링크가 없으면 0 반환
        return 0f;
    }

    // =========================================================
    // 🌟 특정 인덱스의 실시간 좌표 꺼내기 (이중 스왑 제거)
    // =========================================================
    public unsafe Vector3 GetVoxelPosition(int index)
    {
        if (lastStatePtr != IntPtr.Zero && index >= 0 && index < lastVoxelCount)
        {
            // C++에서 이미 (X, Z, Y)로 넘어왔으므로 그대로 리턴!
            return ((VoxelRealTimeState*)lastStatePtr)[index].pos;
        }
        return Vector3.zero;
    }

    // =========================================================
    // 🌟 스케일이 반영된 월드 좌표 기준으로 충돌 찾기 (이중 스왑 제거)
    // =========================================================
    public unsafe bool RaycastVoxel(Ray ray, float pickRadius, out float hitDistance, out int hitVoxelIdx, out Vector3 hitPos)
    {
        hitDistance = float.MaxValue;
        hitVoxelIdx = -1;
        hitPos = Vector3.zero;
        bool found = false;

        if (lastStatePtr == IntPtr.Zero || lastVoxelCount <= 0) return false;

        VoxelRealTimeState* states = (VoxelRealTimeState*)lastStatePtr;

        for (int i = 0; i < lastVoxelCount; i++)
        {
            // 1. C++에서 이미 유니티 로컬 좌표계로 완벽하게 넘어옴
            Vector3 vPosLocal = states[i].pos;
            
            // 2. 유니티의 월드 좌표(10배 스케일/이동/회전 적용)로 변환
            Vector3 vPosWorld = transform.TransformPoint(vPosLocal);
            
            Vector3 rayToVoxel = vPosWorld - ray.origin;
            float projectionLength = Vector3.Dot(rayToVoxel, ray.direction);

            if (projectionLength > 0) 
            {
                Vector3 projectedPoint = ray.origin + ray.direction * projectionLength;
                
                // 거리를 잴 때 변환된 월드 좌표(vPosWorld) 사용
                float distToRay = Vector3.Distance(vPosWorld, projectedPoint);

                if (distToRay <= pickRadius && projectionLength < hitDistance)
                {
                    hitDistance = projectionLength;
                    hitVoxelIdx = i;
                    hitPos = vPosWorld; 
                    found = true;
                }
            }
        }
        return found;
    }

    // =========================================================
    // 투시경(Gizmo) 그리기 (이중 스왑 제거)
    // =========================================================
    private unsafe void OnDrawGizmosSelected()
    {
        if (lastStatePtr == IntPtr.Zero || lastVoxelCount <= 0) return;

        VoxelRealTimeState* states = (VoxelRealTimeState*)lastStatePtr;

        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        int drawCount = Mathf.Min(lastVoxelCount, 1000); 

        for (int i = 0; i < drawCount; i++)
        {
            Vector3 vPosLocal = states[i].pos; // 그대로 사용
            Vector3 vPosWorld = transform.TransformPoint(vPosLocal); // 스케일(10배) 변환
            
            Gizmos.DrawSphere(vPosWorld, 0.05f); // Scene 뷰에 렌더링
        }
    }


    // =========================================================
    // 빌드 버전(실행 파일) 화면에 대시보드를 그리는 인게임 UI 함수
    // =========================================================
/*
    private bool showDashboardInGame = false;

    // 🌟 [최적화 1] 매 프레임 생성되지 않도록 변수를 밖으로 뺌
    private GUIStyle boxStyle;
    private GUIStyle titleStyle;
    
    // 🌟 [최적화 2] 텍스트 캐싱용 타이머 변수
    private string cachedDashboardText = "";
    private float dashboardUpdateTimer = 0f;
    private const float DASHBOARD_UPDATE_INTERVAL = 0.2f; // 0.2초마다 갱신 (초당 5번)

    private void OnGUI()
    {
#if UNITY_SERVER
        return;
#endif
        if (!showDashboardInGame || robotIndex != 0) return;

        // 1. GUIStyle 딱 한 번만 메모리 할당 (Zero-Allocation)
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 14;
            boxStyle.alignment = TextAnchor.UpperLeft;
            boxStyle.normal.textColor = Color.white;

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 15;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.yellow;
        }

        // 2. 타이머를 돌려서 0.2초에 한 번만 거대한 문자열을 만듦 (가비지 95% 감소!)
        dashboardUpdateTimer += Time.deltaTime;
        if (dashboardUpdateTimer >= DASHBOARD_UPDATE_INTERVAL || string.IsNullOrEmpty(cachedDashboardText))
        {
            dashboardUpdateTimer = 0f;
            
            // 텍스트를 만들 때만 인스펙터 변수 갱신
            UpdateEditorInspector();

            cachedDashboardText = 
                $"[ 로봇 인덱스 ] {robotIndex}\n" +
                $"[ 현재 스텝 ] {currentRobotStep:F3}\n" +
                $"[ 전체 복셀 수 ] {numTotalVoxel} 개\n" +
                $"[ 모터 복셀 수 ] {numMotorVoxel} 개\n" +
                $"-----------------------------------\n" +
                $"[ 대상 복셀 번호 ] {inspectVoxelIndex}\n" +
                $"[ 위치 ] {currentPos.ToString("F4")}\n" +
                $"[ 속도 ] {currentVel.ToString("F4")}\n" +
                $"[ 각속도 ] {currentAngVel.ToString("F4")}\n" +
                $"[ 외력 ] {currentAppliedForce.ToString("F4")}\n" +
                $"[ 압력 ] {currentPressure.ToString("F4")}\n" +
                $"-----------------------------------\n" +
                $"수신된 복셀 데이터 : {lastVoxelCount} 개\n" +
                $"수신된 링크 데이터 : {lastLinkCount} 개";
        }

        // 반투명한 검은색 배경 설정
        GUI.backgroundColor = new Color(0, 0, 0, 0.8f);

        // 3. 만들어둔 스타일과 캐싱된 텍스트를 재사용하여 그리기만 함
        GUI.Box(new Rect(10, 10, 300, 320), "\n" + cachedDashboardText, boxStyle);
        GUI.Label(new Rect(20, 15, 280, 25), "🤖 로봇 물리 실시간 대시보드", titleStyle);
    }
*/


}