using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// A plain sky colour for a world with no sky (Phase 4 of the core and modules plan, 2026-11-18): the sky, its sun, clouds and
	/// weather are the World Simulation module's, and without it a world scene's cameras cleared to black. While a world scene is
	/// loaded and nothing has set a sky material (<c>RenderSettings.skybox</c>), each game camera clears to this colour instead;
	/// when a sky is drawn (the module's, or a scene's own skybox) or the world is left, the camera gets its own colour back.
	/// </summary>
	public static class CoreSkyColour
	{
		/// <summary>A clear midday blue.</summary>
		public static readonly Color Sky = new Color(0.53f, 0.68f, 0.86f, 1f);

		// The cameras given the sky colour, with the background colour each had before.
		private static readonly Dictionary<Camera, Color> original = new Dictionary<Camera, Color>();
		private static readonly List<Camera> restore = new List<Camera>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Install()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			original.Clear();
		}

		/// <summary>Whether a camera clears to the plain sky colour now: a game camera in a world with no sky material.</summary>
		public static bool Applies(Camera camera)
		{
			return camera != null && camera.cameraType == CameraType.Game && RenderSettings.skybox == null && WorldSceneSettings.AnyLoaded
				&& (camera.clearFlags == CameraClearFlags.Skybox || camera.clearFlags == CameraClearFlags.SolidColor);
		}

		private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
		{
			if (Applies(camera))
			{
				if (!original.ContainsKey(camera))
				{
					original[camera] = camera.backgroundColor;
				}
				camera.backgroundColor = Sky;
				return;
			}
			if (camera != null && original.TryGetValue(camera, out Color own))
			{
				camera.backgroundColor = own;
				original.Remove(camera);
			}
			// Cameras destroyed while they had the sky colour: forget them.
			if (original.Count > 0)
			{
				restore.Clear();
				foreach (KeyValuePair<Camera, Color> pair in original)
				{
					if (pair.Key == null)
					{
						restore.Add(pair.Key);
					}
				}
				for (int i = 0; i < restore.Count; i++)
				{
					original.Remove(restore[i]);
				}
			}
		}
	}
}
