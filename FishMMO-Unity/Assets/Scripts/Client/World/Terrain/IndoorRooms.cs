using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The rooms round the camera (SceneRoom, baked by the cut), handed to the lit shaders so a room is darker than the
	/// day outside it: inside, the sky's light reaches only through the windows and the door (FishIndoor.hlsl), and the
	/// room's own hearth and candles (SiteLightPool's Indoor lights) do the rest.
	/// </summary>
	/// <remarks>
	/// The nearest <see cref="MaxRooms"/> within <see cref="Reach"/>, as world-to-box matrices (<c>_FishRooms</c>, the box
	/// the unit cube, <c>_FishRoomCount</c>), refound when the camera has moved a few metres or a scene came or went.
	/// <see cref="IndoorAt"/> answers the same for the camera's own ambient (SkySystem). Play mode, client only.
	/// </remarks>
	public static class IndoorRooms
	{
		/// <summary>Twin of FishIndoor.hlsl's FISH_MAX_ROOMS.</summary>
		public const int MaxRooms = 16;
		public const float Reach = 60f;
		private const float Refind = 4f;

		private static readonly int RoomsId = Shader.PropertyToID("_FishRooms");
		private static readonly int CountId = Shader.PropertyToID("_FishRoomCount");
		private static readonly List<SceneRoom> near = new List<SceneRoom>();
		private static readonly Matrix4x4[] matrices = new Matrix4x4[MaxRooms];
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int foundVersion = -1;
		private static int updatedFrame = -1;
		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			hooked = false;
			updatedFrame = -1;
			foundVersion = -1;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
			near.Clear();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			SceneManager.sceneUnloaded += _ => foundVersion = -1;
			Application.quitting += () =>
			{
				RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
				hooked = false;
				Shader.SetGlobalFloat(CountId, 0f);
			};
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			if (updatedFrame == Time.frameCount)
			{
				return;
			}
			updatedFrame = Time.frameCount;
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (SceneSiteEmitters.Version == foundVersion && (eye - foundAt).sqrMagnitude <= Refind * Refind)
			{
				return;
			}
			foundVersion = SceneSiteEmitters.Version;
			foundAt = eye;
			SceneSiteEmitters.NearRooms(eye, Reach, MaxRooms, near);
			for (int i = 0; i < near.Count; i++)
			{
				SceneRoom room = near[i];
				matrices[i] = Matrix4x4.TRS(room.Centre, Quaternion.Euler(0f, room.Yaw, 0f), room.HalfSize).inverse;
			}
			for (int i = near.Count; i < MaxRooms; i++)
			{
				matrices[i] = Matrix4x4.zero;
			}
			Shader.SetGlobalMatrixArray(RoomsId, matrices);
			Shader.SetGlobalFloat(CountId, near.Count);
		}

		/// <summary>How far inside a room a point is, 0 outside … 1 well in (the shader's own measure, FishIndoor).</summary>
		public static float IndoorAt(Vector3 at)
		{
			float best = 0f;
			foreach (SceneRoom room in near)
			{
				Vector3 q = Quaternion.Inverse(Quaternion.Euler(0f, room.Yaw, 0f)) * (at - room.Centre);
				float d = Mathf.Max(Mathf.Abs(q.x) / Mathf.Max(0.01f, room.HalfSize.x), Mathf.Max(Mathf.Abs(q.y) / Mathf.Max(0.01f, room.HalfSize.y), Mathf.Abs(q.z) / Mathf.Max(0.01f, room.HalfSize.z)));
				best = Mathf.Max(best, Mathf.Clamp01((1f - d) / FadeShare));
			}
			return best;
		}

		/// <summary>The share of a room's half-size, in from its walls, over which indoors fades in (FishIndoor.hlsl's twin).</summary>
		public const float FadeShare = 0.2f;
	}
}
