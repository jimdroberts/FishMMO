using System;
using System.Collections.Generic;
using System.Text;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared
{
	/// <summary>The catalogue group a point-of-interest kind belongs to: one map filter row each.</summary>
	/// <remarks>Append only; the ordinal is stored in filter settings.</remarks>
	public enum PointOfInterestGroup : byte
	{
		Water,
		Landform,
		Volcanic,
		Wetland,
		TerrainShaped,
		Wild,
		Sacred,
		Dead,
		Ruins,
		Settlement,
		Coast,
		Encounter,
		Alien,
		Other,
	}

	/// <summary>What a point-of-interest kind is, beyond its group.</summary>
	[Flags]
	public enum PointOfInterestTraits : ushort
	{
		None = 0,
		/// <summary>Found in the generated ground (a fall, a lake, a peak) rather than placed and budgeted.</summary>
		Detected = 1 << 0,
		/// <summary>Stands on the sea or lake floor.</summary>
		Underwater = 1 << 1,
		/// <summary>Its name and spawners take a race (Cave of Orcs).</summary>
		UsesRace = 1 << 2,
		/// <summary>A place people live: a waypoint, a respawn point and services.</summary>
		Settlement = 1 << 3,
		/// <summary>Holds hostile spawners.</summary>
		Hostile = 1 << 4,
		/// <summary>Built from the structure kit.</summary>
		Structure = 1 << 5,
		/// <summary>Changes the ground: a carved cave, an overhang, an arch.</summary>
		Terrain = 1 << 6,
		/// <summary>Needs a flattened pad under its footprint.</summary>
		Pad = 1 << 7,
	}

	/// <summary>The fixed facts about one point-of-interest kind.</summary>
	public readonly struct PointOfInterestKindInfo
	{
		public readonly POIType Kind;
		public readonly PointOfInterestGroup Group;
		public readonly PointOfInterestTraits Traits;
		/// <summary>The world map zoom tier the kind appears at; 0 = always, higher = only zoomed in.</summary>
		public readonly int DetailTier;
		public readonly string DisplayName;

		public PointOfInterestKindInfo(POIType kind, PointOfInterestGroup group, PointOfInterestTraits traits, int detailTier, string displayName)
		{
			Kind = kind;
			Group = group;
			Traits = traits;
			DetailTier = detailTier;
			DisplayName = displayName;
		}

		public bool Has(PointOfInterestTraits trait) => (Traits & trait) == trait;
	}

	/// <summary>
	/// The point-of-interest kinds: their group, traits, map tier, display name and map marker type.
	/// </summary>
	/// <remarks>
	/// A kind IS a <see cref="POIType"/>, so the name generator, the generated scene data and the map all speak of
	/// the same thing. Each kind has its own <see cref="MapMarkerType"/> of the same name (Jim, 2026-10-10), found
	/// by name so the two enums can never drift apart silently: a kind with no marker type answers
	/// <see cref="MapMarkerType.Landmark"/>, and a unit test pins that none does.
	/// </remarks>
	public static class PointOfInterestKinds
	{
		private const PointOfInterestTraits D = PointOfInterestTraits.Detected;
		private const PointOfInterestTraits U = PointOfInterestTraits.Underwater;
		private const PointOfInterestTraits R = PointOfInterestTraits.UsesRace;
		private const PointOfInterestTraits S = PointOfInterestTraits.Settlement;
		private const PointOfInterestTraits H = PointOfInterestTraits.Hostile;
		private const PointOfInterestTraits K = PointOfInterestTraits.Structure;
		private const PointOfInterestTraits T = PointOfInterestTraits.Terrain;
		private const PointOfInterestTraits P = PointOfInterestTraits.Pad;

		private static readonly Dictionary<POIType, PointOfInterestKindInfo> table = Build();
		private static readonly Dictionary<POIType, MapMarkerType> markers = new Dictionary<POIType, MapMarkerType>();
		private static readonly List<PointOfInterestKindInfo> all = new List<PointOfInterestKindInfo>();

		/// <summary>Every kind the generator can place or find, in enum order (Any excluded).</summary>
		public static IReadOnlyList<PointOfInterestKindInfo> All
		{
			get
			{
				if (all.Count == 0)
				{
					foreach (POIType kind in Enum.GetValues(typeof(POIType)))
					{
						if (table.TryGetValue(kind, out PointOfInterestKindInfo info))
						{
							all.Add(info);
						}
					}
				}
				return all;
			}
		}

		/// <summary>The facts about a kind; an unknown kind reads as an untyped landmark.</summary>
		public static PointOfInterestKindInfo Info(POIType kind)
			=> table.TryGetValue(kind, out PointOfInterestKindInfo info)
				? info
				: new PointOfInterestKindInfo(kind, PointOfInterestGroup.Other, PointOfInterestTraits.None, 2, Spaced(kind.ToString()));

		/// <summary>Whether the generator knows this kind.</summary>
		public static bool IsKnown(POIType kind) => table.ContainsKey(kind);

		/// <summary>The map marker type a kind is drawn as: the one of the same name.</summary>
		public static MapMarkerType MarkerFor(POIType kind)
		{
			lock (markers)
			{
				if (!markers.TryGetValue(kind, out MapMarkerType type))
				{
					type = Enum.TryParse(kind.ToString(), false, out MapMarkerType parsed) ? parsed : MapMarkerType.Landmark;
					markers[kind] = type;
				}
				return type;
			}
		}

		/// <summary>The kind a map marker type stands for, or false when it is not a point-of-interest type.</summary>
		public static bool TryKindOf(MapMarkerType type, out POIType kind)
		{
			if (type != MapMarkerType.Landmark && Enum.TryParse(type.ToString(), false, out kind) && table.ContainsKey(kind))
			{
				return true;
			}
			kind = POIType.Any;
			return type == MapMarkerType.Landmark;
		}

		/// <summary>
		/// The colour of each group wherever points of interest are drawn: the atlas globe and preview, the scene gizmos, and
		/// the in-game map's <c>--map-poi-*</c> tokens in FishMMO-Theme.uss (pinned equal by a test).
		/// </summary>
		public static UnityEngine.Color GroupColour(PointOfInterestGroup group)
		{
			switch (group)
			{
				case PointOfInterestGroup.Water: return Rgb(80, 168, 255);
				case PointOfInterestGroup.Landform: return Rgb(205, 170, 120);
				case PointOfInterestGroup.Volcanic: return Rgb(255, 110, 50);
				case PointOfInterestGroup.Wetland: return Rgb(146, 180, 84);
				case PointOfInterestGroup.TerrainShaped: return Rgb(132, 148, 176);
				case PointOfInterestGroup.Wild: return Rgb(222, 132, 74);
				case PointOfInterestGroup.Sacred: return Rgb(255, 238, 150);
				case PointOfInterestGroup.Dead: return Rgb(160, 132, 196);
				case PointOfInterestGroup.Ruins: return Rgb(190, 176, 150);
				case PointOfInterestGroup.Settlement: return Rgb(255, 206, 120);
				case PointOfInterestGroup.Coast: return Rgb(64, 214, 200);
				case PointOfInterestGroup.Encounter: return Rgb(232, 70, 110);
				case PointOfInterestGroup.Alien: return Rgb(190, 110, 255);
				default: return Rgb(236, 220, 180);
			}
		}

		private static UnityEngine.Color Rgb(int r, int g, int b) => new UnityEngine.Color(r / 255f, g / 255f, b / 255f, 1f);

		/// <summary>"HotSpring" → "Hot Spring".</summary>
		public static string Spaced(string name)
		{
			var sb = new StringBuilder(name.Length + 4);
			for (int i = 0; i < name.Length; i++)
			{
				if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
				{
					sb.Append(' ');
				}
				sb.Append(name[i]);
			}
			return sb.ToString();
		}

		private static Dictionary<POIType, PointOfInterestKindInfo> Build()
		{
			var t = new Dictionary<POIType, PointOfInterestKindInfo>();
			void Add(POIType kind, PointOfInterestGroup group, PointOfInterestTraits traits, int tier, string display = null)
				=> t[kind] = new PointOfInterestKindInfo(kind, group, traits, tier, display ?? Spaced(kind.ToString()));

			const PointOfInterestGroup Wa = PointOfInterestGroup.Water, Lf = PointOfInterestGroup.Landform, Vo = PointOfInterestGroup.Volcanic,
				We = PointOfInterestGroup.Wetland, Ts = PointOfInterestGroup.TerrainShaped, Wi = PointOfInterestGroup.Wild,
				Sa = PointOfInterestGroup.Sacred, De = PointOfInterestGroup.Dead, Ru = PointOfInterestGroup.Ruins,
				Se = PointOfInterestGroup.Settlement, Co = PointOfInterestGroup.Coast, En = PointOfInterestGroup.Encounter,
				Al = PointOfInterestGroup.Alien, Ot = PointOfInterestGroup.Other;

			Add(POIType.Landmark, Ot, PointOfInterestTraits.None, 2);
			Add(POIType.Clearing, Lf, D, 3);

			// Water: found in the hydrology.
			Add(POIType.Waterfall, Wa, D, 2);
			Add(POIType.Rapids, Wa, D, 3);
			Add(POIType.River, Wa, D, 2);
			Add(POIType.Lake, Wa, D, 2);
			Add(POIType.Spring, Wa, D, 3);
			Add(POIType.HotSpring, Wa, D, 3);
			Add(POIType.RiverMouth, Wa, D, 3);
			Add(POIType.Delta, Wa, D, 3);

			// Landforms: found in the ground's shape.
			Add(POIType.Peak, Lf, D, 2);
			Add(POIType.Pass, Lf, D, 3);
			Add(POIType.Gorge, Lf, D, 3);
			Add(POIType.Mesa, Lf, D, 3);
			Add(POIType.Butte, Lf, D, 3);
			Add(POIType.Valley, Lf, D, 3);
			Add(POIType.Island, Lf, D, 2);
			Add(POIType.Bay, Lf, D, 3);
			Add(POIType.Headland, Lf, D, 3);
			Add(POIType.NaturalArch, Lf, T, 3);
			Add(POIType.Sinkhole, Lf, D, 3);
			Add(POIType.Crater, Lf, D, 3);
			Add(POIType.DuneSea, Lf, D, 3);
			Add(POIType.SaltFlat, Lf, D, 3);
			Add(POIType.Glacier, Lf, D, 2);

			Add(POIType.Volcano, Vo, D, 2);
			Add(POIType.LavaLake, Vo, D, 2);
			Add(POIType.FumaroleField, Vo, D, 3);
			Add(POIType.ObsidianField, Vo, D | K, 3);

			Add(POIType.SunkenTemple, We, K | R | H, 2);
			Add(POIType.DrownedVillage, We, K | H, 2);
			Add(POIType.WitchHut, We, K | P, 2, "Witch's Hut");
			Add(POIType.StiltVillage, We, K | R | S, 1);
			Add(POIType.BogShrine, We, K, 2);
			Add(POIType.MangroveMaze, We, D, 3);
			Add(POIType.WispHollow, We, K, 3);

			Add(POIType.Cave, Ts, T | R | H, 2);
			Add(POIType.Grotto, Ts, T, 2);
			Add(POIType.SeaCave, Ts, T, 2);
			Add(POIType.IceCave, Ts, T | R | H, 2);
			Add(POIType.LavaTube, Ts, T | R | H, 2);
			Add(POIType.Overhang, Ts, T, 3);

			Add(POIType.Camp, Wi, K | P | R | H, 2, "Encampment");
			Add(POIType.BanditCamp, Wi, K | P | R | H, 2);
			Add(POIType.HuntingLodge, Wi, K | P, 2);
			Add(POIType.LumberCamp, Wi, K | P, 2);
			Add(POIType.FishingCamp, Wi, K | P, 2);
			Add(POIType.MonsterDen, Wi, K | R | H, 2);
			Add(POIType.Nest, Wi, K | R | H, 3);

			Add(POIType.RitualSite, Sa, K | P | R | H, 2);
			Add(POIType.StoneCircle, Sa, K | P, 2);
			Add(POIType.Shrine, Sa, K | P, 2);
			Add(POIType.Temple, Sa, K | P, 1);
			Add(POIType.Monastery, Sa, K | P | S, 1);
			Add(POIType.FeyRing, Sa, K, 3);
			Add(POIType.LeyNexus, Sa, K, 2);
			Add(POIType.FallenStar, Sa, K, 2);
			Add(POIType.CorruptedGrove, Sa, K | H, 2);
			Add(POIType.Portal, Sa, K | P, 1);

			Add(POIType.Graveyard, De, K | P | H, 2);
			Add(POIType.Barrow, De, K | H, 2);
			Add(POIType.Crypt, De, K | P | H, 1);
			Add(POIType.Battlefield, De, K | H, 2);
			Add(POIType.Ossuary, De, K | H, 2);

			Add(POIType.Ruins, Ru, K | P, 2);
			Add(POIType.RuinedTower, Ru, K | P, 2);
			Add(POIType.Monument, Ru, K | P, 2);
			Add(POIType.Statue, Ru, K, 3);
			Add(POIType.Obelisk, Ru, K, 3);
			Add(POIType.AncientRoad, Ru, K, 3);
			Add(POIType.AbandonedFarm, Ru, K | P, 2);
			Add(POIType.Hermitage, Ru, K | P, 2);
			Add(POIType.Oasis, Ru, K | D, 2);

			Add(POIType.Village, Se, K | P | R | S, 1);
			Add(POIType.Town, Se, K | P | R | S, 1);
			Add(POIType.City, Se, K | P | R | S, 0);
			Add(POIType.Capital, Se, K | P | R | S, 0);
			Add(POIType.Port, Se, K | P | R | S, 1);
			Add(POIType.Keep, Se, K | P | R, 1);
			Add(POIType.Castle, Se, K | P | R, 1);
			Add(POIType.Fortress, Se, K | P | R, 1);
			Add(POIType.Tower, Se, K | P, 2, "Watchtower");
			Add(POIType.TradingPost, Se, K | P | S, 1);
			Add(POIType.Waystation, Se, K | P, 2);
			Add(POIType.Bridge, Se, K, 2);
			Add(POIType.Mine, Se, K | P, 2);
			Add(POIType.Quarry, Se, K | P, 2);
			Add(POIType.Lighthouse, Se, K | P, 2);

			Add(POIType.Wreck, Co, K, 2, "Shipwreck");
			Add(POIType.SmugglersCove, Co, K | H, 2, "Smugglers' Cove");
			Add(POIType.PirateCove, Co, K | P | H, 2);
			Add(POIType.CoralReef, Co, D | U, 3);
			Add(POIType.SunkenShip, Co, K | U, 2);
			Add(POIType.SunkenRuins, Co, K | U | H, 2);
			Add(POIType.SunkenCity, Co, K | U | H | R, 1);

			Add(POIType.DungeonEntrance, En, K, 1);
			Add(POIType.BossLair, En, K | P | H, 1, "World Boss Lair");
			Add(POIType.OreVein, En, K, 3, "Rare Ore Vein");
			Add(POIType.CrystalFormation, En, K, 3);
			Add(POIType.AncientTree, En, K, 3);
			Add(POIType.HerbGrove, En, K, 3);

			Add(POIType.IceGeyserField, Al, D, 3);
			Add(POIType.Cryovolcano, Al, D, 2);
			Add(POIType.MethaneLake, Al, D, 2);
			Add(POIType.ImpactBasin, Al, D, 2);
			Add(POIType.TidalRift, Al, D, 3);
			return t;
		}
	}
}
