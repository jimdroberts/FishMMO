using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The weather's fog layer, analytically: lying in the low ground with a top — the FALLBACK, where
	/// the cloud march is not drawing the fog (P5).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The fog is the cloud march's now.</b> A fog is cloud whose base is the ground, and the march
	/// walks it with the clouds (FishCloudVolume.hlsl): its banks, its billowed top, its drift, at every
	/// distance, over the world and the sky alike. This pass drew the same layer as one smooth
	/// trapezoid in altitude from the froxel volume's edge to the horizon — a flat, textureless wash
	/// round every horizon, which was most of what anyone ever saw of a fog. It stands down whenever the
	/// march has drawn the fog for the camera (<see cref="FogLayerView.MarchedThisFrame"/>), and draws
	/// it where the march does not run: a renderer without the cloud feature, or a sky that has not
	/// bound its volumes. It still hands the water its fog either way (below), which the march cannot:
	/// the water is drawn after it.
	/// </para>
	/// <para>
	/// Unity's built-in fog is a function of distance alone, so a valley floor and a ridge at the
	/// same range are equally foggy, a mist hides the sky as much as the ground and a mist, a fog and
	/// a dense fog are three shades of one flat wash. A fog is a layer of chilled air on the ground
	/// with a top (<see cref="FogLayer"/>). This pass reads the world position
	/// behind each pixel out of the depth buffer and integrates that layer along the view ray, in
	/// closed form (FishFogLayer.hlsl), and lights it as a fog is lit: the beam thrown forward round the
	/// sun, the light the fog above has scattered arriving diffuse, the sky's own from all round.
	/// </para>
	/// <para>
	/// <b>As the fallback, it shares the fog with the froxel volume, and never draws the same fog twice.</b> Where
	/// <see cref="FishVolumetricFogFeature"/> runs, it draws the layer from the camera to its far edge
	/// with the fog's structure in it, and this pass starts there and carries the same layer on to the
	/// horizon. Where it does not — WebGL2, which has no compute — this pass draws it all. The
	/// pipeline's own distance fog leaves the fog's drops out wherever this pass is live
	/// (<see cref="DrawsTheLayer"/>), and keeps only what is falling, which does fill the air evenly.
	/// </para>
	/// <para>
	/// Everything about the fog is the weather's: how thick, how deep, how lifted, how it drifts. There
	/// is nothing to author here but the material.
	/// </para>
	/// </remarks>
	public class FishHeightFogFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/Weather/HeightFog";

		[Tooltip("The height-fog material. Left empty, the feature makes one from Fog Shader.")]
		public Material FogMaterial;

		[Tooltip("The height-fog shader (Hidden/FishMMO/Weather/HeightFog). Referenced here so a build carries it: Shader.Find " +
			"only finds a shader something in the build references, and nothing else does. Left empty, it is found by name, " +
			"which works in the editor alone.")]
		public Shader FogShader;

		// The material made from the shader when none is assigned. Kept apart from FogMaterial: written into that
		// serialized field, a saved renderer asset referenced a material that is never saved, and Dispose destroyed
		// whatever the field held — an assigned material asset included.
		[System.NonSerialized] private Material created;

		/// <summary>How far a ray into open sky is followed through the layer when the sky has no reach of its own to say, m.</summary>
		public const float DefaultSkyDistance = 44000f;

		private FogPass pass;

		private static readonly HashSet<FishHeightFogFeature> live = new HashSet<FishHeightFogFeature>();

		/// <summary>
		/// Whether this pass can draw the fog's layer where the cloud march does not: a live, active
		/// feature on the renderer, whose shader this machine can run. While it can — or while the march
		/// draws it — the pipeline's distance fog leaves the fog's drops out (<see cref="FogLayerView.Drawn"/>).
		/// </summary>
		public static bool DrawsTheLayer
		{
			get
			{
				foreach (FishHeightFogFeature feature in live)
				{
					if (feature != null && feature.isActive && feature.Resolve() != null)
					{
						return true;
					}
				}
				return false;
			}
		}

		public override void Create()
		{
			pass = new FogPass
			{
				// After the opaque world and the clouds, before transparents: fog sits over the solid
				// world and the sky behind it, and water and glass are drawn through it rather than
				// behind it. Before the froxel volume, which lies in front of what this draws.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
			live.Add(this);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Material material = Resolve();
			if (material == null)
			{
				return;
			}
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			// An overlay draws into its base camera's frame, which the base has already fogged.
			if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
			{
				return;
			}
			pass.Setup(material);
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (FogMaterial != null)
			{
				return FogMaterial;
			}
			if (created != null)
			{
				return created;
			}
			Shader shader = FogShader != null ? FogShader : Shader.Find(ShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			created = CoreUtils.CreateEngineMaterial(shader);
			return created;
		}

		protected override void Dispose(bool disposing)
		{
			live.Remove(this);
			if (created != null)
			{
				CoreUtils.Destroy(created);
				created = null;
			}
			pass = null;
		}

		private sealed class FogPass : ScriptableRenderPass
		{
			private const string PassName = "Fish Height Fog";

			private static readonly int InverseVPId = Shader.PropertyToID("_FishFogInverseVP");
			private static readonly int RangeId = Shader.PropertyToID("_FishFogRange");
			private static readonly int ForwardId = Shader.PropertyToID("_FishFogForward");

			/* The same fog, published for TRANSPARENT surfaces. This pass fogs what is already in the
			 * frame, before the transparent queue — so the sea, the shore and anything else drawn
			 * after it came out perfectly clear through the thickest fog. They apply it themselves, at
			 * their own depth, from these (FishWaterFog.hlsl): an exponential falloff over the ground,
			 * which is as much of the layer's shape as a surface drawn low on the water needs — its
			 * extinction at the ground, falling off over the layer's depth. Density zero means no fog
			 * this frame. */
			public static readonly int AirParamsId = Shader.PropertyToID("_FishAirFogParams");
			public static readonly int AirColorId = Shader.PropertyToID("_FishAirFogColor");
			public static readonly int AirSunId = Shader.PropertyToID("_FishAirFogSun");
			public static readonly int AirSunColorId = Shader.PropertyToID("_FishAirFogSunColor");
			public static readonly int AirRangeId = Shader.PropertyToID("_FishAirFogRange");

			private Material material;

			public void Setup(Material fogMaterial)
			{
				material = fogMaterial;
			}

			private class FogData
			{
				public Material Material;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				// No fog for transparents unless this pass finds some below.
				Shader.SetGlobalVector(AirParamsId, Vector4.zero);
				if (material == null)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}

				FogLayerView layer = FogLayerView.Current;
				if (!layer.Visible)
				{
					// No fog worth drawing. Skipping the pass entirely is the point of checking.
					return;
				}
				FogLayerView.Lighting lighting = FogLayerView.PublishLighting();

				Camera camera = cameraData.camera;
				Vector3 forward = camera.transform.forward;
				// Where the froxel volume, if it runs for this camera, hands the fog over to this pass. Not
				// when the cloud march has drawn the fog: the froxel volume stands down then too, and the
				// water, which lays this fog over itself from the camera out, must not leave out the half
				// kilometre the volume would have covered.
				bool marched = FogLayerView.MarchedThisFrame;
				float start = marched ? 0f : FishVolumetricFogFeature.ReachFor(camera);
				SkySystem sky = SkySystem.Instance;
				float skyDistance = sky != null ? Mathf.Max(1000f, sky.CloudFarDistance) : DefaultSkyDistance;

				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
				material.SetMatrix(InverseVPId, (projection * view).inverse);
				material.SetVector(RangeId, new Vector4(start, skyDistance, 0f, 0f));
				material.SetVector(ForwardId, new Vector4(forward.x, forward.y, forward.z, 0f));

				// For the water: the layer's extinction at the ground under the camera, falling off over
				// its depth, lit as its middle is lit. A fog the wind has lifted has cleared off the water
				// under it, all but a haze.
				Vector3 eye = cameraData.worldSpaceCameraPos;
				float ground = FogLayerView.GroundUnder(eye);
				FogLayerView.Column column = layer.Over(ground, ground);
				Color lit = layer.LightAt(ground + 0.5f * layer.Depth, column, lighting);
				float onTheWater = layer.Extinction * (1f - 0.8f * Mathf.Clamp01(layer.Lift));
				Shader.SetGlobalVector(AirParamsId, new Vector4(onTheWater, Mathf.Max(1f, layer.Depth), ground, 1f));
				Shader.SetGlobalVector(AirColorId, lit);
				Shader.SetGlobalVector(AirSunId, new Vector4(lighting.ToLight.x, lighting.ToLight.y, lighting.ToLight.z, 0.6f));
				Shader.SetGlobalVector(AirSunColorId, lighting.LightColor);
				// z: 1 when the cloud march has drawn the fog, so the cloud buffer the sea lays over itself
				// (FishWaterBehindClouds) already holds this fog in front of it. A surface that lays that
				// buffer over itself must then leave this fog out, or it takes the drops' light twice; one
				// that does not (the shore, the spray) still needs it.
				Shader.SetGlobalVector(AirRangeId, new Vector4(start, skyDistance, marched ? 1f : 0f, 0f));

				// The fog itself is the cloud march's, where it has run for this camera: it was recorded
				// before this pass and has already drawn the layer, with its structure, all the way out.
				// Drawn again here it would take the same drops' light twice.
				if (marched)
				{
					return;
				}

				using (var builder = renderGraph.AddRasterRenderPass<FogData>(PassName, out FogData data))
				{
					data.Material = material;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((FogData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, 0));
				}
			}
		}
	}
}
