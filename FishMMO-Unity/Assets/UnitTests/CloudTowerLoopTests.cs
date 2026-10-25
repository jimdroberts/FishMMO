using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace FishMMO.UnitTests {
public class CloudTowerLoopTests {
[TestCase(1,1f,0)] [TestCase(32,750f,109)] [TestCase(121,9000f,13579)]
public void RolledLatticeMatchesOriginalAcrossNegativeAndPositiveCells(int period,float cell,int seed) {
if(!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires GPU compute.");
string folder="Assets/CloudTowerFixture_"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);
try {
string path=folder+"/Compare.compute";
File.WriteAllText(path,@"#pragma kernel Compare
#pragma use_dxc vulkan
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
#include ""Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl""
float FishCloudTowerReference(float2 driftedMetres)
{
    float2 p = driftedMetres / max(1.0, _FishCloudColumn.y);
    int period = max(1, (int)_FishCloudColumn.z);
    uint seed = (uint)_FishCloudTowerSeed;
    int2 i = (int2)floor(p);
    float best = 0.0;
    [unroll] for (int dy = -1; dy <= 1; dy++)
    {
        [unroll] for (int dx = -1; dx <= 1; dx++)
        {
            int cx = i.x + dx, cy = i.y + dy;
            int wx = FishCloudWrapCell(cx, period), wy = FishCloudWrapCell(cy, period);
            float strength = smoothstep(0.0, 1.0, saturate((FishCloudWxValue(wx, wy, seed) - 0.55) / 0.30));
            float2 jitter = 0.2 + 0.6 * float2(FishCloudWxValue(wx, wy, seed ^ 0x68E31DA4u), FishCloudWxValue(wx, wy, seed ^ 0xB5297A4Du));
            float radius = 0.65 * (0.75 + 0.5 * FishCloudWxValue(wx, wy, seed ^ 0x1B56C4E9u));
            float d = length(p - (float2(cx, cy) + jitter)) / radius;
            float t = saturate((d - 0.35) / 0.65);
            best = max(best, strength * (1.0 - t * t * (3.0 - 2.0 * t)));
        }
    }
    return best;
}
RWStructuredBuffer<float2> Results;
[numthreads(64,1,1)] void Compare(uint3 id:SV_DispatchThreadID) {
float2 p=float2((int)(id.x%64)-32,(int)(id.x/64)-32)*(_FishCloudColumn.y*0.137)+float2(0.125,-0.875);
Results[id.x]=float2(FishCloudTowerReference(p),FishCloudTowerAnalytic(p));
}
");AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(path);Assert.That(shader,Is.Not.Null);
using(var buffer=new ComputeBuffer(4096,8)) {
shader.SetVector("_FishCloudColumn",new Vector4(1,cell,period,0));shader.SetFloat("_FishCloudTowerSeed",seed);
int kernel=shader.FindKernel("Compare");shader.SetBuffer(kernel,"Results",buffer);shader.Dispatch(kernel,64,1,1);
var result=new Vector2[4096];buffer.GetData(result);float maximum=0;
for(int i=0;i<result.Length;i++) { maximum=Mathf.Max(maximum,result[i].x);Assert.That(result[i].y,Is.EqualTo(result[i].x).Within(0.00001f),"sample "+i); }
if(period>1)Assert.That(maximum,Is.GreaterThan(0.1f),"comparison must exercise actual towers");
}
} finally {AssetDatabase.DeleteAsset(folder);}
}
}}
