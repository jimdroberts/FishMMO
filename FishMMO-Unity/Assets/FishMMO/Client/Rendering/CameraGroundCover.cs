using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Cameras that draw no ground cover (grass, flowers, pebbles, blade grass): the minimap. Trees, rocks and the ground still
	/// draw. Core so the minimap names no module; the Terrain module's
	/// renderers read it.
	/// </summary>
	public static class CameraGroundCover
	{
		private static readonly HashSet<Camera> noCover = new HashSet<Camera>();

		/// <summary>
		/// Keeps <paramref name="camera"/> out of the ground cover, or lets it draw it again: a top-down map has no use for ground
		/// cover, and generating it there is the grass's whole per-camera cost for nothing.
		/// </summary>
		public static void SetDraws(Camera camera, bool draws)
		{
			noCover.RemoveWhere(c => c == null);
			if (camera == null)
			{
				return;
			}
			if (draws)
			{
				noCover.Remove(camera);
			}
			else
			{
				noCover.Add(camera);
			}
		}

		/// <summary>False for a camera kept out of the ground cover (<see cref="SetDraws"/>).</summary>
		public static bool Draws(Camera camera) => !noCover.Contains(camera);
	}
}
