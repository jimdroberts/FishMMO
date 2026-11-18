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
	/// the same thing. Each kind has its own <see cref="MapMarkerType"/> of the same name (2026-10-10), found
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
			Add(POIType.Tower, Se, K | P | R, 2, "Watchtower");
			Add(POIType.TradingPost, Se, K | P | S, 1);
			Add(POIType.Waystation, Se, K | P, 2);
			Add(POIType.Bridge, Se, K, 2);
			Add(POIType.Mine, Se, K | P, 2);
			Add(POIType.Quarry, Se, K | P, 2);
			Add(POIType.Lighthouse, Se, K | P, 2);
			Add(POIType.Dockyard, Se, K | P, 2);
			Add(POIType.Windmill, Se, K | P, 2);
			Add(POIType.Watermill, Se, K | P, 2);
			Add(POIType.LumberMill, Se, K | P, 2, "Lumber Mill");

			Add(POIType.Wreck, Co, K, 2, "Shipwreck");
			Add(POIType.SmugglersCove, Co, K | H, 2, "Smugglers' Cove");
			Add(POIType.PirateCove, Co, K | P | H, 2);
			Add(POIType.CoralReef, Co, D | U, 3);
			Add(POIType.SunkenShip, Co, K | U, 2);
			Add(POIType.SunkenRuins, Co, K | U | H, 2);
			Add(POIType.SunkenCity, Co, K | U | H | R, 1);
			// The sea floor's own sites (2026-10-25, phase I2m, decision 36 (b)): a drowned shrine, a sea monster's den, a crystal
			// garden, a monument and a statue the sea took. Their pieces are the land kinds' until I7 gives them their own.
			Add(POIType.SunkenShrine, Co, K | U, 2, "Drowned Shrine");
			Add(POIType.SeaMonsterDen, Co, K | U | R | H, 2, "Sea Monster's Den");
			Add(POIType.SeaCrystalFormation, Co, K | U, 3, "Sea Crystal Formation");
			Add(POIType.SunkenMonument, Co, K | U, 2);
			Add(POIType.SunkenStatue, Co, K | U, 3);
			// Adrift on the open sea (2026-10-25, I2p): surface sites, not the floor's, so neither U nor K (no template: a derelict's or
			// a whaler's hull rides the tide from its site builder, PointOfInterestAdrift).
			Add(POIType.AdriftDerelict, Co, PointOfInterestTraits.None, 2, "Derelict Adrift");
			Add(POIType.WhaleFall, Co, PointOfInterestTraits.None, 3, "Whale Fall");
			Add(POIType.BarnacleLog, Co, PointOfInterestTraits.None, 3, "Barnacle Log");
			Add(POIType.Longline, Co, PointOfInterestTraits.None, 3);
			Add(POIType.WhaleTow, Co, PointOfInterestTraits.None, 2, "Whale Under Tow");

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

			/* The biome rounds' own kinds (2026-10-25, phase NEW-POI-KINDS), sited only where a biome hosts them. A kind is K only
			 * where the kit's pieces already read as it (a gate, a chapel, a kiln); the rest are named and discoverable but lay
			 * nothing until I7 gives them their pieces, as the whale fall adrift does. */
			Add(POIType.TrenchEdge, Co, U, 3);
			Add(POIType.SeaCavern, Ts, U, 2);
			Add(POIType.CinderCone, Vo, PointOfInterestTraits.None, 3);
			Add(POIType.Kipuka, Vo, PointOfInterestTraits.None, 3);
			Add(POIType.HoluaSlide, Ru, PointOfInterestTraits.None, 3);
			Add(POIType.LavaFall, Vo, PointOfInterestTraits.None, 3);
			Add(POIType.AncientForge, En, K | P, 2);
			Add(POIType.PhoenixNest, En, K | H, 2);
			Add(POIType.LavaCaveTemple, Sa, K | P, 2);
			Add(POIType.MireGate, Ru, K, 2);
			Add(POIType.SunkenPlaza, Ru, K, 2);
			Add(POIType.MarshWatchtower, Se, K | P, 2);
			Add(POIType.ShatteredBridge, Ru, PointOfInterestTraits.None, 3);
			Add(POIType.FallenColossus, Ru, K, 2);
			Add(POIType.SealedVault, En, K | P, 2);
			Add(POIType.DrownedForest, We, PointOfInterestTraits.None, 3);
			Add(POIType.FrogShrine, Sa, K | P, 2);
			Add(POIType.FloatingIsland, We, PointOfInterestTraits.None, 3);
			Add(POIType.FowlersHide, Wi, K | P, 3, "Fowler's Hide");
			Add(POIType.EelWeir, Wi, PointOfInterestTraits.None, 3);
			Add(POIType.BrickKiln, Se, K | P, 2);
			Add(POIType.BlightedCrater, De, K, 2);
			Add(POIType.Chapel, Sa, K | P, 2);
			Add(POIType.HolyWell, Sa, K | P, 2);
			Add(POIType.PlagueVillage, De, K | P | H, 2);
			Add(POIType.DeadForest, De, PointOfInterestTraits.None, 3);
			Add(POIType.WarEngineGraveyard, Ru, PointOfInterestTraits.None, 2);
			Add(POIType.SunkenWhaleFall, Co, U, 3);
			Add(POIType.SeaHall, Ts, PointOfInterestTraits.None, 2);

			// The hot ground's own sites (2026-10-27): found where the cut dug them (GeothermalPointsOfInterest), shown zoomed in; the
			// scene's Great Spout is a Geyser record drawn at tier 2 like a waterfall. A hot spring's site is a HotSpring.
			Add(POIType.Geyser, Vo, D, 3);
			Add(POIType.MudPot, Vo, D, 3, "Mud Pot");
			Add(POIType.Fumarole, Vo, D, 3);
			Add(POIType.SulphurPool, Vo, D, 3, "Sulphur Pool");
			// The fantasy wave (2026-10-27).
			Add(POIType.DragonLair, En, K | R | H | P, 1, "Dragon's Lair");
			Add(POIType.TrollBridge, En, K | H, 2);
			Add(POIType.GiantsSteading, Wi, K | R | H | P, 2, "Giant's Steading");
			Add(POIType.GoblinWarren, Wi, K | R | H, 2);
			Add(POIType.HauntedManor, De, K | H | P, 2);
			Add(POIType.HangingTree, De, K | H, 3);
			Add(POIType.BuriedHoard, En, K, 3);
			Add(POIType.AmbushSite, En, K | R | H, 3);
			Add(POIType.WreckedCaravan, En, K, 3);
			Add(POIType.Crossroads, Ot, K, 3);
			Add(POIType.BoundaryStone, Ru, K, 3);
			Add(POIType.SignalBeacon, Se, K | P, 2);
			Add(POIType.TollGate, Se, K | P, 3);
			Add(POIType.Hamlet, Se, K | P | R | S, 2);
			Add(POIType.ManorHouse, Se, K | P | R, 2);
			Add(POIType.SiegeCamp, En, K | R | H | P, 2);
			Add(POIType.BurnedVillage, Ru, K | P, 2);
			Add(POIType.WarbandCamp, Wi, K | R | H | P, 2);
			Add(POIType.CultistHideout, Sa, K | R | H, 2);
			Add(POIType.AncientWall, Ru, K, 2);
			Add(POIType.SpellScar, Sa, K, 3);
			Add(POIType.PlanarRift, Sa, K | H, 2);
			Add(POIType.PetrifiedBattle, De, K, 2);
			Add(POIType.TitansBones, De, K, 2, "Titan's Bones");
			Add(POIType.WishingStone, Sa, K, 3);
			Add(POIType.ElderMonolith, Ru, K, 2);
			Add(POIType.WaygateRing, Sa, K | P, 1, "Waygate");
			Add(POIType.GolemGraveyard, Ru, K | H, 2);
			Add(POIType.KoboldDelving, Wi, K | R | H, 2);
			Add(POIType.LostLibrary, Ru, K | H, 2);
			Add(POIType.DruidGrove, Sa, K, 2);
			Add(POIType.TreeVillage, Se, K | R | S, 1);
			Add(POIType.TrollStones, Sa, K, 3);
			Add(POIType.SpiderThicket, Wi, K | R | H, 2);
			Add(POIType.CharcoalClamps, Wi, K | P, 3);
			Add(POIType.GreenwoodHideout, Wi, K | R | H, 2);
			Add(POIType.DryadOak, Sa, K, 3, "Dryad's Oak");
			Add(POIType.GoblinMarket, Se, K | R, 2);
			Add(POIType.ElfWaystone, Ru, K, 3);
			Add(POIType.WolfDen, Wi, K | R | H, 3);
			Add(POIType.MossColossus, Ru, K, 2);
			Add(POIType.StepPyramid, Ru, K | H | P, 1);
			Add(POIType.Cenote, Wa, T, 2);
			Add(POIType.JaguarTemple, Sa, K | H | P, 2);
			Add(POIType.SerpentIdol, Sa, K, 3);
			Add(POIType.LostCity, Ru, K | H | P, 1);
			Add(POIType.LizardfolkVillage, Se, K | R | S, 1);
			Add(POIType.IdolVault, En, K | H, 2);
			Add(POIType.Grange, Se, K | P, 2);
			Add(POIType.Dovecote, Se, K, 3);
			Add(POIType.BeeGarden, Se, K, 3);
			Add(POIType.VillageGreen, Se, K, 3);
			Add(POIType.ScarecrowField, De, K | H, 3);
			Add(POIType.LongBarrow, De, K | H, 2);
			Add(POIType.Henge, Sa, K | P, 2);
			Add(POIType.GallowsHill, De, K | H, 3);
			Add(POIType.KurganField, De, K | H, 2);
			Add(POIType.NomadCamp, Se, K | R | S | P, 1);
			Add(POIType.StoneFigures, Ru, K, 3);
			Add(POIType.Kraal, Se, K | R | S | P, 1);
			Add(POIType.Baobab, En, K, 3);
			Add(POIType.Shieling, Wi, K, 3);
			Add(POIType.Necropolis, De, K | H | P, 1);
			Add(POIType.Pyramid, Ru, K | H | P, 1);
			Add(POIType.Sphinx, Ru, K, 2);
			Add(POIType.RockCutFacade, De, K | H | T, 2, "Rock-cut Tomb");
			Add(POIType.SandBuriedTemple, Ru, K | H, 2, "Sand-buried Temple");
			Add(POIType.DjinnPrison, Sa, K | H, 2, "Djinn's Prison");
			Add(POIType.SandwormPit, Wi, T | H, 2);
			Add(POIType.CaravanHalt, Se, K | P, 2);
			Add(POIType.BeastBoneyard, De, K, 3);
			Add(POIType.CliffPalace, Ru, K | H | T, 2);
			Add(POIType.DreadRoad, Ru, K, 3);
			Add(POIType.DwarvenGate, En, K | T | P, 1);
			Add(POIType.MountainKingsHall, Se, K | R | S | P, 1, "Mountain King's Hall");
			Add(POIType.HangingMonastery, Sa, K | T, 2);
			Add(POIType.PassFort, Se, K | P, 1);
			Add(POIType.Eyrie, Wi, K | R | H, 3);
			Add(POIType.GriffinRoost, Wi, K | R | H, 2);
			Add(POIType.WyvernNest, Wi, K | R | H, 2);
			Add(POIType.CliffRelief, Ru, K | T, 2);
			Add(POIType.SummitCairn, Sa, K, 3);
			Add(POIType.PrayerFlagPass, Sa, K, 3);
			Add(POIType.SilverMineTown, Se, K | R | S | P, 1);
			Add(POIType.FrostGiantSteading, Wi, K | R | H | P, 1);
			Add(POIType.RuneStone, Ru, K, 3);
			Add(POIType.FrozenArmy, De, K | H, 2);
			Add(POIType.DragonInTheIce, En, K | H, 1, "Dragon in the Ice");
			Add(POIType.MammothGraveyard, De, K, 3);
			Add(POIType.WinterWolfDen, Wi, K | R | H, 3);
			Add(POIType.AuroraShrine, Sa, K, 2);
			Add(POIType.LichSpire, De, K | H | P, 1);
			Add(POIType.IcePalace, Ru, K | H | P, 1);
			Add(POIType.FrozenShipwreck, Co, K, 2);
			Add(POIType.Crannog, Se, K | R | S, 2);
			Add(POIType.HagsCauldron, Sa, K | H, 3, "Hag's Cauldron");
			Add(POIType.HydraLair, Wi, K | R | H, 1);
			Add(POIType.ReedVillage, Se, K | R | S, 1);
			Add(POIType.CovenCircle, Sa, K | R | H, 2);
			Add(POIType.LizardfolkHatchery, Wi, K | R | H, 3);
			Add(POIType.SwampCathedral, Ru, K | H | P, 1);
			Add(POIType.TidalIslandAbbey, Sa, K | T | P, 1);
			Add(POIType.CliffCastle, Se, K | R | P, 1);
			Add(POIType.SmugglersTunnel, Co, K | R | H, 3, "Smugglers' Tunnel");
			Add(POIType.WreckersBeacon, Co, K | R | H, 3, "Wreckers' Beacon");
			Add(POIType.SirenRock, Co, K | H, 2);
			Add(POIType.SeaWitchGrotto, Co, K | R | H, 2);
			Add(POIType.LongshipLanding, Co, K | R | H | P, 2);
			Add(POIType.KrakenWreck, Co, K, 2);
			Add(POIType.FireTemple, Sa, K | P, 1);
			Add(POIType.FireGiantHall, Wi, K | R | H | P, 1);
			Add(POIType.SalamanderLair, Wi, K | R | H, 3);
			Add(POIType.AshBuriedCity, Ru, K | H | P, 1, "Ash-buried City");
			Add(POIType.MerfolkVillage, Co, K | R | U | S, 1);
			Add(POIType.KrakenGrounds, Co, K | U | H, 1);
			Add(POIType.LadyOfTheLake, Sa, K, 2, "Lady of the Lake");
			Add(POIType.NymphPool, Sa, K, 3);
			Add(POIType.LakeMonsterLair, Wi, K | U | R | H, 2);
			Add(POIType.Commandery, Se, K | R | P, 2);
			Add(POIType.LostThrone, Ru, K, 3);
			Add(POIType.RoyalMausoleum, De, K | H | P, 2);
			return t;
		}
	}
}
