using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// One or two of a type of structure to a settlement (2026-10-20: "There should only be one or two of the same type of structure in
	/// a town, city, village, capital, etc.", PointOfInterestStructureCaps): one of a landmark, at most two of any other building, its
	/// houses, walls and small things free; held where a template's counts are drawn, by the farmsteads, the churchyards, the cemetery
	/// and the trades, every one of them from the site's one count of what it has laid. Pure: no scene, no art.
	/// </summary>
	[TestFixture]
	public class StructureCapTests
	{
		private const string Folder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest";

		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		private static int CapOfQuery(string query) => PointOfInterestStructureCaps.CapOf(PointOfInterestStructureCaps.TypeOfQuery(query));

		// ── Types ─────────────────────────────────────────────────

		[Test]
		public void AQueryAndThePieceItLaysAreOneType()
		{
			foreach (string church in new[] { PointOfInterestTemplateSpecs.Church, PointOfInterestTemplateSpecs.Chapel, PointOfInterestTemplateSpecs.ParishChurch,
				PointOfInterestTemplateSpecs.VillageChurch })
			{
				Assert.That(PointOfInterestStructureCaps.TypeOfQuery(church), Is.EqualTo("Church"), $"{church}: a chapel and a parish church are both churches");
			}
			Assert.That(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.Cathedral), Is.EqualTo("Cathedral"));
			Assert.That(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.Fountain), Is.EqualTo(PointOfInterestStructureCaps.Fountain));
			Assert.That(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.StoneCross), Is.EqualTo("MarketCross"), "the cross is not the fountain");
			Assert.That(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.Palace), Is.EqualTo("ThroneHall"));
			Assert.That(PointOfInterestStructureCaps.TypeOfPiece("MarketCross", 1), Is.EqualTo(PointOfInterestStructureCaps.Fountain));
			Assert.That(PointOfInterestStructureCaps.TypeOfPiece("MarketCross", 0), Is.EqualTo("MarketCross"));
			Assert.That(PointOfInterestStructureCaps.TypeOfPiece("Church", 1), Is.EqualTo("Church"));

			// One of a landmark, two of the rest.
			foreach (string landmark in new[] { PointOfInterestTemplateSpecs.Cathedral, PointOfInterestTemplateSpecs.Palace, PointOfInterestTemplateSpecs.Keep,
				PointOfInterestTemplateSpecs.MarketHall, PointOfInterestTemplateSpecs.Library, PointOfInterestTemplateSpecs.Observatory,
				PointOfInterestTemplateSpecs.CountingHouse, PointOfInterestTemplateSpecs.Temple, PointOfInterestTemplateSpecs.WizardTower })
			{
				Assert.That(CapOfQuery(landmark), Is.EqualTo(PointOfInterestStructureCaps.Landmark), landmark);
			}
			foreach (string building in new[] { PointOfInterestTemplateSpecs.Tavern, PointOfInterestTemplateSpecs.Smithy, PointOfInterestTemplateSpecs.Stable,
				PointOfInterestTemplateSpecs.Well, PointOfInterestTemplateSpecs.Fountain, PointOfInterestTemplateSpecs.ParishChurch, PointOfInterestTemplateSpecs.Barn,
				PointOfInterestTemplateSpecs.Bakery, PointOfInterestTemplateSpecs.Healer, PointOfInterestTemplateSpecs.Workshop, PointOfInterestTemplateSpecs.Brewhouse,
				PointOfInterestTemplateSpecs.Barracks, PointOfInterestTemplateSpecs.GuardHouse, PointOfInterestTemplateSpecs.Warehouse, PointOfInterestTemplateSpecs.Granary,
				PointOfInterestTemplateSpecs.ArcheryRange, PointOfInterestTemplateSpecs.TrainingYard, PointOfInterestTemplateSpecs.DyeWorks,
				PointOfInterestTemplateSpecs.PotteryKiln, PointOfInterestTemplateSpecs.TowerMill })
			{
				Assert.That(CapOfQuery(building), Is.EqualTo(PointOfInterestStructureCaps.Most), building);
			}
			// The houses, the walls and the small things are free.
			foreach (string free in new[] { PointOfInterestTemplateSpecs.House, PointOfInterestTemplateSpecs.Cottage, PointOfInterestTemplateSpecs.StoneHouse,
				PointOfInterestTemplateSpecs.Longhouse, PointOfInterestTemplateSpecs.Stilt, PointOfInterestTemplateSpecs.ShopHouse, PointOfInterestTemplateSpecs.Tent,
				PointOfInterestTemplateSpecs.Wall, PointOfInterestTemplateSpecs.Palisade, PointOfInterestTemplateSpecs.Gate, PointOfInterestTemplateSpecs.WallTower,
				PointOfInterestTemplateSpecs.Watchtower, PointOfInterestTemplateSpecs.Fence, PointOfInterestTemplateSpecs.Stall, PointOfInterestTemplateSpecs.Storage,
				PointOfInterestTemplateSpecs.Banner, PointOfInterestTemplateSpecs.Notice, PointOfInterestTemplateSpecs.Brazier, PointOfInterestTemplateSpecs.TorchPost,
				PointOfInterestTemplateSpecs.Apiary, PointOfInterestTemplateSpecs.Fire, "" })
			{
				Assert.That(CapOfQuery(free), Is.EqualTo(PointOfInterestStructureCaps.Unlimited), free);
			}
		}

		/// <summary>Every capped slot of a settlement's template lays only pieces of the type its query is counted as.</summary>
		[Test]
		public void EveryCappedQueryLaysOnlyPiecesOfItsType()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				if (!PointOfInterestStructureCaps.Applies(spec.Kind))
				{
					continue;
				}
				foreach (PointOfInterestPieceSlot slot in spec.Pieces)
				{
					string type = PointOfInterestStructureCaps.TypeOfQuery(slot.Tag);
					if (PointOfInterestStructureCaps.CapOf(type) == PointOfInterestStructureCaps.Unlimited)
					{
						continue;
					}
					string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
					List<StructurePiece> pieces = StructureKitPieceSource.Candidates(slot.Tag, style);
					Assert.That(pieces, Is.Not.Empty, $"{spec.Name}: '{slot.Tag}'");
					string pins = StructureKitPieceSource.Pins(slot.Tag, out _);
					foreach (StructurePiece piece in pieces)
					{
						int variant = pins != null ? pins[0] - '0' : 0;
						Assert.That(PointOfInterestStructureCaps.TypeOfPiece(piece.Id, variant), Is.EqualTo(type), $"{spec.Name}: '{slot.Tag}' lays {piece.Id}");
					}
				}
			}
		}

		// ── The tally ─────────────────────────────────────────────

		[Test]
		public void ATallyCutsASlotToItsTypesRoom_SlotsOfOneTypeSharingIt()
		{
			var town = new PointOfInterestStructureCaps.Tally(POIType.Town);
			string church = PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.ParishChurch);
			Assert.That(town.Take(church, 1), Is.EqualTo(1));
			Assert.That(town.Take(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.Chapel), 3), Is.EqualTo(1), "the chapel shares the church's room");
			Assert.That(town.Room(church), Is.Zero);
			Assert.That(town.TakeOne(church), Is.False);
			Assert.That(town.Take(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.Tavern), 5), Is.EqualTo(2));
			Assert.That(town.Take(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.MarketHall), 2), Is.EqualTo(1));
			Assert.That(town.Take(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.House), 40), Is.EqualTo(40), "houses are free");
			Assert.That(town.Room(PointOfInterestStructureCaps.TypeOfQuery(PointOfInterestTemplateSpecs.House)), Is.EqualTo(PointOfInterestStructureCaps.Unlimited));
			Assert.That(town.Take("Tavern", -3), Is.Zero);

			// Ruins, the dead and the wild keep what they have.
			foreach (POIType wild in new[] { POIType.Ruins, POIType.Graveyard, POIType.Camp, POIType.BanditCamp, POIType.SunkenCity })
			{
				Assert.That(PointOfInterestStructureCaps.Applies(wild), Is.False, wild.ToString());
				Assert.That(new PointOfInterestStructureCaps.Tally(wild).Take("Tavern", 5), Is.EqualTo(5), wild.ToString());
			}
			foreach (POIType settled in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.TradingPost,
				POIType.StiltVillage, POIType.Monastery, POIType.Keep, POIType.Castle, POIType.Fortress })
			{
				Assert.That(PointOfInterestStructureCaps.Applies(settled), Is.True, settled.ToString());
			}
		}

		// ── The templates ─────────────────────────────────────────

		[Test]
		public void EverySettlementsTemplateAsksForNoMoreOfATypeThanItsCap()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				if (!PointOfInterestStructureCaps.Applies(spec.Kind))
				{
					continue;
				}
				var asked = new Dictionary<string, int>(StringComparer.Ordinal);
				for (int s = 0; s < spec.Pieces.Count; s++)
				{
					PointOfInterestPieceSlot slot = spec.Pieces[s];
					if (PointOfInterestLayouts.Generates(PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role)))
					{
						continue;
					}
					string type = PointOfInterestStructureCaps.TypeOfQuery(slot.Tag);
					if (PointOfInterestStructureCaps.CapOf(type) == PointOfInterestStructureCaps.Unlimited)
					{
						continue;
					}
					asked.TryGetValue(type, out int n);
					asked[type] = n + slot.MaxCount;
					Assert.That(asked[type], Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf(type)), $"{spec.Name}: asks for up to {asked[type]} {type}");
				}
			}
		}

		/// <summary>Lays a spec at a size as PropsFeature does, its counts held to the site's tally: the request and the pieces.</summary>
		private static List<PointOfInterestLayoutPiece> Lay(PointOfInterestTemplateSpec spec, int size, int seed, out PointOfInterestLayoutRequest request)
		{
			PointOfInterestKindRule rule = PointOfInterestRules.Default().RuleFor(spec.Kind);
			float radius = rule != null ? Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, size / 2f) : 40f;
			var random = new DeterministicRNG(seed + size);
			var caps = new PointOfInterestStructureCaps.Tally(spec.Kind);
			request = new PointOfInterestLayoutRequest { Layout = spec.Layout, Radius = radius, Gap = 1f, Kind = spec.Kind, SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset };
			for (int s = 0; s < spec.Pieces.Count; s++)
			{
				PointOfInterestPieceSlot slot = spec.Pieces[s];
				string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
				int count = PropsFeature.CountFor(slot, size, true, random);
				if (!PointOfInterestLayouts.Generates(role))
				{
					count = caps.Take(PointOfInterestStructureCaps.TypeOfQuery(slot.Tag), count);
				}
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
				bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
				request.Slots.Add(new PointOfInterestLayoutSlot
				{
					Role = role,
					Footprint = Vector2.Max(metrics.Footprint, metrics.Sweep),
					Module = metrics.Module,
					Count = count,
					Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
					Join = station ? PropsFeature.StationJoin(slot.Tag, style, 0) : 0f,
					Landmark = PointOfInterestTemplateSpecs.IsLandmark(slot.Tag, spec.Kind),
					Well = slot.Tag == PointOfInterestTemplateSpecs.Well,
				});
				if (PointOfInterestLayouts.Generates(role))
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, random.Next()));
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = Vector2.Max(piece.Footprint, piece.Sweep) });
				}
			}
			return PointOfInterestLayouts.Place(request, random);
		}

		/// <summary>The laid pieces of a spec counted by capped type (in the order the types were first laid).</summary>
		private static List<(string Type, int Count)> Counted(PointOfInterestTemplateSpec spec, List<PointOfInterestLayoutPiece> pieces)
		{
			var counted = new List<(string Type, int Count)>();
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (piece.Item < 0)
				{
					continue;
				}
				string type = PointOfInterestStructureCaps.TypeOfQuery(spec.Pieces[piece.Slot].Tag);
				if (PointOfInterestStructureCaps.CapOf(type) == PointOfInterestStructureCaps.Unlimited)
				{
					continue;
				}
				int at = counted.FindIndex(c => c.Type == type);
				if (at < 0)
				{
					counted.Add((type, 1));
				}
				else
				{
					counted[at] = (type, counted[at].Count + 1);
				}
			}
			return counted;
		}

		[Test]
		public void ASettlementLaysNoMoreOfATypeThanItsCap_AtEverySize()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				if (!PointOfInterestStructureCaps.Applies(spec.Kind))
				{
					continue;
				}
				foreach (int size in spec.Sizes())
				{
					List<PointOfInterestLayoutPiece> pieces = Lay(spec, size, 31 + 17 * size, out _);
					foreach ((string type, int count) in Counted(spec, pieces))
					{
						Assert.That(count, Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf(type)), $"{spec.Name} at size {size}: {count} {type}");
					}
				}
			}
		}

		/// <summary>A template written by hand past the caps (an old asset, or one edited in the inspector) still lays to them.</summary>
		[Test]
		public void AHandWrittenTemplatePastTheCaps_IsHeldToThem()
		{
			PointOfInterestTemplateSpec town = PointOfInterestTemplateSpecs.For(POIType.Town)[0];
			town.S(PointOfInterestTemplateSpecs.Tavern, 4, 4, PointOfInterestSlotRole.Street)
				.S(PointOfInterestTemplateSpecs.Chapel, 3, 3, PointOfInterestSlotRole.Street)
				.S(PointOfInterestTemplateSpecs.MarketHall, 3, 3, PointOfInterestSlotRole.Street)
				.S(PointOfInterestTemplateSpecs.Barn, 5, 5, PointOfInterestSlotRole.Edge);
			for (int seed = 1; seed <= 3; seed++)
			{
				List<PointOfInterestLayoutPiece> pieces = Lay(town, 2, seed, out PointOfInterestLayoutRequest request);
				List<(string Type, int Count)> counted = Counted(town, pieces);
				foreach ((string type, int count) in counted)
				{
					Assert.That(count, Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf(type)), $"seed {seed}: {count} {type}");
				}
				// What was asked of each type across its slots, as the layout was given it: no more than the cap.
				int taverns = 0, churches = 0, halls = 0, barns = 0;
				for (int s = 0; s < request.Slots.Count; s++)
				{
					string type = PointOfInterestStructureCaps.TypeOfQuery(town.Pieces[s].Tag);
					int n = request.Slots[s].Count;
					taverns += type == "Tavern" ? n : 0;
					churches += type == "Church" ? n : 0;
					halls += type == "MarketHall" ? n : 0;
					barns += type == "Barn" ? n : 0;
				}
				Assert.That((taverns, churches, halls, barns), Is.EqualTo((2, 2, 1, 2)), $"seed {seed}: the slots past the caps are cut to them, not dropped");
			}
		}

		// ── What is laid round a settlement ───────────────────────

		[Test]
		public void TheFarmsteadsOutskirtsAndChurchyardsAskForNoMoreThanTheCaps()
		{
			foreach (POIType kind in (POIType[])Enum.GetValues(typeof(POIType)))
			{
				if (!PointOfInterestStructureCaps.Applies(kind))
				{
					continue;
				}
				// A barn to a farmstead.
				Assert.That(PointOfInterestFarmsteads.CountFor(kind).max, Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf(PointOfInterestFarmsteads.BarnType)), $"{kind}: farmsteads");
				Assert.That(PointOfInterestOutskirts.CountFor(kind, PointOfInterestOutskirts.Holding.WaysideCross).max, Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf("WaysideCross")), $"{kind}: wayside crosses");
				Assert.That(PointOfInterestOutskirts.CountFor(kind, PointOfInterestOutskirts.Holding.Shepherd).max, Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf("ShepherdHut")), $"{kind}: shepherds");
				Assert.That(PointOfInterestOutskirts.CountFor(kind, PointOfInterestOutskirts.Holding.Gallows).max, Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf("Gallows")), $"{kind}: gallows");
				for (float roll = 0f; roll < 1f; roll += 0.05f)
				{
					Assert.That(PointOfInterestChurchyards.MausoleumsFor(kind, roll), Is.LessThanOrEqualTo(PointOfInterestStructureCaps.CapOf(PointOfInterestChurchyards.MausoleumType)), $"{kind}: a churchyard's tombs");
				}
			}
		}

		[Test]
		public void NoSettlementLetsMoreThanTwoShopsToATrade()
		{
			var places = new List<Vector2>();
			for (int i = 0; i < 24; i++)
			{
				places.Add(new Vector2((i / 2) * 9f + 3.3f, (i % 2) * 14f - 7f));
			}
			foreach (POIType kind in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.Castle, POIType.Monastery })
			{
				for (int seed = 0; seed < 30; seed++)
				{
					// As many shops as the settlement's trades can take two to each, at most.
					float[] weights = PointOfInterestTrades.Weights(kind, 2);
					int kept = 0;
					foreach (float w in weights)
					{
						kept += w > 0f ? 1 : 0;
					}
					List<Vector2> shops = places.GetRange(0, Mathf.Min(places.Count, kept * PointOfInterestStructureCaps.TradeCap(kind)));
					StructureTrade[] trades = PointOfInterestTrades.Assign(kind, 2, seed, shops);
					var let = new int[StructurePieces.TradeCount];
					foreach (StructureTrade t in trades)
					{
						let[(int)t]++;
						Assert.That(let[(int)t], Is.LessThanOrEqualTo(PointOfInterestStructureCaps.TradeCap(kind)), $"{kind} seed {seed}: {let[(int)t]} shops let to {t}");
					}
				}
			}
		}

		// ── The wiring ────────────────────────────────────────────

		private static string Source(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

		/// <summary>Every stage that lays a capped type reads the site's one count, and the count is no site builder of its own.</summary>
		[Test]
		public void EveryStageThatLaysACappedType_KeepsToTheSitesCount()
		{
			string props = Source(Folder + "/PropsFeature.cs");
			Assert.That(props.Contains("PointOfInterestStructureCaps.Tally caps = PointOfInterestStructureCaps.Tally.Of(context);"), Is.True, "PropsFeature: the site's tally");
			Assert.That(props.Contains("count = caps.Take(PointOfInterestStructureCaps.TypeOfQuery(slot.Tag), count);"), Is.True, "PropsFeature: every slot's count held to it");
			Assert.That(props.IndexOf("count = caps.Take(", StringComparison.Ordinal), Is.LessThan(props.IndexOf("request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = itemFootprint });", StringComparison.Ordinal)),
				"before the items are made: a capped building never leaves a hole in a street");

			string farm = Source(Folder + "/Features/PointOfInterestFarmsteads.cs");
			foreach (string pin in new[] { "wanted = Mathf.Min(wanted, caps.Room(BarnType));", "caps.TakeOne(BarnType);", "caps.TakeOne(StyType);", "caps.TakeOne(HenType);",
				"caps.TakeOne(WellType)", "mix.Sty &= caps.Room(StyType) > 0;", "mix.Hen &= caps.Room(HenType) > 0;" })
			{
				Assert.That(farm.Contains(pin), Is.True, $"PointOfInterestFarmsteads: {pin}");
			}
			Assert.That(Source(Folder + "/Features/PointOfInterestChurchyards.cs").Contains("PointOfInterestStructureCaps.Tally.Of(context).Room(MausoleumType)"), Is.True, "churchyards");
			Assert.That(Source(Folder + "/Features/PointOfInterestOutskirts.cs").Contains("PointOfInterestStructureCaps.Tally.Of(context).Room(PointOfInterestChurchyards.MausoleumType)"), Is.True, "the cemetery");
			Assert.That(Source(Folder + "/Features/PointOfInterestTrades.cs").Contains("PointOfInterestStructureCaps.TradeCap(kind)"), Is.True, "the trades");

			string caps = Source(Folder + "/PointOfInterestStructureCaps.cs");
			Assert.That(caps.Contains("[InitializeOnLoad]") || caps.Contains("PointOfInterestSiteBuilders.Register"), Is.False,
				"not a site builder of its own: a new one would move every later builder's draws");
			Assert.That(caps.Contains("DeterministicRNG") || caps.Contains("context.Random"), Is.False, "draws nothing");
		}
	}
}
