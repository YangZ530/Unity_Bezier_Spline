using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BezierCurve
{
    public Vector3 start;
    public Vector3 cp0;
    public Vector3 cp1;
    public Vector3 end;
    public Vector3 up_begin;
    public Vector3 up_end;
    private Quaternion startQuat;
    private Quaternion endQuat;

    public BezierCurve(Vector3 start, Vector3 p0, Vector3 p1, Vector3 end, Vector3 up_begin, Vector3 up_end)
    {
        this.start = start;
        this.cp0 = p0;
        this.cp1 = p1;
        this.end = end;
        this.up_begin = up_begin;
        this.up_end = up_end;
    }

    public BezierCurve(Vector3 start, Vector3 p0, Vector3 p1, Vector3 end) : this(start, p0, p1, end, Vector3.up, Vector3.up) { }

    public BezierCurve(Vector3 start, Vector3 end) : this(start, start, end, end, Vector3.up, Vector3.up) { }

    public BezierCurve() : this(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.up, Vector3.up) { }

    public Vector3 Point(float t)
    {
        return bezierCurve(t);
    }

    public Vector3 Tangent(float t)
    {
        return derivative(t);
    }

    public Vector3 Up(float t)
    {
        return Vector3.Lerp(up_begin, up_end, t);
    }

    public Quaternion Quat(float t)
    {
        return Quaternion.Lerp(startQuat, endQuat, t);
    }

    public Vector3 Normal(float t)
    {
        return Vector3.Cross(Vector3.Cross(cp0 - start, end - cp1), derivative(t)).normalized;
    }

    public Quaternion Orient(float t)
    {
        return Quaternion.FromToRotation(Vector3.forward, Vector3.up) * Quaternion.LookRotation(derivative(t), Vector3.Cross(derivative(t), Vector3.forward));
    }

    private Vector3 bezierCurve(float t)
    {
        var omt = 1f - t;
        var omt2 = omt * omt;
        var t2 = t * t;
        return
            start * (omt2 * omt)
            + cp0 * (3f * omt2 * t)
            + cp1 * (3f * omt * t2)
            + end * (t2 * t);
    }

    private Vector3 derivative(float t)
    {
        var omt = 1f - t;
        var omt2 = omt * omt;
        var t2 = t * t;
        return (
            start * (-omt2)
            + cp0 * (3f * omt2 - 2f * omt)
            + cp1 * (-3f * t2 + 2f * t)
            + end * (t2)
        ).normalized;
    }
}
