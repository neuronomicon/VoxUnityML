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
 *
 * [2026-09-05 수정 — R6 / Plan A]
 *   링크 응력 조회를 C# 해시 테이블에서 C++ 직접 탐색으로 전환.
 *
 *   [제거된 것]
 *     - linkIndexCache (Dictionary), linkIndexCacheArray (int[2000]), cachedLinkCount
 *     - MAX_VOXEL_SUPPORT 상수
 *     - BuildLinkLookupMap() 전체
 *
 *   [제거 사유]
 *     1) 해시 충돌 시 요청한 복셀 쌍인지 검증하지 않고 값을 반환 → 엉뚱한 링크의 응력
 *        (복셀 516개 → 링크 1500개를 2000칸에 해싱, 부하율 75%로 충돌 상시 발생)
 *     2) C++ 이 포인터→인덱스 변환 실패 시 넣던 -1 센티넬을 걸러내지 않아
 *        깨진 링크가 정상 슬롯을 점거 → 정상 쌍이 깨진 링크의 응력을 수신
 *     3) Mathf.Abs(int.MinValue) 는 OverflowException 을 던짐 (잠복 크래시)
 *
 *   [대체 방식]
 *     C++ FindLinkBetweenVoxels() 가 격자 좌표로 6방향 인접을 O(1) 판정.
 *     해시도 센티넬도 존재하지 않으므로 항상 정확.
 * ==============================================================================
 */

using System;
using System.Runtime.InteropServices;
using UnityEngine;

using Unity.MLAgents; // 🌟 Training 모드인지 Inference 모드인지 구분하기 위해서

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
// 🌟 [R6/Plan A] 더 이상 벌크 전송에 쓰이지 않지만, 외부 코드 호환을 위해 정의는 유지합니다.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LinkRealTimeState
{
    public int voxelIndex1;
    public int voxelIndex2;
    public float stress;
}

// 🌟 [R6/Plan A 추가] 배치 조회용 질의 구조체 (C++ 의 LinkQuery 와 1:1 대응)
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LinkQuery
{
    public int voxelIdxA;
    public int voxelIdxB;

    public LinkQuery(int a, int b) { voxelIdxA = a; voxelIdxB = b; }
}

public class VoxelPhysicsInfo : MonoBehaviour
{
    const string DLL_NAME = VoxelDllConfig.DLL_NAME;

    // [수정] DllImport 서명에 링크 인자 추가
    // 🌟 [R6/Plan A] 시그니처는 그대로 두되, C++ 은 링크 인자에 항상 null / 0 을 반환합니다.
    //    (C++ 쪽 호출처가 모두 정리되면 두 인자를 삭제해도 됩니다)
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Get_Voxel_RealTime_States(int robotIdx,
                                                        out IntPtr stateData, out int voxelCount,
                                                        out IntPtr linkData, out int linkCount,
                                                        out double robotStep);

    // =========================================================================
    // 🌟 [R6/Plan A 추가] 링크 온디맨드 조회 API
    // =========================================================================

    /// <summary>두 복셀 사이 링크의 축 응력. 인접하지 않으면 0.</summary>
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern float Get_Link_Stress_Between(int robotIdx, int voxelIdxA, int voxelIdxB);

    /// <summary>여러 복셀 쌍을 한 번의 락으로 일괄 조회. 반환값은 채워진 개수.</summary>
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int Get_Link_Stresses_Batch(int robotIdx, LinkQuery* queries,
                                                             float* outStresses, int count);

    /// <summary>현재 로봇의 링크 총 개수 (에디터 인스펙터용).</summary>
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Get_Link_Count(int robotIdx);

    /// <summary>linkIdx 번째 링크 정보 조회 (에디터 인스펙터용). 성공 1, 실패 0.</summary>
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Get_Link_Info_At(int robotIdx, int linkIdx,
                                               out int outVoxelIdx1, out int outVoxelIdx2,
                                               out float outStress);


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

    // 🌟 [R6/Plan A] 링크 벌크 버퍼는 폐지되었습니다.
    //    lastLinkStatePtr 는 항상 IntPtr.Zero 이며, 외부 코드 호환을 위해서만 남아 있습니다.
    //    이 포인터를 역참조하는 코드가 있다면 Get_Link_Stress_Between() 으로 교체하세요.
    [Obsolete("Always returns IntPtr.Zero since Plan A. Use GetLinkStress(a, b) instead.")]
    public IntPtr lastLinkStatePtr = IntPtr.Zero;

    // 링크 개수. 에디터에서만 갱신됩니다(빌드에서는 뮤텍스 락을 피하기 위해 조회하지 않음).
    [ReadOnly] public int lastLinkCount = 0;


    // 🌟 1. VoxelEngineCore 참조 변수 추가
    private VoxelEngineCore engineCore;


    public void Fill_Robot_Param( int num_voxel, int num_motor )
    {
        numTotalVoxel = num_voxel;
        numMotorVoxel = num_motor;

        // 🌟 2. 시작 시 씬에 있는 VoxelEngineCore를 찾아 연결합니다.
        engineCore = FindAnyObjectByType<VoxelEngineCore>();

        // 🌟 [R6/Plan A] linkIndexCacheArray 할당 및 2000회 초기화 루프 제거됨.
        //    캐시 배열 자체가 없어졌으므로 여기서 할 일이 없습니다.
    }



    private void Update()
    {
        // 🌟 파이썬과 통신 중(훈련 모드)이 아닐 때(= Inference 모드일 때)만 모니터링 실행
        // 🌟 3. Academy 대신 VoxelEngineCore의 is_ml_agent 값을 직접 읽어옵니다.
        // RL 모드가 꺼져있을 때(false)만 매 프레임 관측(모니터링)을 실행합니다.
        if (engineCore != null && !engineCore.is_ml_agent) ForceMonitorVoxelState();
    }

    // 기존의 private unsafe void MonitorVoxelState()를 다음과 같이 변경합니다.
    public unsafe void ForceMonitorVoxelState()
    {
        // [수정] 함수 호출부
        IntPtr unusedLinkPtr;
        int    unusedLinkCount;

        Get_Voxel_RealTime_States(robotIndex, out lastStatePtr, out lastVoxelCount,
                                  out unusedLinkPtr, out unusedLinkCount, out currentRobotStep);

        // 🌟 [R6/Plan A] BuildLinkLookupMap() 호출 제거됨.
        //    링크 조회는 이제 요청 시점에 C++ 이 직접 수행합니다.

    #if UNITY_EDITOR
        UpdateEditorInspector();
    #endif
    }


    // =========================================================
    // 특정 두 복셀의 인덱스를 주었을 때 그 사이 링크의 물리량(Stress) 꺼내기
    // ---------------------------------------------------------
    // 🌟 [R6/Plan A] 해시 테이블 조회 → C++ 직접 탐색으로 전면 교체.
    //    C++ FindLinkBetweenVoxels() 가 격자 좌표로 6방향 인접을 판정하므로
    //    해시 충돌도, -1 센티넬 오염도, 인덱스 어긋남도 원천적으로 존재하지 않습니다.
    // =========================================================
    public float GetLinkStress(int voxelIdxA, int voxelIdxB)
    {
        if (voxelIdxA < 0 || voxelIdxB < 0)                       return 0f;
        if (voxelIdxA == voxelIdxB)                               return 0f;
        if (numTotalVoxel > 0 &&
            (voxelIdxA >= numTotalVoxel || voxelIdxB >= numTotalVoxel)) return 0f;

        return Get_Link_Stress_Between(robotIndex, voxelIdxA, voxelIdxB);
    }


    // =========================================================
    // 🌟 [R6/Plan A 추가] 여러 복셀 쌍을 한 번에 조회 (고빈도 호출용)
    // ---------------------------------------------------------
    //   GetLinkStress() 를 프레임당 수십 회 이상 호출한다면 이 함수를 쓰세요.
    //   P/Invoke 왕복과 C++ 뮤텍스 락을 count 번이 아니라 1번만 수행합니다.
    //
    //   queries / outStresses 는 호출자가 재사용하는 배열을 넘기면 GC 할당이 0입니다.
    //   fixed 블록은 호출 구간에만 핀을 걸지만, C++ 이 이 포인터를 보관하지 않고
    //   함수 안에서만 사용하므로 안전합니다.
    // =========================================================
    public unsafe int GetLinkStressBatch(LinkQuery[] queries, float[] outStresses, int count)
    {
        if (queries == null || outStresses == null) return 0;
        if (count <= 0) return 0;
        if (count > queries.Length || count > outStresses.Length)
        {
            Debug.LogError($"[VoxelPhysicsInfo] GetLinkStressBatch: count({count}) exceeds array sizes. " +
                           $"queries={queries.Length}, outStresses={outStresses.Length}");
            return 0;
        }

        fixed (LinkQuery* pQ = queries)
        fixed (float* pS = outStresses)
        {
            return Get_Link_Stresses_Batch(robotIndex, pQ, pS, count);
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

        // ▼▼▼ 링크 데이터 읽기 ▼▼▼
        // 🌟 [R6/Plan A] 벌크 버퍼 역참조 → 온디맨드 조회로 교체.
        //    에디터에서만 실행되므로 뮤텍스 락 비용은 문제되지 않습니다.
        lastLinkCount = Get_Link_Count(robotIndex);

        if (lastLinkCount > 0 && inspectLinkIndex >= 0 && inspectLinkIndex < lastLinkCount)
        {
            int v1, v2;
            float st;
            if (Get_Link_Info_At(robotIndex, inspectLinkIndex, out v1, out v2, out st) != 0)
            {
                currentLinkVoxel1 = v1;
                currentLinkVoxel2 = v2;
                currentLinkStress = st;
            }
            else
            {
                currentLinkVoxel1 = -1;
                currentLinkVoxel2 = -1;
                currentLinkStress = 0f;
            }
        }
        else
        {
            currentLinkVoxel1 = -1;
            currentLinkVoxel2 = -1;
            currentLinkStress = 0f;
        }
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
