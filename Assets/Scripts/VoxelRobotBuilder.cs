
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Collections.Generic;

// ==========================================================
// C++ 구조체와 1:1 매칭
// ==========================================================
[StructLayout(LayoutKind.Sequential)]
public struct UnityVxNetGlobalParams
{
    public int real_num_osc;
    public int num_voxel;
    public int num_muscle;
    public int num_sensor;
    public int dimX; public int dimY; public int dimZ;
    
    public double voxel_size;
    public double voxel_density;
    public double g_cte;
    public double static_friction;
    public double kinetic_friction;
    public double poisson_ratio;
    public double ambient_temp;
    public double angle; public double hori; public double vert;
}

[StructLayout(LayoutKind.Sequential)]
public struct UnityVoxelInitData
{
    public int idX; public int idY; public int idZ;
    public int stiffness_I;
    public int vx_type;
    public int effector_idx;
    public int amplitude_I;
    public int phase_I;
    public int sensor_idx;
    public int sensor_weight_I;
    public int sensor_offset_I;
}

public class VoxelRobotBuilder : MonoBehaviour
{
    const string DLL_NAME = VoxelDllConfig.DLL_NAME;

    // 🌟 수정됨: flatNeuroVecArray 파라미터가 완전히 제거되었습니다.
    [DllImport(DLL_NAME)]
    public static extern void Transfer_Voxel_Robot_Params_From_Unity_To_CPP(int robotIdx, int is_unity,
                                                                                UnityVxNetGlobalParams globalParams, 
                                                                                UnityVoxelInitData[] voxelDataArray,
                                                                                string fileNames  );

    
    // ==========================================================
    // DLL Import: C++에서 C#으로 데이터를 거꾸로 가져오는 함수들
    // ==========================================================
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Get_Voxel_Robot_Params_From_CPP_To_Unity(int robotIdx, ref UnityVxNetGlobalParams outGlobalParams);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool Get_Voxel_InitData_From_CPP_To_Unity(int robotIdx, [Out] UnityVoxelInitData[] outVoxelDataArray);


    //[Header("로봇 개수")]    
    [ReadOnly] public int finalRobotCount = 0; // 테스트용 2대
    [ReadOnly] public int[] finalThreadArray;

    // 1. 씬에 존재하는 모든 로봇 인스턴스 스크립트를 찾습니다. (최신 유니티 최적화 코드 적용)
    private VoxelRobotInstance[] robots;


    public UnityVxNetGlobalParams[] robotGlobalparams;

    


    // 🌟 [변경] Start()를 지우고 LoadDataFromCpp() 로 이름 변경
    public void LoadDataFromCpp()
    {        
        int loopCount = (finalRobotCount > 0) ? finalRobotCount : robots.Length;
        VoxelPhysicsInfo[] allPhysicsInfos = FindObjectsByType<VoxelPhysicsInfo>(FindObjectsSortMode.None);

        for (int i = 0; i < loopCount; i++)
        {
            if (i < robots.Length && robots[i].isUnityBuild == false)
            {
                RetrieveRobotDataFromCPP(i);
            }

            VoxelPhysicsInfo targetInfo = null;
            foreach (VoxelPhysicsInfo info in allPhysicsInfos)
            {
                // 인덱스가 매칭되는 로봇 찾기
                if (info.robotIndex == i)
                {
                    targetInfo = info;
                    break;
                }
            }

            if (targetInfo != null && i < robotGlobalparams.Length)
            {
                targetInfo.Fill_Robot_Param(robotGlobalparams[i].num_voxel, robotGlobalparams[i].num_muscle);
            }
        }
    }


    // Init_Voxel_Unity() 이전에 -- 엔진 코어가 켜질 때 이 함수를 호출하여 씬의 로봇들을 수집합니다.
    public void GatherRobotsAndSendToCpp()
    {   
        // 1. 씬에 존재하는 모든 로봇 인스턴스 스크립트를 찾습니다. (최신 유니티 최적화 코드 적용)
        robots = FindObjectsByType<VoxelRobotInstance>(FindObjectsSortMode.None);

        // 🚨 [주의: 삭제된 부분!]
        // 여기에 있던 robots[i].robotIndex = i; 강제 부여 로직을 "삭제"했습니다!
        // 이제 VoxelPhysicsManager가 구역별로 꼬이지 않게 먼저 안전하게 발급합니다.
        // 🌟 [핵심 추가] 씬에 복제된 모든 로봇에게 0번부터 순차적으로 고유 ID를 강제 발급!
    /*    for (int i = 0; i < robots.Length; i++)
        {
            robots[i].robotIndex = i; // 로봇 본체 인덱스

            // 같은 오브젝트에 붙은 다른 스크립트들의 인덱스도 모두 i로 통일시킵니다.
            if (robots[i].TryGetComponent(out VoxelGraphicRenderer r)) r.RenderRobotIndex = i;
            if (robots[i].TryGetComponent(out VoxelPhysicsInfo p)) p.monitorRobotIndex = i;
            if (robots[i].TryGetComponent(out VoxelRobotAgent a)) a.robotIdx = i;
        }
    */

        // 발급된 로봇 인덱스(0, 1, 2...) 오름차순으로 예쁘게 정렬만 해줍니다.
        Array.Sort(robots, (a, b) => a.robotIndex.CompareTo(b.robotIndex));

        // 1. C++로 보낼 128개짜리 파일명 배열 생성 및 초기화
        string[] robotFileNames = new string[128];

        for (int i = 0; i < 128; i++) {
            robotFileNames[i] = ""; // 빈 문자열로 안전하게 초기화
        }

        // 3. 각 로봇의 인덱스 위치에 선택된 파일명(.vox) 저장
        foreach (var inst in robots) {
            if (inst.robotIndex >= 0 && inst.robotIndex < 128) {
                // 커스텀 에디터(인스펙터)에서 선택한 파일명을 배열에 쏙 넣음
                robotFileNames[inst.robotIndex] = inst.selectedVoxFileName ?? "";
            }
        }


        // 2. robotIndex 오름차순(0, 1, 2...)으로 정렬하여 꼬이지 않게 방지합니다.
        Array.Sort(robots, (a, b) => a.robotIndex.CompareTo(b.robotIndex));

        finalRobotCount = robots.Length;
        finalThreadArray = new int[finalRobotCount];

        if (finalRobotCount == 0)
        {
            Debug.LogWarning("[VoxelRobotBuilder] No VoxelRobotInstance attached to Robots!");
            return;
        }

        robotGlobalparams = new UnityVxNetGlobalParams[finalRobotCount];

        // ====================================================================
        // 3. 찾은 로봇 개수만큼 For문을 돌며 C++ 로 데이터 전송
        // ====================================================================
        for (int i = 0; i < finalRobotCount; i++)
        {
            // 각 로봇 오브젝트에 설정된 스레드 개수 수집
            finalThreadArray[i] = robots[i].threadCount;

            if( !robots[i].isUnityBuild )
            {
                Transfer_Voxel_Robot_Params_From_Unity_To_CPP(robots[i].robotIndex, 0, default, null, robotFileNames[i]); 
                Debug.Log($"[VoxelRobotBuilder] [Robot {robots[i].robotIndex}] Loading vox in C++ (Threads: {robots[i].threadCount})");
            }
            else
            {
                // [참고] 아래는 기존처럼 0번 로봇 하드코딩 생성 코드를 예시로 남겨둠.
                // 실제로는 i번째 로봇의 조립 데이터를 여기서 생성/할당해야 함.
                UnityVxNetGlobalParams g_RobotParam = new UnityVxNetGlobalParams {
                    real_num_osc = 2, // neuroVec을 사용하지 않으므로 0 (혹은 2: 구조체 정합성용으로 방치)
                    num_voxel = 2,
                    num_muscle = 1,
                    num_sensor = 0,
                    dimX = 10, dimY = 10, dimZ = 10,
                    voxel_size = 0.01,
                    voxel_density = 1000.0,
                    g_cte = 0.02,
                    static_friction = 1.0,
                    kinetic_friction = 0.5,
                    poisson_ratio = 0.35,
                    ambient_temp = 35.0,
                    angle = 0.0, hori = 0.0, vert = 0.0
                };
                
                UnityVoxelInitData[] voxelParams = new UnityVoxelInitData[g_RobotParam.num_voxel];

                voxelParams[0] = new UnityVoxelInitData { 
                    idX = 0, idY = 0, idZ = 0,             
                    stiffness_I = 500000000,
                    vx_type = 1, 
                    effector_idx = 0,
                    amplitude_I = 6999999, // 사인파 진폭 : 값/1000000
                    phase_I = 29526, // 사인파 위상
                    sensor_idx = 0,
                    sensor_weight_I = 0,
                    sensor_offset_I = 0 };

                voxelParams[1] = new UnityVoxelInitData { 
                    idX = 1, idY = 0, idZ = 0,             
                    stiffness_I = 500000000,
                    vx_type = 0, 
                    effector_idx = 0,
                    amplitude_I = 6999999,
                    phase_I = 29526,
                    sensor_idx = 0,
                    sensor_weight_I = 0,
                    sensor_offset_I = 0 };


                robotGlobalparams[i] = g_RobotParam;

                Transfer_Voxel_Robot_Params_From_Unity_To_CPP(robots[i].robotIndex, 1, g_RobotParam, voxelParams, robotFileNames[i]);
                Debug.Log($"[VoxelRobotBuilder] [Robot {robots[i].robotIndex}] Unity built Data (Threads: {robots[i].threadCount})");
            }
        }
    }


    // ==========================================================
    // isUnityBuild == false 일 때 C++로부터 데이터를 채워오는 로직
    // ==========================================================
    public void RetrieveRobotDataFromCPP(int robotIdx)
    {
        // 1. C++로부터 파라미터 가져오기 (가장 중요한 num_voxel 크기 획득)
        UnityVxNetGlobalParams fetchedParams = robotGlobalparams[robotIdx];
        bool success = Get_Voxel_Robot_Params_From_CPP_To_Unity(robotIdx, ref fetchedParams);

        if (!success)
        {
            Debug.LogError($"[VoxelRobotBuilder] Failed to bring Robot {robotIdx} params from C++ !");
            return;
        }

        // 🌟 스크립트 내부에 선언된 변수에 파라미터 저장 (필요시)
        // this.myGlobalParams = fetchedParams; 

        if (success)
        {
            // 🌟 그냥 바로 지정된 인덱스에 데이터를 덮어쓰기만 하면 됩니다!
            if (robotIdx < robotGlobalparams.Length)
            {
                robotGlobalparams[robotIdx] = fetchedParams;
            }

            Debug.Log($"[VoxelRobotBuilder] [CPP -> Unity] Robot {robotIdx} Loading C++ params success (Num Voxel: {fetchedParams.num_voxel})");
        }
        else
        {
            Debug.LogError($"[VoxelRobotBuilder] [CPP -> Unity] Robot {robotIdx} Failed to load C++ params!");
        }


        int numVoxel = fetchedParams.num_voxel;
        Debug.Log($"[VoxelRobotBuilder] Robot {robotIdx} load complete. Num Voxels: {numVoxel}");

        if (numVoxel > 0)
        {
            // 2. 복셀 개수만큼 C# 배열 메모리 할당 (가장 핵심적인 단계)
            UnityVoxelInitData[] fetchedVoxelData = new UnityVoxelInitData[numVoxel];

            // 3. 방금 만든 배열을 C++로 던져서 내부 데이터를 꽉 채워오기 ([Out] 속성에 의해 값이 복사됨)
            success = Get_Voxel_InitData_From_CPP_To_Unity(robotIdx, fetchedVoxelData);

            if (success)
            {
                Debug.Log($"[VoxelRobotBuilder] (Robot {robotIdx}) {numVoxel} voxel data transfered to C# struct.");
                
                // 🌟 가져온 배열을 C# List 등에 저장하여 인스펙터나 훈련/모니터링 로직에서 활용
                // this.myVoxelDataList = new List<UnityVoxelInitData>(fetchedVoxelData);
            }
            else
            {
                Debug.LogError($"[VoxelRobotBuilder] (Robot {robotIdx}) Voxel data transfer failed.");
            }
        }
    }

}



/*
private void Start_OLD()
    {        
        // 🔍 디버그 1: finalRobotCount 값 및 PhysicsInfo 개수 확인
        VoxelPhysicsInfo[] allPhysicsInfos = FindObjectsByType<VoxelPhysicsInfo>(FindObjectsSortMode.None);
        Debug.Log($"[VoxelRobotBuilder] finalRobotCount: {finalRobotCount}, Num of PhysicsInfo: {allPhysicsInfos.Length}");

        // 만약 finalRobotCount가 0이라면 robots.Count 또는 numberOfRobots를 사용해야 합니다.
        int loopCount = (finalRobotCount > 0) ? finalRobotCount : robots.Length;

        for (int i = 0; i < loopCount; i++)
        {
            // 1. C++ 파일 로드 오브젝트인 경우 C++에서 데이터 역으로 가져오기
            if (i < robots.Length && robots[i].isUnityBuild == false)
            {
                RetrieveRobotDataFromCPP(i);
            }

            // 🔍 디버그 2: 읽어온 파라미터 확인 (0으로 들어오는지 체크)
            if (i < robotGlobalparams.Length)
            {
                Debug.Log($"[VoxelRobotBuilder] [Robot {i} Params] num_voxel: {robotGlobalparams[i].num_voxel}, num_muscle: {robotGlobalparams[i].num_muscle}");
            }

            // 2. 씬에서 해당 로봇(i)에 맞는 VoxelPhysicsInfo 찾기
            VoxelPhysicsInfo targetInfo = null;

            foreach (VoxelPhysicsInfo info in allPhysicsInfos)
            {
                // 부모/자식 오브젝트에 붙어있을 경우까지 대비하여 InParent/InChild 검색
                VoxelRobotInstance rInstance = info.GetComponentInParent<VoxelRobotInstance>();
                if (rInstance == null) rInstance = info.GetComponentInChildren<VoxelRobotInstance>();

                if (rInstance != null && rInstance.robotIndex == i)
                {
                    targetInfo = info;
                    break;
                }
            }

            // 3. 값 대입 및 결과 로그
            if (targetInfo != null && i < robotGlobalparams.Length)
            {
                targetInfo.Fill_Robot_Param(robotGlobalparams[i].num_voxel, robotGlobalparams[i].num_muscle);
                Debug.Log($"[VoxelRobotBuilder] [Success] Robot {i} VoxelPhysicsInfo fill data!");
            }
            else
            {
                Debug.LogWarning($"[VoxelRobotBuilder] [Fail] Robot {i}: Not found VoxelPhysicsInfo, or No Parameters");
            }
        }
    }
*/


/*
    [Header("각 로봇 데이터 종류 (0: vox file, 1: Unity)")]
    public List<int> is_unity = new List<int>();

    //🌟 이동됨: 각 로봇별 내부에 할당할 물리 연산 스레드 개수 설정 리스트
    [Header("각 로봇별 Thread 개수 설정")]
    public List<int> threadsPerRobot = new List<int>();


    // 에디터(인스펙터)에서 값이 변경될 때마다 자동으로 실행되는 유니티 내장 함수
    void OnValidate()
    {
        // 1. 씬에 존재하는 모든 로봇 인스턴스 스크립트를 찾습니다. (최신 유니티 최적화 코드 적용)
        VoxelRobotInstance[] robots = FindObjectsByType<VoxelRobotInstance>(FindObjectsSortMode.None);

        finalRobotCount = robots.Length;



        // numberOfRobots가 0보다 작아지는 것 방지
        if (finalRobotCount < 0) finalRobotCount = 0;

        // 리스트의 크기가 로봇 수보다 부족하면 칸을 추가
        while (is_unity.Count < finalRobotCount)
        {
            is_unity.Add(0); // 기본값 0으로 추가
        }
        
        // 리스트의 크기가 로봇 수보다 많으면 남는 칸을 삭제
        while (is_unity.Count > finalRobotCount)
        {
            is_unity.RemoveAt(is_unity.Count - 1);
        }

        // 🌟 2. 추가됨: 스레드 개수 설정 리스트 크기 자동 동기화
        while (threadsPerRobot.Count < finalRobotCount)
        {
            threadsPerRobot.Add(10); // 기본 스레드 값을 10개로 채우며 추가
        }
        while (threadsPerRobot.Count > finalRobotCount)
        {
            threadsPerRobot.RemoveAt(threadsPerRobot.Count - 1);
        }
    }

    // 유니티 상에서 직접 로봇 조립 데이터를 세팅하여 C++로 쏘아보내는 예제 함수
    public void InitializeRobotDirectlyToCpp()
    {   

        // ====================================================================
        // 1. 배열 껍데기(메모리 공간)를 로봇 대수만큼 먼저 선언합니다.
        // ====================================================================
        UnityVxNetGlobalParams[] globalParams = new UnityVxNetGlobalParams[finalRobotCount];

        // 로봇마다 복셀 개수가 다를 수 있으므로 '가변 배열(Jagged Array)'로 선언
        UnityVoxelInitData[][] voxelArray = new UnityVoxelInitData[finalRobotCount][]; 


        // 1. 글로벌 파라미터 세팅        

        globalParams[0] = new UnityVxNetGlobalParams
        {
            real_num_osc = 2, // neuroVec을 사용하지 않으므로 0 (혹은 2: 구조체 정합성용으로 방치)
            num_voxel = 2,
            num_muscle = 1,
            num_sensor = 0,
            dimX = 2, dimY = 1, dimZ = 1,
            voxel_size = 0.01,
            voxel_density = 1000.0,
            g_cte = 0.02,
            static_friction = 1.0,
            kinetic_friction = 0.5,
            poisson_ratio = 0.35,
            ambient_temp = 35.0,
            angle = 0.0, hori = 0.0, vert = 0.0
        };

        // 2. 복셀 배열 세팅
        voxelArray[0] = new UnityVoxelInitData[globalParams[0].num_voxel];
        
        // 0번 복셀 세팅 (예: 근육)
        voxelArray[0][0] = new UnityVoxelInitData { 
            idX = 0, idY = 0, idZ = 0,             
            stiffness_I = 500000000,
            vx_type = 1, 
            effector_idx = 0,
            amplitude_I = 6999999,
            phase_I = 29526,
            sensor_idx = 0,
            sensor_weight_I = 0,
            sensor_offset_I = 0
        };
        
        // 1번 복셀 세팅 (예: 뼈)
        voxelArray[0][1] = new UnityVoxelInitData { 
            idX = 1, idY = 0, idZ = 0,             
            stiffness_I = 500000000,
            vx_type = 0, 
            effector_idx = 0,
            amplitude_I = 6999999,
            phase_I = 29526,
            sensor_idx = 0,
            sensor_weight_I = 0,
            sensor_offset_I = 0
        };


        // ====================================================================
        // 3. For문을 돌며 C++ 로 통째로 일괄 전송 (실행 구간)
        // ====================================================================
        
        for (int i = 0; i < finalRobotCount; i++)
        {
            // i번째 로봇의 인덱스, 파라미터, 복셀 배열을 꺼내서 함수 호출!
            if( is_unity[i] < 1 )
            {
                Transfer_Voxel_Robot_Params_From_Unity_To_CPP(i, 0, default, null); 
                Debug.Log("유니티에서 로봇없이, C++ 엔진에서 직접 로봇 파일 로딩...");
            }
            else
            {
                Transfer_Voxel_Robot_Params_From_Unity_To_CPP(i, 1, globalParams[i], voxelArray[i]);
                Debug.Log("C++ 엔진에 Unity로부터 빌드 데이터 전송 완료!");
            }
        }       
               
    }

    */

