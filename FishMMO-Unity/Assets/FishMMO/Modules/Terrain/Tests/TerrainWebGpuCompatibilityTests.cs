using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>Split from WebGpuRenderingCompatibilityTests (Phase 4 of the core and modules plan, 2026-11-18): the Terrain module's half.</summary>
	public class TerrainWebGpuCompatibilityTests
	{
        [TestCase(GraphicsDeviceType.WebGPU,true,BatchBufferTarget.RawBuffer,true)]
        [TestCase(GraphicsDeviceType.WebGPU,false,BatchBufferTarget.RawBuffer,false)]
        [TestCase(GraphicsDeviceType.Vulkan,true,BatchBufferTarget.RawBuffer,true)]
        [TestCase(GraphicsDeviceType.Direct3D11,true,BatchBufferTarget.RawBuffer,true)]
        [TestCase(GraphicsDeviceType.Direct3D12,true,BatchBufferTarget.RawBuffer,true)]
        [TestCase(GraphicsDeviceType.Vulkan,false,BatchBufferTarget.RawBuffer,false)]
        [TestCase(GraphicsDeviceType.Vulkan,true,BatchBufferTarget.ConstantBuffer,false)]
        public void BrgUsesCompatibleBackendAndBufferLayout(GraphicsDeviceType api,bool compute,BatchBufferTarget target,bool expected)
            => Assert.That(TerrainGpuRenderer.SupportsBrg(api,compute,target),Is.EqualTo(expected));

        [TestCase(GraphicsDeviceType.Vulkan,false,true)]
        [TestCase(GraphicsDeviceType.WebGPU,true,true)]
        [TestCase(GraphicsDeviceType.WebGPU,false,false)]
        [TestCase(GraphicsDeviceType.Direct3D11,true,false)]
        [TestCase(GraphicsDeviceType.Direct3D12,true,false)]
        [TestCase(GraphicsDeviceType.OpenGLES3,true,false)]
        public void FirstInstanceRequiresMatchingBackendAndEnabledDeviceFeature(GraphicsDeviceType api,bool feature,bool expected)
            => Assert.That(TerrainGpuRenderer.SupportsInstanceIdBase(api,feature),Is.EqualTo(expected));

        // WebGPU's direct header loads read exactly the words the native metadata names (TerrainGpuRenderer.Brg.cs SyncDots).
        [Test]
        public void WebGpuBatchHeaderAddressesMatchTheBatchLayout()
        {
            string include=ProjectFiles.ReadAllText("Assets/FishMMO/Shaders/FishIndirectInstancing.hlsl");
            Assert.That(include,Does.Contain("#define FISH_BRG_CONTACT_ON_ADDRESS "+TerrainBrgMath.ContactOnAddress+"u"));
            Assert.That(include,Does.Contain("#define FISH_BRG_FLORA_ON_ADDRESS "+TerrainBrgMath.FloraOnAddress+"u"));
            Assert.That(TerrainBrgMath.ContactOnAddress&TerrainBrgMath.PerInstanceBit,Is.Zero,"a per-batch word, not per instance");
        }
	}
}
