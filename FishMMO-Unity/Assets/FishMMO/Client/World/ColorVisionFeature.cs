using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The full-screen colour-vision correction: daltonises the 3D view for the colour vision the player
	/// has chosen (<see cref="ClientColorVisionSettings"/>), so colours the eye would confuse are pulled apart.
	/// </summary>
	/// <remarks>
	/// <para>
	/// After post-processing (the frame as tonemapped and graded, in linear values) and before the UI, which
	/// has its own safe colours and must not be shifted twice. One full-screen pass into a new colour
	/// texture that then becomes the camera's colour, so nothing is copied back. With Normal vision, the
	/// filter off or its strength at 0, no pass is added.
	/// </para>
	/// <para>
	/// The correction is the usual daltonisation: simulate what the dichromat sees (Machado et al. 2009,
	/// severity 1), take the difference from the original, and move it into the channels that eye does
	/// distinguish. It cannot add information, only move it, so it is a supplement to the safe colours
	/// (<see cref="ColorVision"/>), not a replacement for them.
	/// </para>
	/// </remarks>
	public class ColorVisionFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/ColorVision";

		[Tooltip("The correction shader (Hidden/FishMMO/ColorVision). Referenced here so a build carries it: Shader.Find " +
			"only finds a shader something in the build references. Left empty, it is found by name, which works in the editor alone.")]
		public Shader CorrectionShader;

		[System.NonSerialized] private Material created;

		private CorrectionPass pass;

		public override void Create()
		{
			pass = new CorrectionPass
			{
				renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
				// Post-processing would otherwise write straight to the back buffer, which a pass cannot read back.
				requiresIntermediateTexture = true,
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (!ClientColorVisionSettings.FilterActive)
			{
				return;
			}
			CameraType type = renderingData.cameraData.cameraType;
			if (type != CameraType.Game && type != CameraType.SceneView)
			{
				return;
			}
			if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
			{
				return;
			}
			Material material = Resolve();
			if (material == null)
			{
				return;
			}
			pass.Setup(material, ColorVision.Mode, ClientColorVisionSettings.FilterStrength);
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (created != null)
			{
				return created;
			}
			Shader shader = CorrectionShader != null ? CorrectionShader : ShaderLookup.Find(ShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			created = CoreUtils.CreateEngineMaterial(shader);
			return created;
		}

		protected override void Dispose(bool disposing)
		{
			if (created != null)
			{
				CoreUtils.Destroy(created);
				created = null;
			}
			pass = null;
		}

		private sealed class CorrectionPass : ScriptableRenderPass
		{
			private static readonly int SimRId = Shader.PropertyToID("_CvdSimR");
			private static readonly int SimGId = Shader.PropertyToID("_CvdSimG");
			private static readonly int SimBId = Shader.PropertyToID("_CvdSimB");
			private static readonly int ParamsId = Shader.PropertyToID("_CvdParams");

			// Machado, Oliveira and Fernandes (2009), severity 1.0, rows of the linear-RGB simulation matrix.
			private static readonly Vector4[] Protanopia =
			{
				new Vector4(0.152286f, 1.052583f, -0.204868f, 0f),
				new Vector4(0.114503f, 0.786281f, 0.099216f, 0f),
				new Vector4(-0.003882f, -0.048116f, 1.051998f, 0f),
			};
			private static readonly Vector4[] Deuteranopia =
			{
				new Vector4(0.367322f, 0.860646f, -0.227968f, 0f),
				new Vector4(0.280085f, 0.672501f, 0.047413f, 0f),
				new Vector4(-0.011820f, 0.042940f, 0.968881f, 0f),
			};
			private static readonly Vector4[] Tritanopia =
			{
				new Vector4(1.255528f, -0.076749f, -0.178779f, 0f),
				new Vector4(-0.078411f, 0.930809f, 0.147602f, 0f),
				new Vector4(0.004733f, 0.691367f, 0.303900f, 0f),
			};

			private Material material;
			private ColorVisionMode mode;
			private float strength;

			public void Setup(Material correction, ColorVisionMode vision, float amount)
			{
				material = correction;
				mode = vision;
				strength = amount;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (material == null || mode == ColorVisionMode.Normal)
				{
					return;
				}
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}
				TextureHandle source = resourceData.activeColorTexture;
				TextureDesc desc = renderGraph.GetTextureDesc(source);
				// A multisampled frame (no post-processing on this camera) cannot be read as a plain texture.
				if (desc.msaaSamples != MSAASamples.None)
				{
					return;
				}

				Vector4[] rows = mode == ColorVisionMode.Protanopia ? Protanopia
					: mode == ColorVisionMode.Deuteranopia ? Deuteranopia
					: Tritanopia;
				material.SetVector(SimRId, rows[0]);
				material.SetVector(SimGId, rows[1]);
				material.SetVector(SimBId, rows[2]);
				material.SetVector(ParamsId, new Vector4(strength, mode == ColorVisionMode.Tritanopia ? 1f : 0f, 0f, 0f));

				desc.name = "_FishColorVision";
				desc.clearBuffer = false;
				TextureHandle destination = renderGraph.CreateTexture(desc);
				renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), "Fish Colour Vision");
				// The corrected frame is the camera's colour from here on: the UI and the final blit read it.
				resourceData.cameraColor = destination;
			}
		}
	}
}
