/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelPhysicsInfoEditor.cs]
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
using UnityEditor;

[CustomEditor(typeof(VoxelPhysicsInfo))]
public class VoxelPhysicsInfoEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 컴포넌트의 최신 데이터를 가져옵니다.
        serializedObject.Update();

        // 현재 인스펙터가 바라보고 있는 실제 VoxelPhysicsInfo 객체 정보 가져오기
        VoxelPhysicsInfo info = (VoxelPhysicsInfo)target;

        // 텍스트박스 공통 스타일 지정 (RichText 활성화로 부분 굵기 조절 가능)
        GUIStyle boxStyle = new GUIStyle(EditorStyles.helpBox);
        boxStyle.fontSize = 12;
        boxStyle.alignment = TextAnchor.MiddleLeft;
        boxStyle.richText = true; 


        // ---------------------------------------------------------
        // [섹션 1] 로봇 상태 대시보드
        // ---------------------------------------------------------
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Robot Status", EditorStyles.boldLabel);

        string robotInfoText = 
            $"<b>[Robot Idx]</b>:{info.robotIndex}  " +
            $"<b>[Num Voxel]</b>:{info.numTotalVoxel}  " +
            $"<b>[Num Motor]</b>:{info.numMotorVoxel}\n" +
            $"-------------------------------------------\n" +
            $"<b>[C++ Step]</b>  {info.currentRobotStep:F3}\n" +
            $"<b>[Last count]</b>: {info.lastVoxelCount} Voxels, {info.lastLinkCount} Links";

        EditorGUILayout.LabelField(robotInfoText, boxStyle);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // 구분선 긋기

        // ---------------------------------------------------------
        // [섹션 2] 모니터링 결과 (대시보드 형태)
        // ---------------------------------------------------------
        EditorGUILayout.LabelField("Voxel Status", EditorStyles.boldLabel);

        // 사용자가 유일하게 입력하는 칸
        EditorGUILayout.PropertyField(serializedObject.FindProperty("inspectVoxelIndex"), new GUIContent("Voxel Index"));

        EditorGUILayout.Space();

        // 촘촘하게 보여줄 텍스트 묶음 만들기
        string physicsText = 
            $"[pos]  {info.currentPos.ToString("F4")}\n" +
            $"[vel]  {info.currentVel.ToString("F4")}\n" +
            $"[avel]  {info.currentAngVel.ToString("F4")}\n" +
            $"[exforce]  {info.currentAppliedForce.ToString("F4")}\n" +
            $"[pressure]  {info.currentPressure.ToString("F4")}";

        // 하나의 텍스트박스로 압축해서 출력
        EditorGUILayout.LabelField(physicsText, boxStyle);

        
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // 구분선 긋기


        // ▼▼▼ [추가] 섹션 3: 개별 링크 물리 대시보드 ▼▼▼
        EditorGUILayout.LabelField("Link Status", EditorStyles.boldLabel);

        // 사용자가 링크 인덱스를 입력하는 칸
        EditorGUILayout.PropertyField(serializedObject.FindProperty("inspectLinkIndex"), new GUIContent("Link Inddex"));

        EditorGUILayout.Space();

        // 텍스트 묶음 만들기
        string linkText = 
            $"<b>[Connected Voxel A]</b>  {info.currentLinkVoxel1}\n" +
            $"<b>[Connected Voxel B]</b>  {info.currentLinkVoxel2}\n" +
            $"<b>[Stress]</b>  {info.currentLinkStress.ToString("F4")}";


        EditorGUILayout.LabelField(linkText, boxStyle);

        
        // 2. 변경된 사항 저장
        serializedObject.ApplyModifiedProperties();

        // 3. 게임 실행 중일 때 인스펙터 화면이 끊기지 않고 부드럽게 갱신되도록 강제 다시 그리기
        if (Application.isPlaying)
        {
            Repaint();
        }
    }
}