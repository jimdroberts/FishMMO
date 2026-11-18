using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The trades a settlement's shop-houses are let to (2026-10-19, PointOfInterestTrades): the same shops always get the same
	/// trades whatever order they were laid in, neighbours never share one, a settlement keeps its own kinds of trade, and
	/// every trade's shop furnishes with its own bench.
	/// </summary>
	[TestFixture]
	public class PointOfInterestTradesTests
	{
		/// <summary>A street of shops 9 m apart on both sides, the way a town's shop-houses front its main street.</summary>
		private static List<Vector2> Street(int count)
		{
			var places = new List<Vector2>();
			for (int i = 0; i < count; i++)
			{
				places.Add(new Vector2((i / 2) * 9f + 3.3f, (i % 2) * 14f - 7f));
			}
			return places;
		}

		[Test]
		public void Assign_IsDeterministic_AndDoesNotHangOnTheOrderShopsWereLaid()
		{
			List<Vector2> places = Street(12);
			StructureTrade[] a = PointOfInterestTrades.Assign(POIType.Town, 1, 12345, places);
			StructureTrade[] b = PointOfInterestTrades.Assign(POIType.Town, 1, 12345, places);
			Assert.That(b, Is.EqualTo(a), "the same shops, the same trades");
			var reversed = new List<Vector2>(places);
			reversed.Reverse();
			StructureTrade[] c = PointOfInterestTrades.Assign(POIType.Town, 1, 12345, reversed);
			for (int i = 0; i < places.Count; i++)
			{
				Assert.That(c[places.Count - 1 - i], Is.EqualTo(a[i]), $"shop {i} changed trade when the shops were laid in another order");
			}
			bool anyDiffers = false;
			for (int seed = 1; seed <= 8 && !anyDiffers; seed++)
			{
				StructureTrade[] other = PointOfInterestTrades.Assign(POIType.Town, 1, seed, places);
				for (int i = 0; i < places.Count; i++)
				{
					anyDiffers |= other[i] != a[i];
				}
			}
			Assert.That(anyDiffers, Is.True, "another site's seed lets its shops otherwise");
		}

		[Test]
		public void Neighbours_NeverShareATrade_AndASettlementKeepsItsOwnTrades()
		{
			for (int seed = 0; seed < 40; seed++)
			{
				List<Vector2> places = Street(10);
				foreach (POIType kind in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Port, POIType.Castle, POIType.Monastery })
				{
					StructureTrade[] trades = PointOfInterestTrades.Assign(kind, 0, seed, places);
					float[] weights = PointOfInterestTrades.Weights(kind, 0);
					for (int i = 0; i < places.Count; i++)
					{
						Assert.That(weights[(int)trades[i]], Is.GreaterThan(0f), $"{kind} seed {seed}: let a shop to {trades[i]}, a trade it never keeps");
						// The nearest other shop within range, by the rule's own measure, never has the same trade when it was let first:
						// in a street every shop's nearest neighbour across or along it is a different trade.
						int nearest = -1;
						float best = PointOfInterestTrades.NeighbourRange * PointOfInterestTrades.NeighbourRange;
						for (int j = 0; j < places.Count; j++)
						{
							float d2 = (places[j] - places[i]).sqrMagnitude;
							if (j != i && d2 < best)
							{
								best = d2;
								nearest = j;
							}
						}
						if (nearest >= 0 && trades[nearest] == trades[i])
						{
							// Allowed only when that neighbour was let after this one and its own nearest let shop was another.
							Assert.That(Less(places[i], places[nearest]), Is.True, $"{kind} seed {seed}: shops {i} and {nearest} side by side are both {trades[i]}");
						}
					}
				}
			}
			StructureTrade[] village = PointOfInterestTrades.Assign(POIType.Village, 0, 7, Street(20));
			foreach (StructureTrade t in village)
			{
				Assert.That(t, Is.Not.EqualTo(StructureTrade.Jeweller).And.Not.EqualTo(StructureTrade.Cartographer).And.Not.EqualTo(StructureTrade.Scribe),
					"a village has no jeweller, chartmaker or scribe");
			}
		}

		/// <summary>The order the shops are let in (by place: x, then z).</summary>
		private static bool Less(Vector2 a, Vector2 b)
		{
			int ax = Mathf.RoundToInt(a.x * 100f), bx = Mathf.RoundToInt(b.x * 100f);
			return ax != bx ? ax < bx : Mathf.RoundToInt(a.y * 100f) < Mathf.RoundToInt(b.y * 100f);
		}

		[Test]
		public void APort_IsAChandlersTown_AndACastleAFletchers()
		{
			int portChandlers = 0, portAll = 0, castleFletchers = 0, castleAll = 0;
			for (int seed = 0; seed < 50; seed++)
			{
				foreach (StructureTrade t in PointOfInterestTrades.Assign(POIType.Port, 1, seed, Street(6)))
				{
					portAll++;
					portChandlers += t == StructureTrade.Chandler ? 1 : 0;
				}
				foreach (StructureTrade t in PointOfInterestTrades.Assign(POIType.Castle, 1, seed, Street(6)))
				{
					castleAll++;
					castleFletchers += t == StructureTrade.Fletcher ? 1 : 0;
				}
			}
			Assert.That(portChandlers / (float)portAll, Is.GreaterThan(1f / StructurePieces.TradeCount), "a port lets more than its share of shops to chandlers");
			Assert.That(castleFletchers / (float)castleAll, Is.GreaterThan(1f / StructurePieces.TradeCount), "a castle lets more than its share of shops to fletchers");
		}

		[Test]
		public void EveryTrade_HasItsPieces_AndItsShopFurnishesWithItsBench()
		{
			Assert.That(Enum.GetValues(typeof(StructureTrade)).Length, Is.EqualTo(StructurePieces.TradeCount));
			foreach (string id in new[] { "TradeSign", "StallGoods", "TradeShelf" })
			{
				Assert.That(StructurePieces.Find(id), Is.Not.Null, id);
				Assert.That(StructurePieces.Find(id).Variants, Is.EqualTo(StructurePieces.TradeCount), $"{id}: one variant per trade");
			}
			var bench = new Dictionary<StructureRoomKind, string>
			{
				{ StructureRoomKind.Tailor, "CuttingTable" }, { StructureRoomKind.Leatherworker, "StitchingBench" }, { StructureRoomKind.Fletcher, "FletchingBench" },
				{ StructureRoomKind.Cobbler, "CobblerBench" }, { StructureRoomKind.Jeweller, "JewellerBench" }, { StructureRoomKind.Chandler, "CandleRack" },
				{ StructureRoomKind.Cartographer, "MapTable" }, { StructureRoomKind.Scribe, "TradeShelf" },
			};
			Func<string, Vector2> footprint = id => { StructurePiece p = StructurePieces.Find(id); return p != null ? p.Footprint : Vector2.zero; };
			// A shop-house's ground floor, 6.4 × 9.4 m, its door in the front wall.
			var door = new StructureDoor { Hinge = new Vector3(1.3f, 0.4f, -4.7f), Across = Vector3.right, Inward = Vector3.forward, Width = 1.1f, Height = 2.3f };
			foreach (StructureTrade trade in Enum.GetValues(typeof(StructureTrade)))
			{
				StructureRoomKind kind = StructurePieces.TradeRoom(trade);
				if (!bench.TryGetValue(kind, out string piece))
				{
					continue;
				}
				var room = new StructureRoom { Centre = new Vector3(0f, 0.4f, 0f), Size = new Vector2(6.4f, 9.4f), Height = 2.8f, Kind = kind };
				for (int seed = 1; seed <= 4; seed++)
				{
					List<InteriorItem> plan = StructureInteriors.Plan(room, new[] { door }, seed * 7919, footprint);
					Assert.That(plan.Exists(it => it.Piece == piece), Is.True, $"{trade} (seed {seed}): its shop has no {piece}");
					foreach (InteriorItem it in plan)
					{
						if (it.Piece == "TradeShelf")
						{
							Assert.That(it.Variant, Is.EqualTo((int)trade), $"{trade}: its shelves are stocked with another trade's wares");
						}
					}
				}
			}
		}
	}
}
