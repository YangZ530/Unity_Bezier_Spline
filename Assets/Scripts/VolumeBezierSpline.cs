using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.Rendering;

public class VolumeBezierSpline : MonoBehaviour
{

    private struct Instance
    {
        public Vector3 startPos;
        public Vector3 endPos;
        public Vector3 startNormalRot;
        public Vector3 endNormalRot;
    }

    [SerializeField] public uint curveSubdivision = 4;
    [SerializeField] public uint meshQuality = 12;
    [SerializeField] public string shaderName = "CurveSegmentInstancing";
    [SerializeField] public float splineWidth = 0.5f;
    [SerializeField] public uint splinePieces = 100;
    [SerializeField] public Bounds bounds;
    [SerializeField] public int seed = 314159265;

    private BezierCurve[] curves;
    private Material material;
    private Mesh mesh;
    private Instance[] instances;
    private ComputeBuffer iBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private ComputeBuffer aBuffer;
    private Dictionary<Camera, CommandBuffer> cams = new Dictionary<Camera, CommandBuffer>();

    private bool initialized = false;

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
        if (!initialized)
        {
            return;
        }
        foreach (var curve in curves)
        {
            Gizmos.color = Color.green;
            var size = Vector3.one * 0.2f;
            Gizmos.DrawCube(curve.start, size);
            Gizmos.DrawCube(curve.cp0, size);
            Gizmos.DrawCube(curve.cp1, size);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(curve.start, curve.cp0);
            Gizmos.DrawLine(curve.cp1, curve.end);
        }
    }

    private void Start()
    {
        Random.InitState(seed);

        if (GetComponent<Renderer>() == null)
        {
            gameObject.AddComponent<MeshRenderer>();
        }

        var shader = Shader.Find(shaderName);
        material = new Material(shader);
        material.hideFlags = HideFlags.HideAndDontSave;
        material.SetFloat("_volume", splineWidth);

        curves = new BezierCurve[splinePieces];

        Vector3[] curvePoints = new Vector3[splinePieces + 1];
        for (int i = 0; i <= splinePieces; i++)
        {
            curvePoints[i].x = Random.Range(bounds.min.x, bounds.max.x);
            curvePoints[i].y = Random.Range(bounds.min.y, bounds.max.y);
            curvePoints[i].z = Random.Range(bounds.min.z, bounds.max.z);
            if (i == 0) continue;
            curves[i - 1] = new BezierCurve(curvePoints[i - 1], curvePoints[i]);
        }

        CalculateControlPoints();

        SetMesh();
        SampleSpline();
        SetBuffers();
        initialized = true;
    }

    private void Update()
    {
        material.SetMatrix("_local2World", transform.localToWorldMatrix);
        material.SetMatrix("_world2Local", transform.worldToLocalMatrix);
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

    // Get a set of control points from a set of start/end points
    // Source: https://www.particleincell.com/2012/bezier-splines/
    private void CalculateControlPoints()
    {
        uint n = splinePieces;

        float[] a = new float[n];
        float[] b = new float[n];
        float[] c = new float[n];

        Vector3[] r = new Vector3[n];

        a[0] = 0;
        b[0] = 2;
        c[0] = 1;
        r[0] = curves[0].start + 2f * curves[0].end;

        for (int i = 1; i < n - 1; i++)
        {
            a[i] = 1;
            b[i] = 4;
            c[i] = 1;
            r[i] = 4f * curves[i].start + 2f * curves[i].end;
        }

        a[n - 1] = 2;
        b[n - 1] = 7;
        c[n - 1] = 8;
        r[n - 1] = 8f * curves[n - 1].start + curves[n - 1].end;

        for (int i = 1; i < n; i++)
        {
            float m = a[i] / b[i - 1];
            b[i] -= m * c[i - 1];
            r[i] -= m * r[i - 1];
        }

        curves[n - 1].cp0 = r[n - 1] / b[n - 1];
        for (int i = (int)n - 2; i >= 0; i--)
        {
            curves[i].cp0 = (r[i] - c[i] * curves[i + 1].cp0) / b[i];
        }

        for (int i = 0; i < n - 1; i++)
        {
            curves[i].cp1 = 2f * curves[i].end - curves[i + 1].cp0;
        }
        curves[n - 1].cp1 = 0.5f * (curves[n - 1].end + curves[n - 1].cp0);
    }

    private void SetBuffers()
    {
        if (iBuffer != null) iBuffer.Release();
        if (aBuffer != null) aBuffer.Release();
        iBuffer = new ComputeBuffer(instances.Length, Marshal.SizeOf(typeof(Instance)), ComputeBufferType.Default);
        iBuffer.SetData(instances);
        material.SetBuffer("_instances", iBuffer);
        aBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = mesh.GetIndexCount(0);
        args[1] = splinePieces * curveSubdivision;
        aBuffer.SetData(args);
    }

    private void SampleSpline()
    {
        instances = new Instance[splinePieces * curveSubdivision];
        for (int i = 0; i < splinePieces; i++)
        {
            for (int j = 0; j < curveSubdivision; j++)
            {
                var t0 = (float)j / (float)curveSubdivision;
                var t1 = (float)(j + 1) / (float)curveSubdivision;
                var pt0 = curves[i].Point(t0);
                var pt1 = curves[i].Point(t1);
                var tan0 = curves[i].Tangent(t0);
                var tan1 = curves[i].Tangent(t1);
                

                var angle = Vector3.Angle(Vector3.forward, tan0) * Mathf.Deg2Rad;
                var axis = Vector3.Cross(tan0, Vector3.forward).normalized;
                var rot0 = axis * angle;

                // Quaternion quat0 = Quaternion.LookRotation(curves[i].Normal(t0), tan0);
                // quat0.ToAngleAxis(out angle, out axis);
                // var rot0 = axis * angle * Mathf.Deg2Rad;

                angle = Vector3.Angle(Vector3.forward, tan1) * Mathf.Deg2Rad;
                axis = Vector3.Cross(tan1, Vector3.forward).normalized;
                var rot1 = axis * angle;

                // Quaternion quat1 = Quaternion.LookRotation(curves[i].Normal(t1), tan1);
                // quat1.ToAngleAxis(out angle, out axis);
                // var rot1 = axis * angle * Mathf.Deg2Rad;

                instances[i * curveSubdivision + j].startPos = pt0;
                instances[i * curveSubdivision + j].endPos = pt1;
                instances[i * curveSubdivision + j].startNormalRot = rot0;
                instances[i * curveSubdivision + j].endNormalRot = rot1;
            }
        }
    }

    private void SetMesh()
    {
        var nbSide = meshQuality;
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
            vertices[current0] = new Vector3(cos, sin, 0);
            normals[current0] = new Vector3(cos, sin, 0);
            uv0[current0] = Vector2.zero;
            uv0[current0].y = rad;
            vertices[current1] = new Vector3(cos, sin, 0);
            normals[current1] = new Vector3(cos, sin, 0);
            uv0[current1] = Vector2.one;
            uv0[current1].y = rad;
            uint next0 = (i + 1) % nbSide;
            uint next1 = (i + 1) % nbSide + nbSide;
            uint triangle = i * 6;
            triangles[triangle++] = (int)current0;
            triangles[triangle++] = (int)current1;
            triangles[triangle++] = (int)next0;
            triangles[triangle++] = (int)current1;
            triangles[triangle++] = (int)next1;
            triangles[triangle] = (int)next0;
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