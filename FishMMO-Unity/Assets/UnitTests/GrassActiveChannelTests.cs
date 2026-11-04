using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
    public class GrassActiveChannelTests
    {
        private const string ShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.compute";
        private const string ProbePath = "Assets/TestHarness/World/Editor/GrassDensityProbe.compute";

        [TestCase(1)]
        [TestCase(8)]
        [TestCase(17)]
        public void SparseDensityMatchesAllChannelsAcrossBilinearBoundariesAndEdges(int resolution)
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a real GPU");
            var build = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            var probe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbePath));
            var density = new Texture2DArray(resolution, resolution, 17, TextureFormat.RGBA32, false, true);
            try
            {
                var blank = new Color32[resolution * resolution];
                var sentinel = new Color32[blank.Length];
                for (int i = 0; i < sentinel.Length; i++) sentinel[i] = new Color32(255, 255, 255, 255);
                density.SetPixels32(sentinel, 0);
                for (int m = 0; m < 16; m++)
                {
                    var pixels = new Color32[blank.Length];
                    for (int j = 0; j < 4; j++)
                    {
                        int channel = m * 4 + j;
                        // Keep some maps completely empty; exercise both 32-bit mask words and channel 63.
                        if (m != 0 && m != 7 && m != 8 && m != 15) continue;
                        int x = (channel * 7 + 8) % resolution, z = (channel * 11 + 7) % resolution;
                        Color32 pixel = pixels[z * resolution + x]; pixel[j] = (byte)(channel + 1);
                        pixels[z * resolution + x] = pixel;
                    }
                    density.SetPixels32(pixels, m + 1);
                }
                density.Apply(false, false);
                int side = (resolution + GrassTerrainAtlas.ActiveRegionSide - 1) / GrassTerrainAtlas.ActiveRegionSide;
                int header = GrassTerrainAtlas.TerrainUints, masks = header + 2;
                var table = new uint[masks + side * side * 2];
                uint Bits(float f) => BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
                table[4] = Bits(resolution); table[5] = Bits(resolution); table[7] = Bits(resolution);
                table[17] = 16; table[33] = 1; table[header] = (uint)masks; table[header + 1] = (uint)side;
                using var shared = new GraphicsBuffer(GraphicsBuffer.Target.Structured, table.Length, 4);
                shared.SetData(table);
                int kernel = build.FindKernel("FishGrassBuildActiveChannels");
                build.SetInt("_GrassActiveTableBase", header); build.SetInt("_GrassActiveTerrain", 0);
                build.SetTexture(kernel, "_GrassDensity", density); build.SetBuffer(kernel, "_GrassActiveOut", shared);
                build.Dispatch(kernel, side, side, 1);
                shared.GetData(table);
                Assert.That(table[33], Is.EqualTo(1), "building masks does not overwrite the original terrain row");
                Assert.That(table[header], Is.EqualTo(masks));
                var points = new List<Vector2>();
                for (float z = -1; z <= resolution + 1; z += .25f)
                    for (float x = -1; x <= resolution + 1; x += .25f) points.Add(new Vector2(x, z));
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Count, 8);
                input.SetData(points);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Count * 16 * 2, 16);
                int check = probe.FindKernel("ProbeDensity");
                probe.SetInt("_GrassActiveTableBase", header); probe.SetInt("_GrassUseActiveChannels", 1);
                probe.SetInt("_ProbeCount", points.Count); probe.SetTexture(check, "_GrassDensity", density);
                probe.SetBuffer(check, "_GrassShared", shared); probe.SetBuffer(check, "_ProbePoints", input);
                probe.SetBuffer(check, "_ProbeDensity", output);
                probe.Dispatch(check, (points.Count * 16 + 63) / 64, 1, 1);
                var result = new Vector4[points.Count * 16 * 2]; output.GetData(result);
                for (int i = 0; i < result.Length; i += 2)
                    Assert.That(result[i + 1], Is.EqualTo(result[i]), $"point {points[i / 32]}, map {(i / 2) % 16}");
                // Empty maps must actually be omitted, rather than passing by keeping every channel.
                Assert.That(table[masks] & 0x0FFFFFF0u, Is.Zero);
                Assert.That((table[masks] | table[masks + 1]) != 0, Is.True);
                probe.SetInt("_GrassUseActiveChannels", 0);
                probe.Dispatch(check, (points.Count * 16 + 63) / 64, 1, 1); output.GetData(result);
                for (int i = 0; i < result.Length; i += 2) Assert.That(result[i + 1], Is.EqualTo(result[i]));
            }
            finally { Object.DestroyImmediate(build); Object.DestroyImmediate(probe); Object.DestroyImmediate(density); }
        }

        [TestCase(16)]
        [TestCase(64)]
        public void AtlasKeepsOriginalRowsAndAllocatesRegionsOnlyForTheWidestScene(int channels)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires GPU");
            var terrains = new GrassTerrain[2];
            using var atlas = new GrassTerrainAtlas();
            try
            {
                for (int i = 0; i < terrains.Length; i++)
                {
                    var terrain = new GrassTerrain { Channels = channels, Size = new Vector3(17, 1, 17), DensityResolution = 17, HeightResolution = 4 };
                    terrains[i] = terrain;
                    var height = new Texture2D(4, 4, TextureFormat.RFloat, false, true); height.Apply(false, true);
                    typeof(GrassTerrain).GetField("heightTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(terrain, height);
                    terrain.Density = new Texture2D[channels / 4];
                    for (int m = 0; m < terrain.Density.Length; m++)
                    {
                        terrain.Density[m] = new Texture2D(17, 17, TextureFormat.RGBA32, false, true);
                        terrain.Density[m].Apply(false, true);
                    }
                    for (int c = 0; c < GrassTerrain.MaxChannels; c++) terrain.ChannelTypes[c] = c < channels ? c : -1;
                }
                Assert.That(atlas.Sync(terrains, _ => null), Is.True);
                int version = atlas.Version;
                Assert.That(atlas.Sync(terrains, _ => null), Is.True); Assert.That(atlas.Version, Is.EqualTo(version));
                int header = 2 * GrassTerrainAtlas.TerrainUints;
                Assert.That(atlas.ActiveTableBase, Is.EqualTo(header));
                int regionWords = channels > GrassTerrain.WideChannels ? 3 * 3 * 2 : 0;
                Assert.That(atlas.LinearTableBase, Is.EqualTo(header + 4 + regionWords * 2));
                Assert.That(atlas.Shared.count, Is.EqualTo(header + 4 + regionWords * 2 + GrassTerrainAtlas.LinearTableEntries));
                var data = new uint[atlas.Shared.count]; atlas.Shared.GetData(data);
                Assert.That(data[header], Is.EqualTo(header + 4));
                Assert.That(data[header + 2], Is.EqualTo(header + 4 + regionWords));
                Assert.That(data[header + 1], Is.EqualTo(3)); Assert.That(data[header + 3], Is.EqualTo(3));
                Assert.That(data[33], Is.Zero); Assert.That(data[GrassTerrainAtlas.TerrainUints + 33], Is.EqualTo(channels / 4));
            }
            finally { foreach (var terrain in terrains) terrain?.Dispose(); }
        }

        [Test, Timeout(600000)]
        public void GrassComputeCompilesForWebGpu()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) Assert.Ignore("WebGL support required");
            CollectionAssert.AreEqual(new[] { GraphicsDeviceType.WebGPU }, PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL));
            string output = Path.Combine(Path.GetTempPath(), "fish-grass-active-webgpu-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            try
            {
                using var scope = new WebGLRenderPipelineScope(true);
                var manifest = CompatibilityBuildPipeline.BuildAssetBundles(output, new[] {
                    new AssetBundleBuild { assetBundleName = "grass-compute", assetNames = new[] { ShaderPath, ProbePath } }
                }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.WebGL);
                Assert.That(manifest, Is.Not.Null);
                Assert.That(new FileInfo(Path.Combine(output, "grass-compute")).Length, Is.GreaterThan(0));
            }
            finally { Directory.Delete(output, true); }
        }
    }
}
