using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The hot air over a lava tube's molten lava round the camera (2026-10-20): each of the nearest
	/// <see cref="MaxSpots"/> lava lights (<see cref="SceneEmitterKind.Magma"/>, baked by the cut) as a ball of boiling air
	/// over its body (<see cref="SceneMagma.HeatBall"/>), for the heat shimmer to bend the frame through
	/// (FishHeatShimmerFeature, FishHeatShimmer.shader's heat spots).
	/// </summary>
	/// <remarks>
	/// Play mode and the scene view, client only. Searched again only when the camera has moved <see cref="Refind"/> metres
	/// or the scenes' emitters changed, so a frame with no lava near costs one distance check.
	/// </remarks>
	public static class CaveLavaHeat
	{
		/// <summary>Spots the shimmer takes at once (FishHeatShimmer.shader's HEAT_SPOTS).</summary>
		public const int MaxSpots = 8;
		/// <summary>Metres from the camera a lava light's hot air is looked for: past it the shimmer is under a pixel.</summary>
		public const float Reach = 45f;
		private const float Refind = 4f;

		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int foundVersion = -1;
		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			near.Clear();
			foundVersion = -1;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
			hooked = false;
		}

		/// <summary>
		/// The hot-air balls round <paramref name="eye"/>, nearest first, into <paramref name="into"/> (xyz centre, w radius;
		/// <see cref="MaxSpots"/> long, the unused ones zero); how many there are.
		/// </summary>
		public static int Spots(Vector3 eye, Vector4[] into)
		{
			if (!hooked)
			{
				hooked = true;
				SceneManager.sceneUnloaded += _ => foundVersion = -1;
			}
			if (SceneSiteEmitters.Version != foundVersion || (eye - foundAt).sqrMagnitude > Refind * Refind)
			{
				foundVersion = SceneSiteEmitters.Version;
				foundAt = eye;
				SceneSiteEmitters.Near(eye, Reach + Refind, kind => kind == SceneEmitterKind.Magma, MaxSpots, near);
			}
			int count = Mathf.Min(near.Count, Mathf.Min(MaxSpots, into.Length));
			for (int i = 0; i < into.Length; i++)
			{
				into[i] = i < count ? SceneMagma.HeatBall(near[i]) : Vector4.zero;
			}
			return count;
		}
	}
}
