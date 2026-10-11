using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The groups of markers the world map lets the player switch on and off.
	/// </summary>
	/// <remarks>
	/// Coarser than <see cref="MapMarkerType"/> on purpose. Nineteen checkboxes is a settings
	/// screen, not a legend; seven is a thing a player reads once and then uses. The mapping from
	/// type to category lives in <see cref="MapFilters"/> so a type added to the enum has exactly
	/// one place to be classified. The hundred point-of-interest types fold into one row per
	/// <see cref="PointOfInterestGroup"/>, listed apart under PLACES.
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

		/*
		 * Generated points of interest: one category per PointOfInterestGroup, in the group enum's order
		 * (Jim, 2026-10-10: one marker type per kind, a hundred of them, so the legend lists the thirteen
		 * groups rather than the kinds). Appended: the ordinal is the bit in the saved filter mask, and the
		 * mask defaults to every bit set, so these arrive switched on for a player who saved a mask before.
		 */
		/// <summary>Falls, rapids, rivers, lakes, springs.</summary>
		PlacesWater,
		/// <summary>Peaks, passes, gorges, islands and the other shapes of the ground.</summary>
		PlacesLandform,
		/// <summary>Volcanoes, lava lakes, fumaroles.</summary>
		PlacesVolcanic,
		/// <summary>Swamp sites: sunken temples, witch huts, stilt villages.</summary>
		PlacesWetland,
		/// <summary>Caves, grottoes, overhangs.</summary>
		PlacesCaves,
		/// <summary>Camps, lodges, dens and nests.</summary>
		PlacesWild,
		/// <summary>Shrines, temples, stone circles, portals.</summary>
		PlacesSacred,
		/// <summary>Graveyards, barrows, crypts, battlefields.</summary>
		PlacesDead,
		/// <summary>Ruins, monuments, old roads.</summary>
		PlacesRuins,
		/// <summary>Villages, towns, cities, keeps and the works around them.</summary>
		PlacesSettlement,
		/// <summary>Wrecks, coves, reefs, sunken places.</summary>
		PlacesCoast,
		/// <summary>World boss lairs and rare finds.</summary>
		PlacesEncounter,
		/// <summary>The strange ground of other worlds.</summary>
		PlacesAlien,
		/// <summary>Points of interest of no other group.</summary>
		PlacesOther,
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

		/// <summary>The categories of the map's own markers, in the order the legend lists them.</summary>
		public static readonly MapFilterCategory[] MarkerCategories =
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
		/// The point-of-interest group categories that some marker type falls into, in group order: the
		/// legend's PLACES rows.
		/// </summary>
		/// <remarks>
		/// Derived from the enum rather than listed, so a group with no marker type of its own (Other,
		/// whose only kind is the plain Landmark, which keeps its Landmarks row) offers no empty row, and
		/// a kind added later brings its group's row with it.
		/// </remarks>
		public static readonly MapFilterCategory[] PlaceCategories = BuildPlaceCategories();

		/// <summary>Every category the legend lists: the marker rows, then the place rows.</summary>
		public static readonly MapFilterCategory[] Categories = Concat(MarkerCategories, PlaceCategories);

		/// <summary>The filter category of a point-of-interest group.</summary>
		/// <param name="group">The group.</param>
		/// <returns>Its category.</returns>
		public static MapFilterCategory CategoryFor(PointOfInterestGroup group)
		{
			switch (group)
			{
				case PointOfInterestGroup.Water: return MapFilterCategory.PlacesWater;
				case PointOfInterestGroup.Landform: return MapFilterCategory.PlacesLandform;
				case PointOfInterestGroup.Volcanic: return MapFilterCategory.PlacesVolcanic;
				case PointOfInterestGroup.Wetland: return MapFilterCategory.PlacesWetland;
				case PointOfInterestGroup.TerrainShaped: return MapFilterCategory.PlacesCaves;
				case PointOfInterestGroup.Wild: return MapFilterCategory.PlacesWild;
				case PointOfInterestGroup.Sacred: return MapFilterCategory.PlacesSacred;
				case PointOfInterestGroup.Dead: return MapFilterCategory.PlacesDead;
				case PointOfInterestGroup.Ruins: return MapFilterCategory.PlacesRuins;
				case PointOfInterestGroup.Settlement: return MapFilterCategory.PlacesSettlement;
				case PointOfInterestGroup.Coast: return MapFilterCategory.PlacesCoast;
				case PointOfInterestGroup.Encounter: return MapFilterCategory.PlacesEncounter;
				case PointOfInterestGroup.Alien: return MapFilterCategory.PlacesAlien;
				default: return MapFilterCategory.PlacesOther;
			}
		}

		/// <summary>The point-of-interest group a category stands for, when it is a place category.</summary>
		/// <param name="category">The category.</param>
		/// <param name="group">The group, or Other when the category is not a place category.</param>
		/// <returns>True for a place category.</returns>
		public static bool TryGroupOf(MapFilterCategory category, out PointOfInterestGroup group)
		{
			foreach (PointOfInterestGroup candidate in (PointOfInterestGroup[])System.Enum.GetValues(typeof(PointOfInterestGroup)))
			{
				if (CategoryFor(candidate) == category)
				{
					group = candidate;
					return true;
				}
			}
			group = PointOfInterestGroup.Other;
			return false;
		}

		/// <summary>The USS class suffix of a point-of-interest group: its name lowercased (the colour token is --map-poi-suffix).</summary>
		public static string GroupClassSuffix(PointOfInterestGroup group) => group.ToString().ToLowerInvariant();

		private static MapFilterCategory[] BuildPlaceCategories()
		{
			var used = new System.Collections.Generic.HashSet<MapFilterCategory>();
			foreach (MapMarkerType type in (MapMarkerType[])System.Enum.GetValues(typeof(MapMarkerType)))
			{
				used.Add(Categorize(type));
			}

			var result = new System.Collections.Generic.List<MapFilterCategory>();
			foreach (PointOfInterestGroup group in (PointOfInterestGroup[])System.Enum.GetValues(typeof(PointOfInterestGroup)))
			{
				MapFilterCategory category = CategoryFor(group);
				if (used.Contains(category) && !result.Contains(category))
				{
					result.Add(category);
				}
			}
			return result.ToArray();
		}

		private static MapFilterCategory[] Concat(MapFilterCategory[] a, MapFilterCategory[] b)
		{
			var result = new MapFilterCategory[a.Length + b.Length];
			a.CopyTo(result, 0);
			b.CopyTo(result, a.Length);
			return result;
		}

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
				case MapFilterCategory.PlacesWater: return "Waters";
				case MapFilterCategory.PlacesLandform: return "Landforms";
				case MapFilterCategory.PlacesVolcanic: return "Volcanic";
				case MapFilterCategory.PlacesWetland: return "Wetlands";
				case MapFilterCategory.PlacesCaves: return "Caves";
				case MapFilterCategory.PlacesWild: return "Camps and Dens";
				case MapFilterCategory.PlacesSacred: return "Sacred Sites";
				case MapFilterCategory.PlacesDead: return "Graves";
				case MapFilterCategory.PlacesRuins: return "Ruins";
				case MapFilterCategory.PlacesSettlement: return "Settlements";
				case MapFilterCategory.PlacesCoast: return "Coast";
				case MapFilterCategory.PlacesEncounter: return "Lairs and Finds";
				case MapFilterCategory.PlacesAlien: return "Strange Ground";
				case MapFilterCategory.PlacesOther: return "Other Places";
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
				case MapMarkerType.Waypoint:
				case MapMarkerType.DungeonEntrance:
					return MapFilterCategory.Landmarks;

				case MapMarkerType.Note:
					return MapFilterCategory.Notes;

				default:
					/* A generated point of interest: its kind's group. Landmark and DungeonEntrance are
					 * kinds too but keep their Landmarks row above, because hand-placed landmarks and
					 * dungeon entrances were filtered there before kinds existed. */
					if (PointOfInterestKinds.TryKindOf(type, out FishMMO.Shared.NameGeneration.POIType kind)
						&& PointOfInterestKinds.IsKnown(kind))
					{
						return CategoryFor(PointOfInterestKinds.Info(kind).Group);
					}
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
