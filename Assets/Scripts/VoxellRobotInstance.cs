/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelRobotInstance.cs]
 * Author       : [Y.S.Shim]
 * Date Created : 2026-08-15
 * 
 * [WARNING] 
 * The code in this file may not be copied, modified, distributed, or used for 
 * commercial purposes without prior authorization. Plagiarism or intentional 
 * removal of copyright notices may result in legal consequences.
 * ==============================================================================
 */

using UnityEngine;

[RequireComponent(typeof(VoxelGraphicRenderer), typeof(VoxelPhysicsInfo))]
public class VoxelRobotInstance : MonoBehaviour
{
    [Header("Robot ID: Set Automatically")]    
    [ReadOnly] public int robotIndex = 0;

    [Header("Multithreading Size")]    
    [Range(1, 64)]
    public int threadCount = 11;

    [Header("Robot Data Loading")]
    [Tooltip("Checked: Use Unity Builder data / Unchecked: Load internal C++ vox files")]
    public bool isUnityBuild = true;


    // 커스텀 에디터에서 그릴 것이므로 기본 인스펙터에서는 숨깁니다.
    [HideInInspector] 
    public string selectedVoxFileName; 


    
    void OnValidate()
    {
        
    }
}