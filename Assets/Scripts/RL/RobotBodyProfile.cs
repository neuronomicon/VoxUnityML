
using UnityEngine;

[CreateAssetMenu(fileName = "NewRobotBody", menuName = "RL Bodies/Voxel Robot Body")]
public class RobotBodyProfile : ScriptableObject
{
    [Header("Body Identity")]
    public string selectedVoxFileName;
    public int expectedVoxelCount = 33;

    [Header("Anatomy Landmarks (Voxel Indices)")]
    public int centerVoxelIdx = 16;
    public int forwardVoxelA  = 17;
    public int forwardVoxelB  = 15;
    public int rightVoxelA    = 21;
    public int rightVoxelB    = 11;

    [Header("Actuation")]
    [Tooltip("is_muscle 복셀 수. CPP_Get_Muscle_Count() 로 검증")]
    public int muscleCount = 33;

    /// 타겟(2) + CoM 속도·각속도(6) + 나머지 복셀 9개씩
    //public int EgocentricStateSize => 2 + 6 + 9 * (expectedVoxelCount - 1);

    [Header("Observation Options")]
    [Tooltip("복셀별 각속도 포함. 끄면 차원 33% 감소 (A/B 실험용)")]
    public bool includeVoxelAngVel = false;

    [Tooltip("타겟 거리 정규화 스케일. 스폰 최대거리 정도로")]
    public float targetDistScale = 0.5f;    // c++ 단위

    // 타겟(3) + CoM속도(3) + 평균각속도(3) + 복셀당 6 또는 9
    public int PerVoxelSize        => includeVoxelAngVel ? 9 : 6;
    public int EgocentricStateSize => 9 + PerVoxelSize * expectedVoxelCount;
}