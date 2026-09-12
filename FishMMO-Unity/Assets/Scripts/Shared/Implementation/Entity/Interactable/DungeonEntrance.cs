using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interactable representing a dungeon entrance. Displays a title and optional image in the UI.
	/// </summary>
	/// <remarks>
	/// Also the thing that puts the entrance on the minimap and the world map: it requires a
	/// <see cref="MapMarker"/> and fills it in on awake, so an entrance is mapped without any
	/// authoring beyond placing it. It appears once the player has explored the chunk it stands in
	/// — see <see cref="MapMarkerVisibility.Discovered"/> — which for a fixture the player has to
	/// walk to is the same thing as appearing on approach, and it stays on the map afterwards.
	/// </remarks>
	[RequireComponent(typeof(MapMarker))]
	public class DungeonEntrance : Interactable, IDungeonEntrance, IMapMarkerIconSource
	{
		/// <summary>
		/// The display title for the dungeon entrance, shown in the UI.
		/// </summary>
		private string title = "Dungeon";

		/// <summary>
		/// The image representing the dungeon entrance in the UI (client only).
		/// </summary>
		public Sprite DungeonImage;

		/// <summary>
		/// The name of the dungeon associated with this entrance.
		/// </summary>
		public string DungeonName;

		/// <summary>
		/// The dungeon this entrance leads to: its description, artwork and difficulty list.
		/// </summary>
		/// <remarks>
		/// Optional. An entrance without one leads to a dungeon with a single unnamed difficulty
		/// and default rules, which is exactly how every entrance behaved before difficulties
		/// existed — so leaving it unset is a working configuration, not a broken one.
		/// </remarks>
		public DungeonTemplate Template;

		/// <summary>
		/// Achievement to increment when a player enters this dungeon.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		string IDungeonEntrance.DungeonName => DungeonName;

		/// <inheritdoc />
		int IDungeonEntrance.DungeonTemplateID => Template != null ? Template.ID : 0;

		/// <inheritdoc />
		AchievementTemplate IDungeonEntrance.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Gets the display title for the dungeon entrance.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// The name drawn beside the entrance's map marker.
		/// </summary>
		/// <remarks>
		/// The template's player-facing name wins over the field the entrance was authored with:
		/// that field is the dungeon <i>scene</i> name — an internal identifier — while the template
		/// carries what the dungeon is actually called, which is also what the dungeon finder shows.
		/// </remarks>
		public string ResolvedDungeonName => Template != null ? Template.ResolvedDisplayName : DungeonName;

		/// <inheritdoc />
		/// <remarks>
		/// Read live rather than copied into the marker, because the template loads its artwork
		/// asynchronously and there is nothing to copy at awake time. See
		/// <see cref="IMapMarkerIconSource"/>.
		/// </remarks>
		Sprite IMapMarkerIconSource.MapIcon =>
			Template != null && Template.Icon != null ? Template.Icon : DungeonImage;

#if !UNITY_SERVER
		/// <summary>
		/// Fills in the marker that puts this entrance on both maps.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Only the three fields that describe what this object <i>is</i> are written: its type, the
		/// rule that decides when it may be drawn, and a label when the marker has none. Icon,
		/// IconSize, Priority, ClampToEdge and the two ShowOn flags are left alone, so an author who
		/// wants to override any of them on a particular entrance still can.
		/// </para>
		/// <para>
		/// The marker is created if it is missing. <see cref="RequireComponentAttribute"/> is an
		/// editor-time guarantee — it adds the component when this one is added, and to existing
		/// objects when the scene is next opened — so a build taken from a scene that was never
		/// re-saved in the editor would otherwise have an entrance with no marker at all, and the
		/// only symptom would be an entrance that silently never appears on the map.
		/// </para>
		/// </remarks>
		public override void OnAwake()
		{
			MapMarker marker = GetComponent<MapMarker>();
			if (marker == null)
			{
				marker = gameObject.AddComponent<MapMarker>();
			}

			marker.Type = MapMarkerType.DungeonEntrance;
			marker.Visibility = MapMarkerVisibility.Discovered;

			if (string.IsNullOrEmpty(marker.Label))
			{
				marker.Label = ResolvedDungeonName;
			}
		}
#endif
	}
}