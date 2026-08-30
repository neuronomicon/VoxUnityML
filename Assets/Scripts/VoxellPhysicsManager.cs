/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxellPhysicsManager.cs]
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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class VoxelPhysicsManager : MonoBehaviour
{
    // 🚨 1. 글로벌 싱글톤(Instance) 완전 제거됨
    //public static VoxelPhysicsManager Instance { get; private set; }

    [StructLayout(LayoutKind.Sequential)] 
    public struct UnityColliderInfo {
        public int objectId;
        public int colliderType; 
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public Vector3 extents;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnityReactionForce {
        public int objectId;
        public Vector3 force;
        public Vector3 position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelForceData {
        public int targetRobotIdx;
        public int targetVoxelIdx;
        public Vector3 forceVec;
    }

    private const string DLL_NAME = VoxelDllConfig.DLL_NAME; 

/*    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Send_Interactive_Force_Commands(VoxelForceData[] cmdArray, int cmdCount);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Set_Unity_Colliders(UnityColliderInfo[] colliders, int count);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Get_Reaction_Forces([In, Out] UnityReactionForce[] outForces, int maxCount);
*/

    // 🌟 2. DLL 통신 인터페이스에 로봇 식별표(int robotIdx) 추가
 /*   [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Send_Interactive_Force_Commands(int robotIdx, VoxelForceData[] cmdArray, int cmdCount);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Set_Unity_Colliders(int robotIdx, UnityColliderInfo[] colliders, int count);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Get_Reaction_Forces(int robotIdx, [In, Out] UnityReactionForce[] outForces, int maxCount);
*/

    // 기존의 [] 배열 선언을 포인터 * 로 변경합니다.
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void Send_Interactive_Force_Commands(int robotIdx, VoxelForceData* cmdArray, int cmdCount);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void Set_Unity_Colliders(int robotIdx, UnityColliderInfo* colliders, int count);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int Get_Reaction_Forces(int robotIdx, UnityReactionForce* outForces, int maxCount);


/*
    [Header("시뮬레이션 스케일 동기화")]
    [Tooltip("렌더러의 스케일과 똑같이 맞춰주세요 (기본값 10)")]
    public float simScale = 10f; 
    [Header("상호작용할 유니티 물리 객체들 (구, 박스)")]
    public Collider[] interactiveObjects; 
    [Header("이 훈련장에 속한 로봇들 (자동 수집)")]
    [ReadOnly] public VoxelPhysicsInfo[] localRobots;
*/

    [Header("Simulation Scale Synchronization")]
    [Tooltip("Please match this exactly with the renderer's scale (Default: 10)")]
    public float simScale = 10f; 

    [Header("Interactable Unity Physics Objects (Spheres, Boxes)")]
    public Collider[] interactiveObjects; 

    [Header("Robots in this Training Area (Auto-Collected)")]
    [ReadOnly] public VoxelPhysicsInfo[] localRobots;

    
    private Dictionary<int, Rigidbody> rbDict = new Dictionary<int, Rigidbody>();
    private UnityColliderInfo[] sendCollidersBuffer;
    private UnityReactionForce[] receiveForcesBuffer;

    // 여러 로봇의 명령을 섞어 담아둔 뒤 전송 시점에 필터링하여 분배합니다.
    private List<VoxelForceData> dragCommands = new List<VoxelForceData>();


    // 🌟 1. VoxelEngineCore 참조 변수 추가 (is_ml_agent=false 일때 FixedUpdate() 실행 하기 위해)
    private VoxelEngineCore engineCore;
    
    // 🌟 [변경] Awake() 삭제 후 InitializeArea()로 교체
    // 이제 혼자 깨어나지 않고, VoxelEngineCore가 순서대로 호출해 줍니다.
    public void InitializeArea(ref int globalRobotCounter)
    {        
        // 1. 이 훈련장(프리팹)의 자식으로 포함된 로봇들만 로컬 배열로 수집
        localRobots = GetComponentsInChildren<VoxelPhysicsInfo>();

        // 2. 엔진이 넘겨준 글로벌 카운터를 사용해 인덱스를 부여
        foreach (var robotInfo in localRobots)
        {
            int assignedIndex = globalRobotCounter++; // 현재 번호 부여 후 1 증가
            
            // 같은 오브젝트에 붙어있는 스크립트들을 찾아 인덱스 완전 통일
            robotInfo.robotIndex = assignedIndex;
            
            if (robotInfo.TryGetComponent(out VoxelRobotInstance instance))
                instance.robotIndex = assignedIndex;
                
            if (robotInfo.TryGetComponent(out VoxelGraphicRenderer renderer))
                renderer.robotIndex = assignedIndex;
                
            if (robotInfo.TryGetComponent(out VoxelRobotAgent agent))
                agent.robotIdx = assignedIndex;
        }

        sendCollidersBuffer = new UnityColliderInfo[10];
        receiveForcesBuffer = new UnityReactionForce[200]; 

        foreach (var col in interactiveObjects)
        {
            if (col != null && col.attachedRigidbody != null)
            {
                rbDict[col.gameObject.GetInstanceID()] = col.attachedRigidbody;
            }
        }

        engineCore = FindAnyObjectByType<VoxelEngineCore>();
    }


    public void AddMouseDragCommand(int robotIdx, int voxelIdx, Vector3 force)
    {
        // 마우스 드래그 힘은 타겟 로봇의 번호표를 달고 배열에 대기합니다.
        dragCommands.Add(new VoxelForceData {
            targetRobotIdx = robotIdx,
            targetVoxelIdx = voxelIdx,
            forceVec = force
        });
    }


    // 이 함수를 VoxelPhysicsManager 내부에 새로 추가하세요!
    private void FixedUpdate()
    {
        // 씬 초기화(InitializeArea)가 끝나서 로봇 목록이 있을 때만 실행
        if (engineCore != null && !engineCore.is_ml_agent)
        if (localRobots != null && localRobots.Length > 0)
        {
            // RL 모드가 켜져있든 꺼져있든, 매 물리 프레임마다 C++과 충돌 정보를 교환합니다.
            SyncPhysicsWithCpp();
        }
    }


    
    // 1. 클래스 상단에 재사용할 임시 버퍼 선언
    private VoxelForceData[] tempDragsBuffer = new VoxelForceData[10];  // C++ 의 MAX_INTERACTION

    // 🌟 4. VoxelRLManager에서 모든 훈련장을 돌며 일제히 호출하는 핵심 동기화 함수
    public void SyncPhysicsWithCpp()
    {
        //Debug.Log("VoxelPhysicsManager.SyncPhysicsWithCpp() 시작...");

        // 이 훈련장에 소속된 n대의 로봇 각각의 시점에서 시뮬레이션 데이터를 동기화합니다.
        foreach (var robot in localRobots)
        {
            int rIdx = robot.robotIndex;
            
           
        unsafe{

            // [1] 마우스 드래그 조종 힘 C++로 전송 (해당 로봇의 명령만 필터링)
            int dragCount = 0;
            for (int c = 0; c < dragCommands.Count && dragCount < tempDragsBuffer.Length; c++)
            {
                if (dragCommands[c].targetRobotIdx == rIdx) tempDragsBuffer[dragCount++] = dragCommands[c];
            }

            if (dragCount > 0)
            {
                fixed (VoxelForceData* pDrags = tempDragsBuffer) {
                    Send_Interactive_Force_Commands(rIdx, pDrags, dragCount);
                }
            } 
            else 
            {
                Send_Interactive_Force_Commands(rIdx, null, 0);
            }

        /*
            // [1] 마우스 드래그 조종 힘 C++로 전송 (해당 로봇의 명령만 필터링)
            List<VoxelForceData> myDrags = dragCommands.FindAll(c => c.targetRobotIdx == rIdx);
            if (myDrags.Count > 0) 
            {
                var arr = myDrags.ToArray();

                int arlen = arr.Length;
                //Debug.Log($"[PhysManager] index:{rIdx}, arrlen:{arlen}");

                //Send_Interactive_Force_Commands(rIdx, arr, arr.Length);
                // 🌟 배열을 메모리에 고정하고 포인터 획득
                fixed (VoxelForceData* pDrags = arr) {
                    Send_Interactive_Force_Commands(rIdx, pDrags, arr.Length);
                }
            } 
            else 
            {
                //Debug.Log($"[PhysManager] index:{rIdx}..null...0");
                Send_Interactive_Force_Commands(rIdx, null, 0);
            }
        */



            // [2] 유니티 씬의 콜라이더 정보 C++로 전송
            int activeCount = 0;
            for (int i = 0; i < interactiveObjects.Length; i++)
            {
                Collider col = interactiveObjects[i];
                if (col == null || !col.gameObject.activeInHierarchy) continue;

                if (rbDict.TryGetValue(col.gameObject.GetInstanceID(), out Rigidbody rb))
                {
                    sendCollidersBuffer[activeCount].objectId = col.gameObject.GetInstanceID();

                    // 🌟 핵심: '각 로봇 본체의 Transform'을 기준으로 유니티 월드 좌표를 로컬 상대 좌표로 변환!
                    //Vector3 localPos = robot.transform.InverseTransformPoint(rb.position);
                    //Vector3 pos = localPos / simScale;
                    //sendCollidersBuffer[activeCount].pos = new Vector3(pos.x, pos.z, pos.y);

                    // 🟢 수정된 코드: InverseTransformPoint가 이미 1/10 스케일 축소를 수행하므로 그대로 사용!
                    Vector3 localPos = robot.transform.InverseTransformPoint(rb.position);
                    sendCollidersBuffer[activeCount].pos = new Vector3(localPos.x, localPos.z, localPos.y);

                    // (주의: Direction 함수는 스케일을 무시하므로 아래 속도/각속도 나누기 코드는 그대로 유지합니다)                  


                    // 속도/각속도 역시 로봇을 기준으로 방향 벡터(Direction) 변환
                    Vector3 localVel = robot.transform.InverseTransformDirection(rb.linearVelocity);
                    Vector3 vel = localVel / simScale;
                    sendCollidersBuffer[activeCount].velocity = new Vector3(vel.x, vel.z, vel.y);

                    Vector3 localAngVel = robot.transform.InverseTransformDirection(rb.angularVelocity);
                    sendCollidersBuffer[activeCount].angularVelocity = new Vector3(localAngVel.x, localAngVel.z, localAngVel.y);

                    // 회전 변환 (훈련장/로봇의 부모 회전 역행렬을 곱하여 상대 회전으로 만듦)
                    Quaternion localRot = Quaternion.Inverse(robot.transform.rotation) * rb.rotation;
                    sendCollidersBuffer[activeCount].rot = new Quaternion(localRot.x, localRot.z, localRot.y, -localRot.w);

                    if (col is SphereCollider sphere) {
                        sendCollidersBuffer[activeCount].colliderType = 0;
                        float radius = sphere.radius * Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.y, col.transform.lossyScale.z);
                        sendCollidersBuffer[activeCount].extents = new Vector3(radius / simScale, 0, 0);
                    } 
                    else if (col is BoxCollider box) {
                        sendCollidersBuffer[activeCount].colliderType = 1;
                        Vector3 ext = Vector3.Scale(box.size * 0.5f, col.transform.lossyScale) / simScale;
                        sendCollidersBuffer[activeCount].extents = new Vector3(ext.x, ext.z, ext.y);
                    }
                    activeCount++;
                }
            }

            //Set_Unity_Colliders(rIdx, sendCollidersBuffer, activeCount);

            //Debug.Log($"[PhysManager] At SyncPhysicsWithCpp(): call Set_Unity_Colliders()...");

            if (activeCount > 0) {
                // C++과 동일하게 rIdx, 배열, 개수 순서로 정확히 전달
                //Debug.Log($"rIdx:{rIdx}, {sendCollidersBuffer}, activeCount:{activeCount}");
                //Set_Unity_Colliders(rIdx, sendCollidersBuffer, activeCount);
                // 🌟 마샬링 과정 없이 배열의 시작 주소(포인터)를 C++로 직배송
                fixed (UnityColliderInfo* pColliders = sendCollidersBuffer) {
                    Set_Unity_Colliders(rIdx, pColliders, activeCount);
                }
            } else {
                // 콜라이더가 없을 때는 안전하게 null 전달
                //Debug.Log($"rIdx:{rIdx}, null");
                Set_Unity_Colliders(rIdx, null, 0);
            }

            //Debug.Log($"Set_Unity_Colliders() Done!");


            // [3] C++에서 계산된 반작용 타격 지점 및 힘 가져와서 유니티 Rigidbody에 누적
            //int contactCount = Get_Reaction_Forces(rIdx, receiveForcesBuffer, receiveForcesBuffer.Length);
            // [3] 리액션 포스 수신
            int contactCount = 0;
            // 🌟 20,000개짜리 거대한 배열도 포인터로 넘기면 오버헤드 0, 크래시 위험 0
            fixed (UnityReactionForce* pForces = receiveForcesBuffer) {
                contactCount = Get_Reaction_Forces(rIdx, pForces, receiveForcesBuffer.Length);
            }

            
            for (int i = 0; i < contactCount; i++)
            {
                if (rbDict.TryGetValue(receiveForcesBuffer[i].objectId, out Rigidbody rb))
                {
                    // 🌟 핵심: C++에서 가져온 상대 좌표를 '로봇 본체의 Transform'을 기준으로 유니티 월드 좌표로 복구!
                    //Vector3 localHitPos = receiveForcesBuffer[i].position * simScale;
                    //Vector3 worldHitPos = robot.transform.TransformPoint(localHitPos);

                    // 🟢 수정된 코드: TransformPoint가 이미 10배 스케일 확대를 수행하므로 그대로 넣습니다!
                    Vector3 worldHitPos = robot.transform.TransformPoint(receiveForcesBuffer[i].position);


                    Vector3 localForce = receiveForcesBuffer[i].force * simScale;
                    Vector3 worldForce = robot.transform.TransformDirection(localForce);

                    // 누적 연산이므로 한 훈련장 내의 로봇 5대가 동시에 큐브를 밀어도 힘이 합산됩니다!
                    rb.AddForceAtPosition(worldForce, worldHitPos, ForceMode.Force);
                }
            }
        }
        } // 개별 훈련장 내부의 로봇 루프 종료

        

        // 모든 로봇에 대한 전송 처리가 끝났으므로 이번 프레임의 마우스 드래그 명령 초기화
        dragCommands.Clear();
    }


}


/*
private void Awake_OLD()
    {        
        //if (Instance == null) Instance = this;
        //else Destroy(gameObject);

        // 🌟 3. 이 훈련장(프리팹)의 자식으로 포함된 로봇들만 로컬 배열로 자동 수집
        localRobots = GetComponentsInChildren<VoxelPhysicsInfo>();

        sendCollidersBuffer = new UnityColliderInfo[100];
        receiveForcesBuffer = new UnityReactionForce[20000]; 

        foreach (var col in interactiveObjects)
        {
            if (col != null && col.attachedRigidbody != null)
            {
                rbDict[col.gameObject.GetInstanceID()] = col.attachedRigidbody;
            }
        }
    }
*/

/*  // 
    public void SyncPhysicsWithCpp_OLD()
    {
        // 1. 마우스 드래그 조종 힘 C++로 전송
        if (dragCommands.Count > 0) {
            var dragCommandBuffer = dragCommands.ToArray();
            Send_Interactive_Force_Commands(dragCommandBuffer, dragCommandBuffer.Length);
            dragCommands.Clear(); 
        } else {
            Send_Interactive_Force_Commands(null, 0); 
        }

        // 2. 유니티 씬의 콜라이더 정보 C++로 전송 (🌟 스케일 축소 및 Y/Z 스왑 적용)
        int activeCount = 0;
        for (int i = 0; i < interactiveObjects.Length; i++)
        {
            Collider col = interactiveObjects[i];
            if (col == null || !col.gameObject.activeInHierarchy) continue;

            if (rbDict.TryGetValue(col.gameObject.GetInstanceID(), out Rigidbody rb))
            {
                sendCollidersBuffer[activeCount].objectId = col.gameObject.GetInstanceID();

                // 🌟 위치와 속도를 1/10 로 줄이고, Y와 Z축을 교환해서 C++ 로 보냄
                Vector3 pos = rb.position / simScale;
                sendCollidersBuffer[activeCount].pos = new Vector3(pos.x, pos.z, pos.y);

                Vector3 vel = rb.linearVelocity / simScale; // 구버전 유니티면 rb.velocity 사용
                sendCollidersBuffer[activeCount].velocity = new Vector3(vel.x, vel.z, vel.y);

                Vector3 angVel = rb.angularVelocity;
                sendCollidersBuffer[activeCount].angularVelocity = new Vector3(angVel.x, angVel.z, angVel.y);

                // 회전(Quaternion)은 축 교환 및 방향 반전 반영
                sendCollidersBuffer[activeCount].rot = new Quaternion(rb.rotation.x, rb.rotation.z, rb.rotation.y, -rb.rotation.w);

                if (col is SphereCollider sphere) {
                    sendCollidersBuffer[activeCount].colliderType = 0;
                    float radius = sphere.radius * Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.y, col.transform.lossyScale.z);
                    // 구의 반지름도 1/10 로 축소
                    sendCollidersBuffer[activeCount].extents = new Vector3(radius / simScale, 0, 0);
                } 
                else if (col is BoxCollider box) {
                    sendCollidersBuffer[activeCount].colliderType = 1;
                    Vector3 ext = Vector3.Scale(box.size * 0.5f, col.transform.lossyScale) / simScale;
                    // 박스 크기도 1/10 축소 및 Y/Z 스왑
                    sendCollidersBuffer[activeCount].extents = new Vector3(ext.x, ext.z, ext.y);
                }
                activeCount++;
            }
        }
        Set_Unity_Colliders(sendCollidersBuffer, activeCount);

        // 3. C++에서 계산된 반작용 타격 지점 및 힘 적용 (🌟 스케일 뻥튀기 복구)
        int contactCount = Get_Reaction_Forces(receiveForcesBuffer, receiveForcesBuffer.Length);
        
        for (int i = 0; i < contactCount; i++)
        {
            if (rbDict.TryGetValue(receiveForcesBuffer[i].objectId, out Rigidbody rb))
            {
                // C++ 코드를 보면 이 두 값은 이미 (X, Z, Y) 스왑이 되어서 넘어오지만, 크기는 0.01 배율입니다.
                // 🌟 유니티 월드에서 정확한 타격점에 힘을 가하려면 위치와 힘에 모두 simScale(10)을 곱해줘야 합니다.
                Vector3 worldHitPos = receiveForcesBuffer[i].position * simScale;
                Vector3 appliedForce = receiveForcesBuffer[i].force * simScale;

                rb.AddForceAtPosition(appliedForce, worldHitPos, ForceMode.Force);
            }
        }
    }
*/