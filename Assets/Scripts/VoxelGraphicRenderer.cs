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

    #endif
    }

    unsafe void Update()
    {
    #if !UNITY_SERVER

        IntPtr triPtr, linePtr;
        int triCount, lineCount;

        // C++에서 렌더링 데이터 훔쳐오기
        Fill_Voxel_Triangle_and_Line( robotIndex, out triPtr, out triCount, out linePtr, out lineCount);

        if (triCount > 0 && triCount <= maxVertexCapacity)
        {
            NativeArray<VtxDxAll> nativeTriangles = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<VtxDxAll>(
                (void*)triPtr, triCount, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeTriangles, AtomicSafetyHandle.Create());
#endif
            mesh.SetVertexBufferData(nativeTriangles, 0, 0, triCount);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, triCount, MeshTopology.Triangles));
        }

        if (lineCount > 0 && lineCount <= maxLineVertexCapacity)
        {
            NativeArray<VtxDxAll> nativeLines = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<VtxDxAll>(
                (void*)linePtr, lineCount, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeLines, AtomicSafetyHandle.Create());
#endif
            lineMesh.SetVertexBufferData(nativeLines, 0, 0, lineCount);
            lineMesh.SetSubMesh(0, new SubMeshDescriptor(0, lineCount, MeshTopology.Lines));
            Graphics.DrawMesh(lineMesh, transform.localToWorldMatrix, lineMaterial, gameObject.layer);
        }
    #endif
    }

    void OnDestroy()
    {
        if (persistentIndices.IsCreated) persistentIndices.Dispose();
        if (persistentLineIndices.IsCreated) persistentLineIndices.Dispose();
    }
}