using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A fixed landmark baked into a scene's map definition: a town, a dungeon entrance, a flight
	/// point, a vista. Drawn on both maps as a marker with a label.
	/// </summary>
	/// <remarks>
	/// Distinct from a <see cref="MapMarker"/> even though both end up as an icon on the map.
	/// A marker belongs to a GameObject that has to exist — so an NPC only appears once the
	/// server has streamed it, which is correct for an NPC and wrong for a mountain. A point of
	/// interest is scene data: it is known from the map definition alone, needs nothing spawned,
	/// and stays on the world map when the player is on the other side of the zone.
	/// </remarks>
	[Serializable]
	public class MapPointOfInterestDetails
	{
		/// <summary>The player-facing name of the landmark.</summary>
		public string Name;

		/// <summary>Optional one-line description shown in the world map's tooltip.</summary>
		[TextArea(1, 3)]
		public string Description;

		/// <summary>Position of the landmark in world space.</summary>
		public Vector3 Position;

		/// <summary>Which marker family this landmark belongs to, for filtering and icon choice.</summary>
		public MapMarkerType Type = MapMarkerType.Landmark;

		/// <summary>Icon drawn for the landmark. Falls back to the type's default when null.</summary>
		public Sprite Icon;

		/// <summary>
		/// Zoom tier at which the landmark starts being drawn on the world map, lowest first.
		/// </summary>
		public int DetailTier;

		/// <summary>
		/// Whether the landmark stays hidden until the fog of war has revealed its cell.
		/// </summary>
		public bool RequiresDiscovery = true;

		/// <summary>
		/// Whether the landmark is also drawn on the minimap when it falls inside the view.
		/// </summary>
		public bool ShowOnMinimap = true;
	}
}
