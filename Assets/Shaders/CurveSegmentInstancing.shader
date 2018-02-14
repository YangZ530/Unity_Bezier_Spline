// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "CurveSegmentInstancing"
{

Properties
{
    _Shininess("Shininess", Float) = 10.0
}

SubShader
{

Tags{ "Queue" = "Transparent" }
LOD 300

CGINCLUDE

#include "UnityCG.cginc"
#include "Lighting.cginc"

struct Instance
{
    float3 pos0;
    float3 pos1;
    float3 rot0;
    float3 rot1;
    float3 scale0;
    float3 scale1;
    float4 color0;
    float4 color1;
};

#if defined(SHADER_API_D3D11) && SHADER_TARGET >= 45
StructuredBuffer<Instance> _instances;
#endif

uniform float4x4 _local2World;
uniform float4x4 _world2Local;
uniform float _volume;
float _Shininess;

struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv0 : TEXCOORD0;
};

struct v2f
{
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
    float3 posWorld : TEXCOORD0;
    float4 color : COLOR;
};

inline float3 rotate(float3 v, float3 rot)
{
    float3 a = normalize(rot);
    float angle = length(rot);
    if (abs(angle) < 0.1) return v;
    float s = sin(angle);
    float c = cos(angle);
    float r = 1.0 - c;
    float3x3 m = float3x3(
        a.x * a.x * r + c,          a.y * a.x * r + a.z * s,        a.z * a.x * r - a.y * s,
        a.x * a.y * r - a.z * s,    a.y * a.y * r + c,              a.z * a.y * r + a.x * s,
        a.x * a.z * r + a.y * s,    a.y * a.z * r - a.x * s,        a.z * a.z * r + c
    );
    return mul(m, v);
}

inline float4 quatInv(const float4 q)
{
    return float4(-q.xyz, q.w);
}

inline float4 quatDot(const float4 q1, const float4 q2)
{
    float scal = q1.w * q2.w - dot(q1.xyz, q2.xyz);
    float3 v = cross(q1.xyz, q2.xyz) + q1.w * q2.xyz + q2.w * q1.xyz;
    return float4(v, scal);
}

inline float3 quatMul(const float4 q, const float3 v)
{
    return quatDot(q, quatDot(float4(v, 0), quatInv(q))).xyz;
}

v2f vert(appdata v, uint instanceID : SV_InstanceID)
{
    float4 color = 1;
#if defined(SHADER_API_D3D11) && SHADER_TARGET >= 45
    Instance p = _instances[instanceID];
    v.vertex.xyz *= v.uv0.x == 0 ? p.scale0 : p.scale1;
    v.vertex.xyz = rotate(v.vertex.xyz, v.uv0.x == 0 ? p.rot0 : p.rot1) * _volume;
    v.vertex.xyz += v.uv0.x == 0 ? p.pos0 : p.pos1;

    v.normal.xyz = rotate(v.normal.xyz, v.uv0.x == 0 ? p.rot0 : p.rot1);

    v.vertex = mul(_local2World, v.vertex);
    v.normal = mul(_local2World, float4(v.normal, 0)).xyz;
    v.normal = normalize(v.normal);
    color = v.uv0.x == 0 ? p.color0 : p.color1;
#endif
    v2f o;
    o.posWorld = v.vertex.xyz;
    o.position = mul(UNITY_MATRIX_VP, v.vertex);
    o.normal = v.normal;
    o.color = color;
    return o;
}

float4 frag(v2f i) : COLOR
{
    float4 albedo = i.color;
    float3 n = i.normal;
    float3 view = normalize(_WorldSpaceCameraPos - i.posWorld.xyz);
    float3 light = normalize(_WorldSpaceLightPos0.xyz);

    float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb * albedo.rgb;
    float3 h = normalize(light + view);
    float diff = max(0, dot(n, light));
    float nh = max(0, dot(n, h));
    float3 diffuse = _LightColor0.rgb * diff * albedo.rgb;
    float3 specular = _LightColor0.rgb * pow(saturate(nh), _Shininess);

    return float4(ambient + diffuse + specular, 1);
}

ENDCG

Pass
{

Name "MeshInstancing"
Tags { "LightMode" = "ForwardBase" }
ZWrite On

CGPROGRAM

#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fwdbase
#pragma target 5.0

ENDCG

}

}

Fallback "Diffuse"

}