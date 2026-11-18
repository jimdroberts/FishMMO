using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// What the instanced terrain tree renderer (<see cref="TerrainTreeInstancing"/>) and detail renderer
	/// (<see cref="TerrainDetailInstancing"/>) share: one camera's view for gathering (frustum planes,
	/// LOD factor, shadow sweep), and the 1023-instance batch draw. The arithmetic itself (frustum test,
	/// box distances, shadow sweep, batch split) is <see cref="TerrainTreeMath"/>.
	/// </summary>
	public static class TerrainInstancingShared
	{
		/// <summary>Cameras that draw no terrain details (grass, flowers, pebbles) and no blade grass: the minimap.</summary>
		private static readonly System.Collections.Generic.HashSet<Camera> noDetails = new System.Collections.Generic.HashSet<Camera>();

		/// <summary>
		/// Keeps <paramref name="camera"/> out of the detail renderer and the blade grass (trees, rocks and the
		/// ground still draw): a top-down map has no use for ground cover, and generating it there is the
		/// grass's whole per-camera cost for nothing.
		/// </summary>
		public static void SetDrawsDetails(Camera camera, bool draws)
		{
			noDetails.RemoveWhere(c => c == null);
			if (camera == null)
			{
				return;
			}
			if (draws)
			{
				noDetails.Remove(camera);
			}
			else
			{
				noDetails.Add(camera);
			}
		}

		/// <summary><see cref="Draws"/>, less the cameras kept out of the details (<see cref="SetDrawsDetails"/>).</summary>
		public static bool DrawsDetails(Camera camera) => Draws(camera) && !noDetails.Contains(camera);

		private static readonly Plane[] planes = new Plane[6];

		/// <summary>
		/// The view a camera gathers with: its frustum (the planes array is shared and refilled on every call;
		/// cameras render one after another), its biased LOD factor, and the sun and URP shadow distance for
		/// keeping off-screen shadow casters.
		/// </summary>
		public static TerrainTreeField.View ViewOf(Camera camera)
		{
			GeometryUtility.CalculateFrustumPlanes(camera, planes);
			var view = new TerrainTreeField.View
			{
				Position = camera.transform.position,
				Planes = planes,
				Orthographic = camera.orthographic,
				ScreenFactor = TerrainTreeMath.ScreenFactor(camera.fieldOfView, camera.orthographic, camera.orthographicSize, QualitySettings.lodBias),
				MaximumLodLevel = QualitySettings.maximumLODLevel,
			};

			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			float shadowDistance = pipeline != null && pipeline.supportsMainLightShadows ? Mathf.Min(pipeline.shadowDistance, camera.farClipPlane) : 0f;
			if (shadowDistance > 0f)
			{
				Light sun = RenderSettings.sun;
				view.Shadows = true;
				view.ShadowDistance = shadowDistance;
				view.LightDirection = sun != null && sun.isActiveAndEnabled ? sun.transform.forward : Vector3.down;
			}
			return view;
		}

		/// <summary>True for the cameras the instanced terrain renderers draw for: game, scene view (play mode) and reflection probes.</summary>
		public static bool Draws(Camera camera)
		{
			if (camera == null)
			{
				return false;
			}
			CameraType type = camera.cameraType;
			return type == CameraType.Game || type == CameraType.SceneView || type == CameraType.Reflection;
		}

		/// <summary>
		/// Draws <paramref name="count"/> matrices in batches of <see cref="TerrainTreeMath.MaxInstancesPerBatch"/>,
		/// each from a sub-array starting at its first instance. Returns the draws issued.
		/// </summary>
		public static int DrawBatches(in RenderParams rp, Mesh mesh, int submesh, NativeArray<Matrix4x4> matrices, int count)
		{
			int batches = TerrainTreeMath.BatchCount(count);
			for (int b = 0; b < batches; b++)
			{
				TerrainTreeMath.Batch(count, b, out int start, out int size);
				Graphics.RenderMeshInstanced(rp, mesh, submesh, matrices.GetSubArray(start, size), size, 0);
			}
			return batches;
		}
	}
}
