using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A discoverable fast-travel point. Interacting with it unlocks it for the character; once
	/// unlocked it appears on that character's world map and can be travelled to.
	/// </summary>
	/// <remarks>
	/// <para><b>Authoring.</b> Place one in a world scene and give it a <see cref="WaypointIndex"/>
	/// that no other waypoint in that scene uses — the index is the bit the character's unlock
	/// record stores, so it must never change once the scene ships; a renumbered waypoint is a
	/// waypoint every existing character loses. Adding a new one at the next free index is safe.
	/// The world scene details cache rebuild reports duplicates.</para>
	/// <para><b>Behaviour.</b> Like every interactable, what happens on interaction is the ECA
	/// trigger list: the shipped <c>Waypoint Interact</c> trigger runs <see cref="UnlockWaypointAction"/>
	/// and then the discovery achievement. Travel is not an interaction — it is requested from
	/// the map, validated by the server, and the arrival runs <see cref="OnTravelTriggers"/>.
	/// <see cref="TravelConditions"/> are the designer's gate on that request.</para>
	/// <para><b>Do not put a <c>MapMarker</c> on the same object.</b> A marker would draw the
	/// waypoint for anyone who streamed it, discovered or not; the map draws waypoints from the
	/// character's unlock record instead, and <c>MapMarkerFilter</c> drops any marker of the
	/// waypoint type to keep that true.</para>
	/// </remarks>
	public class Waypoint : Interactable, IWaypoint
	{
		[Header("Waypoint")]
		[Tooltip("Index of this waypoint within its scene, unique per scene and stable forever. It is the bit a character's unlock record stores.")]
		[SerializeField]
		private int waypointIndex;

		[Tooltip("Player-facing name shown on the map. Uses the GameObject name when empty.")]
		public string WaypointName;

		[TextArea(1, 3)]
		[Tooltip("Optional description shown in the map's waypoint panel.")]
		[SerializeField]
		private string description;

		[Tooltip("Icon drawn on the map. Uses the waypoint marker style when empty.")]
		public Sprite Icon;

		[Tooltip("Where a travelling character lands. Uses this object's transform when empty.")]
		[SerializeField]
		private Transform arrivalPoint;

		[Header("ECA - Travel")]
		[Tooltip("Conditions the traveller must meet to fast travel here. Evaluated on the server.")]
		[SerializeReference, SubclassSelector]
		private List<BaseCondition> travelConditions = new List<BaseCondition>();

		[Tooltip("Triggers invoked server-side after a character arrives here by fast travel.")]
		[SerializeField]
		private List<Trigger> onTravelTriggers = new List<Trigger>();

		/// <inheritdoc />
		public int WaypointIndex => waypointIndex;

		/// <inheritdoc />
		public string SceneName => gameObject.scene.name;

		/// <inheritdoc />
		public string ResolvedName => string.IsNullOrWhiteSpace(WaypointName) ? gameObject.name : WaypointName;

		string IWaypoint.WaypointName => ResolvedName;

		/// <inheritdoc />
		public string Description => description;

		/// <inheritdoc />
		public Transform ArrivalPoint => arrivalPoint != null ? arrivalPoint : transform;

		/// <inheritdoc />
		public List<BaseCondition> TravelConditions => travelConditions;

		/// <inheritdoc />
		public List<Trigger> OnTravelTriggers => onTravelTriggers;

		/// <inheritdoc />
		public override string Title => "Waypoint";

		/// <inheritdoc />
		public override Color TitleColor => TinyColor.ToUnityColor(TinyColor.skyBlue);

		/// <summary>
		/// Registers the scene object and the waypoint. See <see cref="WaypointRegistry"/>.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();
			WaypointRegistry.Register(this);
		}

		/// <summary>
		/// Drops the waypoint from the registry when its scene unloads or it is despawned.
		/// </summary>
		public override void OnStopServer()
		{
			base.OnStopServer();
			WaypointRegistry.Unregister(this);
		}

		/// <summary>
		/// Produces the form baked into the world scene details cache.
		/// </summary>
		public SceneWaypointDetails ToDetails()
		{
			return new SceneWaypointDetails()
			{
				Index = waypointIndex,
				Name = ResolvedName,
				Description = description,
				Position = transform.position,
				Icon = Icon,
			};
		}

#if UNITY_EDITOR
		/// <summary>
		/// Clamps the index. Overrides rather than hides FishNet's validation, which assigns the
		/// behaviour's component index; hiding it would silently skip that.
		/// </summary>
		protected override void OnValidate()
		{
			base.OnValidate();

			if (!WaypointUnlockMask.IsValidIndex(waypointIndex))
			{
				Debug.LogWarning($"Waypoint '{name}': index {waypointIndex} is outside [0, {WaypointUnlockMask.MaxIndex}]; clamping.", this);
				waypointIndex = Mathf.Clamp(waypointIndex, 0, WaypointUnlockMask.MaxIndex);
			}
		}
#endif
	}
}
