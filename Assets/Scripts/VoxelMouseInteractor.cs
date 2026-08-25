/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelMouseInteractor.cs]
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
using UnityEngine.InputSystem; 

public class VoxelMouseInteractor : MonoBehaviour
{
/* 
    [Header("클릭(마우스 픽킹) 설정")]
    // 🌟 1. 픽킹 반경을 복셀 월드 크기(0.1)의 절반인 0.05로 대폭 줄여서 아주 정밀하게 맞췄을 때만 클릭되도록 합니다.
    [Tooltip("마우스 클릭을 인식할 반경 (복셀 크기의 절반 정도가 적당합니다. 예: 0.05)")]
    public float pickRadius = 0.05f; 
    [Header("드래그 설정")]
    public float dragForceK = 1000f; 
    [Header("시각적 피드백 (선택 마커)")]
    public bool showPickIndicator = true;
    private GameObject pickIndicator; 
*/

    [Header("Click (Mouse Picking) Settings")]
    // 🌟 1. Significantly reduced the picking radius to 0.05 (half the voxel world size of 0.1) to ensure clicks are registered only with high precision.
    [Tooltip("Radius for detecting mouse clicks (about half the voxel size is recommended, e.g., 0.05)")]
    public float pickRadius = 0.05f; 

    [Header("Drag Settings")]
    public float dragForceK = 1000f;

    [Header("Visual Feedback (Selection Marker)")]
    public bool showPickIndicator = true;
    private GameObject pickIndicator; 



    private bool isDragging = false;
    private float dragPlaneDistance; 

    private VoxelPhysicsInfo selectedRobotInfo;
    private int selectedVoxelIdx = -1;

    void Start()
    {
        pickIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pickIndicator.name = "Voxel_Pick_Indicator";
        Destroy(pickIndicator.GetComponent<Collider>()); 
        
        // 🌟 2. 복셀 크기(0.1)에 맞춰 레이저를 아주 가늘고(0.05) 짧게(2.0) 수정했습니다.
        pickIndicator.transform.localScale = new Vector3(0.03f, 0.7f, 0.01f);

        Renderer r = pickIndicator.GetComponent<Renderer>();
        if (r != null && r.material != null) {
            // 눈에 확 띄는 밝은 노란색/빨간색 계열로 변경
            r.material.color = new Color(1f, 0.2f, 0.2f); 
        }

        pickIndicator.SetActive(false);
    }

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryPickAnyVoxel();
        }

        if (Mouse.current.leftButton.isPressed && isDragging && selectedRobotInfo != null)
        {
            Vector2 mousePos2D = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos2D);
            Vector3 mouseWorldPos = ray.GetPoint(dragPlaneDistance);

            Vector3 currentLocalPos = selectedRobotInfo.GetVoxelPosition(selectedVoxelIdx);
            Vector3 currentWorldPos = selectedRobotInfo.transform.TransformPoint(currentLocalPos);

            Vector3 mouseLocalPos = selectedRobotInfo.transform.InverseTransformPoint(mouseWorldPos);
            Vector3 forceDirection = (mouseLocalPos - currentLocalPos);
            Vector3 appliedForce = forceDirection * dragForceK;

          /*  if (VoxelPhysicsManager.Instance != null)
            {
                VoxelPhysicsManager.Instance.AddMouseDragCommand(
                    selectedRobotInfo.monitorRobotIndex, 
                    selectedVoxelIdx, 
                    appliedForce
                );
            }
        */
            // 🟢 변경 후 (클릭된 로봇이 속한 훈련장 프리팹의 매니저를 역추적하여 명령 하달)
            VoxelPhysicsManager localManager = selectedRobotInfo.GetComponentInParent<VoxelPhysicsManager>();
            if (localManager != null)
            {
                localManager.AddMouseDragCommand( selectedRobotInfo.robotIndex, selectedVoxelIdx, appliedForce );
            }

            if (showPickIndicator)
            {
                UpdateIndicator(currentWorldPos);
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            selectedRobotInfo = null;
            if (pickIndicator != null) pickIndicator.SetActive(false);
        }
    }

    private void TryPickAnyVoxel()
    {
        Vector2 mousePos2D = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos2D);

        float closestOverallDistance = float.MaxValue;
        VoxelPhysicsInfo bestRobot = null;
        int bestVoxelIdx = -1;

        VoxelPhysicsInfo[] allRobots = FindObjectsByType<VoxelPhysicsInfo>(FindObjectsSortMode.None);

        foreach (var robotInfo in allRobots)
        {
            if (robotInfo.RaycastVoxel(ray, pickRadius, out float hitDist, out int hitIdx, out Vector3 hitPos))
            {
                if (hitDist < closestOverallDistance)
                {
                    closestOverallDistance = hitDist;
                    bestRobot = robotInfo;
                    bestVoxelIdx = hitIdx;
                }
            }
        }

        if (bestRobot != null)
        {
            selectedRobotInfo = bestRobot;
            selectedVoxelIdx = bestVoxelIdx;
            dragPlaneDistance = closestOverallDistance; 
            isDragging = true;
            selectedRobotInfo.inspectVoxelIndex = bestVoxelIdx;

            if (showPickIndicator && pickIndicator != null) 
            {
                pickIndicator.SetActive(true);
            }
        }
    }

    private void UpdateIndicator(Vector3 voxelWorldPos)
    {
        if (pickIndicator == null) return;

        Vector3 dirFromCamera = Camera.main.transform.position - voxelWorldPos;
        
        // 🌟 3. 실린더가 복셀을 뚫고 들어가지 않게 카메라 쪽으로 살짝 빼줍니다 (길이의 절반만큼)
        // 유니티 실린더 원형은 높이가 2 이므로, localScale.y가 0.2 이면 실제 높이는 0.4 입니다.
        float cylinderHalfHeight = pickIndicator.transform.localScale.y; 
        Vector3 offsetPos = voxelWorldPos + (dirFromCamera.normalized * cylinderHalfHeight);

        pickIndicator.transform.position = offsetPos;
        pickIndicator.transform.up = dirFromCamera.normalized;
    }
}