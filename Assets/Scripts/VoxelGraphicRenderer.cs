/*
 * ==============================================================================
 * Copyright (c) 2026 [Y.S.Shim(NeuronomicoN)]. All rights reserved.
 * 
 * Project      : [Voxelyze-Unity-MLAgents]
 * File         : [VoxelGraphicRenderer.cs]
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
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;

// C++ 구조체와 메모리 레이아웃을 100% 동일하게 맞춤
//[StructLayout(LayoutKind.Sequential)]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VtxDxAll
{
    public Vector3 pos;         // XMFLOAT3 -> 12 bytes
    public Vector3 norm;        // XMFLOAT3 -> 12 bytes
    public Vector3 normflat;    // XMFLOAT3 -> 12 bytes
    public Vector2 tex;         // XMFLOAT2 -> 8 bytes
    public Color col;           // XMFLOAT4 -> 16 bytes (Vector4도 가능)
    public int matMode;         // int      -> 4 bytes
}  

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VoxelGraphicRenderer : MonoBehaviour
{
    const string DLL_NAME = VoxelDllConfig.DLL_NAME;

    [DllImport(DLL_NAME)]
    public static extern void Fill_Voxel_Triangle_and_Line( int robotIdx,
                                                            out IntPtr triData, out int triCount, 
                                                            out IntPtr lineData, out int lineCount );
    [Header("이 스크립트가 렌더링할 타겟 로봇")]
    [ReadOnly] public int robotIndex = 0; // 🌟 이 로봇 번호만 C++에 요청함


    [Header("Rendering Capacity")]
    public int maxVertexCapacity = 100000;
    public int maxLineVertexCapacity = 50000;

    private Mesh mesh;
    private NativeArray<int> persistentIndices;
    
    private Mesh lineMesh;
    private NativeArray<int> persistentLineIndices;
    private Material lineMaterial;

    void Start()
    {
    
    #if UNITY_SERVER

        // 서버 빌드에서는 렌더링 로직이 필요 없으므로 컴포넌트를 즉시 끄고 탈출합니다.
        this.enabled = false;
        return;

    #else

        // 🌟 [추가 방어벽] -batchmode -nographics 로 실행되어 그래픽 카드가 꺼져있을 때 셰이더 크래시 원천 차단!
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            this.enabled = false;
            return;
        }

        VertexAttributeDescriptor[] vertexLayout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),            
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.Float32, 3),            
            new VertexAttributeDescriptor(VertexAttribute.Tangent,   VertexAttributeFormat.Float32, 3),            
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),            
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),  
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.SInt32,  1)  
        };

        transform.localScale = new Vector3(10f, 10f, 10f);
        Bounds hugeBounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f));

        // 삼각형 메쉬 설정
        mesh = new Mesh { bounds = hugeBounds };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().mesh = mesh;

        mesh.SetVertexBufferParams(maxVertexCapacity, vertexLayout);
        persistentIndices = new NativeArray<int>(maxVertexCapacity, Allocator.Persistent);
        for (int i = 0; i < maxVertexCapacity; i++) persistentIndices[i] = i;
        mesh.SetIndexBufferParams(maxVertexCapacity, IndexFormat.UInt32);
        mesh.SetIndexBufferData(persistentIndices, 0, 0, maxVertexCapacity);

        // 라인 메쉬 설정
        lineMesh = new Mesh { bounds = hugeBounds };
        lineMesh.MarkDynamic();

        lineMesh.SetVertexBufferParams(maxLineVertexCapacity, vertexLayout);
        persistentLineIndices = new NativeArray<int>(maxLineVertexCapacity, Allocator.Persistent);
        for (int i = 0; i < maxLineVertexCapacity; i++) persistentLineIndices[i] = i;
        lineMesh.SetIndexBufferParams(maxLineVertexCapacity, IndexFormat.UInt32);
        lineMesh.SetIndexBufferData(persistentLineIndices, 0, 0, maxLineVertexCapacity);

        lineMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.black };

        // 🌟 [중요 복구!] 메모리 폭발(Graphics.DrawMesh)을 막기 위한 라인 전용 자식 오브젝트 생성
        GameObject lineObj = new GameObject("LineRendererObject");
        lineObj.transform.SetParent(this.transform, false);
        lineObj.layer = this.gameObject.layer;
        
        MeshFilter lineMf = lineObj.AddComponent<MeshFilter>();
        MeshRenderer lineMr = lineObj.AddComponent<MeshRenderer>();
        lineMf.mesh = lineMesh;
        lineMr.material = lineMaterial;

    #endif
    }

    unsafe void Update()
    {
    #if !UNITY_SERVER

        IntPtr triPtr, linePtr;
        int triCount, lineCount;

        // C++에서 렌더링 데이터 훔쳐오기
        Fill_Voxel_Triangle_and_Line( robotIndex, out triPtr, out triCount, out linePtr, out lineCount);

        //🌟 [핵심 해결책] 유니티 내부의 Job 생성 및 메모리 누수 검사를 완전히 건너뛰는 강제 옵션
        MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | 
                                MeshUpdateFlags.DontValidateIndices | 
                                MeshUpdateFlags.DontNotifyMeshUsers;

        if (triCount > 0 && triCount <= maxVertexCapacity)
        // 방어벽: 포인터가 Zero가 아닐 때만 실행
        if (triPtr != IntPtr.Zero && triCount > 0 && triCount <= maxVertexCapacity)
        {
            NativeArray<VtxDxAll> nativeTriangles = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<VtxDxAll>(
                (void*)triPtr, triCount, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeTriangles, AtomicSafetyHandle.Create());
            // 🌟 1. 생성한 핸들을 변수에 저장해 둡니다.
            var triSafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeTriangles, triSafetyHandle);
#endif
            
            //mesh.SetVertexBufferData(nativeTriangles, 0, 0, triCount);
            //mesh.SetSubMesh(0, new SubMeshDescriptor(0, triCount, MeshTopology.Triangles));

            // 🌟 옵션(flags)을 추가하여 GPU에 다이렉트로 꽂아버립니다. (JobTempAlloc 누수 원천 차단)
            mesh.SetVertexBufferData(nativeTriangles, 0, 0, triCount, 0, flags);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, triCount, MeshTopology.Triangles), flags);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // 🌟 2. GPU 업로드가 끝났으므로 핸들을 즉시 해제하여 메모리 누수를 완벽히 방지합니다!
            AtomicSafetyHandle.Release(triSafetyHandle);
#endif

        }
        else if (triCount > maxVertexCapacity)
        {
            // 🌟 용량이 꽉 차서 렌더링이 무시되었음을 알림
            Debug.LogWarning($"[VoxelGraphicRenderer] 로봇 {robotIndex}번의 정점 개수({triCount})가 최대 용량({maxVertexCapacity})을 초과하여 렌더링이 생략되었습니다!");
        }

        else // triCount == 0 인 경우 (로봇이 파괴되었거나 데이터가 없을 때)
        {
            // 🌟 0개로 설정하여 잔상(고스트)을 화면에서 지워줍니다.
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), flags);
        }

        // ==============================================================
        // 2. 라인 렌더링 처리
        // ==============================================================
        if (linePtr != IntPtr.Zero && lineCount > 0 && lineCount <= maxLineVertexCapacity)
        {
            NativeArray<VtxDxAll> nativeLines = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<VtxDxAll>(
                (void*)linePtr, lineCount, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeLines, AtomicSafetyHandle.Create());
            // 🌟 1. 라인용 안전 핸들 변수 저장
            var lineSafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeLines, lineSafetyHandle);
#endif
            //lineMesh.SetVertexBufferData(nativeLines, 0, 0, lineCount);
            //lineMesh.SetSubMesh(0, new SubMeshDescriptor(0, lineCount, MeshTopology.Lines));

            // 🌟 라인 렌더링에도 동일하게 옵션(flags) 추가
            lineMesh.SetVertexBufferData(nativeLines, 0, 0, lineCount, 0, flags);
            lineMesh.SetSubMesh(0, new SubMeshDescriptor(0, lineCount, MeshTopology.Lines), flags);

            //Graphics.DrawMesh(lineMesh, transform.localToWorldMatrix, lineMaterial, gameObject.layer);
            // ❌ Graphics.DrawMesh는 자식 오브젝트가 대신 그리므로 완전히 삭제되었습니다!

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // 🌟 2. 즉시 해제
            AtomicSafetyHandle.Release(lineSafetyHandle);
#endif

        }
        else if (lineCount > maxLineVertexCapacity)
        {
            Debug.LogWarning($"[VoxelGraphicRenderer] 로봇 {robotIndex}번의 라인 개수({lineCount})가 최대 용량({maxLineVertexCapacity})을 초과했습니다!");
        }
        else // lineCount == 0 인 경우
        {
            lineMesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Lines), flags);
        }
    #endif
    }

    void OnDestroy()
    {
        if (persistentIndices.IsCreated) persistentIndices.Dispose();
        if (persistentLineIndices.IsCreated) persistentLineIndices.Dispose();
    }
}