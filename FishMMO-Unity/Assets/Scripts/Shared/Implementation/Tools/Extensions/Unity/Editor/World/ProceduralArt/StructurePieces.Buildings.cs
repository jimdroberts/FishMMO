#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Buildings: houses, longhouse, towers, walls, gatehouse, keep, stilt huts, well, lighthouse, market stall, pier, bridge.</summary>
	public static partial class StructurePieces
	{
		private static void AddBuildings(List<StructurePiece> list)
		{
			Piece(list, "TimberHouseSmall", StructureStyle.Timber, StructureSize.Building, "A half-timbered cottage, 5×6 m; v0 thatched, v1 shingled with the gable to the front.",
				TimberHouseSmall, "building", "house");
			Piece(list, "TimberHouseLarge", StructureStyle.Timber, StructureSize.Building, "A two-storey jettied house, 7×9 m; v1 with a stone ground floor.",
				TimberHouseLarge, "building", "house");
			Piece(list, "StoneHouse", StructureStyle.Stone, StructureSize.Building, "A stone house, 6×7 m; v0 thatched, v1 shingled.", StoneHouse, "building", "house");
			Piece(list, "Longhouse", StructureStyle.Timber, StructureSize.Building, "A long hall, 6×16 m, door in the front gable, crossed gable horns; v1 turf-walled.",
				Longhouse, "building", "house");
			Piece(list, "Watchtower", StructureStyle.Timber, StructureSize.Building, "A timber tower, platform at 6 m under a pyramid roof; v1 on a stone base.",
				Watchtower, "building", "tower", "military");
			Piece(list, "WallSegment", StructureStyle.Stone, StructureSize.Medium, "Six metres of curtain wall; v0 crenellated ashlar 5 m, v1 a coped fieldstone wall 3 m.",
				WallSegment, "wall", "military").ModuleLength = 6f;
			Piece(list, "WallTower", StructureStyle.Stone, StructureSize.Building, "A corner tower for the wall; v0 round and crenellated, v1 square with a pyramid roof.",
				WallTower, "wall", "tower", "military", "building");
			Piece(list, "Gatehouse", StructureStyle.Stone, StructureSize.Building, "Two towers over a 3.5×4.5 m passage with a raised portcullis, 12 m wide; v1 round towers.",
				Gatehouse, "wall", "gate", "military", "building").ModuleLength = 12f;
			Piece(list, "Keep", StructureStyle.Stone, StructureSize.Building, "A squat square keep, 10×10×12 m, turrets at the corners; v1 buttressed under a pyramid roof.",
				Keep, "building", "keep", "tower", "military");
			Piece(list, "StiltHut", StructureStyle.Timber, StructureSize.Building, "A hut on a stilted platform with a ladder; v1 round with a conical roof.",
				StiltHut, "building", "house", "stilt", "swamp", "water");
			Piece(list, "StiltHouse", StructureStyle.Timber, StructureSize.Building, "The small timber house on stilts, with a deck; v1 shingled.",
				StiltHouse, "building", "house", "stilt", "swamp", "water");
			Piece(list, "Well", StructureStyle.Stone, StructureSize.Medium, "A stone well; v0 roofed with a roller, v1 an iron-cranked windlass.", Well, "well", "building");
			Piece(list, "Lighthouse", StructureStyle.Stone, StructureSize.Building, "A banded tower 17 m high with a gallery and an iron lantern; v1 plain stone.",
				Lighthouse, "lighthouse", "building", "tower", "water");
			Piece(list, "MarketStall", StructureStyle.Timber, StructureSize.Medium, "A counter under a striped awning, 3×2 m; v1 plain awning and side poles.",
				MarketStall, "market", "camp");
			var pier = Piece(list, "Pier", StructureStyle.Timber, StructureSize.Medium, "Six metres of plank pier, 3 m wide, along z, on piles to 5 m below the deck; v1 with mooring posts and a ladder.",
				Pier, "dock", "water", "swamp");
			pier.Anchor = StructureAnchor.Deck;
			pier.ModuleLength = 6f;
			var bridge = Piece(list, "BridgeSpan", StructureStyle.Timber, StructureSize.Medium,
				"Four metres of railed plank bridge along z, laid end to end to any length; v0 has a trestle at its −z end, v1 none.", BridgeSpan, "bridge", "water");
			bridge.Anchor = StructureAnchor.Deck;
			bridge.ModuleLength = 4f;
		}

		// ── Houses ────────────────────────────────────────────────────

		private static House Cottage(int variant) => new House
		{
			Width = 5f, Depth = 6f, WallHeight = 2.6f, Rise = 2.1f, Overhang = 0.5f, Wall = 0.25f, Plinth = 0.4f,
			WallMaterial = StructureMaterial.Plaster, RoofMaterial = variant == 0 ? StructureMaterial.Thatch : StructureMaterial.Timber,
			PlinthMaterial = StructureMaterial.Fieldstone, Frame = true, RidgeAlongX = variant == 0, Chimney = true, Windows = 2,
			DoorWidth = 1f, DoorHeight = 2f, DoorX = variant == 0 ? -0.8f : 0f, Key = 1,
		};

		private static void TimberHouseSmall(StructureDraft d) => BuildHouse(d, Cottage(d.Variant));

		private static void TimberHouseLarge(StructureDraft d)
		{
			bool stone = d.Variant == 1;
			var house = new House
			{
				Width = 7f, Depth = 8.5f, WallHeight = 2.7f, Rise = 2.6f, Overhang = 0.5f, Wall = 0.3f, Plinth = 0.4f, Upper = 2.5f,
				WallMaterial = stone ? StructureMaterial.Stone : StructureMaterial.Plaster, RoofMaterial = StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = !stone, RidgeAlongX = true, Chimney = true, Windows = 3,
				DoorWidth = 1.2f, DoorHeight = 2.1f, DoorX = 0f, Key = 1,
			};
			BuildHouse(d, house);
			if (stone && d.Mid)
			{
				// The upper storey and its gables are still timber-framed plaster: re-dress them and frame it.
				foreach (StructureSolid s in d.Solids)
				{
					if (((s.Key >= 7 && s.Key <= 10) || s.Key == 13 || s.Key == 14) && s.Material == StructureMaterial.Stone)
					{
						s.Material = StructureMaterial.Plaster;
					}
				}
				Framing(d, 7f, 9f, 0.4f + 2.7f + 0.24f, 0.4f + 2.7f + 0.24f + 2.5f, 400);
			}
		}

		private static void StoneHouse(StructureDraft d)
		{
			BuildHouse(d, new House
			{
				Width = 6f, Depth = 7f, WallHeight = 3f, Rise = 2.4f, Overhang = 0.4f, Wall = 0.5f, Plinth = 0.3f,
				WallMaterial = StructureMaterial.Stone, RoofMaterial = d.Variant == 0 ? StructureMaterial.Thatch : StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = false, RidgeAlongX = d.Variant == 0, Chimney = true, Windows = 2,
				DoorWidth = 1.1f, DoorHeight = 2.1f, DoorX = d.Variant == 0 ? 0.9f : 0f, Key = 1,
			});
		}

		private static void Longhouse(StructureDraft d)
		{
			bool turf = d.Variant == 1;
			var house = new House
			{
				Width = 6f, Depth = 16f, WallHeight = turf ? 1.6f : 1.9f, Rise = 3.6f, Overhang = 0.7f, Wall = turf ? 0.9f : 0.25f, Plinth = 0.25f,
				WallMaterial = turf ? StructureMaterial.Earth : StructureMaterial.Timber, RoofMaterial = turf ? StructureMaterial.Earth : StructureMaterial.Thatch,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = false, RidgeAlongX = false, Chimney = false, Windows = 0,
				DoorWidth = 1.4f, DoorHeight = 2.2f, DoorX = 0f, Key = 1,
			};
			BuildHouse(d, house);
			float top = house.Plinth + house.WallHeight;
			float ridge = top + house.Rise;
			if (turf)
			{
				// The timber gable fronts a turf house shows, front and back.
				foreach (StructureSolid s in d.Solids)
				{
					if ((s.Key == 13 || s.Key == 14) && s.Material == StructureMaterial.Earth)
					{
						s.Material = StructureMaterial.Timber;
					}
				}
			}
			if (d.Mid)
			{
				// Crossed horns at both gable peaks.
				for (int e = -1; e <= 1; e += 2)
				{
					float z = e * (house.Depth * 0.5f + house.Overhang - 0.1f);
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(-s * 0.9f, ridge - 0.7f, z), new Vector3(s * 0.55f, ridge + 0.75f, z), 0.18f, 0.12f,
							StructureMaterial.Timber, 500 + e * 4 + s, StructureRole.Roof));
					}
				}
			}
			if (d.Fine && !turf)
			{
				// Posts along the long walls, carrying the eaves.
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = 0; i < 6; i++)
					{
						float z = -6.5f + i * 2.6f;
						d.Add(StructureShapes.Beam(new Vector3(s * 3.08f, 0f, z), new Vector3(s * 3.08f, top, z), 0.2f, 0.2f, StructureMaterial.Timber, 520 + i + (s > 0 ? 10 : 0), StructureRole.Detail));
					}
				}
			}
		}

		// ── Towers and walls ──────────────────────────────────────────

		private static void Watchtower(StructureDraft d)
		{
			bool stoneBase = d.Variant == 1;
			const float platform = 6f, half = 1.6f, foot = 2f;
			float legFrom = stoneBase ? 2.2f : -0.4f;
			if (stoneBase)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (2.2f - FoundationDepth) * 0.5f, 0f), new Vector3(foot * 2.2f, 2.2f + FoundationDepth, foot * 2.2f),
					d.Bevel(0.05f), StructureMaterial.Fieldstone, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.95f, -foot * 1.1f - 0.04f), new Vector3(1f, 1.9f, 0.1f), StructureMaterial.Timber, 2, StructureRole.Detail)).Grain = Vector3.up;
			}
			float footSpread = stoneBase ? half + 0.1f : foot;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					var a = new Vector3(sx * footSpread, legFrom, sz * footSpread);
					var b = new Vector3(sx * half, platform + 2.4f, sz * half);
					d.Add(StructureShapes.Beam(a, b, 0.24f, 0.24f, StructureMaterial.Timber, 10 + sx + 3 * sz));
				}
			}
			if (d.Mid)
			{
				// Cross braces on each face, two tiers.
				int k = 20;
				float[] tiers = stoneBase ? new[] { 2.4f, 4.2f, 5.9f } : new[] { 0f, 3f, 5.9f };
				for (int t = 0; t + 1 < tiers.Length; t++)
				{
					float y0 = tiers[t], y1 = tiers[t + 1];
					float w0 = Mathf.Lerp(footSpread, half, Mathf.InverseLerp(legFrom, platform + 2.4f, y0)) + 0.13f;
					float w1 = Mathf.Lerp(footSpread, half, Mathf.InverseLerp(legFrom, platform + 2.4f, y1)) + 0.13f;
					for (int face = 0; face < 4; face++)
					{
						Quaternion q = Quaternion.Euler(0f, face * 90f, 0f);
						d.Add(StructureShapes.Beam(q * new Vector3(-w0, y0 + 0.2f, -w0), q * new Vector3(w1, y1 - 0.1f, -w1), 0.12f, 0.1f, StructureMaterial.Timber, k++, StructureRole.Detail));
						if (d.Fine)
						{
							d.Add(StructureShapes.Beam(q * new Vector3(w0, y0 + 0.2f, -w0), q * new Vector3(-w1, y1 - 0.1f, -w1), 0.12f, 0.1f, StructureMaterial.Timber, k++, StructureRole.Detail));
						}
					}
				}
			}
			// The platform, its rail and the roof on the four legs.
			d.Add(StructureShapes.Box(new Vector3(0f, platform, 0f), new Vector3(half * 2f + 0.8f, 0.2f, half * 2f + 0.8f), StructureMaterial.Timber, 40));
			float edge = half + 0.35f;
			for (int face = 0; face < 4; face++)
			{
				Quaternion q = Quaternion.Euler(0f, face * 90f, 0f);
				d.Add(StructureShapes.Beam(q * new Vector3(-edge, platform + 1.05f, -edge), q * new Vector3(edge, platform + 1.05f, -edge), 0.1f, 0.12f, StructureMaterial.Timber, 41 + face));
				if (d.Mid)
				{
					d.Add(StructureShapes.Block(q * new Vector3(0f, platform + 0.45f, -edge), new Vector3(edge * 2f, 0.7f, 0.06f), q,
						StructureMaterial.Timber, 45 + face, StructureRole.Detail));
				}
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2((half + 0.9f) * 1.414f, platform + 2.4f), new Vector2(0f, platform + 4.2f) }, 4,
				stoneBase ? StructureMaterial.Timber : StructureMaterial.Thatch, 50, false, Mathf.PI * 0.25f, false, false, StructureRole.Roof));
			// A ladder up the front.
			float ladderZ = -(stoneBase ? half + 0.25f : foot - 0.1f);
			float ladderFrom = stoneBase ? 2.2f : -0.1f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.25f, ladderFrom, ladderZ - 0.4f), new Vector3(s * 0.25f, platform + 0.9f, -half - 0.45f), 0.07f, 0.07f, StructureMaterial.Timber, 60 + s));
			}
			if (d.Mid)
			{
				int rungs = Mathf.RoundToInt((platform - ladderFrom) / 0.4f);
				for (int i = 1; i < rungs; i++)
				{
					float t = i / (float)rungs;
					Vector3 p = Vector3.Lerp(new Vector3(0f, ladderFrom, ladderZ - 0.4f), new Vector3(0f, platform + 0.9f, -half - 0.45f), t);
					d.Add(StructureShapes.Beam(p + Vector3.left * 0.28f, p + Vector3.right * 0.28f, 0.05f, 0.05f, StructureMaterial.Timber, 70 + i, StructureRole.Detail));
				}
			}
		}

		private static void WallSegment(StructureDraft d)
		{
			const float module = 6f;
			if (d.Variant == 0)
			{
				const float height = 5f, thick = 1.6f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (0.8f - FoundationDepth) * 0.5f, 0.1f), new Vector3(module, 0.8f + FoundationDepth, thick + 0.4f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(module, height, thick), StructureMaterial.Stone, 2));
				// Parapet on the outer (front) face with merlons; a low rail wall at the back of the walk.
				d.Add(StructureShapes.Box(new Vector3(0f, height + 0.45f, -thick * 0.5f + 0.25f), new Vector3(module, 0.9f, 0.5f), StructureMaterial.Stone, 3));
				d.Add(StructureShapes.Box(new Vector3(0f, height + 0.25f, thick * 0.5f - 0.15f), new Vector3(module, 0.5f, 0.3f), StructureMaterial.Stone, 4));
				Crenellate(d, new Vector3(-module * 0.5f, height + 0.9f, -thick * 0.5f + 0.25f), new Vector3(module * 0.5f, height + 0.9f, -thick * 0.5f + 0.25f),
					0.5f, 0.85f, 0.9f, 0.6f, StructureMaterial.Stone, 10);
				if (d.Mid)
				{
					// A string course along the front.
					d.Add(StructureShapes.Box(new Vector3(0f, height - 0.15f, -thick * 0.5f - 0.06f), new Vector3(module, 0.18f, 0.14f), StructureMaterial.Stone, 5, StructureRole.Detail));
				}
				return;
			}
			const float h = 3f, t = 0.9f;
			d.Add(StructureShapes.Box(new Vector3(0f, (h - FoundationDepth) * 0.5f, 0f), new Vector3(module, h + FoundationDepth, t), StructureMaterial.Fieldstone, 2));
			var coping = new[] { new Vector2(-t * 0.5f - 0.08f, h), new Vector2(t * 0.5f + 0.08f, h), new Vector2(0f, h + 0.45f) };
			d.Add(StructureShapes.Prism(coping, -module * 0.5f, module * 0.5f, StructureMaterial.Stone, 3).Transform(Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f))));
			if (d.Mid)
			{
				// Through-stones sticking out of the face.
				for (int i = 0; i < 4; i++)
				{
					float x = -2.2f + i * 1.45f + d.Range(i, 1, -0.3f, 0.3f);
					float y = 0.8f + d.Range(i, 2, 0f, 1.6f);
					d.Add(RoughStone(d, new Vector3(x, y, -t * 0.5f), new Vector3(0.45f, 0.3f, 0.4f), 0f, 20 + i, 2, StructureMaterial.Fieldstone, StructureRole.Detail));
				}
			}
		}

		private static void WallTower(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float r = 3.2f, wall = 1.1f, height = 9f;
				int seg = d.Seg(16, 10);
				// A battered foot, the wall in blocks (a ruin breaks each its own way), a floor at the top, merlons round it.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(r + 0.5f, -FoundationDepth), new Vector2(r + 0.5f, 0.4f), new Vector2(r, 1.6f), new Vector2(r - wall, 1.6f), new Vector2(r - wall, -FoundationDepth) },
					seg, StructureMaterial.Stone, 1, true, 0f, true));
				RingWall(d, Vector3.zero, r - wall, r, 1.6f, height, seg, StructureMaterial.Stone, 10);
				d.Add(StructureShapes.Cylinder(r - wall + 0.05f, height - 0.35f, height - 0.1f, seg, StructureMaterial.Timber, 40, false, StructureRole.Roof));
				d.Add(StructureShapes.Cylinder(r - wall + 0.05f, 0f, 0.3f, seg, StructureMaterial.Earth, 41));
				if (d.Mid)
				{
					// A corbel band under the parapet.
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r - 0.1f, height - 0.6f), new Vector2(r + 0.25f, height - 0.6f), new Vector2(r + 0.25f, height), new Vector2(r - 0.1f, height) },
						seg, StructureMaterial.Stone, 42, true, 0f, true, false, StructureRole.Detail));
				}
				int merlons = d.Seg(12, 8);
				for (int i = 0; i < merlons; i++)
				{
					float a0 = (i + 0.2f) * 2f * Mathf.PI / merlons, a1 = (i + 0.75f) * 2f * Mathf.PI / merlons;
					d.Add(RingBlock(Vector3.zero, r - 0.55f, r + 0.2f, height, height + 1f, a0, a1, StructureMaterial.Stone, 60 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 1.1f, -r - 0.42f), new Vector3(1.1f, 2.2f, 0.12f), StructureMaterial.Timber, 80, StructureRole.Detail)).Grain = Vector3.up;
				if (d.Mid)
				{
					for (int i = 0; i < 4; i++)
					{
						float a = (i * 90f + 45f) * Mathf.Deg2Rad;
						Window(d, new Vector3(Mathf.Cos(a) * r, 5.5f, Mathf.Sin(a) * r), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 90 + i * 4);
					}
				}
				return;
			}
			// Square, with a pyramid roof.
			const float w = 6f, hs = 10f, th = 1f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (0.8f - FoundationDepth) * 0.5f, 0f), new Vector3(w + 0.6f, 0.8f + FoundationDepth, w + 0.6f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
			Walls(d, Vector3.zero, w, w, th, 0.8f, hs, StructureMaterial.Stone, 2);
			d.Add(StructureShapes.Box(new Vector3(0f, 0.9f, 0f), new Vector3(w - 2f * th, 0.2f, w - 2f * th), StructureMaterial.Earth, 6));
			d.Add(StructureShapes.Box(new Vector3(0f, hs + 0.15f, 0f), new Vector3(w + 0.5f, 0.3f, w + 0.5f), StructureMaterial.Stone, 7, StructureRole.Roof));
			d.Add(StructureShapes.Lathe(new[] { new Vector2((w * 0.5f + 0.5f) * 1.414f, hs + 0.3f), new Vector2(0f, hs + 4f) }, 4, StructureMaterial.Timber, 8, false, Mathf.PI * 0.25f, false, false, StructureRole.Roof));
			d.Add(StructureShapes.Box(new Vector3(0f, 1.9f, -w * 0.5f - 0.05f), new Vector3(1.2f, 2.2f, 0.12f), StructureMaterial.Timber, 9, StructureRole.Detail)).Grain = Vector3.up;
			if (d.Mid)
			{
				Window(d, new Vector3(0f, 6.5f, -w * 0.5f), Vector3.back, 20);
				Window(d, new Vector3(-w * 0.5f, 6.5f, 0f), Vector3.left, 24);
				Window(d, new Vector3(w * 0.5f, 6.5f, 0f), Vector3.right, 28);
			}
		}

		private static void Gatehouse(StructureDraft d)
		{
			bool round = d.Variant == 1;
			const float towerH = 9f, depth = 4f;
			for (int s = -1; s <= 1; s += 2)
			{
				var c = new Vector3(s * 4f, 0f, 0f);
				int key = 10 + (s > 0 ? 40 : 0);
				if (round)
				{
					int seg = d.Seg(14, 8);
					d.Add(StructureShapes.Lathe(new[] { new Vector2(2.4f, -FoundationDepth), new Vector2(2.4f, 0.6f), new Vector2(2.1f, 1.4f), new Vector2(2.1f, towerH), new Vector2(0f, towerH) },
						seg, StructureMaterial.Stone, key, true).Transform(Matrix4x4.Translate(c)));
					int merlons = d.Seg(10, 6);
					for (int i = 0; i < merlons; i++)
					{
						float a0 = (i + 0.2f) * 2f * Mathf.PI / merlons, a1 = (i + 0.75f) * 2f * Mathf.PI / merlons;
						d.Add(RingBlock(c, 1.7f, 2.2f, towerH, towerH + 0.9f, a0, a1, StructureMaterial.Stone, key + 10 + i, StructureRole.Detail));
					}
				}
				else
				{
					d.Add(StructureShapes.ChamferBox(c + new Vector3(0f, (towerH - FoundationDepth) * 0.5f, 0f), new Vector3(4f, towerH + FoundationDepth, 4.4f), d.Bevel(0.05f), StructureMaterial.Stone, key));
					for (int f = 0; f < 4; f++)
					{
						Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
						Vector3 a = c + q * new Vector3(-(f % 2 == 0 ? 2f : 2.2f) + 0.25f, towerH, -(f % 2 == 0 ? 2.2f : 2f) + 0.25f);
						Vector3 b = c + q * new Vector3((f % 2 == 0 ? 2f : 2.2f) - 0.25f, towerH, -(f % 2 == 0 ? 2.2f : 2f) + 0.25f);
						Crenellate(d, a, b, 0.5f, 0.9f, 0.8f, 0.55f, StructureMaterial.Stone, key + 10 + f * 8);
					}
				}
				if (d.Mid)
				{
					Window(d, c + new Vector3(0f, 6f, -(round ? 2.1f : 2.2f)), Vector3.back, key + 60);
				}
			}
			// The block over the passage, its parapet, and the portcullis hanging half raised.
			const float passageW = 3.5f, passageH = 4.5f;
			d.Add(StructureShapes.Box(new Vector3(0f, (passageH + towerH - 1f) * 0.5f, 0f), new Vector3(4.6f, towerH - 1f - passageH, depth), StructureMaterial.Stone, 100));
			d.Add(StructureShapes.Box(new Vector3(0f, -0.15f, 0f), new Vector3(passageW + 0.4f, 0.3f, depth), StructureMaterial.Fieldstone, 101));
			Crenellate(d, new Vector3(-2.3f, towerH - 1f, -depth * 0.5f + 0.25f), new Vector3(2.3f, towerH - 1f, -depth * 0.5f + 0.25f), 0.5f, 0.9f, 0.8f, 0.55f, StructureMaterial.Stone, 110);
			int bars = d.Lod == 0 ? 7 : d.Lod == 1 ? 5 : 3;
			float bottom = passageH - 1.4f;
			for (int i = 0; i < bars; i++)
			{
				float x = -passageW * 0.5f + passageW * (i + 0.5f) / bars;
				d.Add(StructureShapes.Box(new Vector3(x, (bottom + passageH) * 0.5f, -depth * 0.5f + 0.4f), new Vector3(0.08f, passageH - bottom, 0.08f), StructureMaterial.Iron, 120 + i, StructureRole.Detail));
			}
			if (d.Mid)
			{
				foreach (float y in new[] { bottom + 0.3f, bottom + 0.9f })
				{
					d.Add(StructureShapes.Box(new Vector3(0f, y, -depth * 0.5f + 0.4f), new Vector3(passageW, 0.08f, 0.1f), StructureMaterial.Iron, y < bottom + 0.5f ? 130 : 131, StructureRole.Detail));
				}
			}
		}

		private static void Keep(StructureDraft d)
		{
			bool roofed = d.Variant == 1;
			float w = roofed ? 12f : 10f, dz = roofed ? 9f : 10f, h = 12f, th = 1.6f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (1f - FoundationDepth) * 0.5f, 0f), new Vector3(w + 1.2f, 1f + FoundationDepth, dz + 1.2f), d.Bevel(0.05f), StructureMaterial.Stone, 1));
			Walls(d, Vector3.zero, w, dz, th, 1f, h, StructureMaterial.Stone, 2);
			d.Add(StructureShapes.Box(new Vector3(0f, 1.1f, 0f), new Vector3(w - 2f * th, 0.2f, dz - 2f * th), StructureMaterial.Earth, 6));
			d.Add(StructureShapes.Box(new Vector3(0f, h - 0.2f, 0f), new Vector3(w - 2f * th + 0.1f, 0.4f, dz - 2f * th + 0.1f), StructureMaterial.Timber, 7, StructureRole.Roof));
			if (roofed)
			{
				// Pilaster buttresses on the long walls, and a hipped roof.
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = -1; i <= 1; i++)
					{
						d.Add(StructureShapes.ChamferBox(new Vector3(i * 3.6f, h * 0.45f, s * (dz * 0.5f + 0.3f)), new Vector3(1.1f, h * 0.9f, 0.6f), d.Bevel(0.04f), StructureMaterial.Stone, 20 + i + 3 * s));
					}
				}
				var ridge = new[] { new Vector2(-(w * 0.5f + 0.5f), h), new Vector2(w * 0.5f + 0.5f, h), new Vector2(w * 0.5f - 2.5f, h + 4f), new Vector2(-(w * 0.5f - 2.5f), h + 4f) };
				StructureSolid roof = StructureShapes.Prism(ridge, -dz * 0.5f - 0.5f, dz * 0.5f + 0.5f, StructureMaterial.Timber, 30, StructureRole.Roof);
				// Hip the ends: cut each gable end back to a slope.
				float k = 4f / (dz * 0.5f + 0.5f);
				var n0 = new Vector3(0f, 1f, k).normalized;
				var n1 = new Vector3(0f, 1f, -k).normalized;
				roof.Clip(n0, Vector3.Dot(n0, new Vector3(0f, h + 4f, 0f)));
				roof.Clip(n1, Vector3.Dot(n1, new Vector3(0f, h + 4f, 0f)));
				d.Add(roof);
			}
			else
			{
				// Merlons on every edge, and a turret proud at each corner.
				for (int f = 0; f < 4; f++)
				{
					Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
					float hx = (f % 2 == 0 ? w : dz) * 0.5f, hz = (f % 2 == 0 ? dz : w) * 0.5f;
					Crenellate(d, q * new Vector3(-hx + 1.4f, h, -hz + 0.3f), q * new Vector3(hx - 1.4f, h, -hz + 0.3f), 0.6f, 1f, 0.9f, 0.6f, StructureMaterial.Stone, 40 + f * 10);
				}
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						var c = new Vector3(sx * (w * 0.5f - 0.6f), 0f, sz * (dz * 0.5f - 0.6f));
						d.Add(StructureShapes.ChamferBox(c + new Vector3(0f, (h + 2.2f) * 0.5f, 0f), new Vector3(2.4f, h + 2.2f, 2.4f), d.Bevel(0.04f), StructureMaterial.Stone, 90 + sx + 3 * sz));
						if (d.Mid)
						{
							d.Add(StructureShapes.ChamferBox(c + new Vector3(0f, h + 2.5f, 0f), new Vector3(2.7f, 0.6f, 2.7f), d.Bevel(0.03f), StructureMaterial.Stone, 100 + sx + 3 * sz, StructureRole.Detail));
						}
					}
				}
			}
			// The door up a flight of steps, and arrow slits.
			d.Add(StructureShapes.Box(new Vector3(0f, 3.2f, -dz * 0.5f - 0.06f), new Vector3(1.4f, 2.4f, 0.14f), StructureMaterial.Timber, 110, StructureRole.Detail)).Grain = Vector3.up;
			int steps = d.Mid ? 8 : 3;
			for (int i = 0; i < steps; i++)
			{
				float t = (i + 1f) / steps;
				float top = 2f * t;
				float zFront = -dz * 0.5f - 0.06f - (1f - t) * 3.2f - 0.6f;
				d.Add(StructureShapes.Box(new Vector3(0f, (top - 0.3f) * 0.5f, (zFront + (-dz * 0.5f)) * 0.5f), new Vector3(1.8f, top + 0.3f, -dz * 0.5f - zFront), StructureMaterial.Stone, 120 + i));
			}
			if (d.Mid)
			{
				for (int f = 0; f < 4; f++)
				{
					Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
					float hz = (f % 2 == 0 ? dz : w) * 0.5f;
					for (int i = -1; i <= 1; i += 2)
					{
						d.Add(StructureShapes.Block(q * new Vector3(i * 2.4f, 7.5f, -hz - 0.02f), new Vector3(0.18f, 1.3f, 0.06f), q, StructureMaterial.Iron, 130 + f * 2 + (i > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
		}

		// ── Stilts ────────────────────────────────────────────────────

		/// <summary>A deck on log stilts: <paramref name="size"/> wide and deep, its top at <paramref name="deck"/>, with a ladder at the front.</summary>
		private static void StiltPlatform(StructureDraft d, Vector2 size, float deck, int key)
		{
			d.Add(StructureShapes.Box(new Vector3(0f, deck - 0.12f, 0f), new Vector3(size.x, 0.24f, size.y), StructureMaterial.Timber, key));
			int nx = size.x > 4.5f ? 4 : 3, nz = size.y > 4.5f ? 4 : 3;
			for (int i = 0; i < nx; i++)
			{
				for (int j = 0; j < nz; j++)
				{
					float x = Mathf.Lerp(-size.x * 0.5f + 0.25f, size.x * 0.5f - 0.25f, i / (nx - 1f));
					float z = Mathf.Lerp(-size.y * 0.5f + 0.25f, size.y * 0.5f - 0.25f, j / (nz - 1f));
					bool inner = i > 0 && i < nx - 1 && j > 0 && j < nz - 1;
					if (inner && !d.Mid)
					{
						continue;
					}
					Log(d, new Vector3(x, -1.2f, z), new Vector3(x + d.Range(key + i * 7 + j, 1, -0.05f, 0.05f), deck - 0.2f, z), 0.13f, key + 10 + i * 7 + j, 0f, StructureMaterial.Timber, 7);
				}
			}
			// The ladder.
			float front = -size.y * 0.5f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.25f, -0.3f, front - 0.9f), new Vector3(s * 0.25f, deck + 0.05f, front - 0.05f), 0.07f, 0.07f, StructureMaterial.Timber, key + 60 + s));
			}
			if (d.Mid)
			{
				int rungs = Mathf.Max(2, Mathf.RoundToInt(deck / 0.35f));
				for (int i = 1; i < rungs; i++)
				{
					float t = i / (float)rungs;
					Vector3 p = Vector3.Lerp(new Vector3(0f, -0.3f, front - 0.9f), new Vector3(0f, deck + 0.05f, front - 0.05f), t);
					d.Add(StructureShapes.Beam(p + Vector3.left * 0.28f, p + Vector3.right * 0.28f, 0.05f, 0.05f, StructureMaterial.Timber, key + 70 + i, StructureRole.Detail));
				}
			}
		}

		private static void StiltHut(StructureDraft d)
		{
			const float deck = 2f;
			StiltPlatform(d, new Vector2(4.4f, 4.4f), deck, 200);
			if (d.Variant == 0)
			{
				BuildHouse(d, new House
				{
					Width = 3.2f, Depth = 3.2f, WallHeight = 2.1f, Rise = 1.5f, Overhang = 0.45f, Wall = 0.12f, Floor = deck,
					WallMaterial = StructureMaterial.Timber, RoofMaterial = StructureMaterial.Thatch, PlinthMaterial = StructureMaterial.Timber,
					RidgeAlongX = true, Windows = 1, DoorWidth = 0.9f, DoorHeight = 1.8f, DoorX = 0f, Key = 1,
				});
				return;
			}
			int seg = d.Seg(12, 8);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.7f, deck), new Vector2(1.7f, deck + 2f) }, seg, StructureMaterial.Timber, 1, false, 0f, false, true));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(2.4f, deck + 1.65f), new Vector2(0f, deck + 4.1f) }, seg, StructureMaterial.Thatch, 2, true, 0f, false, true, StructureRole.Roof));
			d.Add(StructureShapes.Box(new Vector3(0f, deck + 0.9f, -1.68f), new Vector3(0.85f, 1.75f, 0.1f), StructureMaterial.Timber, 3, StructureRole.Detail)).Grain = Vector3.up;
		}

		private static void StiltHouse(StructureDraft d)
		{
			const float deck = 1.8f;
			StiltPlatform(d, new Vector2(6.4f, 8f), deck, 600);
			House h = Cottage(d.Variant);
			h.Floor = deck;
			h.Plinth = 0f;
			h.Chimney = false;
			BuildHouse(d, h);
		}

		// ── Utility ───────────────────────────────────────────────────

		private static void Well(StructureDraft d)
		{
			int seg = d.Seg(14, 8);
			// The ring: an annulus (up the outside, across the top, down the inside), and dark water inside it.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.65f, -0.4f), new Vector2(0.98f, -0.4f), new Vector2(0.98f, 0.8f), new Vector2(0.92f, 0.88f), new Vector2(0.71f, 0.88f), new Vector2(0.65f, 0.8f) },
				seg, StructureMaterial.Fieldstone, 1, true, 0f, true));
			d.Add(StructureShapes.Cylinder(0.66f, -0.4f, 0.25f, seg, StructureMaterial.Iron, 2));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.85f, -0.3f, 0f), new Vector3(s * 0.85f, 2.1f, 0f), 0.16f, 0.16f, StructureMaterial.Timber, 3 + s));
			}
			StructureSolid roller = StructureShapes.Rod(new Vector3(-0.95f, 1.55f, 0f), new Vector3(0.95f, 1.55f, 0f), 0.08f, d.Seg(8, 5), StructureMaterial.Timber, 6);
			d.Add(roller);
			// A bucket hanging under the roller.
			d.Add(StructureShapes.Rod(new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1.1f, 0f), 0.008f, 4, StructureMaterial.Timber, 7, StructureRole.Detail));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.12f, 0.8f), new Vector2(0.15f, 1.1f), new Vector2(0f, 1.1f) }, d.Seg(8, 5), StructureMaterial.Timber, 8, true, 0f, false, true, StructureRole.Detail));
			if (d.Variant == 0)
			{
				GableRoof(d, 2f, 1.4f, 2.1f, 0.6f, 0.15f, 0.07f, StructureMaterial.Timber, 10, Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)));
				d.Add(StructureShapes.Beam(new Vector3(-0.95f, 2.15f, 0f), new Vector3(0.95f, 2.15f, 0f), 0.14f, 0.14f, StructureMaterial.Timber, 12));
				return;
			}
			// An iron crank on the roller's end.
			d.Add(StructureShapes.Beam(new Vector3(1.0f, 1.55f, 0f), new Vector3(1.0f, 1.25f, -0.05f), 0.04f, 0.04f, StructureMaterial.Iron, 14));
			d.Add(StructureShapes.Beam(new Vector3(1.0f, 1.25f, -0.05f), new Vector3(1.18f, 1.25f, -0.05f), 0.035f, 0.035f, StructureMaterial.Iron, 15));
		}

		private static void Lighthouse(StructureDraft d)
		{
			bool banded = d.Variant == 0;
			int seg = d.Seg(20, 10);
			const float r0 = 3.2f, r1 = 2.3f, h = 14f;
			// The base, then the tower in three bands.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (0.8f - FoundationDepth) * 0.5f, 0f), new Vector3(r0 * 2.4f, 0.8f + FoundationDepth, r0 * 2.4f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
			for (int b = 0; b < 3; b++)
			{
				float y0 = 0.8f + (h - 0.8f) * b / 3f, y1 = 0.8f + (h - 0.8f) * (b + 1) / 3f;
				float ra = Mathf.Lerp(r0, r1, (y0 - 0.8f) / (h - 0.8f)), rb = Mathf.Lerp(r0, r1, (y1 - 0.8f) / (h - 0.8f));
				StructureMaterial m = banded ? (b % 2 == 0 ? StructureMaterial.Plaster : StructureMaterial.Dyed) : StructureMaterial.Stone;
				d.Add(StructureShapes.Lathe(new[] { new Vector2(ra, y0), new Vector2(rb, y1) }, seg, m, 2 + b));
			}
			// Gallery, rail, lantern, cap.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r1 + 0.7f, h), new Vector2(r1 + 0.7f, h + 0.3f), new Vector2(0f, h + 0.3f) }, seg, StructureMaterial.Stone, 10));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r1 + 0.6f, h + 1.05f), new Vector2(r1 + 0.68f, h + 1.05f), new Vector2(r1 + 0.68f, h + 1.12f), new Vector2(r1 + 0.6f, h + 1.12f) },
				seg, StructureMaterial.Iron, 11, true, 0f, true));
			if (d.Mid)
			{
				int posts = d.Fine ? 12 : 8;
				for (int i = 0; i < posts; i++)
				{
					float a = i * 2f * Mathf.PI / posts;
					var p = new Vector3(Mathf.Cos(a) * (r1 + 0.64f), 0f, Mathf.Sin(a) * (r1 + 0.64f));
					d.Add(StructureShapes.Box(p + Vector3.up * (h + 0.7f), new Vector3(0.05f, 0.8f, 0.05f), StructureMaterial.Iron, 20 + i, StructureRole.Detail));
				}
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.4f, h + 0.3f), new Vector2(1.4f, h + 0.9f) }, 8, StructureMaterial.Iron, 40, false));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.25f, h + 0.9f), new Vector2(1.25f, h + 2.4f) }, 8, StructureMaterial.Plaster, 41, false));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.65f, h + 2.4f), new Vector2(0f, h + 3.5f) }, 8, StructureMaterial.Iron, 42, false, 0f, false, false, StructureRole.Roof));
			d.Add(StructureShapes.Sphere(new Vector3(0f, h + 3.6f, 0f), new Vector3(0.18f, 0.18f, 0.18f), d.Seg(8, 4), d.Fine ? 5 : 3, StructureMaterial.Iron, 43));
			if (d.Mid)
			{
				// Mullions round the lantern.
				for (int i = 0; i < 8; i++)
				{
					float a = (i + 0.5f) * Mathf.PI / 4f;
					var p = new Vector3(Mathf.Cos(a) * 1.3f, h + 1.65f, Mathf.Sin(a) * 1.3f);
					d.Add(StructureShapes.Block(p, new Vector3(0.08f, 1.5f, 0.08f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), StructureMaterial.Iron, 50 + i, StructureRole.Detail));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, 1.9f, -r0 + 0.02f), new Vector3(1.1f, 2.2f, 0.3f), StructureMaterial.Timber, 60, StructureRole.Detail)).Grain = Vector3.up;
			if (d.Mid)
			{
				Window(d, new Vector3(0f, 7f, -Mathf.Lerp(r0, r1, (7f - 0.8f) / (h - 0.8f))), Vector3.back, 62);
			}
		}

		private static void MarketStall(StructureDraft d)
		{
			bool striped = d.Variant == 0;
			const float w = 3f, dz = 2f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					float top = sz < 0 ? 2.4f : 2.0f;
					d.Add(StructureShapes.Beam(new Vector3(sx * (w * 0.5f - 0.08f), -0.2f, sz * (dz * 0.5f - 0.08f)), new Vector3(sx * (w * 0.5f - 0.08f), top, sz * (dz * 0.5f - 0.08f)),
						0.12f, 0.12f, StructureMaterial.Timber, 1 + sx + 3 * sz));
				}
			}
			// The counter at the front, a shelf behind.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.95f, -dz * 0.5f + 0.35f), new Vector3(w - 0.1f, 0.1f, 0.7f), StructureMaterial.Timber, 10));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.47f, -dz * 0.5f + 0.1f), new Vector3(w - 0.2f, 0.9f, 0.06f), StructureMaterial.Timber, 11));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 1.1f, dz * 0.5f - 0.2f), new Vector3(w - 0.2f, 0.06f, 0.35f), StructureMaterial.Timber, 12, StructureRole.Detail));
			}
			// The awning, sloping from front to back: in slats of two cloths, or one sheet.
			float slope = Mathf.Atan2(0.4f, dz + 0.6f) * Mathf.Rad2Deg;
			int slats = striped ? (d.Lod == 2 ? 1 : 6) : 1;
			for (int i = 0; i < slats; i++)
			{
				float x0 = -w * 0.5f - 0.2f + (w + 0.4f) * i / slats, x1 = -w * 0.5f - 0.2f + (w + 0.4f) * (i + 1) / slats;
				StructureMaterial cloth = striped && slats > 1 && i % 2 == 1 ? StructureMaterial.Canvas : StructureMaterial.Dyed;
				d.Add(StructureShapes.Block(new Vector3((x0 + x1) * 0.5f, 2.25f, 0f), new Vector3(x1 - x0, 0.04f, dz + 0.6f), Quaternion.Euler(slope, 0f, 0f), cloth, 20 + i, StructureRole.Roof));
			}
			if (d.Mid)
			{
				// Wares: two small crates and a basket on the counter.
				d.Add(StructureShapes.Box(new Vector3(-0.9f, 1.17f, -dz * 0.5f + 0.35f), new Vector3(0.5f, 0.34f, 0.4f), StructureMaterial.Timber, 30, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(-0.3f, 1.12f, -dz * 0.5f + 0.4f), new Vector3(0.4f, 0.24f, 0.35f), StructureMaterial.Timber, 31, StructureRole.Detail));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.18f, 1f), new Vector2(0.24f, 1.22f), new Vector2(0f, 1.18f) }, d.Seg(10, 6), StructureMaterial.Thatch, 32, true, 0f, false, false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(new Vector3(0.7f, 0f, -dz * 0.5f + 0.35f))));
			}
			if (!striped)
			{
				// Side poles holding the awning's front edge out.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * (w * 0.5f + 0.15f), -0.2f, -dz * 0.5f - 0.3f), new Vector3(s * (w * 0.5f + 0.15f), 2.35f, -dz * 0.5f - 0.3f), 0.08f, 0.08f, StructureMaterial.Timber, 40 + s));
				}
			}
		}

		// ── Water ─────────────────────────────────────────────────────

		/// <summary>A plank deck along z from <paramref name="z0"/> to <paramref name="z1"/>, its top at y = 0, on two stringers.</summary>
		private static void Deck(StructureDraft d, float width, float z0, float z1, int key)
		{
			float length = z1 - z0;
			if (d.Fine)
			{
				int planks = Mathf.RoundToInt(length / 0.3f);
				float pitch = length / planks;
				for (int i = 0; i < planks; i++)
				{
					float z = z0 + pitch * (i + 0.5f);
					float wobble = d.Range(key + i, 1, -0.04f, 0.04f);
					d.Add(StructureShapes.Box(new Vector3(wobble, -0.04f, z), new Vector3(width + d.Range(key + i, 2, -0.05f, 0.05f), 0.08f, pitch - 0.02f), StructureMaterial.Timber, key + i)).Grain = Vector3.right;
				}
			}
			else
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, (z0 + z1) * 0.5f), new Vector3(width, 0.08f, length), StructureMaterial.Timber, key)).Grain = Vector3.right;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * (width * 0.5f - 0.25f), -0.2f, z0), new Vector3(s * (width * 0.5f - 0.25f), -0.2f, z1), 0.15f, 0.24f, StructureMaterial.Timber, key + 100 + s));
			}
		}

		private static void Pier(StructureDraft d)
		{
			const float width = 3f, length = 6f;
			Deck(d, width, -length * 0.5f, length * 0.5f, 1);
			bool moored = d.Variant == 1;
			for (int s = -1; s <= 1; s += 2)
			{
				for (int e = -1; e <= 1; e += 2)
				{
					float x = s * (width * 0.5f + 0.05f), z = e * (length * 0.5f - 0.4f);
					float top = moored && e > 0 ? 0.75f : -0.1f;
					Log(d, new Vector3(x, -5f, z), new Vector3(x, top, z), 0.17f, 200 + s + 3 * e, 0f, StructureMaterial.Timber, 8);
				}
				// A cross brace under the deck between the piles on each side.
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * (width * 0.5f + 0.2f), -2.4f, -length * 0.5f + 0.4f), new Vector3(s * (width * 0.5f + 0.2f), -0.35f, length * 0.5f - 0.4f), 0.1f, 0.18f,
						StructureMaterial.Timber, 210 + s, StructureRole.Detail));
				}
			}
			if (moored)
			{
				// A ladder down the side into the water.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(width * 0.5f + 0.15f, -2.5f, s * 0.25f), new Vector3(width * 0.5f + 0.15f, 0.5f, s * 0.25f), 0.07f, 0.07f, StructureMaterial.Timber, 220 + s));
				}
				if (d.Mid)
				{
					for (int i = 0; i < 7; i++)
					{
						float y = -2.3f + i * 0.4f;
						d.Add(StructureShapes.Beam(new Vector3(width * 0.5f + 0.15f, y, -0.28f), new Vector3(width * 0.5f + 0.15f, y, 0.28f), 0.05f, 0.05f, StructureMaterial.Timber, 230 + i, StructureRole.Detail));
					}
				}
			}
		}

		private static void BridgeSpan(StructureDraft d)
		{
			const float width = 2.6f, length = 4f;
			Deck(d, width, -length * 0.5f, length * 0.5f, 1);
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (width * 0.5f + 0.06f);
				d.Add(StructureShapes.Beam(new Vector3(x, -0.2f, -length * 0.5f + 0.06f), new Vector3(x, 1.1f, -length * 0.5f + 0.06f), 0.12f, 0.12f, StructureMaterial.Timber, 200 + s));
				d.Add(StructureShapes.Beam(new Vector3(x, 1.0f, -length * 0.5f), new Vector3(x, 1.0f, length * 0.5f), 0.1f, 0.12f, StructureMaterial.Timber, 203 + s));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, 0.5f, -length * 0.5f), new Vector3(x, 0.5f, length * 0.5f), 0.07f, 0.08f, StructureMaterial.Timber, 206 + s, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(x, -0.15f, -length * 0.5f + 0.15f), new Vector3(x, 0.95f, length * 0.5f - 0.15f), 0.07f, 0.07f, StructureMaterial.Timber, 209 + s, StructureRole.Detail));
				}
			}
			if (d.Variant == 0)
			{
				// A trestle at the −z end: two legs down to 4 m, braced.
				float z = -length * 0.5f + 0.3f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * (width * 0.5f + 0.25f), -4f, z), new Vector3(s * (width * 0.5f - 0.25f), -0.3f, z), 0.2f, 0.2f, StructureMaterial.Timber, 220 + s));
				}
				d.Add(StructureShapes.Beam(new Vector3(-width * 0.5f - 0.1f, -0.42f, z), new Vector3(width * 0.5f + 0.1f, -0.42f, z), 0.18f, 0.22f, StructureMaterial.Timber, 223));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(-width * 0.5f - 0.15f, -3.6f, z), new Vector3(width * 0.5f - 0.3f, -0.6f, z), 0.12f, 0.12f, StructureMaterial.Timber, 224, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(width * 0.5f + 0.15f, -3.6f, z), new Vector3(-width * 0.5f + 0.3f, -0.6f, z), 0.12f, 0.12f, StructureMaterial.Timber, 225, StructureRole.Detail));
				}
			}
		}
	}
}
#endif
