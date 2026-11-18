using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The local player's view of which portals are open, per scene, as the server last reported it.
	/// </summary>
	/// <remarks>
	/// Written by the client's portal state system from <see cref="PortalStatesBroadcast"/> and
	/// <see cref="PortalStateChangedBroadcast"/>; read by <see cref="PortalActivation"/> to choose its
	/// look. A timed opening carries its own end, in local realtime, so it closes on the client
	/// without another message. Presentation only — the server decides every use.
	/// </remarks>
	public static class PortalClientStates
	{
		/// <summary>Scene name → portal index → realtime it closes at (<see cref="float.PositiveInfinity"/> for never).</summary>
		private static readonly Dictionary<string, Dictionary<int, float>> scenes = new Dictionary<string, Dictionary<int, float>>(StringComparer.Ordinal);

		/// <summary>Raised with the scene name after its entries change.</summary>
		public static event Action<string> Changed;

		/// <summary>Whether a portal is open for the local player now.</summary>
		public static bool IsActive(string sceneName, int portalIndex)
		{
			return SecondsLeft(sceneName, portalIndex) > 0.0f;
		}

		/// <summary>
		/// Seconds a portal stays open for the local player: <see cref="float.PositiveInfinity"/> when it
		/// does not close, 0 when closed or unknown.
		/// </summary>
		public static float SecondsLeft(string sceneName, int portalIndex)
		{
			if (string.IsNullOrEmpty(sceneName) ||
				!scenes.TryGetValue(sceneName, out Dictionary<int, float> scene) ||
				!scene.TryGetValue(portalIndex, out float closesAt))
			{
				return 0.0f;
			}
			if (float.IsPositiveInfinity(closesAt))
			{
				return float.PositiveInfinity;
			}
			return Mathf.Max(0.0f, closesAt - Time.realtimeSinceStartup);
		}

		/// <summary>Replaces a scene's entries with a full report.</summary>
		public static void ApplyScene(string sceneName, ushort[] indices, int[] remainingSeconds)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}
			if (!scenes.TryGetValue(sceneName, out Dictionary<int, float> scene))
			{
				scene = new Dictionary<int, float>();
				scenes[sceneName] = scene;
			}
			scene.Clear();
			int count = indices == null ? 0 : indices.Length;
			for (int i = 0; i < count; ++i)
			{
				int remaining = remainingSeconds != null && i < remainingSeconds.Length ? remainingSeconds[i] : PortalActivationRules.NoExpiry;
				Set(scene, indices[i], remaining);
			}
			Changed?.Invoke(sceneName);
		}

		/// <summary>Applies one portal's change.</summary>
		public static void Apply(string sceneName, int portalIndex, int remainingSeconds)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}
			if (!scenes.TryGetValue(sceneName, out Dictionary<int, float> scene))
			{
				scene = new Dictionary<int, float>();
				scenes[sceneName] = scene;
			}
			Set(scene, portalIndex, remainingSeconds);
			Changed?.Invoke(sceneName);
		}

		/// <summary>Forgets everything. Disconnect.</summary>
		public static void Clear()
		{
			scenes.Clear();
		}

		private static void Set(Dictionary<int, float> scene, int portalIndex, int remainingSeconds)
		{
			if (remainingSeconds == 0)
			{
				scene.Remove(portalIndex);
				return;
			}
			scene[portalIndex] = remainingSeconds < 0
				? float.PositiveInfinity
				: Time.realtimeSinceStartup + remainingSeconds;
		}
	}
}
