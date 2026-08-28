using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks a landmark in the scene — a town, a dungeon entrance, a flight point, a vista.
	/// Harvested into the scene's <see cref="WorldMapDefinition"/> when the world scene details
	/// cache is rebuilt.
	/// </summary>
	/// <remarks>
	/// Unlike a <see cref="MapMarker"/>, nothing needs to be spawned for this to appear: the
	/// landmark is scene data, so the world map can draw a town the player has explored while
	/// they stand on the far side of the zone with none of it streamed.
	/// </remarks>
	public class MapPointOfInterest : MonoBehaviour
	{
		/// <summary>Player-facing name. Falls back to the GameObject name when empty.</summary>
		[Tooltip("Player-facing name. Uses the GameObject name when empty.")]
		public string LandmarkName;

		/// <summary>Optional one-line description shown in the world map's tooltip.</summary>
		[TextArea(1, 3)]
		[Tooltip("Optional description shown in the world map tooltip.")]
		public string Description;

		/// <summary>Which marker family this landmark belongs to.</summary>
		[Tooltip("Marker family, used for filtering and for the default icon.")]
		public MapMarkerType Type = MapMarkerType.Landmark;

		/// <summary>Icon drawn for the landmark. Falls back to the type default when null.</summary>
		[Tooltip("Icon drawn for the landmark. Uses the type's default icon when empty.")]
		public Sprite Icon;

		/// <summary>Zoom tier at which the landmark starts being drawn, lowest first.</summary>
		[Tooltip("Zoom tier the landmark appears at. 0 = always, higher = only when zoomed in.")]
		public int DetailTier;

		/// <summary>Whether the landmark stays hidden until its cell has been explored.</summary>
		[Tooltip("Hide the landmark until the player has explored the area around it.")]
		public bool RequiresDiscovery = true;

		/// <summary>Whether the landmark is also drawn on the minimap.</summary>
		[Tooltip("Also draw this landmark on the minimap when it falls inside the view.")]
		public bool ShowOnMinimap = true;

		/// <summary>The name this landmark will be baked with.</summary>
		public string ResolvedName =>
			string.IsNullOrWhiteSpace(LandmarkName) ? gameObject.name : LandmarkName;

		/// <summary>
		/// Produces the serializable form baked into the map definition.
		/// </summary>
		/// <returns>The baked details for this landmark.</returns>
		public MapPointOfInterestDetails ToDetails()
		{
			return new MapPointOfInterestDetails()
			{
				Name = ResolvedName,
				Description = Description,
				Position = transform.position,
				Type = Type,
				Icon = Icon,
				DetailTier = Mathf.Max(0, DetailTier),
				RequiresDiscovery = RequiresDiscovery,
				ShowOnMinimap = ShowOnMinimap,
			};
		}

#if UNITY_EDITOR
		/// <summary>
		/// Draws a pin at the landmark so it can be placed against the terrain.
		/// </summary>
		private void OnDrawGizmos()
		{
			Gizmos.color = new Color(0.95f, 0.78f, 0.36f, 0.9f);
			Gizmos.DrawWireSphere(transform.position, 1.0f);
			Gizmos.DrawLine(transform.position, transform.position + (Vector3.up * 4.0f));

			UnityEditor.Handles.color = Gizmos.color;
			UnityEditor.Handles.Label(transform.position + (Vector3.up * 4.5f), ResolvedName);
		}
#endif
	}
}
