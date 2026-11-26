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
		[Tooltip("What this marker represents. Drives the shape and colour it draws in, draw order and filter row.")]
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

		/// <summary>
		/// Icon drawn for this marker. Falls back to <see cref="ResolvedIcon"/> when null.
		/// </summary>
		/// <remarks>
		/// Authored artwork for this particular object. There is no type-to-icon table to fall back
		/// on — a marker with neither this nor an <see cref="IMapMarkerIconSource"/> beside it is
		/// drawn as its type's shape, which is the type's USS rule and not an icon at all.
		/// </remarks>
		[Tooltip("Icon drawn for this marker. Falls back to the object's icon source, then to the type's shape.")]
		public Sprite Icon;

		/// <summary>Tint multiplied into the icon.</summary>
		[Tooltip("Tint multiplied into the icon.")]
		public Color Tint = Color.white;

		/// <summary>
		/// Text drawn beside the icon on an object that is not a character. Empty means no label.
		/// </summary>
		/// <remarks>
		/// Ignored on a character. An NPC is labelled from itself — its live name and its interactable's
		/// <see cref="IInteractable.Title"/>, see <see cref="NpcName"/> and <see cref="NpcTitle"/> — because a
		/// static label is copied along with everything else when a prefab is cloned from another, which
		/// is how every townsperson cloned from a merchant came to be drawn as "Goods" and every simulated
		/// player as "Bank". A player character is never labelled: a name attached to a throttled position
		/// would hand a modified client the one piece of information the throttling exists to withhold.
		/// </remarks>
		[Tooltip("Static text drawn beside the icon on a fixture. Ignored on characters: an NPC is labelled with its own name and title.")]
		public string Label;

		/// <summary>
		/// Text shown only when the pointer rests on a fixture's marker, and as its name when it is chosen
		/// to be guided to. Empty falls back to <see cref="Label"/>. Ignored on characters.
		/// </summary>
		/// <remarks>
		/// For a fixture that should be identifiable without putting text on the map: a plot for sale is a
		/// green square on the map and says what it costs only when asked.
		/// </remarks>
		[Tooltip("Hover-only text for a fixture. Empty uses the label. Ignored on characters.")]
		public string HoverText;

		/// <summary>The hover text of a fixture: <see cref="HoverText"/>, else <see cref="Label"/>, else null.</summary>
		public string ResolvedHoverText => !string.IsNullOrEmpty(HoverText) ? HoverText
			: !string.IsNullOrEmpty(Label) ? Label
			: null;

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
		/// Supplies the icon when this marker has none authored, resolved once on enable.
		/// </summary>
		/// <remarks>
		/// Cached for the same reason <see cref="Character"/> is, and read live rather than copied:
		/// a source whose artwork loads asynchronously has nothing to give at enable time. See
		/// <see cref="IMapMarkerIconSource"/>.
		/// </remarks>
#if !UNITY_SERVER
		private IMapMarkerIconSource iconSource;
#endif

		/// <summary>
		/// The interactable on this object, when it has one, resolved once on enable. Null on the server.
		/// </summary>
		public IInteractable Interactable { get; private set; }

		/// <summary>Whether this marker is on a non-player character.</summary>
		public bool IsNpc => Character != null && !(Character is IPlayerCharacter);

		/// <summary>
		/// The NPC's live name, or null when this is not an NPC.
		/// </summary>
		/// <remarks>
		/// Read live: a generated NPC is named by the naming system after it wakes, and a pooled one is
		/// renamed on every respawn.
		/// </remarks>
		public string NpcName => IsNpc && !string.IsNullOrWhiteSpace(Character.Name) ? Character.Name : null;

		/// <summary>
		/// The NPC's role — its interactable's title, such as "Banker" — or null when it is not an NPC, has
		/// no interactable, or the interactable has no title.
		/// </summary>
		public string NpcTitle
		{
			get
			{
				if (!IsNpc || Interactable == null)
				{
					return null;
				}
				string title = Interactable.Title;
				return string.IsNullOrWhiteSpace(title) ? null : title;
			}
		}

		/// <summary>The name the kept "Name &lt;Title&gt;" label was built from.</summary>
		private string composedName;

		/// <summary>The title the kept "Name &lt;Title&gt;" label was built from.</summary>
		private string composedTitle;

		/// <summary>The kept "Name &lt;Title&gt;" label.</summary>
		private string composedLabel;

		/// <summary>
		/// An NPC's map label from its name and title: "Name &lt;Title&gt;", either part alone, or null.
		/// </summary>
		/// <param name="showName">Whether the name is included.</param>
		/// <param name="showTitle">Whether the title is included.</param>
		/// <returns>The label, or null when there is nothing to draw.</returns>
		/// <remarks>
		/// The map asks for this ten times a second per NPC in view, so the combined form is kept and
		/// rebuilt only when the name or title changes. Compared by value: the name is the GameObject's,
		/// which the engine hands back as a new string on every read.
		/// </remarks>
		public string ComposeNpcLabel(bool showName, bool showTitle)
		{
			string name = showName ? NpcName : null;
			string title = showTitle ? NpcTitle : null;

			if (name == null)
			{
				return title;
			}
			if (title == null)
			{
				return name;
			}

			if (composedLabel == null || !string.Equals(name, composedName, System.StringComparison.Ordinal) ||
				!string.Equals(title, composedTitle, System.StringComparison.Ordinal))
			{
				composedName = name;
				composedTitle = title;
				composedLabel = name + " <" + title + ">";
			}
			return composedLabel;
		}

		/// <summary>
		/// The icon to draw: this marker's own, else its icon source's, else null.
		/// </summary>
		/// <remarks>
		/// Null is a normal answer, not a failure — the view then draws the type's shape from its
		/// USS rule.
		/// </remarks>
		public Sprite ResolvedIcon
		{
			get
			{
				if (Icon != null)
				{
					return Icon;
				}

#if !UNITY_SERVER
				return iconSource != null ? iconSource.MapIcon : null;
#else
				return null;
#endif
			}
		}

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
			Interactable = GetComponent<IInteractable>();
			iconSource = GetComponent<IMapMarkerIconSource>();
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
