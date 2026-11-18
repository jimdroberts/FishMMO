#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A trade a shop-house is let to (2026-10-19: "Did we get a carpenter, tailor, leatherworker, fletcher ... and other profession
	/// archetypes?"): what its hanging sign shows, what lies on its stall-board and how its shop is furnished.
	/// </summary>
	/// <remarks>Append-only: the value is the variant of <c>TradeSign</c>, <c>StallGoods</c> and <c>TradeShelf</c>.</remarks>
	public enum StructureTrade
	{
		/// <summary>Shears on the sign; folded bolts of cloth on the board.</summary>
		Tailor,
		/// <summary>A saddle; hides, satchels and belts.</summary>
		Leatherworker,
		/// <summary>A bow and crossed arrows (the fletcher's and bowyer's); quivers, sheaves of arrows and a bow.</summary>
		Fletcher,
		/// <summary>A boot; boots in pairs.</summary>
		Cobbler,
		/// <summary>A ring with its stone (the goldsmith's); a tray of rings, chalices and a casket.</summary>
		Jeweller,
		/// <summary>Three candles; candles standing and in bundles, blocks of tallow.</summary>
		Chandler,
		/// <summary>A compass rose; rolled charts, an open map and a globe.</summary>
		Cartographer,
		/// <summary>A quill in its inkpot; scrolls, books and ink.</summary>
		Scribe,
		/// <summary>A pretzel, the bakers' own sign; loaves and a basket of bread.</summary>
		Baker,
		/// <summary>A barrel (the cooper's and carpenter's); casks, buckets and bowls.</summary>
		Cooper,
		/// <summary>A great key (the ironmonger's and locksmith's); pots, pans and lanterns.</summary>
		Ironmonger,
		/// <summary>A mortar and pestle; jars, bundles of herbs and a mortar.</summary>
		Apothecary,
	}

	/// <summary>
	/// The trades a shop-house is let to (2026-10-19): their hanging signs, their wares on the stall-board and on the shop's
	/// shelves, and the furniture of their workshops behind. A medieval street showed its trades by the sign hung from an iron
	/// bracket over the door and by what was laid out on the board folded down from the shop window; the work was done in the
	/// room behind, where the customer came in. PointOfInterestTrades chooses each shop-house's trade and lays its sign and its
	/// goods at the building's marks; PointOfInterestInteriors furnishes its shop as that trade's room.
	/// </summary>
	public static partial class StructurePieces
	{
		/// <summary>How many trades there are (<see cref="StructureTrade"/>): the variants of the sign, the stall goods and the wares shelf.</summary>
		public const int TradeCount = 12;

		/// <summary>The room a trade's shop is furnished as (the trades with no room of their own take the nearest one).</summary>
		public static StructureRoomKind TradeRoom(StructureTrade trade)
		{
			switch (trade)
			{
				case StructureTrade.Tailor: return StructureRoomKind.Tailor;
				case StructureTrade.Leatherworker: return StructureRoomKind.Leatherworker;
				case StructureTrade.Fletcher: return StructureRoomKind.Fletcher;
				case StructureTrade.Cobbler: return StructureRoomKind.Cobbler;
				case StructureTrade.Jeweller: return StructureRoomKind.Jeweller;
				case StructureTrade.Chandler: return StructureRoomKind.Chandler;
				case StructureTrade.Cartographer: return StructureRoomKind.Cartographer;
				case StructureTrade.Scribe: return StructureRoomKind.Scribe;
				case StructureTrade.Baker: return StructureRoomKind.Bakery;
				case StructureTrade.Cooper: return StructureRoomKind.Workshop;
				default: return StructureRoomKind.Shop;
			}
		}

		private static void AddProfessions(List<StructurePiece> list)
		{
			string[] trades = { "furniture", "tradesign", "tailor", "leather", "fletcher", "cobbler", "jeweller", "chandler", "cartographer", "scribe" };
			// Laid by PointOfInterestTrades at a shop-house's marks, never drawn by a template's query.
			StructurePiece sign = Piece(list, "TradeSign", StructureStyle.Timber, StructureSize.Small,
				"A shop's hanging sign, its fixing at the origin under the bracket's arm (the arm runs along x), its rings 0.38 m either side, the board hanging 0.88×0.62 m in the x-y plane with the trade's emblem standing proud of both faces; one variant per StructureTrade (v0 tailor's shears ... v11 apothecary's mortar).",
				TradeSign, trades);
			sign.Variants = TradeCount;
			sign.Anchor = StructureAnchor.Roof;
			sign.Collides = false;
			StructurePiece goods = Piece(list, "StallGoods", StructureStyle.Timber, StructureSize.Small,
				"A trade's wares laid out on a shop's stall-board, 2.7×0.62 m, the board's top at y = 0; one variant per StructureTrade (v0 bolts of cloth ... v11 jars and herbs).",
				StallGoods, trades);
			goods.Variants = TradeCount;
			goods.Collides = false;
			Piece(list, "TradeShelf", StructureStyle.Timber, StructureSize.Small,
				"Wall shelves, 1.2 m wide and 1.85 high, stocked with a trade's wares; one variant per StructureTrade (v0 cloth ... v11 jars and herbs).",
				TradeShelf, trades).Variants = TradeCount;
			Piece(list, "CuttingTable", StructureStyle.Timber, StructureSize.Small,
				"A tailor's cutting table, 1.8×0.9 m and waist high: v0 a length of cloth laid out with shears and a yardstick, the bolt at its end; v1 cut pieces on top and bolts on a shelf beneath.",
				CuttingTable, "furniture", "tailor");
			Piece(list, "DressForm", StructureStyle.Timber, StructureSize.Small,
				"A tailor's dressed form on a three-legged stand: v0 a buttoned doublet, v1 a gown to the floor.",
				DressForm, "furniture", "tailor");
			Piece(list, "StitchingBench", StructureStyle.Timber, StructureSize.Small,
				"A leatherworker's: v0 a stitching horse (a bench sat astride, its jaws clamping a strap), v1 a bench with a hide spread on it, a mallet and awls, tools on a board behind.",
				StitchingBench, "furniture", "leather");
			Piece(list, "HideRack", StructureStyle.Timber, StructureSize.Small,
				"A leatherworker's: v0 a hide laced into a pole frame leaning on the wall, v1 a saddle on its stand under a rail of harness.",
				HideRack, "furniture", "leather");
			Piece(list, "FletchingBench", StructureStyle.Timber, StructureSize.Small,
				"A fletcher's and bowyer's bench, 1.6×0.65 m: v0 shafts, feathers and the glue pot, v1 a bow drawn on the tillering post behind it and staves leaning by.",
				FletchingBench, "furniture", "fletcher");
			Piece(list, "BowRack", StructureStyle.Timber, StructureSize.Small,
				"A fletcher's stock against the wall: v0 strung bows on a rack, v1 two tubs of arrows.",
				BowRack, "furniture", "fletcher");
			Piece(list, "CobblerBench", StructureStyle.Timber, StructureSize.Small,
				"A cobbler's: v0 the low bench with its tray of lasts and tools, a boot on the lasting post; v1 a rack of boots on sloping shelves.",
				CobblerBench, "furniture", "cobbler");
			// The goldsmith's furnace is a little charcoal fire: an indoor fire's sparks, its light the room's.
			Piece(list, "JewellerBench", StructureStyle.Timber, StructureSize.Small,
				"A jeweller's and goldsmith's: v0 the bench with its cut-out and catch-skin, a small charcoal furnace and bellows at one end; v1 a locked display case of gold under an iron grille, a strongbox beneath.",
				JewellerBench, "furniture", "jeweller");
			Piece(list, "CandleRack", StructureStyle.Timber, StructureSize.Small,
				"A chandler's: v0 a dipping frame of candles hung in pairs by their wicks over a tallow trough, v1 a tallow vat on its stones by a rail of drying candles.",
				CandleRack, "furniture", "chandler");
			Piece(list, "MapTable", StructureStyle.Timber, StructureSize.Small,
				"A cartographer's broad table, 1.8×1 m: v0 a chart weighted with stones, dividers, ink and a candle; v1 a sea chart, an astrolabe and books; rolled charts on the shelf beneath.",
				MapTable, "furniture", "cartographer");
			Piece(list, "ChartPress", StructureStyle.Timber, StructureSize.Small,
				"A cartographer's: v0 a press of flat chart drawers with a globe on it, v1 a great globe in its horizon ring on a floor stand.",
				ChartPress, "furniture", "cartographer");
		}

		// ── The sign ─────────────────────────────────────────────────

		private static void TradeSign(StructureDraft d)
		{
			const float width = 0.88f, top = -0.15f, height = 0.62f, thick = 0.07f;
			float mid = top - height * 0.5f;
			// The two irons it hangs by, each from an eye round the bracket's arm.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.38f, -0.02f, 0f), new Vector3(s * 0.38f, top + 0.02f, 0f), 0.025f, 0.025f, StructureMaterial.Iron, 1 + (s > 0 ? 1 : 0), StructureRole.Detail));
				if (d.Fine)
				{
					d.Add(Wares.Ring(new Vector3(s * 0.38f, -0.03f, 0f), 0.04f, 0.022f, 0.02f, 8, Quaternion.Euler(0f, 0f, 90f), StructureMaterial.Iron, 3 + (s > 0 ? 1 : 0)));
				}
			}
			if (d.Mid)
			{
				// A painted field in a moulded frame.
				d.Add(StructureShapes.Box(new Vector3(0f, mid, 0f), new Vector3(width - 0.1f, height - 0.1f, thick - 0.02f), StructureMaterial.Dyed, 5));
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.025f, 0f), new Vector3(width, 0.05f, thick), StructureMaterial.Timber, 6)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, top - height + 0.025f, 0f), new Vector3(width, 0.05f, thick), StructureMaterial.Timber, 7)).Grain = Vector3.right;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.025f), mid, 0f), new Vector3(0.05f, height - 0.1f, thick), StructureMaterial.Timber, 8 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
				}
			}
			else
			{
				d.Add(StructureShapes.Box(new Vector3(0f, mid, 0f), new Vector3(width, height, thick), StructureMaterial.Timber, 5));
			}
			// The emblem stands proud of both faces, so the sign reads from either way along the street.
			Emblem(d, (StructureTrade)d.Variant, new Vector3(0f, mid, 0f), 0.11f);
		}

		/// <summary>A trade's emblem in the x-y plane about <paramref name="c"/>, about 0.45 m across, <paramref name="t"/> thick along z.</summary>
		private static void Emblem(StructureDraft d, StructureTrade trade, Vector3 c, float t)
		{
			const int k = 20;
			switch (trade)
			{
				case StructureTrade.Tailor:
				{
					// Open shears: two tapering blades crossed at the rivet, their bows below.
					d.Add(Wares.Taper(c, new Vector2(-0.05f, -0.07f), new Vector2(0.19f, 0.2f), 0.06f, 0.012f, t, StructureMaterial.Iron, k));
					d.Add(Wares.Taper(c, new Vector2(0.05f, -0.07f), new Vector2(-0.19f, 0.2f), 0.06f, 0.012f, t, StructureMaterial.Iron, k + 1));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(Wares.Stroke(c, new Vector2(0f, -0.06f), new Vector2(s * 0.09f, -0.15f), 0.028f, t, StructureMaterial.Iron, k + 2 + (s > 0 ? 1 : 0)));
						d.Add(Wares.Ring(c + new Vector3(s * 0.12f, -0.19f, 0f), 0.065f, 0.04f, t, d.Seg(10, 6), Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Iron, k + 4 + (s > 0 ? 1 : 0)));
					}
					if (d.Fine)
					{
						d.Add(Wares.Disc(c + new Vector3(0f, -0.04f, 0f), 0.02f, t + 0.02f, 6, StructureMaterial.Iron, k + 6));
					}
					break;
				}
				case StructureTrade.Leatherworker:
				{
					// A saddle from the side, its pommel to +x: the seat, cantle and pommel, the skirt, a stirrup on its leather.
					d.Add(Wares.Stroke(c, new Vector2(-0.15f, 0.0f), new Vector2(0.13f, 0.0f), 0.05f, t, StructureMaterial.Canvas, k));
					d.Add(Wares.Stroke(c, new Vector2(-0.16f, -0.01f), new Vector2(-0.21f, 0.11f), 0.05f, t, StructureMaterial.Canvas, k + 1));
					d.Add(Wares.Stroke(c, new Vector2(0.12f, -0.01f), new Vector2(0.18f, 0.09f), 0.05f, t, StructureMaterial.Canvas, k + 2));
					d.Add(Wares.Shape(c, new[] { new Vector2(-0.18f, -0.02f), new Vector2(-0.13f, -0.17f), new Vector2(0.12f, -0.17f), new Vector2(0.17f, -0.02f) }, t * 0.8f, StructureMaterial.Canvas, k + 3));
					if (d.Mid)
					{
						d.Add(Wares.Stroke(c, new Vector2(0.02f, -0.12f), new Vector2(0.02f, -0.22f), 0.02f, t + 0.01f, StructureMaterial.Canvas, k + 4));
						d.Add(Wares.Ring(c + new Vector3(0.02f, -0.25f, 0f), 0.045f, 0.028f, t + 0.01f, 4, Quaternion.Euler(90f, 0f, 45f), StructureMaterial.Iron, k + 5));
					}
					if (d.Fine)
					{
						d.Add(Wares.Stroke(c, new Vector2(-0.16f, 0.03f), new Vector2(0.14f, 0.03f), 0.012f, t + 0.012f, StructureMaterial.Timber, k + 6));
					}
					break;
				}
				case StructureTrade.Fletcher:
				{
					// A strung bow on the left, two arrows crossed over it.
					int bends = d.Mid ? 4 : 2;
					Vector2 last = Vector2.zero;
					for (int i = 0; i <= bends; i++)
					{
						float s = -1f + 2f * i / bends;
						var p = new Vector2(-0.07f - 0.12f * (1f - s * s), 0.21f * s);
						if (i > 0)
						{
							d.Add(Wares.Stroke(c, last, p, 0.03f, t, StructureMaterial.Timber, k + i));
						}
						last = p;
					}
					d.Add(Wares.Stroke(c, new Vector2(-0.07f, -0.21f), new Vector2(-0.07f, 0.21f), 0.008f, t * 0.5f, StructureMaterial.Canvas, k + 6));
					for (int s = -1; s <= 1; s += 2)
					{
						var tail = new Vector2(-s * 0.18f, -0.19f);
						var head = new Vector2(s * 0.15f, 0.16f);
						d.Add(Wares.Stroke(c, tail, head, 0.018f, t + 0.01f, StructureMaterial.Timber, k + 8 + (s > 0 ? 1 : 0)));
						Vector2 dir = (head - tail).normalized, n = new Vector2(-dir.y, dir.x);
						d.Add(Wares.Shape(c, new[] { head - n * 0.035f, head + dir * 0.08f, head + n * 0.035f }, t + 0.016f, StructureMaterial.Iron, k + 10 + (s > 0 ? 1 : 0)));
						if (d.Mid)
						{
							Vector2 f = tail + dir * 0.03f;
							d.Add(Wares.Shape(c, new[] { f, f + dir * 0.09f, f + n * 0.04f }, t + 0.014f, StructureMaterial.Dyed, k + 12 + (s > 0 ? 1 : 0)));
							d.Add(Wares.Shape(c, new[] { f, f - n * 0.04f, f + dir * 0.09f }, t + 0.014f, StructureMaterial.Dyed, k + 14 + (s > 0 ? 1 : 0)));
						}
					}
					break;
				}
				case StructureTrade.Cobbler:
				{
					// A riding boot, its toe to +x.
					d.Add(Wares.Shape(c, new[] { new Vector2(-0.12f, -0.05f), new Vector2(0.03f, -0.05f), new Vector2(0.04f, 0.19f), new Vector2(-0.11f, 0.19f) }, t, StructureMaterial.Canvas, k));
					d.Add(Wares.Shape(c, new[] { new Vector2(-0.12f, -0.18f), new Vector2(0.14f, -0.18f), new Vector2(0.2f, -0.14f), new Vector2(0.19f, -0.09f), new Vector2(0.1f, -0.04f), new Vector2(-0.12f, -0.04f) },
						t, StructureMaterial.Canvas, k + 1));
					d.Add(Wares.Stroke(c, new Vector2(-0.13f, -0.19f), new Vector2(0.19f, -0.19f), 0.025f, t + 0.01f, StructureMaterial.Timber, k + 2));
					if (d.Mid)
					{
						// The turned-down cuff and the heel.
						d.Add(Wares.Stroke(c, new Vector2(-0.13f, 0.19f), new Vector2(0.05f, 0.19f), 0.05f, t + 0.01f, StructureMaterial.Canvas, k + 3));
						d.Add(Wares.Stroke(c, new Vector2(-0.12f, -0.215f), new Vector2(-0.05f, -0.215f), 0.03f, t + 0.01f, StructureMaterial.Timber, k + 4));
					}
					break;
				}
				case StructureTrade.Jeweller:
				{
					// A ring and its stone in a raised setting.
					d.Add(Wares.Ring(c + new Vector3(0f, -0.06f, 0f), 0.15f, 0.11f, t, d.Seg(16, 8), Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Iron, k));
					d.Add(StructureShapes.Box(c + new Vector3(0f, 0.1f, 0f), new Vector3(0.08f, 0.04f, t), StructureMaterial.Iron, k + 1, StructureRole.Detail));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.05f), new Vector2(0.07f, 0f), new Vector2(0.04f, 0.04f) }, d.Seg(8, 4), StructureMaterial.Dyed, k + 2, false, 0f, false, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(c + new Vector3(0f, 0.17f, 0f))));
					break;
				}
				case StructureTrade.Chandler:
				{
					// Three candles in a holder, the middle one taller.
					d.Add(Wares.Stroke(c, new Vector2(-0.2f, -0.19f), new Vector2(0.2f, -0.19f), 0.04f, t, StructureMaterial.Iron, k));
					for (int i = -1; i <= 1; i++)
					{
						float h = i == 0 ? 0.3f : 0.23f;
						var foot = c + new Vector3(i * 0.13f, -0.17f, 0f);
						d.Add(StructureShapes.Cylinder(0.05f, 0f, h, d.Seg(8, 5), StructureMaterial.Canvas, k + 2 + i, false, StructureRole.Detail).Transform(Matrix4x4.Translate(foot)));
						if (d.Mid)
						{
							d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.03f), new Vector2(0f, 0.09f) }, 5, StructureMaterial.Thatch, k + 5 + i, false, 0f, false, false, StructureRole.Detail)
								.Transform(Matrix4x4.Translate(foot + Vector3.up * (h + 0.005f))));
						}
					}
					break;
				}
				case StructureTrade.Cartographer:
				{
					// A compass rose: four long points, four short, on a ring.
					int points = d.Mid ? 16 : 8;
					var star = new Vector2[points];
					for (int i = 0; i < points; i++)
					{
						float a = i * Mathf.PI * 2f / points;
						int step = 16 / points;
						int at = i * step;
						float r = at % 4 == 0 ? 0.22f : at % 2 == 0 ? 0.13f : 0.045f;
						star[i] = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
					}
					d.Add(Wares.Shape(c, star, t, StructureMaterial.Iron, k));
					d.Add(Wares.Ring(c, 0.16f, 0.13f, t * 0.8f, d.Seg(16, 8), Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Canvas, k + 1));
					if (d.Fine)
					{
						d.Add(Wares.Disc(c, 0.03f, t + 0.02f, 8, StructureMaterial.Dyed, k + 2));
					}
					break;
				}
				case StructureTrade.Scribe:
				{
					// A quill in its inkpot: the feather a lens leaning out to the upper right.
					var nib = new Vector2(-0.1f, -0.1f);
					Vector2 dir = new Vector2(0.62f, 0.78f).normalized, n = new Vector2(-dir.y, dir.x);
					d.Add(Wares.Stroke(c, nib, nib + dir * 0.14f, 0.014f, t * 0.6f, StructureMaterial.Canvas, k));
					int half = d.Mid ? 5 : 3;
					var lens = new Vector2[half * 2];
					for (int i = 0; i < half; i++)
					{
						float u = (i + 0.5f) / half;
						float w = 0.05f * Mathf.Sin(u * Mathf.PI);
						Vector2 along = nib + dir * (0.1f + 0.3f * u);
						lens[i] = along - n * w;
						lens[half * 2 - 1 - i] = along + n * w;
					}
					d.Add(Wares.Shape(c, lens, t, StructureMaterial.Canvas, k + 1));
					d.Add(StructureShapes.Cylinder(0.08f, 0f, 0.1f, d.Seg(10, 6), StructureMaterial.Iron, k + 2, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c + new Vector3(-0.1f, -0.22f, 0f))));
					if (d.Mid)
					{
						d.Add(StructureShapes.Cylinder(0.045f, 0.1f, 0.13f, d.Seg(8, 5), StructureMaterial.Iron, k + 3, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c + new Vector3(-0.1f, -0.22f, 0f))));
					}
					break;
				}
				case StructureTrade.Baker:
				{
					// A pretzel: its loop and the two arms crossed over its middle.
					// Its dough rope is rounder front to back than across, so it stands well proud of the board.
					var plump = Matrix4x4.Translate(c) * Matrix4x4.Scale(new Vector3(1.2f, 0.88f, 1.6f)) * Matrix4x4.Translate(-c);
					d.Add(Wares.Torus(c + new Vector3(0f, -0.02f, 0f), 0.15f, 0.04f, d.Seg(14, 8), d.Fine ? 6 : 4, Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Timber, k).Transform(plump));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Rod(c + new Vector3(-s * 0.06f, 0.115f, 0f), c + new Vector3(s * 0.1f, -0.135f, 0f), 0.034f, d.Seg(6, 4), StructureMaterial.Timber, k + 2 + (s > 0 ? 1 : 0), StructureRole.Detail).Transform(plump));
					}
					break;
				}
				case StructureTrade.Cooper:
				{
					// A barrel on its side, hooped.
					Matrix4x4 lie = Matrix4x4.TRS(c, Quaternion.Euler(0f, 0f, 90f), Vector3.one);
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.1f, -0.17f), new Vector2(0.13f, 0f), new Vector2(0.1f, 0.17f) }, d.Seg(10, 6), StructureMaterial.Timber, k, true, 0f, false, true, StructureRole.Detail).Transform(lie));
					if (d.Mid)
					{
						for (int s = -1; s <= 1; s += 2)
						{
							d.Add(StructureShapes.Cylinder(0.118f, s * 0.12f - 0.0125f, s * 0.12f + 0.0125f, d.Seg(10, 6), StructureMaterial.Iron, k + 1 + (s > 0 ? 1 : 0), false, StructureRole.Detail).Transform(lie));
						}
					}
					break;
				}
				case StructureTrade.Ironmonger:
				{
					// A great key, its bow to the upper left.
					d.Add(Wares.Ring(c + new Vector3(-0.13f, 0.11f, 0f), 0.08f, 0.045f, t, d.Seg(10, 6), Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Iron, k));
					d.Add(Wares.Stroke(c, new Vector2(-0.075f, 0.055f), new Vector2(0.16f, -0.18f), 0.032f, t, StructureMaterial.Iron, k + 1));
					var n = new Vector2(0.7071f, 0.7071f);
					d.Add(Wares.Stroke(c, new Vector2(0.1f, -0.12f), new Vector2(0.1f, -0.12f) + n * 0.08f, 0.032f, t, StructureMaterial.Iron, k + 2));
					if (d.Mid)
					{
						d.Add(Wares.Stroke(c, new Vector2(0.145f, -0.165f), new Vector2(0.145f, -0.165f) + n * 0.06f, 0.032f, t, StructureMaterial.Iron, k + 3));
					}
					break;
				}
				default:
				{
					// The apothecary's mortar, its pestle standing in it.
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.08f, 0f), new Vector2(0.13f, 0.06f), new Vector2(0.14f, 0.14f) }, d.Seg(10, 6), StructureMaterial.Monolith, k, true, 0f, false, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(c + new Vector3(0f, -0.25f, 0f))));
					d.Add(StructureShapes.Rod(c + new Vector3(-0.02f, -0.14f, 0f), c + new Vector3(0.13f, 0.2f, 0f), 0.03f, d.Seg(6, 4), StructureMaterial.Timber, k + 1, StructureRole.Detail));
					break;
				}
			}
		}

		// ── Goods ────────────────────────────────────────────────────

		private static void StallGoods(StructureDraft d)
		{
			Wares.Goods(d, (StructureTrade)d.Variant, Vector3.zero, Wares.StallWidth, Wares.StallDepth, 0.6f, 100, 540, 0);
		}

		private static void TradeShelf(StructureDraft d)
		{
			const float width = 1.2f, depth = 0.36f, height = 1.85f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.03f), height * 0.5f, 0f), new Vector3(0.05f, height, depth), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
			}
			d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, depth * 0.5f - 0.015f), new Vector3(width, height, 0.03f), StructureMaterial.Timber, 3)).Grain = Vector3.up;
			float[] boards = { 0.05f, 0.5f, 0.95f, 1.4f, 1.83f };
			for (int i = 0; i < boards.Length; i++)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, boards[i], -0.01f), new Vector3(width - 0.06f, 0.03f, depth - 0.02f), StructureMaterial.Timber, 10 + i)).Grain = Vector3.right;
			}
			var trade = (StructureTrade)d.Variant;
			// The wares on the four lower boards, from what the frame leaves of the budget, no board taking much more than its share.
			int left = 480;
			for (int i = 0; i < boards.Length - 1; i++)
			{
				left -= Wares.Goods(d, trade, new Vector3(0f, boards[i] + 0.015f, -0.02f), width - 0.12f, depth - 0.06f, boards[i + 1] - boards[i] - 0.06f, 200 + 40 * i, Mathf.Min(left, 170), 1);
			}
		}

		// ── Trade furniture ──────────────────────────────────────────

		private static void CuttingTable(StructureDraft d)
		{
			const float top = 0.9f;
			Wares.Table(d, 1.8f, 0.9f, top, 1);
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Box(new Vector3(-0.1f, top + 0.006f, 0f), new Vector3(1.3f, 0.012f, 0.72f), StructureMaterial.Dyed, 20, StructureRole.Detail));
				d.Add(StructureShapes.Rod(new Vector3(0.68f, top + 0.08f, -0.36f), new Vector3(0.68f, top + 0.08f, 0.36f), 0.08f, d.Seg(10, 6), StructureMaterial.Dyed, 21, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(-0.1f, top + 0.018f, -0.3f), new Vector3(1f, 0.012f, 0.035f), StructureMaterial.Timber, 22, StructureRole.Detail));
				}
				if (d.Fine)
				{
					// Shears open on the cloth, and a cake of chalk.
					d.Add(StructureShapes.Block(new Vector3(-0.4f, top + 0.02f, 0.1f), new Vector3(0.26f, 0.01f, 0.03f), Quaternion.Euler(0f, 20f, 0f), StructureMaterial.Iron, 23, StructureRole.Detail));
					d.Add(StructureShapes.Block(new Vector3(-0.4f, top + 0.03f, 0.1f), new Vector3(0.26f, 0.01f, 0.03f), Quaternion.Euler(0f, -20f, 0f), StructureMaterial.Iron, 24, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0.2f, top + 0.022f, 0.2f), new Vector3(0.06f, 0.02f, 0.04f), StructureMaterial.Canvas, 25, StructureRole.Detail));
				}
				return;
			}
			// Bolts on a shelf between the legs, the pieces cut on top.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.22f, 0f), new Vector3(1.6f, 0.03f, 0.75f), StructureMaterial.Timber, 20)).Grain = Vector3.right;
			Wares.Goods(d, StructureTrade.Tailor, new Vector3(0f, 0.235f, 0f), 1.5f, 0.66f, 0.55f, 300, 220, 1);
			if (d.Mid)
			{
				for (int i = 0; i < 3; i++)
				{
					d.Add(StructureShapes.Block(new Vector3(-0.5f + i * 0.45f, top + 0.006f, d.Range(30 + i, 1, -0.15f, 0.15f)), new Vector3(0.36f, 0.012f, 0.28f),
						Quaternion.Euler(0f, d.Range(30 + i, 2, -25f, 25f), 0f), i == 1 ? StructureMaterial.Canvas : StructureMaterial.Dyed, 30 + i, StructureRole.Detail));
				}
			}
		}

		private static void DressForm(StructureDraft d)
		{
			bool gown = d.Variant == 1;
			// The stand: three feet and a pole up into the form.
			for (int i = 0; i < 3; i++)
			{
				float a = (90f + i * 120f) * Mathf.Deg2Rad;
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.28f, 0f), new Vector3(Mathf.Cos(a) * 0.27f, 0.025f, Mathf.Sin(a) * 0.27f), 0.04f, 0.04f, StructureMaterial.Timber, 1 + i));
			}
			d.Add(StructureShapes.Cylinder(0.025f, 0.25f, 1f, d.Seg(6, 4), StructureMaterial.Timber, 4, true));
			// The form: a linen torso, flattened front to back.
			var flat = Matrix4x4.Scale(new Vector3(1f, 1f, 0.72f));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.11f, 0.98f), new Vector2(0.15f, 1.1f), new Vector2(0.13f, 1.22f), new Vector2(0.17f, 1.38f), new Vector2(0.15f, 1.48f), new Vector2(0.06f, 1.55f) },
				d.Seg(12, 6), StructureMaterial.Canvas, 5).Transform(flat));
			d.Add(StructureShapes.Cylinder(0.035f, 1.55f, 1.66f, d.Seg(6, 4), StructureMaterial.Timber, 6));
			if (d.Mid)
			{
				d.Add(StructureShapes.Sphere(new Vector3(0f, 1.68f, 0f), new Vector3(0.045f, 0.03f, 0.045f), 6, 3, StructureMaterial.Timber, 7, StructureRole.Detail));
			}
			if (!gown)
			{
				// A doublet buttoned down its front (−z).
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.155f, 1.0f), new Vector2(0.165f, 1.1f), new Vector2(0.145f, 1.22f), new Vector2(0.18f, 1.36f), new Vector2(0.165f, 1.47f) },
					d.Seg(12, 6), StructureMaterial.Dyed, 8, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Scale(new Vector3(1f, 1f, 0.74f))));
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Rod(new Vector3(s * 0.15f, 1.43f, 0f), new Vector3(s * 0.22f, 1.3f, 0f), 0.05f, d.Seg(6, 4), StructureMaterial.Dyed, 9 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
				if (d.Fine)
				{
					for (int i = 0; i < 5; i++)
					{
						float y = 1.05f + i * 0.09f;
						float r = y < 1.22f ? Mathf.Lerp(0.165f, 0.145f, (y - 1.1f) / 0.12f) : Mathf.Lerp(0.145f, 0.18f, (y - 1.22f) / 0.14f);
						d.Add(StructureShapes.Box(new Vector3(0f, y, -r * 0.74f - 0.006f), new Vector3(0.018f, 0.018f, 0.012f), StructureMaterial.Iron, 12 + i, StructureRole.Detail));
					}
				}
				return;
			}
			// A gown: its bodice and a skirt to the floor over the stand.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, 1.05f), new Vector2(0.145f, 1.2f), new Vector2(0.18f, 1.36f), new Vector2(0.162f, 1.46f) },
				d.Seg(12, 6), StructureMaterial.Dyed, 8, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Scale(new Vector3(1f, 1f, 0.74f))));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.3f, 0.04f), new Vector2(0.26f, 0.35f), new Vector2(0.2f, 0.75f), new Vector2(0.15f, 1.1f) },
				d.Seg(14, 8), StructureMaterial.Dyed, 9));
			if (d.Fine)
			{
				d.Add(StructureShapes.Cylinder(0.158f, 1.08f, 1.12f, 12, StructureMaterial.Canvas, 10, false, StructureRole.Detail));
			}
		}

		private static void StitchingBench(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// A stitching horse: a bench sat astride, the jaws at its front end clamping a strap for the two needles.
				const float seat = 0.48f;
				d.Add(StructureShapes.Box(new Vector3(0f, seat, 0f), new Vector3(1.3f, 0.07f, 0.32f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(sx * 0.5f, seat - 0.03f, sz * 0.1f), new Vector3(sx * 0.58f, 0f, sz * 0.2f), 0.06f, 0.06f, StructureMaterial.Timber, 2 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
					}
				}
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(new Vector3(0.42f, seat + 0.3f, s * 0.045f), new Vector3(0.12f, 0.6f, 0.05f), Quaternion.Euler(0f, 0f, -12f), StructureMaterial.Timber, 6 + (s > 0 ? 1 : 0)));
				}
				if (d.Mid)
				{
					d.Add(StructureShapes.Block(new Vector3(0.36f, seat + 0.62f, 0f), new Vector3(0.05f, 0.3f, 0.012f), Quaternion.Euler(0f, 0f, -12f), StructureMaterial.Canvas, 8, StructureRole.Detail));
					// A hide folded on the bench's back end.
					d.Add(StructureShapes.Box(new Vector3(-0.4f, seat + 0.05f, 0f), new Vector3(0.38f, 0.03f, 0.3f), StructureMaterial.Canvas, 9, StructureRole.Detail));
				}
				if (d.Fine)
				{
					d.Add(StructureShapes.Rod(new Vector3(-0.1f, seat + 0.04f, 0.08f), new Vector3(0.08f, seat + 0.04f, 0.1f), 0.012f, 5, StructureMaterial.Timber, 10, StructureRole.Detail, 0.04f));
					d.Add(Wares.Ring(new Vector3(-0.05f, seat + 0.05f, -0.08f), 0.06f, 0.035f, 0.025f, 8, Quaternion.identity, StructureMaterial.Canvas, 11));
				}
				return;
			}
			const float top = 0.85f;
			Wares.Table(d, 1.6f, 0.65f, top, 1);
			// A hide spread on the bench, flat.
			var hide = new Vector2[d.Mid ? 10 : 6];
			for (int i = 0; i < hide.Length; i++)
			{
				float a = i * Mathf.PI * 2f / hide.Length;
				float r = d.Range(40 + i, 1, 0.85f, 1f);
				hide[i] = new Vector2(Mathf.Cos(a) * 0.48f * r, Mathf.Sin(a) * 0.26f * r);
			}
			d.Add(StructureShapes.Prism(hide, -0.006f, 0.006f, StructureMaterial.Canvas, 20, StructureRole.Detail).Transform(Matrix4x4.TRS(new Vector3(-0.15f, top + 0.006f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			if (d.Mid)
			{
				// A mallet, and the tool board on the wall behind.
				d.Add(StructureShapes.Rod(new Vector3(0.5f, top + 0.05f, -0.1f), new Vector3(0.62f, top + 0.05f, -0.1f), 0.05f, d.Seg(8, 5), StructureMaterial.Timber, 21, StructureRole.Detail));
				d.Add(StructureShapes.Rod(new Vector3(0.56f, top + 0.05f, -0.1f), new Vector3(0.56f, top + 0.05f, -0.32f), 0.012f, 4, StructureMaterial.Timber, 22, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.4f, 0.31f), new Vector3(1.4f, 0.45f, 0.025f), StructureMaterial.Timber, 23)).Grain = Vector3.right;
			}
			if (d.Fine)
			{
				for (int i = 0; i < 5; i++)
				{
					float x = -0.5f + i * 0.25f;
					d.Add(StructureShapes.Box(new Vector3(x, top + 0.4f, 0.29f), new Vector3(0.025f, 0.22f, 0.02f), i % 2 == 0 ? StructureMaterial.Iron : StructureMaterial.Timber, 30 + i, StructureRole.Detail));
				}
				// A round knife's half-moon blade on the hide.
				var moon = new Vector2[6];
				for (int i = 0; i < 6; i++)
				{
					float a = Mathf.PI * i / 5f;
					moon[i] = new Vector2(Mathf.Cos(a) * 0.07f, Mathf.Sin(a) * 0.05f);
				}
				d.Add(StructureShapes.Prism(moon, -0.004f, 0.004f, StructureMaterial.Iron, 36, StructureRole.Detail).Transform(Matrix4x4.TRS(new Vector3(0.1f, top + 0.018f, 0.05f), Quaternion.Euler(90f, 30f, 0f), Vector3.one)));
			}
		}

		private static void HideRack(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// A hide laced into a frame of poles, leaned back against the wall (+z).
				int first = d.Solids.Count;
				for (int s = -1; s <= 1; s += 2)
				{
					Log(d, new Vector3(s * 0.6f, 0f, 0f), new Vector3(s * 0.6f, 1.95f, 0f), 0.04f, 1 + (s > 0 ? 1 : 0));
				}
				Log(d, new Vector3(-0.68f, 0.25f, 0f), new Vector3(0.68f, 0.25f, 0f), 0.035f, 3);
				Log(d, new Vector3(-0.68f, 1.78f, 0f), new Vector3(0.68f, 1.78f, 0f), 0.035f, 4);
				int n = d.Mid ? 12 : 8;
				var outline = new Vector2[n];
				for (int i = 0; i < n; i++)
				{
					float a = i * Mathf.PI * 2f / n;
					float r = d.Range(10 + i, 1, 0.82f, 1f);
					outline[i] = new Vector2(Mathf.Cos(a) * 0.48f * r, 1.02f + Mathf.Sin(a) * 0.66f * r);
				}
				d.Add(StructureShapes.Prism(outline, -0.012f, 0.012f, StructureMaterial.Canvas, 5));
				if (d.Fine)
				{
					// The lacing from the hide's edge to the frame.
					for (int i = 0; i < n; i++)
					{
						Vector2 p = outline[i];
						Vector2 to = Mathf.Abs(p.x) > 0.3f ? new Vector2(Mathf.Sign(p.x) * 0.6f, p.y) : new Vector2(p.x, p.y > 1f ? 1.78f : 0.25f);
						d.Add(StructureShapes.Beam(new Vector3(p.x, p.y, 0f), new Vector3(to.x, to.y, 0f), 0.008f, 0.008f, StructureMaterial.Canvas, 20 + i, StructureRole.Detail));
					}
				}
				// Leaned 8 degrees, centred on its footprint.
				float lean = 8f;
				float shift = 1.95f * Mathf.Sin(lean * Mathf.Deg2Rad) * 0.5f;
				TransformSince(d, first, Matrix4x4.Translate(new Vector3(0f, 0f, -shift)) * Matrix4x4.Rotate(Quaternion.Euler(lean, 0f, 0f)));
				return;
			}
			// A saddle on a trestle stand, the harness on a rail behind.
			const float beam = 0.85f;
			d.Add(StructureShapes.Box(new Vector3(0f, beam, -0.02f), new Vector3(0.9f, 0.1f, 0.12f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * 0.38f, beam - 0.04f, -0.02f), new Vector3(sx * 0.44f, 0f, -0.02f + sz * 0.2f), 0.06f, 0.06f, StructureMaterial.Timber, 2 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, beam + 0.08f, -0.02f), new Vector3(0.55f, 0.06f, 0.3f), StructureMaterial.Canvas, 6));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Block(new Vector3(0f, beam - 0.08f, -0.02f + s * 0.17f), new Vector3(0.42f, 0.34f, 0.025f), Quaternion.Euler(s * 12f, 0f, 0f), StructureMaterial.Canvas, 7 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Block(new Vector3(0.25f, beam + 0.15f, -0.02f), new Vector3(0.07f, 0.12f, 0.2f), Quaternion.Euler(0f, 0f, -20f), StructureMaterial.Canvas, 9));
			d.Add(StructureShapes.Block(new Vector3(-0.27f, beam + 0.17f, -0.02f), new Vector3(0.05f, 0.16f, 0.28f), Quaternion.Euler(0f, 0f, 25f), StructureMaterial.Canvas, 10));
			// The rail of pegs on the wall behind, harness hanging from it.
			d.Add(StructureShapes.Box(new Vector3(0f, 1.6f, 0.2f), new Vector3(1.3f, 0.08f, 0.05f), StructureMaterial.Timber, 11)).Grain = Vector3.right;
			if (d.Mid)
			{
				for (int i = 0; i < 3; i++)
				{
					float x = -0.45f + i * 0.45f;
					d.Add(StructureShapes.Box(new Vector3(x, 1.28f, 0.16f), new Vector3(0.05f, 0.62f, 0.012f), StructureMaterial.Canvas, 12 + i, StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0f, beam - 0.2f, -0.02f + s * 0.2f), new Vector3(0f, 0.5f, -0.02f + s * 0.22f), 0.025f, 0.006f, StructureMaterial.Canvas, 16 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(Wares.Ring(new Vector3(0f, 0.46f, -0.02f + s * 0.22f), 0.05f, 0.032f, 0.02f, 4, Quaternion.Euler(0f, 0f, 45f) * Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Iron, 18 + (s > 0 ? 1 : 0)));
				}
			}
		}

		private static void FletchingBench(StructureDraft d)
		{
			const float top = 0.82f;
			Wares.Table(d, 1.6f, 0.65f, top, 1);
			if (d.Variant == 0)
			{
				// A tied bundle of shafts, feathers sorted for the fletching, the glue pot.
				d.Add(StructureShapes.Rod(new Vector3(-0.7f, top + 0.05f, 0.12f), new Vector3(0.1f, top + 0.05f, 0.12f), 0.05f, d.Seg(8, 5), StructureMaterial.Timber, 20, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.05f, top, top + 0.08f, d.Seg(8, 5), StructureMaterial.Iron, 21, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.6f, 0f, 0.15f))));
				if (d.Mid)
				{
					for (int i = 0; i < 4; i++)
					{
						d.Add(StructureShapes.Block(new Vector3(0.3f + (i % 2) * 0.12f, top + 0.01f, -0.12f + (i / 2) * 0.1f), new Vector3(0.1f, 0.02f, 0.06f), Quaternion.Euler(0f, d.Range(22 + i, 1, -30f, 30f), 0f),
							i % 2 == 0 ? StructureMaterial.Canvas : StructureMaterial.Dyed, 22 + i, StructureRole.Detail));
					}
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Cylinder(0.055f, top + 0.03f, top + 0.07f, d.Seg(8, 5), StructureMaterial.Canvas, 26 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
							.Transform(Matrix4x4.TRS(new Vector3(-0.3f + s * 0.4f, 0f, 0.12f), Quaternion.identity, Vector3.one)));
					}
				}
				if (d.Fine)
				{
					for (int i = 0; i < 5; i++)
					{
						float z = -0.2f + i * 0.04f;
						d.Add(StructureShapes.Rod(new Vector3(-0.6f, top + 0.008f, z), new Vector3(0.12f, top + 0.008f, z + d.Range(30 + i, 1, -0.03f, 0.03f)), 0.007f, 3, StructureMaterial.Timber, 30 + i, StructureRole.Detail));
					}
				}
				return;
			}
			// The bowyer's: the tillering post at the back of the bench, a bow drawn on it; staves leaning on the wall.
			d.Add(StructureShapes.Beam(new Vector3(0.55f, 0f, 0.27f), new Vector3(0.55f, 1.95f, 0.27f), 0.1f, 0.1f, StructureMaterial.Timber, 20));
			Vector2 last = Vector2.zero;
			int bends = d.Mid ? 4 : 2;
			for (int i = 0; i <= bends; i++)
			{
				float s = -1f + 2f * i / bends;
				var p = new Vector2(0.55f + 0.7f * s * 0.5f, 1.85f - 0.16f * (1f - s * s));
				if (i > 0)
				{
					d.Add(StructureShapes.Beam(new Vector3(last.x, last.y, 0.2f), new Vector3(p.x, p.y, 0.2f), 0.03f, 0.04f, StructureMaterial.Timber, 21 + i, StructureRole.Detail));
				}
				last = p;
			}
			if (d.Mid)
			{
				// The string drawn down to a notch in the post.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0.55f + s * 0.35f, 1.85f, 0.2f), new Vector3(0.55f, 1.45f, 0.2f), 0.008f, 0.008f, StructureMaterial.Canvas, 27 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			for (int i = 0; i < 3; i++)
			{
				float x = -0.72f + i * 0.07f;
				d.Add(StructureShapes.Beam(new Vector3(x, 0f, -0.05f + i * 0.04f), new Vector3(x + 0.02f, 1.85f, 0.28f), 0.04f, 0.035f, StructureMaterial.Timber, 30 + i));
			}
			if (d.Fine)
			{
				// Shavings and a drawknife on the bench.
				for (int i = 0; i < 4; i++)
				{
					d.Add(StructureShapes.Block(new Vector3(-0.1f + i * 0.1f, top + 0.006f, d.Range(40 + i, 1, -0.15f, 0.1f)), new Vector3(0.08f, 0.012f, 0.025f), Quaternion.Euler(0f, d.Range(40 + i, 2, 0f, 180f), 0f),
						StructureMaterial.Timber, 40 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Box(new Vector3(0.1f, top + 0.012f, -0.15f), new Vector3(0.3f, 0.015f, 0.04f), StructureMaterial.Iron, 45, StructureRole.Detail));
			}
		}

		private static void BowRack(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// Strung bows hung on a rack against the wall.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * 0.62f, 0.95f, 0.12f), new Vector3(0.08f, 1.9f, 0.08f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
				}
				foreach ((float y, int key) in new[] { (0.35f, 3), (1.0f, 4), (1.75f, 5) })
				{
					d.Add(StructureShapes.Box(new Vector3(0f, y, 0.12f), new Vector3(1.32f, 0.06f, 0.05f), StructureMaterial.Timber, key)).Grain = Vector3.right;
				}
				int bends = d.Mid ? 4 : 2;
				for (int b = 0; b < 4; b++)
				{
					float x = -0.42f + b * 0.28f;
					Vector2 last = Vector2.zero;
					for (int i = 0; i <= bends; i++)
					{
						float s = -1f + 2f * i / bends;
						var p = new Vector2(x - 0.1f * (1f - s * s), 1.05f + 0.72f * s);
						if (i > 0)
						{
							d.Add(StructureShapes.Beam(new Vector3(last.x, last.y, 0.05f), new Vector3(p.x, p.y, 0.05f), 0.03f, 0.035f, StructureMaterial.Timber, 10 + b * 6 + i));
						}
						last = p;
					}
					d.Add(StructureShapes.Beam(new Vector3(x, 0.33f, 0.05f), new Vector3(x, 1.77f, 0.05f), 0.008f, 0.008f, StructureMaterial.Canvas, 40 + b, StructureRole.Detail));
				}
				return;
			}
			// Two tubs of arrows, their fletchings up.
			for (int s = -1; s <= 1; s += 2)
			{
				var at = new Vector3(s * 0.32f, 0f, 0f);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.18f, 0f), new Vector2(0.22f, 0.28f), new Vector2(0.2f, 0.55f) }, d.Seg(12, 6), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0), true, 0f, false, true)
					.Transform(Matrix4x4.Translate(at)));
				if (d.Mid)
				{
					d.Add(StructureShapes.Cylinder(0.215f, 0.4f, 0.43f, d.Seg(12, 6), StructureMaterial.Iron, 3 + (s > 0 ? 1 : 0), false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
				}
				int arrows = d.Fine ? 9 : d.Mid ? 5 : 2;
				for (int i = 0; i < arrows; i++)
				{
					float a = i * 2.39996f, r = 0.15f * Mathf.Sqrt((i + 0.5f) / arrows);
					var foot = at + new Vector3(Mathf.Cos(a) * r, 0.5f, Mathf.Sin(a) * r);
					Vector3 tip = foot + new Vector3(Mathf.Cos(a) * r * 0.5f, 0.42f, Mathf.Sin(a) * r * 0.5f);
					int key = 10 + (s > 0 ? 20 : 0) + i;
					d.Add(StructureShapes.Rod(foot, tip, 0.008f, 3, StructureMaterial.Timber, key, StructureRole.Detail));
					if (d.Fine)
					{
						d.Add(StructureShapes.Block(tip - (tip - foot).normalized * 0.06f, new Vector3(0.035f, 0.1f, 0.006f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), StructureMaterial.Dyed, key + 50, StructureRole.Detail));
					}
				}
			}
		}

		private static void CobblerBench(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// The low bench: the cobbler's seat at one end, a tray of lasts and tools at the other; a boot on the lasting post before it.
				const float seat = 0.42f;
				d.Add(StructureShapes.Box(new Vector3(0f, seat, 0.08f), new Vector3(1.3f, 0.06f, 0.42f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(sx * 0.58f, (seat - 0.03f) * 0.5f, 0.08f + sz * 0.16f), new Vector3(0.06f, seat - 0.03f, 0.06f), StructureMaterial.Timber, 2 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
					}
				}
				// The tray's low sides round the right half of the bench.
				d.Add(StructureShapes.Box(new Vector3(0.3f, seat + 0.06f, 0.28f), new Vector3(0.68f, 0.06f, 0.03f), StructureMaterial.Timber, 6));
				d.Add(StructureShapes.Box(new Vector3(0.3f, seat + 0.06f, -0.12f), new Vector3(0.68f, 0.06f, 0.03f), StructureMaterial.Timber, 7));
				d.Add(StructureShapes.Box(new Vector3(-0.03f, seat + 0.06f, 0.08f), new Vector3(0.03f, 0.06f, 0.4f), StructureMaterial.Timber, 8));
				// The lasting post, a boot on its last.
				d.Add(StructureShapes.Beam(new Vector3(-0.45f, 0f, -0.28f), new Vector3(-0.45f, 0.62f, -0.28f), 0.06f, 0.06f, StructureMaterial.Timber, 9));
				d.Add(StructureShapes.Box(new Vector3(-0.45f, 0.66f, -0.28f), new Vector3(0.24f, 0.08f, 0.09f), StructureMaterial.Timber, 10));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(-0.45f, 0.7f, -0.28f), new Vector3(0.26f, 0.06f, 0.1f), StructureMaterial.Canvas, 11, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(-0.53f, 0.83f, -0.28f), new Vector3(0.11f, 0.22f, 0.1f), StructureMaterial.Canvas, 12, StructureRole.Detail));
					for (int i = 0; i < 3; i++)
					{
						d.Add(StructureShapes.ChamferBox(new Vector3(0.15f + i * 0.17f, seat + 0.07f, 0.08f), new Vector3(0.09f, 0.08f, 0.24f), d.Bevel(0.02f), StructureMaterial.Timber, 13 + i, StructureRole.Detail));
					}
				}
				if (d.Fine)
				{
					d.Add(StructureShapes.Rod(new Vector3(0.18f, seat + 0.04f, -0.06f), new Vector3(0.36f, seat + 0.04f, -0.08f), 0.01f, 4, StructureMaterial.Timber, 16, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0.45f, seat + 0.05f, -0.06f), new Vector3(0.1f, 0.03f, 0.03f), StructureMaterial.Iron, 17, StructureRole.Detail));
				}
				return;
			}
			// A rack of boots on three sloping shelves.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.6f, 0.75f, 0f), new Vector3(0.05f, 1.5f, 0.34f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
			}
			const float tilt = 12f;
			for (int r = 0; r < 3; r++)
			{
				float y = 0.25f + r * 0.48f;
				Quaternion q = Quaternion.Euler(-tilt, 0f, 0f);
				d.Add(StructureShapes.Block(new Vector3(0f, y, 0f), new Vector3(1.15f, 0.025f, 0.32f), q, StructureMaterial.Timber, 3 + r));
				for (int p = 0; p < 2; p++)
				{
					int first = d.Solids.Count;
					Wares.Item(d, Wares.Goods_BootPair, 60 + r * 4 + p, true);
					TransformSince(d, first, Matrix4x4.TRS(new Vector3(-0.28f + p * 0.56f, y + 0.0125f, 0f), q, Vector3.one));
				}
			}
		}

		private static void JewellerBench(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float top = 0.95f;
				// The top, cut away at the front's middle where the goldsmith sits, the catch-skin slung beneath the cut.
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.03f, 0.15f), new Vector3(1.4f, 0.06f, 0.4f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * 0.45f, top - 0.03f, -0.2f), new Vector3(0.5f, 0.06f, 0.3f), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
					for (int z = -1; z <= 1; z += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * 0.64f, (top - 0.06f) * 0.5f, 0.05f + z * 0.25f), new Vector3(0.07f, top - 0.06f, 0.07f), StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0) + (z > 0 ? 2 : 0)));
					}
				}
				// The little charcoal furnace on its stone at the right-hand end, its bellows beside it.
				d.Add(StructureShapes.Box(new Vector3(0.5f, top + 0.12f, 0.15f), new Vector3(0.3f, 0.24f, 0.3f), StructureMaterial.Fieldstone, 8));
				d.Add(StructureShapes.Box(new Vector3(0.5f, top + 0.245f, 0.15f), new Vector3(0.18f, 0.012f, 0.18f), StructureMaterial.Ash, 9, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, top - 0.2f, -0.2f), new Vector3(0.38f, 0.015f, 0.28f), StructureMaterial.Canvas, 10, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0f, (top - 0.06f) * 0.5f, 0.33f), new Vector3(1.22f, top - 0.2f, 0.025f), StructureMaterial.Timber, 11)).Grain = Vector3.right;
					d.Add(StructureShapes.Prism(new[] { new Vector2(0f, 0.08f), new Vector2(-0.3f, 0.03f), new Vector2(-0.3f, -0.03f), new Vector2(0f, -0.08f) }, -0.07f, 0.07f, StructureMaterial.Canvas, 12, StructureRole.Detail)
						.Transform(Matrix4x4.TRS(new Vector3(0.32f, top + 0.08f, 0.15f), Quaternion.Euler(0f, 0f, 0f), Vector3.one)));
				}
				if (d.Fine)
				{
					d.Add(StructureShapes.Cylinder(0.035f, top + 0.25f, top + 0.3f, 6, StructureMaterial.Earth, 13, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.5f, 0f, 0.15f))));
					d.Add(StructureShapes.Box(new Vector3(0f, top - 0.01f, -0.06f), new Vector3(0.05f, 0.03f, 0.12f), StructureMaterial.Timber, 14, StructureRole.Detail));
					for (int i = 0; i < 3; i++)
					{
						d.Add(StructureShapes.Box(new Vector3(-0.35f + i * 0.1f, top + 0.006f, 0.1f), new Vector3(0.015f, 0.012f, 0.14f), StructureMaterial.Iron, 15 + i, StructureRole.Detail));
					}
				}
				d.Emit(new Vector3(0.5f, top + 0.27f, 0.15f), SceneEmitterKind.Hearth);
				return;
			}
			// A display case on legs, its gold on velvet under a locked iron grille; a strongbox beneath.
			const float floor = 0.8f, lid = 1.06f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(sx * 0.56f, floor * 0.5f, sz * 0.21f), new Vector3(0.06f, floor, 0.06f), StructureMaterial.Timber, 1 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, (floor + lid) * 0.5f, 0f), new Vector3(1.2f, lid - floor, 0.5f), StructureMaterial.Timber, 5)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, lid + 0.005f, 0f), new Vector3(1.1f, 0.01f, 0.42f), StructureMaterial.Dyed, 6, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.17f, 0f), new Vector3(0.6f, 0.34f, 0.4f), StructureMaterial.Timber, 7)).Grain = Vector3.right;
			if (d.Mid)
			{
				// The grille's frame and bars, and two cups standing within.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, lid + 0.2f, s * 0.235f), new Vector3(1.2f, 0.04f, 0.03f), StructureMaterial.Timber, 8 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Box(new Vector3(s * 0.585f, lid + 0.1f, 0f), new Vector3(0.03f, 0.2f, 0.5f), StructureMaterial.Timber, 10 + (s > 0 ? 1 : 0)));
				}
				for (int i = 0; i < 6; i++)
				{
					float x = -0.5f + i * 0.2f;
					d.Add(StructureShapes.Box(new Vector3(x, lid + 0.2f, 0f), new Vector3(0.015f, 0.015f, 0.44f), StructureMaterial.Iron, 12 + i, StructureRole.Detail));
				}
				for (int s = -1; s <= 1; s += 2)
				{
					int first = d.Solids.Count;
					Wares.Item(d, Wares.Goods_Chalice, 20 + (s > 0 ? 1 : 0), false);
					TransformSince(d, first, Matrix4x4.Translate(new Vector3(s * 0.3f, lid + 0.01f, 0.02f)));
				}
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * 0.2f, 0.17f, 0f), new Vector3(0.04f, 0.36f, 0.42f), StructureMaterial.Iron, 30 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				for (int i = 0; i < 6; i++)
				{
					d.Add(StructureShapes.Cylinder(0.02f, lid + 0.01f, lid + 0.022f, 6, StructureMaterial.Iron, 40 + i, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(-0.08f + (i % 3) * 0.08f, 0f, -0.08f + (i / 3) * 0.12f))));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.24f, -0.205f), new Vector3(0.07f, 0.09f, 0.02f), StructureMaterial.Iron, 46, StructureRole.Detail));
			}
		}

		private static void CandleRack(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// A dipping frame: candles hung in pairs by their wicks over two rods, a trough beneath for the drips.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * 0.55f, 0.88f, 0.05f), new Vector3(0.08f, 1.76f, 0.08f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
					d.Add(StructureShapes.Box(new Vector3(s * 0.55f, 0.03f, 0.05f), new Vector3(0.1f, 0.06f, 0.5f), StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 1.72f, 0.05f), new Vector3(1.2f, 0.08f, 0.08f), StructureMaterial.Timber, 5)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, 0.15f, 0.05f), new Vector3(0.95f, 0.3f, 0.4f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.305f, 0.05f), new Vector3(0.85f, 0.012f, 0.3f), StructureMaterial.Canvas, 7, StructureRole.Detail));
				}
				int pairs = d.Fine ? 6 : d.Mid ? 4 : 2;
				for (int r = 0; r < 2; r++)
				{
					float z = -0.07f + r * 0.24f;
					d.Add(StructureShapes.Beam(new Vector3(-0.52f, 1.52f, z), new Vector3(0.52f, 1.52f, z), 0.025f, 0.025f, StructureMaterial.Iron, 8 + r));
					for (int i = 0; i < pairs; i++)
					{
						float x = -0.4f + i * (0.8f / Mathf.Max(1, pairs - 1));
						for (int side = -1; side <= 1; side += 2)
						{
							float h = d.Range(20 + r * 20 + i * 2 + (side > 0 ? 1 : 0), 1, 0.26f, 0.34f);
							d.Add(StructureShapes.Cylinder(0.02f, 1.5f - h, 1.5f, 4, StructureMaterial.Canvas, 20 + r * 20 + i * 2 + (side > 0 ? 1 : 0), false, StructureRole.Detail)
								.Transform(Matrix4x4.Translate(new Vector3(x, 0f, z + side * 0.035f))));
						}
					}
				}
				return;
			}
			// The tallow vat on its ring of stones, a rail of candles hung to dry beside it.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.3f, 0.12f), new Vector2(0.36f, 0.35f), new Vector2(0.34f, 0.58f) }, d.Seg(12, 6), StructureMaterial.Iron, 1).Transform(Matrix4x4.Translate(new Vector3(-0.28f, 0f, 0f))));
			d.Add(StructureShapes.Cylinder(0.36f, 0f, 0.14f, d.Seg(10, 6), StructureMaterial.Fieldstone, 2).Transform(Matrix4x4.Translate(new Vector3(-0.28f, 0f, 0f))));
			if (d.Mid)
			{
				d.Add(StructureShapes.Cylinder(0.31f, 0.58f, 0.59f, d.Seg(12, 6), StructureMaterial.Canvas, 3, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(-0.28f, 0f, 0f))));
			}
			for (int s = 0; s < 2; s++)
			{
				float x = s == 0 ? 0.18f : 0.56f;
				d.Add(StructureShapes.Box(new Vector3(x, 0.78f, 0.15f), new Vector3(0.06f, 1.56f, 0.06f), StructureMaterial.Timber, 4 + s)).Grain = Vector3.up;
			}
			d.Add(StructureShapes.Beam(new Vector3(0.15f, 1.45f, 0.15f), new Vector3(0.59f, 1.45f, 0.15f), 0.03f, 0.03f, StructureMaterial.Timber, 6));
			int hung = d.Fine ? 5 : d.Mid ? 3 : 1;
			for (int i = 0; i < hung; i++)
			{
				float x = 0.25f + i * (0.26f / Mathf.Max(1, hung - 1));
				for (int side = -1; side <= 1; side += 2)
				{
					d.Add(StructureShapes.Cylinder(0.02f, 1.12f, 1.43f, 4, StructureMaterial.Canvas, 10 + i * 2 + (side > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(x, 0f, 0.15f + side * 0.035f))));
				}
			}
		}

		private static void MapTable(StructureDraft d)
		{
			const float top = 0.85f;
			Wares.Table(d, 1.8f, 1f, top, 1);
			// The shelf beneath with its rolled charts.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.2f, 0f), new Vector3(1.6f, 0.03f, 0.85f), StructureMaterial.Timber, 20)).Grain = Vector3.right;
			int rolls = d.Mid ? 4 : 2;
			for (int i = 0; i < rolls; i++)
			{
				float z = -0.25f + i * 0.14f;
				d.Add(StructureShapes.Rod(new Vector3(-0.55f, 0.25f, z), new Vector3(0.45f, 0.25f, z), 0.035f, d.Seg(6, 4), StructureMaterial.Canvas, 21 + i, StructureRole.Detail));
			}
			if (d.Variant == 0)
			{
				// A chart held flat with stones, the dividers on it, ink and a candle.
				d.Add(StructureShapes.Box(new Vector3(-0.1f, top + 0.004f, 0f), new Vector3(1.2f, 0.008f, 0.8f), StructureMaterial.Canvas, 30, StructureRole.Detail));
				if (d.Mid)
				{
					for (int i = 0; i < 4; i++)
					{
						float x = -0.1f + (i % 2 == 0 ? -0.55f : 0.55f), z = i < 2 ? -0.36f : 0.36f;
						d.Add(StructureShapes.ChamferBox(new Vector3(x, top + 0.035f, z), new Vector3(0.08f, 0.06f, 0.07f), d.Bevel(0.015f), StructureMaterial.Fieldstone, 31 + i, StructureRole.Detail));
					}
					d.Add(StructureShapes.Cylinder(0.035f, top, top + 0.06f, 6, StructureMaterial.Iron, 35, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.72f, 0f, 0.3f))));
				}
				if (d.Fine)
				{
					d.Add(StructureShapes.Block(new Vector3(0f, top + 0.012f, 0.05f), new Vector3(0.2f, 0.008f, 0.012f), Quaternion.Euler(0f, 15f, 0f), StructureMaterial.Iron, 36, StructureRole.Detail));
					d.Add(StructureShapes.Block(new Vector3(0.02f, top + 0.02f, 0.05f), new Vector3(0.2f, 0.008f, 0.012f), Quaternion.Euler(0f, -15f, 0f), StructureMaterial.Iron, 37, StructureRole.Detail));
					d.Add(StructureShapes.Cylinder(0.05f, top, top + 0.02f, 8, StructureMaterial.Iron, 38, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.72f, 0f, -0.3f))));
					d.Add(StructureShapes.Cylinder(0.02f, top + 0.02f, top + 0.18f, 6, StructureMaterial.Canvas, 39, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.72f, 0f, -0.3f))));
				}
				return;
			}
			// A sea chart, an astrolabe on its stand, books.
			d.Add(StructureShapes.Box(new Vector3(-0.25f, top + 0.004f, 0f), new Vector3(0.9f, 0.008f, 0.7f), StructureMaterial.Canvas, 30, StructureRole.Detail));
			d.Add(StructureShapes.Cylinder(0.06f, top, top + 0.03f, 8, StructureMaterial.Iron, 31, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.5f, 0f, 0.2f))));
			d.Add(Wares.Ring(new Vector3(0.5f, top + 0.17f, 0.2f), 0.13f, 0.11f, 0.015f, d.Seg(14, 8), Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Iron, 32));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0.5f, top + 0.06f, 0.2f), new Vector3(0.02f, 0.06f, 0.02f), StructureMaterial.Iron, 33, StructureRole.Detail));
				int first = d.Solids.Count;
				Wares.Item(d, Wares.Goods_BookStack, 34, false);
				TransformSince(d, first, Matrix4x4.Translate(new Vector3(0.55f, top, -0.25f)));
			}
			if (d.Fine)
			{
				d.Add(StructureShapes.Box(new Vector3(0.5f, top + 0.17f, 0.2f), new Vector3(0.22f, 0.012f, 0.012f), StructureMaterial.Iron, 40, StructureRole.Detail));
			}
		}

		private static void ChartPress(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// A press of shallow drawers for charts laid flat, a globe on it.
				const float height = 0.9f;
				d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, 0.02f), new Vector3(1.2f, height, 0.62f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				if (d.Mid)
				{
					for (int i = 0; i < 5; i++)
					{
						float y = 0.12f + i * 0.165f;
						d.Add(StructureShapes.Box(new Vector3(0f, y, -0.3f), new Vector3(1.1f, 0.14f, 0.02f), StructureMaterial.Timber, 2 + i, StructureRole.Detail)).Grain = Vector3.right;
						if (d.Fine)
						{
							d.Add(StructureShapes.Box(new Vector3(0f, y, -0.315f), new Vector3(0.12f, 0.025f, 0.015f), StructureMaterial.Iron, 10 + i, StructureRole.Detail));
						}
					}
				}
				int first = d.Solids.Count;
				Wares.Item(d, Wares.Goods_Globe, 20, false);
				TransformSince(d, first, Matrix4x4.Translate(new Vector3(0.3f, height, 0.05f)));
				if (d.Mid)
				{
					first = d.Solids.Count;
					Wares.Item(d, Wares.Goods_MapRolls, 30, false);
					TransformSince(d, first, Matrix4x4.Translate(new Vector3(-0.25f, height, 0f)));
				}
				return;
			}
			// A great globe in its horizon ring, on four legs.
			const float ring = 0.85f, radius = 0.33f;
			for (int i = 0; i < 4; i++)
			{
				float a = (45f + i * 90f) * Mathf.Deg2Rad;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.38f, ring - 0.02f, Mathf.Sin(a) * 0.38f), new Vector3(Mathf.Cos(a) * 0.42f, 0f, Mathf.Sin(a) * 0.42f), 0.05f, 0.05f, StructureMaterial.Timber, 1 + i));
			}
			d.Add(Wares.Ring(new Vector3(0f, ring, 0f), 0.44f, 0.36f, 0.04f, d.Seg(16, 8), Quaternion.identity, StructureMaterial.Timber, 5));
			d.Add(StructureShapes.Sphere(new Vector3(0f, ring + 0.02f, 0f), Vector3.one * radius, d.Seg(12, 8), d.Fine ? 7 : 5, StructureMaterial.Canvas, 6));
			if (d.Mid)
			{
				d.Add(Wares.Ring(new Vector3(0f, ring + 0.02f, 0f), radius + 0.035f, radius + 0.01f, 0.02f, d.Seg(16, 8), Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, 90f), StructureMaterial.Iron, 7));
				d.Add(Wares.Ring(new Vector3(0f, 0.18f, 0f), 0.4f, 0.36f, 0.04f, d.Seg(12, 6), Quaternion.identity, StructureMaterial.Timber, 8));
			}
		}

		/// <summary>
		/// The shapes the trades share (2026-10-19): an emblem's strokes and rings in a sign's plane, a bench's frame, and the
		/// wares laid out on a stall-board or a shelf. A nested class so its many small names stay out of the kit's own.
		/// </summary>
		private static class Wares
		{
			/// <summary>The stall-board's goods' size (x along the board, z across it), metres.</summary>
			public const float StallWidth = 2.7f, StallDepth = 0.62f;

			// ── Emblem shapes, in a plane facing ±z ──

			/// <summary>A flat bar from <paramref name="a"/> to <paramref name="b"/> in the x-y plane about <paramref name="c"/>, <paramref name="w"/> wide, <paramref name="t"/> thick.</summary>
			public static StructureSolid Stroke(Vector3 c, Vector2 a, Vector2 b, float w, float t, StructureMaterial m, int key)
			{
				Vector2 dir = b - a;
				float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
				Vector2 mid = (a + b) * 0.5f;
				return StructureShapes.Block(c + new Vector3(mid.x, mid.y, 0f), new Vector3(Mathf.Max(0.01f, dir.magnitude), w, t), Quaternion.Euler(0f, 0f, angle), m, key, StructureRole.Detail);
			}

			/// <summary>A tapering blade from <paramref name="a"/> (<paramref name="w0"/> wide) to <paramref name="b"/> (<paramref name="w1"/>).</summary>
			public static StructureSolid Taper(Vector3 c, Vector2 a, Vector2 b, float w0, float w1, float t, StructureMaterial m, int key)
			{
				Vector2 dir = (b - a).normalized, n = new Vector2(-dir.y, dir.x);
				return Shape(c, new[] { a - n * (w0 * 0.5f), b - n * (w1 * 0.5f), b + n * (w1 * 0.5f), a + n * (w0 * 0.5f) }, t, m, key);
			}

			/// <summary>A flat shape (star-shaped about its centroid) in the x-y plane about <paramref name="c"/>, <paramref name="t"/> thick.</summary>
			public static StructureSolid Shape(Vector3 c, IList<Vector2> outline, float t, StructureMaterial m, int key)
			{
				return StructureShapes.Prism(outline, -t * 0.5f, t * 0.5f, m, key, StructureRole.Detail).Transform(Matrix4x4.Translate(c));
			}

			/// <summary>A disc facing ±z.</summary>
			public static StructureSolid Disc(Vector3 c, float r, float t, int segments, StructureMaterial m, int key)
			{
				return StructureShapes.Cylinder(r, -t * 0.5f, t * 0.5f, segments, m, key, false, StructureRole.Detail).Transform(Matrix4x4.TRS(c, Quaternion.Euler(90f, 0f, 0f), Vector3.one));
			}

			/// <summary>A flat ring (outer and inner radius, thickness) about the y axis turned by <paramref name="turn"/> (Euler(90, 0, 0): facing ±z).</summary>
			public static StructureSolid Ring(Vector3 c, float outer, float inner, float t, int segments, Quaternion turn, StructureMaterial m, int key)
			{
				var profile = new[] { new Vector2(outer, -t * 0.5f), new Vector2(outer, t * 0.5f), new Vector2(inner, t * 0.5f), new Vector2(inner, -t * 0.5f) };
				return StructureShapes.Lathe(profile, segments, m, key, false, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.TRS(c, turn, Vector3.one));
			}

			/// <summary>A round-sectioned ring (a pretzel's loop) of radius <paramref name="major"/> and thickness <paramref name="minor"/>, turned by <paramref name="turn"/>.</summary>
			public static StructureSolid Torus(Vector3 c, float major, float minor, int segments, int sides, Quaternion turn, StructureMaterial m, int key)
			{
				var profile = new Vector2[sides];
				for (int i = 0; i < sides; i++)
				{
					float a = i * Mathf.PI * 2f / sides;
					profile[i] = new Vector2(major + Mathf.Cos(a) * minor, Mathf.Sin(a) * minor);
				}
				return StructureShapes.Lathe(profile, segments, m, key, true, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.TRS(c, turn, Vector3.one));
			}

			// ── A bench's frame ──

			/// <summary>A plank top at <paramref name="top"/> on four square legs, rails between them at levels 0-1.</summary>
			public static void Table(StructureDraft d, float width, float depth, float top, int key)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.03f, 0f), new Vector3(width, 0.06f, depth), StructureMaterial.Timber, key)).Grain = Vector3.right;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(sx * (width * 0.5f - 0.08f), (top - 0.06f) * 0.5f, sz * (depth * 0.5f - 0.08f)), new Vector3(0.08f, top - 0.06f, 0.08f),
							StructureMaterial.Timber, key + 1 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0))).Grain = Vector3.up;
					}
				}
				if (d.Mid)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(0f, top - 0.14f, sz * (depth * 0.5f - 0.08f)), new Vector3(width - 0.2f, 0.08f, 0.04f), StructureMaterial.Timber, key + 5 + (sz > 0 ? 1 : 0), StructureRole.Detail)).Grain = Vector3.right;
					}
				}
			}

			// ── Wares ──

			public const int Goods_ClothStack = 0, Goods_ClothRoll = 1, Goods_BootPair = 2, Goods_HideStack = 3, Goods_Satchel = 4, Goods_BeltCoil = 5,
				Goods_Quiver = 6, Goods_ArrowSheaf = 7, Goods_BowFlat = 8, Goods_CandleRow = 9, Goods_CandleBundle = 10, Goods_TallowBlock = 11,
				Goods_JewelTray = 12, Goods_Chalice = 13, Goods_Casket = 14, Goods_ScrollPile = 15, Goods_BookStack = 16, Goods_Inkpot = 17,
				Goods_MapRolls = 18, Goods_Globe = 19, Goods_OpenMap = 20, Goods_Loaves = 21, Goods_RoundLoaves = 22, Goods_BreadBasket = 23,
				Goods_Cask = 24, Goods_Bucket = 25, Goods_Bowls = 26, Goods_Pot = 27, Goods_Pan = 28, Goods_Lantern = 29, Goods_Jars = 30,
				Goods_HerbBundle = 31, Goods_Mortar = 32;

			/// <summary>Each item's size laid out (x along the board, y up, z across it), metres.</summary>
			private static readonly Vector3[] Sizes =
			{
				new Vector3(0.34f, 0.3f, 0.26f), new Vector3(0.56f, 0.14f, 0.16f), new Vector3(0.24f, 0.32f, 0.3f), new Vector3(0.42f, 0.09f, 0.3f),
				new Vector3(0.28f, 0.24f, 0.14f), new Vector3(0.16f, 0.05f, 0.16f), new Vector3(0.16f, 0.62f, 0.16f), new Vector3(0.62f, 0.09f, 0.12f),
				new Vector3(1.02f, 0.04f, 0.14f), new Vector3(0.3f, 0.26f, 0.06f), new Vector3(0.3f, 0.06f, 0.08f), new Vector3(0.2f, 0.1f, 0.14f),
				new Vector3(0.38f, 0.06f, 0.28f), new Vector3(0.12f, 0.18f, 0.12f), new Vector3(0.28f, 0.19f, 0.2f), new Vector3(0.32f, 0.1f, 0.12f),
				new Vector3(0.26f, 0.16f, 0.2f), new Vector3(0.12f, 0.2f, 0.1f), new Vector3(0.52f, 0.13f, 0.1f), new Vector3(0.28f, 0.42f, 0.28f),
				new Vector3(0.44f, 0.07f, 0.3f), new Vector3(0.3f, 0.07f, 0.16f), new Vector3(0.24f, 0.13f, 0.2f), new Vector3(0.3f, 0.2f, 0.3f),
				new Vector3(0.28f, 0.3f, 0.28f), new Vector3(0.24f, 0.24f, 0.24f), new Vector3(0.22f, 0.12f, 0.22f), new Vector3(0.24f, 0.2f, 0.24f),
				new Vector3(0.46f, 0.05f, 0.24f), new Vector3(0.16f, 0.3f, 0.16f), new Vector3(0.3f, 0.17f, 0.12f), new Vector3(0.36f, 0.08f, 0.1f),
				new Vector3(0.22f, 0.24f, 0.22f),
			};

			/// <summary>What each trade lays out, in the order it lays it (each row starts at a seeded place in its list).</summary>
			private static int[] ListOf(StructureTrade trade)
			{
				switch (trade)
				{
					case StructureTrade.Tailor: return new[] { Goods_ClothStack, Goods_ClothRoll, Goods_ClothStack, Goods_ClothRoll, Goods_ClothStack };
					case StructureTrade.Leatherworker: return new[] { Goods_HideStack, Goods_Satchel, Goods_BeltCoil, Goods_Satchel, Goods_BeltCoil };
					case StructureTrade.Fletcher: return new[] { Goods_Quiver, Goods_ArrowSheaf, Goods_BowFlat, Goods_Quiver, Goods_ArrowSheaf };
					case StructureTrade.Cobbler: return new[] { Goods_BootPair, Goods_BootPair, Goods_BeltCoil, Goods_BootPair };
					case StructureTrade.Jeweller: return new[] { Goods_JewelTray, Goods_Chalice, Goods_Casket, Goods_Chalice, Goods_JewelTray };
					case StructureTrade.Chandler: return new[] { Goods_CandleRow, Goods_CandleBundle, Goods_TallowBlock, Goods_CandleRow, Goods_CandleBundle };
					case StructureTrade.Cartographer: return new[] { Goods_Globe, Goods_MapRolls, Goods_OpenMap, Goods_MapRolls, Goods_Inkpot };
					case StructureTrade.Scribe: return new[] { Goods_ScrollPile, Goods_BookStack, Goods_Inkpot, Goods_ScrollPile, Goods_BookStack };
					case StructureTrade.Baker: return new[] { Goods_BreadBasket, Goods_Loaves, Goods_RoundLoaves, Goods_Loaves, Goods_RoundLoaves };
					case StructureTrade.Cooper: return new[] { Goods_Cask, Goods_Bucket, Goods_Bowls, Goods_Cask, Goods_Bowls };
					case StructureTrade.Ironmonger: return new[] { Goods_Pot, Goods_Pan, Goods_Lantern, Goods_Pot, Goods_Lantern };
					default: return new[] { Goods_Jars, Goods_HerbBundle, Goods_Mortar, Goods_Jars, Goods_HerbBundle };
				}
			}

			/// <summary>
			/// Lays a trade's wares on a board: its top's middle at <paramref name="at"/>, <paramref name="width"/> along x and
			/// <paramref name="depth"/> across, nothing taller than <paramref name="maxHeight"/>; in one row, or two (the taller
			/// behind) on a deep board. What is laid is chosen at the finest level and kept at every level (each item only
			/// simpler), stopping at <paramref name="budget"/> triangles, so no coarser level outgrows a finer one. Each item is
			/// built at <paramref name="detail"/>'s level at its finest (1 on a shelf of many: more wares, each plainer).
			/// </summary>
			/// <returns>The triangles it laid at the finest level (the budget it used).</returns>
			public static int Goods(StructureDraft d, StructureTrade trade, Vector3 at, float width, float depth, float maxHeight, int key, int budget, int detail)
			{
				int[] list = ListOf(trade);
				int rows = depth >= 0.42f ? 2 : 1;
				float rowDepth = depth / rows;
				int spent = 0;
				for (int r = 0; r < rows; r++)
				{
					// Each row its share of the budget, and what the rows before it left.
					int share = budget * (r + 1) / rows;
					// What fits the row, in the list's order from a seeded start.
					var chosen = new List<int>();
					float used = 0f;
					int start = (int)(d.Rand(key + r, 1) * list.Length) % list.Length;
					for (int n = 0; n < list.Length * 2; n++)
					{
						int item = list[(start + n) % list.Length];
						Vector3 size = Sizes[item];
						if (size.y > maxHeight || size.z > rowDepth + 0.04f || used + size.x > width)
						{
							continue;
						}
						int cost = Cost(d, item, key + r * 16 + chosen.Count, detail);
						if (spent + cost > share)
						{
							continue;
						}
						spent += cost;
						chosen.Add(item);
						used += size.x + 0.04f;
					}
					float x = -0.5f * (used - 0.04f);
					float z = rows == 1 ? 0f : r == 0 ? rowDepth * 0.5f : -rowDepth * 0.5f;
					for (int i = 0; i < chosen.Count; i++)
					{
						Vector3 size = Sizes[chosen[i]];
						int itemKey = key + r * 16 + i;
						var aside = new StructureDraft(d.Piece, d.Variant, Mathf.Max(d.Lod, detail), d.GenerationSeed);
						Item(aside, chosen[i], itemKey, false);
						float yaw = d.Range(itemKey, 7, -8f, 8f);
						Matrix4x4 place = Matrix4x4.TRS(at + new Vector3(x + size.x * 0.5f, 0f, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one);
						foreach (StructureSolid solid in aside.Solids)
						{
							d.Add(solid.Transform(place));
						}
						x += size.x + 0.04f;
					}
				}
				return spent;
			}

			/// <summary>An item's triangles at the finest level it is built at (built aside and counted).</summary>
			private static int Cost(StructureDraft d, int item, int key, int detail)
			{
				var aside = new StructureDraft(d.Piece, d.Variant, detail, d.GenerationSeed);
				Item(aside, item, key, false);
				int tris = 0;
				foreach (StructureSolid s in aside.Solids)
				{
					foreach (StructureFace f in s.Faces)
					{
						tris += Mathf.Max(0, f.Points.Count - 2);
					}
				}
				return tris;
			}

			/// <summary>One item of wares at the origin, its base on y = 0, its front to −z. <paramref name="lite"/>: the plainer form (a rack of many).</summary>
			public static void Item(StructureDraft d, int item, int key, bool lite)
			{
				int k = key * 16;
				switch (item)
				{
					case Goods_ClothStack:
					{
						int bolts = 2 + (d.Rand(key, 1) < 0.5f ? 1 : 0);
						for (int i = 0; i < bolts; i++)
						{
							d.Add(StructureShapes.Block(new Vector3(d.Range(key, 2 + i, -0.015f, 0.015f), 0.045f + i * 0.092f, 0f), new Vector3(0.32f, 0.09f, 0.24f), Quaternion.Euler(0f, d.Range(key, 5 + i, -6f, 6f), 0f),
								d.Rand(key, 9 + i) < 0.6f ? StructureMaterial.Dyed : StructureMaterial.Canvas, k + i, StructureRole.Detail));
						}
						break;
					}
					case Goods_ClothRoll:
						d.Add(StructureShapes.Rod(new Vector3(-0.25f, 0.07f, 0f), new Vector3(0.25f, 0.07f, 0f), 0.07f, d.Seg(8, 5), StructureMaterial.Dyed, k, StructureRole.Detail));
						if (d.Fine)
						{
							d.Add(StructureShapes.Rod(new Vector3(-0.275f, 0.07f, 0f), new Vector3(0.275f, 0.07f, 0f), 0.02f, 5, StructureMaterial.Timber, k + 1, StructureRole.Detail));
						}
						break;
					case Goods_BootPair:
						for (int s = -1; s <= 1; s += 2)
						{
							float x = s * 0.06f;
							d.Add(StructureShapes.Box(new Vector3(x, 0.05f, -0.03f), new Vector3(0.09f, 0.08f, 0.26f), StructureMaterial.Canvas, k + (s > 0 ? 1 : 0), StructureRole.Detail));
							d.Add(StructureShapes.Box(new Vector3(x, 0.19f, 0.05f), new Vector3(0.09f, 0.22f, 0.11f), StructureMaterial.Canvas, k + 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
							if (d.Mid)
							{
								d.Add(StructureShapes.Box(new Vector3(x, 0.29f, 0.05f), new Vector3(0.1f, 0.05f, 0.12f), StructureMaterial.Canvas, k + 4 + (s > 0 ? 1 : 0), StructureRole.Detail));
							}
							if (d.Fine && !lite)
							{
								d.Add(StructureShapes.Box(new Vector3(x, 0.006f, -0.03f), new Vector3(0.095f, 0.012f, 0.27f), StructureMaterial.Timber, k + 6 + (s > 0 ? 1 : 0), StructureRole.Detail));
							}
						}
						break;
					case Goods_HideStack:
						for (int i = 0; i < 3; i++)
						{
							d.Add(StructureShapes.Block(new Vector3(0f, 0.014f + i * 0.027f, 0f), new Vector3(0.4f - i * 0.04f, 0.025f, 0.28f - i * 0.03f), Quaternion.Euler(0f, d.Range(key, 1 + i, -10f, 10f), 0f),
								StructureMaterial.Canvas, k + i, StructureRole.Detail));
						}
						break;
					case Goods_Satchel:
						d.Add(StructureShapes.Box(new Vector3(0f, 0.09f, 0f), new Vector3(0.26f, 0.18f, 0.1f), StructureMaterial.Canvas, k, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.Block(new Vector3(0f, 0.15f, -0.055f), new Vector3(0.26f, 0.1f, 0.012f), Quaternion.Euler(-8f, 0f, 0f), StructureMaterial.Canvas, k + 1, StructureRole.Detail));
						}
						if (d.Fine)
						{
							d.Add(StructureShapes.Beam(new Vector3(-0.12f, 0.17f, 0f), new Vector3(0f, 0.23f, 0f), 0.02f, 0.006f, StructureMaterial.Canvas, k + 2, StructureRole.Detail));
							d.Add(StructureShapes.Beam(new Vector3(0f, 0.23f, 0f), new Vector3(0.12f, 0.17f, 0f), 0.02f, 0.006f, StructureMaterial.Canvas, k + 3, StructureRole.Detail));
						}
						break;
					case Goods_BeltCoil:
						d.Add(Ring(new Vector3(0f, 0.018f, 0f), 0.075f, 0.045f, 0.035f, d.Seg(10, 6), Quaternion.identity, StructureMaterial.Canvas, k));
						if (d.Fine)
						{
							d.Add(StructureShapes.Box(new Vector3(0.07f, 0.02f, 0f), new Vector3(0.025f, 0.04f, 0.04f), StructureMaterial.Iron, k + 1, StructureRole.Detail));
						}
						break;
					case Goods_Quiver:
					{
						d.Add(StructureShapes.Cylinder(0.06f, 0f, 0.42f, d.Seg(8, 5), StructureMaterial.Canvas, k, false, StructureRole.Detail));
						int arrows = d.Fine ? 5 : d.Mid ? 3 : 1;
						for (int i = 0; i < arrows; i++)
						{
							float a = i * 2.39996f, r = 0.035f * Mathf.Sqrt((i + 0.5f) / arrows);
							var foot = new Vector3(Mathf.Cos(a) * r, 0.4f, Mathf.Sin(a) * r);
							d.Add(StructureShapes.Rod(foot, foot + new Vector3(Mathf.Cos(a) * 0.02f, 0.2f, Mathf.Sin(a) * 0.02f), 0.007f, 3, StructureMaterial.Timber, k + 1 + i, StructureRole.Detail));
							if (d.Fine)
							{
								d.Add(StructureShapes.Block(foot + new Vector3(Mathf.Cos(a) * 0.017f, 0.16f, Mathf.Sin(a) * 0.017f), new Vector3(0.03f, 0.07f, 0.005f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), StructureMaterial.Dyed, k + 8 + i, StructureRole.Detail));
							}
						}
						break;
					}
					case Goods_ArrowSheaf:
						d.Add(StructureShapes.Rod(new Vector3(-0.3f, 0.045f, 0f), new Vector3(0.3f, 0.045f, 0f), 0.04f, d.Seg(8, 5), StructureMaterial.Timber, k, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.Box(new Vector3(-0.24f, 0.045f, 0f), new Vector3(0.1f, 0.09f, 0.09f), StructureMaterial.Dyed, k + 1, StructureRole.Detail));
							d.Add(StructureShapes.Rod(new Vector3(0.02f, 0.045f, 0f), new Vector3(0.05f, 0.045f, 0f), 0.044f, d.Seg(8, 5), StructureMaterial.Canvas, k + 2, StructureRole.Detail));
						}
						if (d.Fine)
						{
							d.Add(StructureShapes.Box(new Vector3(0.3f, 0.045f, 0f), new Vector3(0.03f, 0.06f, 0.06f), StructureMaterial.Iron, k + 3, StructureRole.Detail));
						}
						break;
					case Goods_BowFlat:
					{
						int bends = d.Mid ? 4 : 2;
						Vector3 last = Vector3.zero;
						for (int i = 0; i <= bends; i++)
						{
							float s = -1f + 2f * i / bends;
							var p = new Vector3(0.5f * s, 0.016f, 0.05f * (1f - s * s) - 0.02f);
							if (i > 0)
							{
								d.Add(StructureShapes.Beam(last, p, 0.03f, 0.03f, StructureMaterial.Timber, k + i, StructureRole.Detail));
							}
							last = p;
						}
						if (d.Mid)
						{
							d.Add(StructureShapes.Beam(new Vector3(-0.49f, 0.012f, -0.02f), new Vector3(0.49f, 0.012f, -0.02f), 0.008f, 0.008f, StructureMaterial.Canvas, k + 8, StructureRole.Detail));
						}
						break;
					}
					case Goods_CandleRow:
					{
						int candles = d.Mid ? 5 : 3;
						for (int i = 0; i < candles; i++)
						{
							float x = -0.12f + i * (0.24f / (candles - 1));
							d.Add(StructureShapes.Cylinder(0.022f, 0f, d.Range(key, 1 + i, 0.18f, 0.26f), d.Seg(6, 4), StructureMaterial.Canvas, k + i, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(x, 0f, 0f))));
						}
						break;
					}
					case Goods_CandleBundle:
						if (!d.Mid)
						{
							d.Add(StructureShapes.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.28f, 0.06f, 0.08f), StructureMaterial.Canvas, k, StructureRole.Detail));
							break;
						}
						for (int i = 0; i < 5; i++)
						{
							float y = i < 3 ? 0.018f : 0.048f, z = i < 3 ? (i - 1) * 0.036f : (i - 3.5f) * 0.036f;
							d.Add(StructureShapes.Rod(new Vector3(-0.14f, y, z), new Vector3(0.14f, y, z), 0.018f, d.Seg(6, 4), StructureMaterial.Canvas, k + i, StructureRole.Detail));
						}
						if (d.Fine)
						{
							d.Add(StructureShapes.Box(new Vector3(0f, 0.033f, 0f), new Vector3(0.025f, 0.07f, 0.12f), StructureMaterial.Canvas, k + 6, StructureRole.Detail));
						}
						break;
					case Goods_TallowBlock:
						d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.1f, 0.14f), d.Bevel(0.015f), StructureMaterial.Canvas, k, StructureRole.Detail));
						break;
					case Goods_JewelTray:
						d.Add(StructureShapes.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.38f, 0.04f, 0.28f), StructureMaterial.Timber, k, StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(0f, 0.044f, 0f), new Vector3(0.32f, 0.01f, 0.22f), StructureMaterial.Dyed, k + 1, StructureRole.Detail));
						if (d.Fine)
						{
							for (int i = 0; i < 6; i++)
							{
								d.Add(StructureShapes.Cylinder(0.02f, 0.049f, 0.061f, 6, StructureMaterial.Iron, k + 2 + i, false, StructureRole.Detail)
									.Transform(Matrix4x4.Translate(new Vector3(-0.1f + (i % 3) * 0.1f, 0f, -0.05f + (i / 3) * 0.1f))));
							}
						}
						break;
					case Goods_Chalice:
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.045f, 0f), new Vector2(0.012f, 0.025f), new Vector2(0.012f, 0.09f), new Vector2(0.05f, 0.13f), new Vector2(0.055f, 0.18f) },
							d.Seg(10, 6), StructureMaterial.Iron, k, true, 0f, false, false, StructureRole.Detail));
						break;
					case Goods_Casket:
						d.Add(StructureShapes.Box(new Vector3(0f, 0.06f, 0f), new Vector3(0.26f, 0.12f, 0.18f), StructureMaterial.Timber, k, StructureRole.Detail));
						d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.145f, 0f), new Vector3(0.27f, 0.05f, 0.19f), d.Bevel(0.012f), StructureMaterial.Timber, k + 1, StructureRole.Detail));
						if (d.Mid)
						{
							for (int s = -1; s <= 1; s += 2)
							{
								d.Add(StructureShapes.Box(new Vector3(s * 0.08f, 0.09f, 0f), new Vector3(0.02f, 0.18f, 0.2f), StructureMaterial.Iron, k + 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
							}
						}
						break;
					case Goods_ScrollPile:
					{
						int n = d.Mid ? 5 : 2;
						for (int i = 0; i < n; i++)
						{
							float y = i < 3 ? 0.028f : 0.075f, z = i < 3 ? (i - 1) * 0.058f : (i - 3.5f) * 0.058f;
							d.Add(StructureShapes.Rod(new Vector3(-0.15f, y, z), new Vector3(0.15f, y, z), 0.028f, d.Seg(6, 4), StructureMaterial.Canvas, k + i, StructureRole.Detail));
						}
						break;
					}
					case Goods_BookStack:
						for (int i = 0; i < 3; i++)
						{
							StructureMaterial m = i == 1 ? StructureMaterial.Timber : StructureMaterial.Dyed;
							d.Add(StructureShapes.Block(new Vector3(0f, 0.025f + i * 0.05f, 0f), new Vector3(0.24f - i * 0.02f, 0.05f, 0.18f - i * 0.01f), Quaternion.Euler(0f, d.Range(key, 1 + i, -12f, 12f), 0f), m, k + i, StructureRole.Detail));
						}
						break;
					case Goods_Inkpot:
						d.Add(StructureShapes.Cylinder(0.035f, 0f, 0.055f, d.Seg(8, 5), StructureMaterial.Iron, k, false, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.Block(new Vector3(0.02f, 0.12f, 0f), new Vector3(0.01f, 0.18f, 0.03f), Quaternion.Euler(0f, 0f, -18f), StructureMaterial.Canvas, k + 1, StructureRole.Detail));
						}
						break;
					case Goods_MapRolls:
						for (int i = 0; i < 3; i++)
						{
							float y = i < 2 ? 0.035f : 0.095f, z = i < 2 ? (i - 0.5f) * 0.07f : 0f;
							d.Add(StructureShapes.Rod(new Vector3(-0.25f + i * 0.01f, y, z), new Vector3(0.25f - i * 0.01f, y, z), 0.035f, d.Seg(8, 5), StructureMaterial.Canvas, k + i, StructureRole.Detail));
						}
						if (d.Fine)
						{
							d.Add(StructureShapes.Rod(new Vector3(0.05f, 0.095f, 0f), new Vector3(0.075f, 0.095f, 0f), 0.038f, 6, StructureMaterial.Dyed, k + 4, StructureRole.Detail));
						}
						break;
					case Goods_Globe:
						d.Add(StructureShapes.Cylinder(0.1f, 0f, 0.03f, d.Seg(10, 6), StructureMaterial.Timber, k, false, StructureRole.Detail));
						d.Add(StructureShapes.Cylinder(0.02f, 0.03f, 0.17f, d.Seg(6, 4), StructureMaterial.Timber, k + 1, false, StructureRole.Detail));
						d.Add(StructureShapes.Sphere(new Vector3(0f, 0.29f, 0f), Vector3.one * 0.12f, d.Seg(10, 6), d.Fine ? 6 : 4, StructureMaterial.Canvas, k + 2, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(Ring(new Vector3(0f, 0.29f, 0f), 0.135f, 0.125f, 0.015f, d.Seg(12, 6), Quaternion.Euler(0f, 0f, 90f), StructureMaterial.Iron, k + 3));
						}
						break;
					case Goods_OpenMap:
						d.Add(StructureShapes.Box(new Vector3(0f, 0.004f, 0f), new Vector3(0.42f, 0.008f, 0.28f), StructureMaterial.Canvas, k, StructureRole.Detail));
						d.Add(StructureShapes.Rod(new Vector3(-0.2f, 0.028f, 0.14f), new Vector3(0.2f, 0.028f, 0.14f), 0.025f, d.Seg(6, 4), StructureMaterial.Canvas, k + 1, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.ChamferBox(new Vector3(-0.17f, 0.03f, -0.1f), new Vector3(0.06f, 0.045f, 0.05f), d.Bevel(0.01f), StructureMaterial.Fieldstone, k + 2, StructureRole.Detail));
						}
						break;
					case Goods_Loaves:
						for (int s = -1; s <= 1; s += 2)
						{
							d.Add(StructureShapes.Sphere(new Vector3(s * 0.075f, 0.035f, 0f), new Vector3(0.07f, 0.045f, 0.12f), d.Seg(8, 5), d.Mid ? 4 : 3, StructureMaterial.Timber, k + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
						break;
					case Goods_RoundLoaves:
						d.Add(StructureShapes.Sphere(new Vector3(-0.05f, 0.04f, 0f), new Vector3(0.08f, 0.05f, 0.08f), d.Seg(8, 5), d.Mid ? 4 : 3, StructureMaterial.Timber, k, StructureRole.Detail));
						d.Add(StructureShapes.Sphere(new Vector3(0.06f, 0.04f, 0.02f), new Vector3(0.07f, 0.045f, 0.07f), d.Seg(8, 5), d.Mid ? 4 : 3, StructureMaterial.Timber, k + 1, StructureRole.Detail));
						break;
					case Goods_BreadBasket:
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.11f, 0f), new Vector2(0.145f, 0.12f) }, d.Seg(10, 6), StructureMaterial.Thatch, k, true, 0f, false, false, StructureRole.Detail));
						d.Add(StructureShapes.Sphere(new Vector3(-0.05f, 0.13f, 0f), new Vector3(0.1f, 0.05f, 0.06f), d.Seg(8, 5), 4, StructureMaterial.Timber, k + 1, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.Sphere(new Vector3(0.06f, 0.14f, 0.03f), new Vector3(0.08f, 0.05f, 0.07f), d.Seg(8, 5), 4, StructureMaterial.Timber, k + 2, StructureRole.Detail));
						}
						break;
					case Goods_Cask:
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.11f, 0f), new Vector2(0.135f, 0.15f), new Vector2(0.11f, 0.3f) }, d.Seg(10, 6), StructureMaterial.Timber, k, true, 0f, false, true, StructureRole.Detail));
						if (d.Mid)
						{
							for (int s = 0; s < 2; s++)
							{
								float y = s == 0 ? 0.05f : 0.25f;
								d.Add(StructureShapes.Cylinder(0.122f, y - 0.012f, y + 0.012f, d.Seg(10, 6), StructureMaterial.Iron, k + 1 + s, false, StructureRole.Detail));
							}
						}
						break;
					case Goods_Bucket:
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.09f, 0f), new Vector2(0.11f, 0.2f) }, d.Seg(10, 6), StructureMaterial.Timber, k, true, 0f, false, true, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.Cylinder(0.106f, 0.13f, 0.15f, d.Seg(10, 6), StructureMaterial.Iron, k + 1, false, StructureRole.Detail));
						}
						if (d.Fine)
						{
							d.Add(StructureShapes.Beam(new Vector3(-0.1f, 0.2f, 0f), new Vector3(0f, 0.23f, 0f), 0.008f, 0.008f, StructureMaterial.Iron, k + 2, StructureRole.Detail));
							d.Add(StructureShapes.Beam(new Vector3(0f, 0.23f, 0f), new Vector3(0.1f, 0.2f, 0f), 0.008f, 0.008f, StructureMaterial.Iron, k + 3, StructureRole.Detail));
						}
						break;
					case Goods_Bowls:
						for (int i = 0; i < (d.Mid ? 3 : 1); i++)
						{
							d.Add(StructureShapes.Lathe(new[] { new Vector2(0.05f, 0f), new Vector2(0.1f, 0.045f) }, d.Seg(10, 6), StructureMaterial.Timber, k + i, true, 0f, false, false, StructureRole.Detail)
								.Transform(Matrix4x4.Translate(new Vector3(0f, i * 0.035f, 0f))));
						}
						break;
					case Goods_Pot:
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.07f, 0f), new Vector2(0.115f, 0.08f), new Vector2(0.1f, 0.16f), new Vector2(0.105f, 0.18f) }, d.Seg(10, 6), StructureMaterial.Iron, k, true, 0f, false, false, StructureRole.Detail));
						if (d.Mid)
						{
							d.Add(StructureShapes.Box(new Vector3(0f, 0.19f, 0f), new Vector3(0.04f, 0.02f, 0.04f), StructureMaterial.Iron, k + 1, StructureRole.Detail));
						}
						break;
					case Goods_Pan:
						d.Add(StructureShapes.Cylinder(0.12f, 0f, 0.035f, d.Seg(10, 6), StructureMaterial.Iron, k, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(-0.1f, 0f, 0f))));
						d.Add(StructureShapes.Box(new Vector3(0.12f, 0.025f, 0f), new Vector3(0.22f, 0.015f, 0.03f), StructureMaterial.Iron, k + 1, StructureRole.Detail));
						break;
					case Goods_Lantern:
						d.Add(StructureShapes.Box(new Vector3(0f, 0.01f, 0f), new Vector3(0.13f, 0.02f, 0.13f), StructureMaterial.Iron, k, StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.11f, 0.16f, 0.11f), StructureMaterial.Canvas, k + 1, StructureRole.Detail));
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.09f, 0f), new Vector2(0f, 0.07f) }, 4, StructureMaterial.Iron, k + 2, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
							.Transform(Matrix4x4.Translate(new Vector3(0f, 0.18f, 0f))));
						if (d.Fine)
						{
							d.Add(Ring(new Vector3(0f, 0.27f, 0f), 0.025f, 0.015f, 0.008f, 6, Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Iron, k + 3));
						}
						break;
					case Goods_Jars:
						for (int i = 0; i < 3; i++)
						{
							float sc = d.Range(key, 1 + i, 0.8f, 1.1f);
							IList<Vector2> profile = d.Mid
								? new[] { new Vector2(0.04f, 0f), new Vector2(0.05f, 0.05f), new Vector2(0.04f, 0.11f), new Vector2(0.028f, 0.12f), new Vector2(0.032f, 0.145f) }
								: new[] { new Vector2(0.045f, 0f), new Vector2(0.045f, 0.13f) };
							d.Add(StructureShapes.Lathe(profile, d.Seg(8, 5), i == 1 ? StructureMaterial.Earth : StructureMaterial.Canvas, k + i, true, 0f, false, false, StructureRole.Detail)
								.Transform(Matrix4x4.TRS(new Vector3(-0.1f + i * 0.1f, 0f, 0f), Quaternion.identity, Vector3.one * sc)));
						}
						break;
					case Goods_HerbBundle:
						d.Add(StructureShapes.Rod(new Vector3(-0.17f, 0.04f, 0f), new Vector3(0.17f, 0.04f, 0f), 0.04f, d.Seg(6, 4), StructureMaterial.Thatch, k, StructureRole.Detail, 0.15f));
						if (d.Mid)
						{
							d.Add(StructureShapes.Rod(new Vector3(-0.1f, 0.04f, 0f), new Vector3(-0.07f, 0.04f, 0f), 0.044f, d.Seg(6, 4), StructureMaterial.Canvas, k + 1, StructureRole.Detail));
						}
						break;
					default:
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.07f, 0f), new Vector2(0.095f, 0.04f), new Vector2(0.1f, 0.1f) }, d.Seg(10, 6), StructureMaterial.Monolith, k, true, 0f, false, false, StructureRole.Detail));
						d.Add(StructureShapes.Rod(new Vector3(0f, 0.06f, 0f), new Vector3(0.06f, 0.23f, 0.02f), 0.018f, d.Seg(6, 4), StructureMaterial.Timber, k + 1, StructureRole.Detail));
						break;
				}
			}
		}
	}
}
#endif
