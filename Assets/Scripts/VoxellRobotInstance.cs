using UnityEngine;

[RequireComponent(typeof(VoxelGraphicRenderer), typeof(VoxelPhysicsInfo))]
public class VoxelRobotInstance : MonoBehaviour
{
    [Header("로봇 기본 ID (필수설정!!): PhysicsInfo와 Renderer에 자동 설정됨")]
    [Tooltip("이 로봇의 고유 인덱스 (0번부터 순차적으로 부여)")]
    [ReadOnly] public int robotIndex = 0;

    [Header("Multithreading")]
    [Tooltip("이 로봇이 C++ 내부에서 사용할 물리 연산 스레드 개수")]
    [Range(1, 64)]
    public int threadCount = 11;

    [Header("데이터 로드 방식")]
    [Tooltip("체크하면 유니티 Builder 데이터 사용, 해제하면 C++ 내부 vox 파일 로드")]
    public bool isUnityBuild = true;


    // 커스텀 에디터에서 그릴 것이므로 기본 인스펙터에서는 숨깁니다.
    [HideInInspector] 
    public string selectedVoxFileName; 


    
    void OnValidate()
    {
        
    }
}