
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
    [Tooltip("Number of is_muscle voxels. Verified via CPP_Get_Muscle_Count().")]
    public int muscleCount = 33;

    /// 타겟(2) + CoM 속도·각속도(6) + 나머지 복셀 9개씩
    //public int EgocentricStateSize => 2 + 6 + 9 * (expectedVoxelCount - 1);

    [Header("Observation Options")]    
    [Tooltip("Include angular velocity per voxel. Disabling reduces dimensions by 33% (for A/B testing).")]
    public bool includeVoxelAngVel = false;

    [Tooltip("Normalization scale for target distance. Typically set around the maximum spawn distance.")]
    public float targetDistScale = 0.5f;    // c++ 단위


    [Header("Observation Voxel Subset")]
    [Tooltip("Leave empty to observe all voxels. If specified, only these indices are observed (does not affect CoM calculation).")]
    public int[] observedVoxelIndices;

    public int ObservedVoxelCount =>
        (observedVoxelIndices != null && observedVoxelIndices.Length > 0)
            ? observedVoxelIndices.Length
            : expectedVoxelCount;


    // 타겟(3) + CoM속도(3) + 평균각속도(3) + 복셀당 6 또는 9
    public int PerVoxelSize        => includeVoxelAngVel ? 9 : 6;
    
    //public int EgocentricStateSize => 9 + PerVoxelSize * expectedVoxelCount;
    public int EgocentricStateSize => 9 + PerVoxelSize * ObservedVoxelCount;

}