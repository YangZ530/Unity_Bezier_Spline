using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public class VolumeBezierCurve : MonoBehaviour
{

    private struct Instance
    {
        public Vector3 startPos;
        public Vector3 endPos;
        public Vector3 startNormalRot;
        public Vector3 endNormalRot;
    };

    [SerializeField] public Vector3 startPoint;
    [SerializeField] public Vector3 endPoint;
    [SerializeField] public Vector3 controlPoint0;
    [SerializeField] public Vector3 controlPoint1;
    [SerializeField] public uint meshQuality = 12;
    [SerializeField] public uint curveSubdivision = 4;
    [SerializeField] public string shaderName = "CurveSegmentInstancing";
    [SerializeField] public float splineWidth = 0.5f;

    private BezierCurve curve;
    private Mesh mesh;
    private Material material;
    private uint nbSide;
    private uint subdivision;
    private Instance[] instances;
    private ComputeBuffer iBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private ComputeBuffer aBuffer;

    private Dictionary<Camera, CommandBuffer> cams = new Dictionary<Camera, CommandBuffer>();

    private bool initialized = false;

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(startPoint, controlPoint0);
        Gizmos.DrawLine(controlPoint1, endPoint);
        Gizmos.color = Color.green;
        Vector3 size = Vector3.one * 0.1f;
        Gizmos.DrawCube(startPoint, size);
        Gizmos.DrawCube(controlPoint0, size);
        Gizmos.DrawCube(controlPoint1, size);
        Gizmos.DrawCube(endPoint, size);
    }

    private void Start()
    {
        if (GetComponent<Renderer>() == null)
        {
            gameObject.AddComponent<MeshRenderer>();
        }
        var shader = Shader.Find(shaderName);
        material = new Material(shader);
    }

    private void Update()
    {
        if (!initialized)
        {
            SetMesh();
            SetupBuffer();
            SetupInstanceData();
            initialized = true;
        }

        var resample = false;

        material.SetFloat("_volume", splineWidth);
        material.SetMatrix("_local2World", transform.localToWorldMatrix);
        material.SetMatrix("_world2Local", transform.worldToLocalMatrix);

        if (meshQuality != nbSide)
        {
            SetMesh();
        }
        if (curveSubdivision != subdivision)
        {
            SetupBuffer();
            resample = true;
        }

        if (resample)
        {
            SetupInstanceData();
        }

        initialized = true;
    }

    private void OnWillRenderObject()
    {
        if (!gameObject.activeInHierarchy || !enabled || !initialized)
        {
            CleanUp();
            return;
        }
        var cam = Camera.current;
        if (!cam) return;

        if (cams.ContainsKey(cam)) return;

        var cb = new CommandBuffer();
        cb.name = "GPU Instancing Bezier Curve";
        cams[cam] = cb;

        cb.DrawMeshInstancedIndirect(mesh, 0, material, -1, aBuffer, 0);

        cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
    }

    private void OnDestroy()
    {
        if (iBuffer != null) iBuffer.Release();
        if (aBuffer != null) aBuffer.Release();
    }

    private void CleanUp()
    {
        foreach (var cam in cams)
        {
            if (cam.Key)
            {
                cam.Key.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, cam.Value);
            }
        }
        cams.Clear();
    }

    private void SetCurve()
    {
        curve = new BezierCurve(startPoint, controlPoint0,
            controlPoint1, endPoint);
    }

    private void SetupBuffer()
    {
        if (iBuffer != null) iBuffer.Release();
        if (aBuffer != null) aBuffer.Release();
        subdivision = curveSubdivision;
        iBuffer = new ComputeBuffer((int)subdivision, Marshal.SizeOf(typeof(Instance)), ComputeBufferType.Default);
        instances = new Instance[subdivision];
        material.SetBuffer("_instances", iBuffer);
        aBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = mesh.GetIndexCount(0);
        args[1] = subdivision;
        aBuffer.SetData(args);
    }

    private void SetupInstanceData()
    {
        SetCurve();
        for (int i = 0; i < subdivision; i++)
        {
            var t0 = (float)i / (float)subdivision;
            var t1 = (float)(i + 1) / (float)subdivision;
            var pt0 = curve.Point(t0);
            var pt1 = curve.Point(t1);
            var tan0 = curve.Tangent(t0);
            var tan1 = curve.Tangent(t1);

            var angle = Vector3.Angle(Vector3.up, tan0) * Mathf.Deg2Rad;
            var axis = Vector3.Cross(tan0, Vector3.up).normalized;
            var rot0 = axis * angle;
            angle = Vector3.Angle(Vector3.up, tan1) * Mathf.Deg2Rad;
            axis = Vector3.Cross(tan1, Vector3.up).normalized;
            var rot1 = axis * angle;

            instances[i].startPos = pt0;
            instances[i].endPos = pt1;
            instances[i].startNormalRot = rot0;
            instances[i].endNormalRot = rot1;
        }
        iBuffer.SetData(instances);
        args[1] = subdivision;
        aBuffer.SetData(args);
    }

    private void SetMesh()
    {
        nbSide = meshQuality;
        Vector3[] vertices = new Vector3[nbSide * 2];
        Vector3[] normals = new Vector3[nbSide * 2];
        Vector2[] uv0 = new Vector2[nbSide * 2]; // 0: start vertices, 1: end vertices
        int[] triangles = new int[nbSide * 2 * 3];
        float hheight = 0.5f;
        for (uint i = 0; i < nbSide; i++)
        {
            float rad = (float)i / (float)nbSide * Mathf.PI * 2f + Mathf.PI / 2f;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            uint current0 = i;
            uint current1 = i + nbSide;
            vertices[current0] = new Vector3(0, -hheight, 0);
            normals[current0] = new Vector3(cos, 0, sin);
            uv0[current0] = Vector2.zero;
            uv0[current0].y = rad;
            vertices[current1] = new Vector3(0, hheight, 0);
            normals[current1] = new Vector3(cos, 0, sin);
            uv0[current1] = Vector2.one;
            uv0[current1].y = rad;
            uint next0 = (i + 1) % nbSide;
            uint next1 = (i + 1) % nbSide + nbSide;
            uint triangle = i * 6;
            triangles[triangle++] = (int)current0;
            triangles[triangle++] = (int)next0;
            triangles[triangle++] = (int)current1;
            triangles[triangle++] = (int)current1;
            triangles[triangle++] = (int)next0;
            triangles[triangle] = (int)next1;
        }
        mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.uv = uv0;
        if (aBuffer != null)
        {
            args[0] = mesh.GetIndexCount(0);
            aBuffer.SetData(args);
        }
    }

}