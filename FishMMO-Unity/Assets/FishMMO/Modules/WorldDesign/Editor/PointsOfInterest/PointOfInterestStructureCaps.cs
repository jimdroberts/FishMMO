#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How many of one type of structure a settlement keeps (2026-10-20: "There should only be one or two of the same type of structure
	/// in a town, city, village, capital, etc."): one of a landmark (its cathedral, its palace or keep, its market hall, library,
	/// observatory, counting house, temple, wizard's tower), at most two of any other building it is known by (its taverns, smithies,
	/// bakehouses, barns, wells, fountains, mausoleums…), and at most two shops let to any one trade.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What is not capped.</b> Its houses, cottages, longhouses, shop-houses (each let to a trade, and the trades are capped:
	/// PointOfInterestTrades), bothies and tents, which are most of a settlement and come in several pieces and variants; its walls,
	/// towers, gates, lychgates and fences; and its small things: stalls, crates, barrels, notice boards, banners, braziers, lamps,
	/// hives, ricks, carts, woodpiles, gravestones. A type is capped only where <see cref="CapOf"/> names it.
	/// </para>
	/// <para>
	/// <b>Which sites.</b> Every place people live (PointOfInterestTraits.Settlement: a village, a town, a city, the capital, a port, a
	/// trading post, a stilt village, a monastery) and every stronghold with a ward (a keep, a castle, a fortress), with everything
	/// laid round it: its farmsteads, its outskirts, its churchyards (<see cref="Applies"/>). Ruins, the dead and the wild keep theirs.
	/// </para>
	/// <para>
	/// <b>One count per site.</b> What the site has laid is <see cref="PointOfInterestSiteContext.Laid"/>, every prop every feature and
	/// builder laid through <see cref="PointOfInterestSiteContext.AddProp"/>: <see cref="Tally.Of"/> counts it by type, so each stage
	/// that lays a capped type (PropsFeature's slots before its layout, a churchyard's mausoleums, the farmsteads' barns, sties, hen
	/// houses and wells, the cemetery's tombs) takes what room the stages before it left. A template's slots are capped where their
	/// counts are drawn, before the layout, so a capped building never leaves a hole in a street. Pure apart from reading the laid
	/// props; no draw of any stream is taken here.
	/// </para>
	/// </remarks>
	public static class PointOfInterestStructureCaps
	{
		/// <summary>The cap of a type nothing caps.</summary>
		public const int Unlimited = int.MaxValue;

		/// <summary>The most of a landmark a settlement keeps, and of any other capped building (and of shops let to one trade).</summary>
		public const int Landmark = 1, Most = 2;

		/// <summary>The type a fountain is (MarketCross's second variant): another thing on the square than the stone cross.</summary>
		public const string Fountain = "Fountain";

		/// <summary>Whether a site of <paramref name="kind"/> keeps to the caps: every settlement and every stronghold with a ward.</summary>
		public static bool Applies(POIType kind)
			=> PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Settlement) || kind == POIType.Keep || kind == POIType.Castle || kind == POIType.Fortress;

		/// <summary>
		/// The most of a type (a kit piece's id, <see cref="TypeOfPiece"/>) a settlement keeps: <see cref="Landmark"/> for its
		/// landmarks, <see cref="Most"/> for its other buildings, <see cref="Unlimited"/> for everything not named (see the remarks).
		/// </summary>
		public static int CapOf(string type)
		{
			switch (type)
			{
				// The landmarks: one to a settlement.
				case "Cathedral":
				case "ThroneHall":
				case "Keep":
				case "MarketHall":
				case "Library":
				case "Observatory":
				case "CountingHouse":
				case "TownTemple":
				case "WizardTower":
				case "Lighthouse":
				case "Gallows":
				case "Pillory":
				case "Maypole":
				case "MarketCross":
				case "Obelisk":
					return Landmark;
				// The rest of its buildings and monuments: one or two.
				case "Church":
				case "Tavern":
				case "Smithy":
				case "Stables":
				case "Well":
				case Fountain:
				case "Granary":
				case "Dovecote":
				case "TanningYard":
				case "LimeKiln":
				case "Healer":
				case "Bakery":
				case "Workshop":
				case "Brewhouse":
				case "Barracks":
				case "GuardHouse":
				case "Warehouse":
				case "ArcheryRange":
				case "TrainingYard":
				case "DyeWorks":
				case "PotteryKiln":
				case "Barn":
				case "Pigsty":
				case "HenHouse":
				case "TowerMill":
				case "Watermill":
				case "Sawmill":
				case "ShepherdHut":
				case "Sheepfold":
				case "Mausoleum":
				case "Statue":
				case "WaysideCross":
					return Most;
				default:
					return Unlimited;
			}
		}

		/// <summary>The most of a type a site of <paramref name="kind"/> keeps: <see cref="CapOf"/> where the caps apply, else unlimited.</summary>
		public static int CapFor(POIType kind, string type) => Applies(kind) ? CapOf(type) : Unlimited;

		/// <summary>The most shops a site of <paramref name="kind"/> lets to any one trade (PointOfInterestTrades).</summary>
		public static int TradeCap(POIType kind) => Applies(kind) ? Most : Unlimited;

		/// <summary>
		/// The type a laid kit piece is: its id, but for the market cross's fountain (its second variant), which is
		/// <see cref="Fountain"/>. A chapel and a parish church are both a Church.
		/// </summary>
		public static string TypeOfPiece(string id, int variant)
		{
			if (string.IsNullOrEmpty(id))
			{
				return string.Empty;
			}
			return id == "MarketCross" && variant == 1 ? Fountain : id;
		}

		/// <summary>
		/// The type a template slot's query lays (pure): an exact piece's id ("=Tavern", its variant pin read only for the fountain);
		/// a tag query's first tag as the piece it names in a settlement ("tavern" Tavern, "stable" Stables, "temple" TownTemple);
		/// empty for a query that names no capped type ("house", "tower+wall", "market").
		/// </summary>
		public static string TypeOfQuery(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
			{
				return string.Empty;
			}
			string pins = StructureKitPieceSource.Pins(query.Trim(), out string bare);
			bare = (bare ?? string.Empty).Trim();
			if (bare.Length == 0)
			{
				return string.Empty;
			}
			if (bare[0] == '=')
			{
				string id = bare.Substring(1).Trim();
				bool fountain = id == "MarketCross" && !string.IsNullOrEmpty(pins) && pins.Trim('1').Length == 0;
				return TypeOfPiece(id, fountain ? 1 : 0);
			}
			int end = bare.IndexOfAny(TagSeparators);
			string tag = (end < 0 ? bare : bare.Substring(0, end)).Trim();
			switch (tag)
			{
				case "tavern": return "Tavern";
				case "smithy": return "Smithy";
				case "stable": return "Stables";
				case "church": return "Church";
				case "cathedral": return "Cathedral";
				case "temple": return "TownTemple";
				case "well": return "Well";
				case "keep": return "Keep";
				case "lighthouse": return "Lighthouse";
				case "barn": return "Barn";
				case "library": return "Library";
				case "observatory": return "Observatory";
				case "warehouse": return "Warehouse";
				case "barracks": return "Barracks";
				case "healer": return "Healer";
				case "bakery": return "Bakery";
				case "workshop": return "Workshop";
				case "brewery": return "Brewhouse";
				case "guildhall": return "MarketHall";
				case "bank": return "CountingHouse";
				case "wizard": return "WizardTower";
				case "dovecote": return "Dovecote";
				case "windmill": return "TowerMill";
				case "watermill": return "Watermill";
				case "sawmill": return "Sawmill";
				default: return string.Empty;
			}
		}

		private static readonly char[] TagSeparators = { '+', '!' };

		/// <summary>
		/// A site's structures counted by type (only the capped types), and the room each has left under its cap. Lookups only: no
		/// order of anything is read from its map.
		/// </summary>
		public sealed class Tally
		{
			private readonly bool capped;
			private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

			/// <summary>An empty tally for a site of <paramref name="kind"/>.</summary>
			public Tally(POIType kind)
			{
				capped = Applies(kind);
			}

			/// <summary>A site's tally as laid so far: every capped piece in its <see cref="PointOfInterestSiteContext.Laid"/>.</summary>
			public static Tally Of(PointOfInterestSiteContext context)
			{
				var tally = new Tally(context?.Record != null ? context.Record.Kind : POIType.Any);
				if (!tally.capped || context.Laid == null)
				{
					return tally;
				}
				foreach (PointOfInterestSiteContext.PlacedProp laid in context.Laid)
				{
					if (laid.Prefab != null && StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece piece, out int variant))
					{
						tally.Count(TypeOfPiece(piece.Id, variant), 1);
					}
				}
				return tally;
			}

			/// <summary>How many of a type are counted.</summary>
			public int CountOf(string type) => !string.IsNullOrEmpty(type) && counts.TryGetValue(type, out int n) ? n : 0;

			/// <summary>How many more of a type the site may lay: <see cref="Unlimited"/> for a type (or a site) nothing caps.</summary>
			public int Room(string type)
			{
				int cap = capped && !string.IsNullOrEmpty(type) ? CapOf(type) : Unlimited;
				return cap == Unlimited ? Unlimited : Math.Max(0, cap - CountOf(type));
			}

			/// <summary>Takes up to <paramref name="wanted"/> of a type's room and counts it; returns how many were taken.</summary>
			public int Take(string type, int wanted)
			{
				int n = Math.Min(Math.Max(0, wanted), Room(type));
				Count(type, n);
				return n;
			}

			/// <summary>Takes one of a type's room where it has any; true when it did.</summary>
			public bool TakeOne(string type) => Take(type, 1) == 1;

			private void Count(string type, int n)
			{
				if (n <= 0 || string.IsNullOrEmpty(type) || CapOf(type) == Unlimited)
				{
					return;
				}
				counts[type] = CountOf(type) + n;
			}
		}
	}
}
#endif
