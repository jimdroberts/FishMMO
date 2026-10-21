#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Lets a site's shop-houses to their trades (2026-10-19: "Did we get a carpenter, tailor, leatherworker, fletcher ... and
	/// other profession archetypes?"): after the template's features, every laid building with a <c>"sign"</c> or
	/// <c>"stall"</c> mark (StructurePieces.MarksOf: the ShopHouse) is given a trade, its TradeSign hung at the sign mark and
	/// its StallGoods laid on the stall-board; PointOfInterestInteriors furnishes its shop as that trade's room.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Not a site builder of its own.</b> Each registered site builder draws from the site's generator salted by its place
	/// in the registration order (the editor's order of static constructors, by file), so a new one ahead of another would
	/// move that one's draws (the vines'): PointOfInterestInteriors lays the trades (<see cref="Lay"/>) before it furnishes.
	/// The trades are a pure function of the site (<see cref="TradesOf"/>), worked out again by whoever asks. The shops it reads are the laid pieces with a
	/// sign or stall mark; only a template's features lay those, all of them before any site builder runs, so every builder
	/// sees the same set. (A site builder that laid a shop-house itself would break that.)
	/// </para>
	/// <para>
	/// <b>Its own stream.</b> Drawn from the site's seed and each shop's place, never from the context's generator, so
	/// nothing laid before moves.
	/// </para>
	/// <para>
	/// <b>What a settlement keeps.</b> A village has the everyday trades (baker, cobbler, tailor, cooper, leatherworker,
	/// ironmonger, a chandler); a town adds the jeweller and the scribe; a city or capital has every trade; a port is a
	/// ship-chandler's, a cooper's and a chartmaker's town; a castle's shops are the garrison's (fletcher, leatherworker,
	/// ironmonger); a monastery's are the scribe's, the apothecary's and the chandler's (<see cref="Weights"/>). Two shops
	/// within <see cref="NeighbourRange"/> never share a trade, a trade already let is less likely to be let again, and no
	/// settlement lets more than two shops to one trade (PointOfInterestStructureCaps.TradeCap, 2026-10-20).
	/// </para>
	/// </remarks>
	public static class PointOfInterestTrades
	{
		/// <summary>The marks a shop-house carries (StructurePieces.ShopHouse): its sign's fixing, its stall-board's top, and the board's size.</summary>
		public const string SignMark = "sign", StallMark = "stall", StallSizeMark = "stall-size";

		/// <summary>How near two shops are neighbours, which never share a trade, metres.</summary>
		public const float NeighbourRange = 30f;

		/// <summary>The trades' own salt in the site's seed.</summary>
		private const int Salt = 0x7EAD5;

		/// <summary>Lets the site's shop-houses to their trades: each one's sign hung and its stall-board laid (PointOfInterestInteriors calls it).</summary>
		public static void Lay(PointOfInterestSiteContext context)
		{
			List<Shop> shops = ShopsOf(context);
			if (shops.Count == 0)
			{
				return;
			}
			StructureTrade[] trades = Assign(context, shops);
			StructurePiece goodsPiece = StructurePieces.Find("StallGoods");
			int missing = 0;
			for (int i = 0; i < shops.Count; i++)
			{
				Shop shop = shops[i];
				PointOfInterestSiteContext.PlacedProp laid = shop.Laid;
				int trade = (int)trades[i];
				Vector2 stallSize = Vector2.zero;
				foreach (StructureMark mark in StructurePieces.MarksOf(shop.Piece, shop.Variant))
				{
					if (mark.Name == StallSizeMark)
					{
						stallSize = new Vector2(mark.At.x, mark.At.z);
					}
				}
				foreach (StructureMark mark in StructurePieces.MarksOf(shop.Piece, shop.Variant))
				{
					string id = mark.Name == SignMark ? "TradeSign" : mark.Name == StallMark ? "StallGoods" : null;
					if (id == null)
					{
						continue;
					}
					GameObject prefab = StructureKit.Prefab(id, trade, shop.Finish) ?? StructureKit.Prefab(id, trade);
					if (prefab == null)
					{
						missing++;
						continue;
					}
					// The goods fit the board: shrunk (never stretched) to a smaller board than they were laid out for.
					float fit = 1f;
					if (id == "StallGoods" && goodsPiece != null && stallSize.x > 0f && stallSize.y > 0f)
					{
						Vector2 own = goodsPiece.Footprint;
						fit = Mathf.Min(1f, stallSize.x / Mathf.Max(0.01f, own.x), stallSize.y / Mathf.Max(0.01f, own.y));
					}
					Vector3 at = laid.Position + laid.Rotation * Vector3.Scale(mark.At, laid.Scale);
					Quaternion turn = laid.Rotation * Quaternion.Euler(0f, mark.Yaw, 0f);
					context.AddProp(prefab, at, turn, laid.Scale * fit);
				}
			}
			if (missing > 0)
			{
				PointOfInterestFeatureKit.Note(context, $"{missing} trade signs or stall goods have no art yet (Generate Biome Art, then regenerate).");
			}
		}

		/// <summary>One laid building a trade can be let to: as laid, and its piece, variant and finish.</summary>
		public struct Shop
		{
			public PointOfInterestSiteContext.PlacedProp Laid;
			public StructurePiece Piece;
			public int Variant;
			public StructureFinish Finish;
		}

		/// <summary>The site's laid buildings with a sign or stall mark, in the order they were laid.</summary>
		public static List<Shop> ShopsOf(PointOfInterestSiteContext context)
		{
			var shops = new List<Shop>();
			if (context?.Laid == null)
			{
				return shops;
			}
			foreach (PointOfInterestSiteContext.PlacedProp laid in context.Laid)
			{
				if (laid.Prefab == null || !StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece piece, out int variant, out StructureFinish finish))
				{
					continue;
				}
				foreach (StructureMark mark in StructurePieces.MarksOf(piece, variant))
				{
					if (mark.Name == SignMark || mark.Name == StallMark)
					{
						shops.Add(new Shop { Laid = laid, Piece = piece, Variant = variant, Finish = finish });
						break;
					}
				}
			}
			return shops;
		}

		/// <summary>
		/// Every shop's trade at the site, keyed by where its building was laid (<see cref="PlaceKey"/>): what
		/// PointOfInterestInteriors furnishes a shop room by (its PlacedRoom.Position is its building's).
		/// </summary>
		public static Dictionary<(int, int, int), StructureTrade> TradesOf(PointOfInterestSiteContext context)
		{
			var map = new Dictionary<(int, int, int), StructureTrade>();
			List<Shop> shops = ShopsOf(context);
			if (shops.Count == 0)
			{
				return map;
			}
			StructureTrade[] trades = Assign(context, shops);
			for (int i = 0; i < shops.Count; i++)
			{
				map[PlaceKey(shops[i].Laid.Position)] = trades[i];
			}
			return map;
		}

		/// <summary>A building's place to the centimetre: the same for its laid prop and its rooms.</summary>
		public static (int, int, int) PlaceKey(Vector3 position) =>
			(Mathf.RoundToInt(position.x * 100f), Mathf.RoundToInt(position.y * 100f), Mathf.RoundToInt(position.z * 100f));

		private static StructureTrade[] Assign(PointOfInterestSiteContext context, List<Shop> shops)
		{
			var places = new Vector2[shops.Count];
			for (int i = 0; i < shops.Count; i++)
			{
				places[i] = new Vector2(shops[i].Laid.Position.x, shops[i].Laid.Position.z);
			}
			PointOfInterestRecord record = context.Record;
			return Assign(record != null ? record.Kind : POIType.Village, record != null ? record.SizeClass : (byte)0, record != null ? record.SiteSeed : 0, places);
		}

		/// <summary>
		/// The trades of a settlement's shops (pure): each drawn by its settlement's <see cref="Weights"/> from the seed and its
		/// place, taken in order of place (so the answer does not hang on the order they were laid), a trade already let made
		/// less likely, and never its nearest let neighbour's within <see cref="NeighbourRange"/>.
		/// </summary>
		public static StructureTrade[] Assign(POIType kind, byte sizeClass, int seed, IReadOnlyList<Vector2> places)
		{
			int n = places.Count;
			var trades = new StructureTrade[n];
			var order = new int[n];
			for (int i = 0; i < n; i++)
			{
				order[i] = i;
			}
			System.Array.Sort(order, (a, b) =>
			{
				int ax = Mathf.RoundToInt(places[a].x * 100f), bx = Mathf.RoundToInt(places[b].x * 100f);
				if (ax != bx)
				{
					return ax.CompareTo(bx);
				}
				int az = Mathf.RoundToInt(places[a].y * 100f), bz = Mathf.RoundToInt(places[b].y * 100f);
				return az != bz ? az.CompareTo(bz) : a.CompareTo(b);
			});
			float[] baseWeights = Weights(kind, sizeClass);
			// No more than two shops to a trade in a settlement (2026-10-20, PointOfInterestStructureCaps).
			int cap = PointOfInterestStructureCaps.TradeCap(kind);
			var used = new int[StructurePieces.TradeCount];
			var done = new List<int>(n);
			var weights = new float[StructurePieces.TradeCount];
			foreach (int i in order)
			{
				// The nearest shop already let, if it is a neighbour: its trade is not let twice side by side.
				int neighbour = -1;
				float nearest = NeighbourRange * NeighbourRange;
				foreach (int j in done)
				{
					float d2 = (places[j] - places[i]).sqrMagnitude;
					if (d2 < nearest)
					{
						nearest = d2;
						neighbour = (int)trades[j];
					}
				}
				float total = 0f;
				for (int t = 0; t < weights.Length; t++)
				{
					weights[t] = t == neighbour || used[t] >= cap ? 0f : baseWeights[t] / (1f + used[t]);
					total += weights[t];
				}
				if (total <= 0f)
				{
					// Every trade the settlement keeps is next door or let to its cap: let one again rather than leave the shop shut,
					// a trade under its cap beside its neighbour first, else any it keeps, the least let likeliest.
					for (int t = 0; t < weights.Length; t++)
					{
						weights[t] = used[t] < cap ? baseWeights[t] : 0f;
						total += weights[t];
					}
					if (total <= 0f)
					{
						for (int t = 0; t < weights.Length; t++)
						{
							weights[t] = baseWeights[t] / (1f + used[t]);
							total += weights[t];
						}
					}
				}
				uint h = PointOfInterestPlanner.Hash(seed ^ Salt, Mathf.RoundToInt(places[i].x * 10f), Mathf.RoundToInt(places[i].y * 10f), 0x5409);
				float pick = PointOfInterestPlanner.Unit(h) * total;
				int chosen = weights.Length - 1;
				for (int t = 0; t < weights.Length; t++)
				{
					if (weights[t] <= 0f)
					{
						continue;
					}
					chosen = t;
					pick -= weights[t];
					if (pick < 0f)
					{
						break;
					}
				}
				trades[i] = (StructureTrade)chosen;
				used[chosen]++;
				done.Add(i);
			}
			return trades;
		}

		/// <summary>
		/// How often a settlement of a kind lets a shop to each trade (index = <see cref="StructureTrade"/>; zero: never). A bigger
		/// settlement (its size class) keeps more of the rarer trades: the jeweller, the cartographer and the scribe.
		/// </summary>
		public static float[] Weights(POIType kind, byte sizeClass)
		{
			float[] w;
			switch (kind)
			{
				//                     Tailor Leather Fletch Cobbler Jewel Chandl Carto Scribe Baker Cooper Ironm Apoth
				case POIType.City:
				case POIType.Capital:
					w = new[] { 2f, 1.5f, 1f, 1.5f, 1.5f, 1.5f, 1f, 1.5f, 2f, 1.5f, 1.5f, 1.5f };
					break;
				case POIType.Town:
					w = new[] { 2f, 1.5f, 1f, 1.5f, 0.6f, 1.2f, 0.3f, 0.8f, 2.5f, 1.5f, 1.5f, 1f };
					break;
				// Ship-chandlers, coopers for the casks, chartmakers and bakers of ship's biscuit.
				case POIType.Port:
				case POIType.Dockyard:
				case POIType.Lighthouse:
					w = new[] { 1f, 1f, 0.5f, 1f, 0.5f, 3f, 2f, 0.5f, 2f, 2.5f, 1.5f, 0.5f };
					break;
				// The garrison's: bows and arrows, harness, ironmongery.
				case POIType.Keep:
				case POIType.Castle:
				case POIType.Fortress:
					w = new[] { 1f, 2f, 3f, 1.5f, 0.3f, 1f, 0.3f, 0.5f, 2f, 1.5f, 2f, 0.8f };
					break;
				// The brothers' own: the scriptorium's, the infirmarian's herbs and the chandler's candles.
				case POIType.Monastery:
				case POIType.Temple:
				case POIType.Hermitage:
					w = new[] { 0.5f, 0.5f, 0f, 0.5f, 0.3f, 2.5f, 0.5f, 4f, 2f, 0.5f, 0.5f, 3f };
					break;
				// What travellers buy: harness and boots, lamps, maps of the road.
				case POIType.TradingPost:
				case POIType.Waystation:
					w = new[] { 0.5f, 2f, 1f, 1.5f, 0.3f, 1.5f, 1f, 0.3f, 1.5f, 1f, 2f, 0.5f };
					break;
				// A village (and anything else with a shop): the everyday trades.
				default:
					w = new[] { 1.5f, 1.5f, 1f, 2f, 0f, 1f, 0f, 0f, 3f, 1.5f, 1.5f, 0.8f };
					break;
			}
			float rare = 1f + 0.5f * Mathf.Min((int)sizeClass, 2);
			w[(int)StructureTrade.Jeweller] *= rare;
			w[(int)StructureTrade.Cartographer] *= rare;
			w[(int)StructureTrade.Scribe] *= rare;
			return w;
		}
	}
}
#endif
