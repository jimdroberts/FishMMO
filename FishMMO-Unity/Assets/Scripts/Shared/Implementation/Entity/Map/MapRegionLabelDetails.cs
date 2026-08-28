using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A named area drawn as text across the world map, and used to answer "where am I" on the
	/// minimap's location readout.
	/// </summary>
	/// <remarks>
	/// Baked from a <see cref="MapRegionLabel"/> placed in the scene. Stored in world space, not
	/// map space: a re-bake that changes the map texture's resolution or the scene's bounds must
	/// not move the labels, and world space is the only coordinate system both the bake and the
	/// runtime agree on without a conversion.
	/// </remarks>
	[Serializable]
	public class MapRegionLabelDetails
	{
		/// <summary>The player-facing name of the region.</summary>
		public string Name;

		/// <summary>Centre of the region in world space.</summary>
		public Vector3 Position;

		/// <summary>
		/// Horizontal radius of the region in metres. A position within this distance of
		/// <see cref="Position"/> is considered inside the region.
		/// </summary>
		public float Radius;

		/// <summary>
		/// Zoom tier at which the label starts being drawn on the world map, lowest first.
		/// Continents at 0, provinces at 1, individual settlements at 2 and beyond.
		/// </summary>
		public int DetailTier;

		/// <summary>
		/// Whether the player must have explored this region before its name is shown.
		/// </summary>
		public bool RequiresDiscovery;

		/// <summary>
		/// Whether a position lies inside this region, ignoring height.
		/// </summary>
		/// <param name="worldPosition">The position to test.</param>
		/// <returns>True when the position is within <see cref="Radius"/> horizontally.</returns>
		/// <remarks>
		/// Height is ignored on purpose. A region is an area on a map, and a player standing on a
		/// tower or at the bottom of a ravine inside a valley is still in that valley — testing
		/// the full 3D distance would name the region correctly at ground level and then stop
		/// naming it as soon as the player climbed anything.
		/// </remarks>
		public bool Contains(Vector3 worldPosition)
		{
			float dx = worldPosition.x - Position.x;
			float dz = worldPosition.z - Position.z;
			return (dx * dx) + (dz * dz) <= Radius * Radius;
		}
	}
}
