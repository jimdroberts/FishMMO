using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// How far the terrain's grass and small details are drawn: the player's choice, in metres.
	/// </summary>
	/// <remarks>
	/// <para><b>It replaces the scene's own detail distance on the GPU-driven path.</b> Unity's terrain
	/// caps a detail distance at 250 m and draws every instance to it at full density, so scenes were
	/// authored short (Cov Viaduct: 120 m). The GPU-driven renderer (<c>TerrainDetailInstancing</c>)
	/// culls on the GPU and thins grass with distance (full density near, a dithered fraction toward the
	/// edge), so it can draw much further for little cost. On the CPU fallback (WebGL2) the scene's
	/// distance stays the ceiling: there every drawn instance is CPU work.</para>
	///
	/// <para><b>A performance control.</b> Resident instances, chunk builds and memory grow with the
	/// square of the distance; the thinning bounds what is drawn, not what is kept.</para>
	/// </remarks>
	public static class ClientGrassSettings
	{
		/// <summary>Grass distance a fresh install uses, in metres.</summary>
		/// <remarks>
		/// The blade grass covers the ground in full to 50 m and thins into the far field's ground colour beyond (its rings
		/// run to 600 m for a player who sets it further). 500 m was tried as the default (2026-10-07) and judged excessive.
		/// </remarks>
		public const float DefaultDistance = 200f;

		/// <summary>Shortest grass distance offered, in metres.</summary>
		public const float MinimumDistance = 50f;

		/// <summary>Longest grass distance offered, in metres.</summary>
		/// <remarks>
		/// The blade grass and the GPU detail scatter hold nothing per blade, so their cost past a few hundred metres is the
		/// far field's thin share. The CPU-built mesh detail chunks (ferns, shrubs) stop at
		/// <c>TerrainDetailInstancing.MaxChunkDistance</c> whatever this is: their resident instances grow with its square.
		/// </remarks>
		public const float MaximumDistance = 1000f;

		/// <summary>Raised when the grass distance changes. The detail renderer re-reads it.</summary>
		public static event Action OnChanged;

		/// <summary>The chosen grass distance, in metres.</summary>
		public static float Distance => ClientSettings.GetFloat(
			ClientSettings.GrassDistanceKey, DefaultDistance, MinimumDistance, MaximumDistance);

		/// <summary>Writes the grass distance and notifies the renderer.</summary>
		public static void SetDistance(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				value = DefaultDistance;
			}
			ClientSettings.Set(ClientSettings.GrassDistanceKey, Mathf.Clamp(value, MinimumDistance, MaximumDistance));
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientGrassSettings", "A grass-settings subscriber threw.", ex);
			}
		}
	}
}
