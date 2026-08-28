using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The groups of markers the world map lets the player switch on and off.
	/// </summary>
	/// <remarks>
	/// Coarser than <see cref="MapMarkerType"/> on purpose. Seventeen checkboxes is a settings
	/// screen, not a legend; six is a thing a player reads once and then uses. The mapping from
	/// type to category lives in <see cref="MapFilters"/> so a type added to the enum has exactly
	/// one place to be classified.
	/// </remarks>
	public enum MapFilterCategory : byte
	{
		/// <summary>The player's own party and guild.</summary>
		Group = 0,
		/// <summary>Other player characters.</summary>
		Players,
		/// <summary>Vendors, trainers, quest givers, bankers and other services.</summary>
		Services,
		/// <summary>Hostile creatures.</summary>
		Enemies,
		/// <summary>Gathering nodes and world interactables.</summary>
		Resources,
		/// <summary>Authored landmarks and teleporters.</summary>
		Landmarks,
		/// <summary>The player's own pinned notes.</summary>
		Notes,
	}

	/// <summary>
	/// Which marker categories the player currently wants to see, remembered between sessions.
	/// </summary>
	/// <remarks>
	/// <para>State lives in <see cref="ClientSettings"/> rather than on the panel, because the
	/// minimap will want the same switches eventually and because a filter the player has to set
	/// again on every login is worse than no filter at all.</para>
	/// <para>The player's own character is never filterable. A map that can hide where you are is
	/// a map with a way to break itself, and no player has ever wanted that.</para>
	/// </remarks>
	public static class MapFilters
	{
		/// <summary>Configuration key holding the enabled categories as a bit field.</summary>
		public const string FilterMaskKey = "Map.Filters";

		/// <summary>Every category, in the order the legend lists them.</summary>
		public static readonly MapFilterCategory[] Categories =
		{
			MapFilterCategory.Group,
			MapFilterCategory.Players,
			MapFilterCategory.Services,
			MapFilterCategory.Enemies,
			MapFilterCategory.Resources,
			MapFilterCategory.Landmarks,
			MapFilterCategory.Notes,
		};

		/// <summary>
		/// The player-facing name of a category.
		/// </summary>
		/// <param name="category">The category.</param>
		/// <returns>Its label.</returns>
		public static string Label(MapFilterCategory category)
		{
			switch (category)
			{
				case MapFilterCategory.Group: return "Party and Guild";
				case MapFilterCategory.Players: return "Other Players";
				case MapFilterCategory.Services: return "Vendors and Services";
				case MapFilterCategory.Enemies: return "Enemies";
				case MapFilterCategory.Resources: return "Resources";
				case MapFilterCategory.Landmarks: return "Landmarks";
				case MapFilterCategory.Notes: return "My Notes";
				default: return category.ToString();
			}
		}

		/// <summary>
		/// Which category a marker type belongs to.
		/// </summary>
		/// <param name="type">The marker type.</param>
		/// <returns>Its category.</returns>
		public static MapFilterCategory Categorize(MapMarkerType type)
		{
			switch (type)
			{
				case MapMarkerType.PartyMember:
				case MapMarkerType.GuildMember:
					return MapFilterCategory.Group;

				case MapMarkerType.FriendlyPlayer:
				case MapMarkerType.NeutralPlayer:
				case MapMarkerType.HostilePlayer:
					return MapFilterCategory.Players;

				case MapMarkerType.Vendor:
				case MapMarkerType.QuestGiver:
				case MapMarkerType.Trainer:
				case MapMarkerType.Service:
				case MapMarkerType.NPC:
					return MapFilterCategory.Services;

				case MapMarkerType.Enemy:
					return MapFilterCategory.Enemies;

				case MapMarkerType.Resource:
				case MapMarkerType.Interactable:
					return MapFilterCategory.Resources;

				case MapMarkerType.Teleporter:
				case MapMarkerType.Landmark:
					return MapFilterCategory.Landmarks;

				case MapMarkerType.Note:
					return MapFilterCategory.Notes;

				default:
					return MapFilterCategory.Landmarks;
			}
		}

		/// <summary>
		/// Whether a category is currently shown.
		/// </summary>
		/// <param name="category">The category.</param>
		/// <returns>True when its markers should be drawn.</returns>
		public static bool IsEnabled(MapFilterCategory category)
		{
			return (Mask() & (1 << (int)category)) != 0;
		}

		/// <summary>
		/// Whether a marker type is currently shown.
		/// </summary>
		/// <param name="type">The marker type.</param>
		/// <returns>True when the marker should be drawn.</returns>
		public static bool IsEnabled(MapMarkerType type)
		{
			// The player's own marker is not filterable; see the class remarks.
			if (type == MapMarkerType.Self)
			{
				return true;
			}

			return IsEnabled(Categorize(type));
		}

		/// <summary>
		/// Shows or hides a category.
		/// </summary>
		/// <param name="category">The category.</param>
		/// <param name="enabled">Whether its markers should be drawn.</param>
		public static void SetEnabled(MapFilterCategory category, bool enabled)
		{
			int mask = Mask();
			int bit = 1 << (int)category;

			mask = enabled ? (mask | bit) : (mask & ~bit);
			ClientSettings.Set(FilterMaskKey, mask);
		}

		/// <summary>
		/// The stored bit field, defaulting to everything on.
		/// </summary>
		/// <returns>The mask.</returns>
		/// <remarks>
		/// Read rather than cached. It is one lookup in a dictionary that is already in memory,
		/// and it is consulted once per marker per refresh — a few hundred times a second at
		/// worst, which is nothing next to the layout work each marker causes anyway.
		/// </remarks>
		private static int Mask()
		{
			return ClientSettings.GetInt(FilterMaskKey, ~0);
		}
	}
}
