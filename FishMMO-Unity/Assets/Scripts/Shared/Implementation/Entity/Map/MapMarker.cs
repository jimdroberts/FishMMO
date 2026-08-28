using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes an object appear on the minimap and the world map. Attach to any prefab — a
	/// character, an NPC, a gathering node, a door — that players should be able to find.
	/// </summary>
	/// <remarks>
	/// <para><b>What replaced what.</b> This supersedes the old <c>MinimapIcon</c>, which
	/// spawned a child <c>SpriteRenderer</c> onto a dedicated "Minimap" layer at a fixed world
	/// height of 999 so that an overhead camera would photograph it. That approach could not do
	/// most of what a map needs — a sprite in a render texture has no label, no tooltip, cannot be
	/// clamped to the edge of the frame when its object is off screen, and scales with the map's
	/// zoom instead of staying legible. Worse, it made map visibility a property of a Unity layer:
	/// anything on that layer was on the map, whether or not the player was supposed to know where
	/// it was, and the only way to hide one thing from one player was to move a GameObject between
	/// layers at runtime.</para>
	///
	/// <para><b>What this does instead.</b> The component is data and a registration; it spawns
	/// nothing and renders nothing. The client's map panels read
	/// <see cref="MapMarkerRegistry"/>, apply <see cref="Visibility"/> against the local player's
	/// relationship to this object, and draw the survivors as UI elements. Every marker is
	/// therefore filtered by a rule the map subsystem owns, and gains labels, tooltips, edge
	/// clamping and zoom-independent sizing for free.</para>
	///
	/// <para><b>Server builds.</b> Registration is compiled out under <c>UNITY_SERVER</c>. The
	/// component still exists there so shared prefabs deserialise identically on both sides —
	/// stripping the type would make every prefab that carries one log a missing-script warning on
	/// the server — but it holds no state and does no work.</para>
	/// </remarks>
	public class MapMarker : MonoBehaviour
	{
		/// <summary>What this marker represents.</summary>
		[Tooltip("What this marker represents. Drives the default icon, draw order and filter row.")]
		public MapMarkerType Type = MapMarkerType.Interactable;

		/// <summary>The rule deciding whether the local player may see this marker.</summary>
		/// <remarks>
		/// Left at <see cref="MapMarkerVisibility.Always"/> for world fixtures. A prefab that can
		/// be a player character should use <see cref="MapMarkerVisibility.Detection"/>: the
		/// runtime promotes it to full fidelity for party and guild members by itself, so
		/// authoring the strict rule costs nothing and a prefab that is never revisited stays
		/// safe.
		/// </remarks>
		[Tooltip("Rule deciding whether the local player may see this marker.")]
		public MapMarkerVisibility Visibility = MapMarkerVisibility.Always;

		/// <summary>Icon drawn for this marker. Falls back to the type's default when null.</summary>
		[Tooltip("Icon drawn for this marker. Uses the type's default icon when empty.")]
		public Sprite Icon;

		/// <summary>Tint multiplied into the icon.</summary>
		[Tooltip("Tint multiplied into the icon.")]
		public Color Tint = Color.white;

		/// <summary>
		/// Text drawn beside the icon. Empty means no label.
		/// </summary>
		/// <remarks>
		/// Never used for a character's name. Names come from the character itself and are
		/// suppressed for anything the detection rule covers — a name attached to a throttled
		/// position would hand a modified client the one piece of information the throttling
		/// exists to withhold.
		/// </remarks>
		[Tooltip("Static text drawn beside the icon. Leave empty for no label.")]
		public string Label;

		/// <summary>Size of the icon in UI points at the map's base scale.</summary>
		[Tooltip("Size of the icon in UI points at the map's base scale.")]
		public float IconSize = 16.0f;

		/// <summary>
		/// Whether the marker is pinned to the edge of the frame, pointing at its object, when
		/// the object is outside the current view.
		/// </summary>
		[Tooltip("Pin the marker to the frame edge, pointing at the object, when it is off the map view.")]
		public bool ClampToEdge;

		/// <summary>
		/// Draw priority when markers overlap. Higher wins; ties fall back to the type ordinal.
		/// </summary>
		[Tooltip("Draw priority when markers overlap. Higher draws on top.")]
		public int Priority;

		/// <summary>Whether the marker is drawn on the minimap.</summary>
		[Tooltip("Draw this marker on the minimap.")]
		public bool ShowOnMinimap = true;

		/// <summary>Whether the marker is drawn on the world map.</summary>
		[Tooltip("Draw this marker on the world map.")]
		public bool ShowOnWorldMap = true;

		/// <summary>
		/// The character this marker belongs to, when it is on one. Null for world fixtures.
		/// </summary>
		/// <remarks>
		/// Resolved once, on enable, and used by the client to work out the live relationship
		/// between the local player and this object. Cached rather than looked up per frame
		/// because <c>GetComponent</c> against an interface walks every component on the
		/// GameObject, and the map refreshes this for every marker in view.
		/// </remarks>
		public ICharacter Character { get; private set; }

		/// <summary>
		/// Where the marker is drawn. Defaults to this transform.
		/// </summary>
		/// <remarks>
		/// Overridable so a marker on a large object can sit at its entrance rather than at its
		/// pivot, which for a building is usually its corner.
		/// </remarks>
		[Tooltip("Optional transform the marker is drawn at. Uses this object when empty.")]
		public Transform MarkerAnchor;

		/// <summary>The world position the marker is drawn at.</summary>
		public Vector3 Position => MarkerAnchor != null ? MarkerAnchor.position : transform.position;

		/// <summary>The direction the marker's icon faces, in degrees clockwise from world north.</summary>
		public float FacingDegrees
		{
			get
			{
				Transform anchor = MarkerAnchor != null ? MarkerAnchor : transform;
				return anchor.eulerAngles.y;
			}
		}

#if !UNITY_SERVER
		/// <summary>
		/// Joins the registry so the map panels can find this marker.
		/// </summary>
		private void OnEnable()
		{
			Character = GetComponent<ICharacter>();
			MapMarkerRegistry.Register(this);
		}

		/// <summary>
		/// Leaves the registry.
		/// </summary>
		/// <remarks>
		/// <c>OnDisable</c> rather than <c>OnDestroy</c>: an object pooled back out of the world is
		/// disabled, not destroyed, and a marker left in the registry for a disabled object draws
		/// at whatever position it was pooled at — which for a respawning creature is the place it
		/// died.
		/// </remarks>
		private void OnDisable()
		{
			MapMarkerRegistry.Unregister(this);
		}
#endif
	}
}
