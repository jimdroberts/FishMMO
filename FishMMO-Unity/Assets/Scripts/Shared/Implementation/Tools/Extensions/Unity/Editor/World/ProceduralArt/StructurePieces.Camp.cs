#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Camp pieces: tents, lean-tos, fire rings, palisades, stores, banners, bedding.</summary>
	public static partial class StructurePieces
	{
		private static void AddCamp(List<StructurePiece> list)
		{
			Piece(list, "TentSmall", StructureStyle.Hide, StructureSize.Medium, "A two-person ridge tent; v1 dyed with low side walls.", TentSmall, "tent", "camp");
			Piece(list, "TentMedium", StructureStyle.Hide, StructureSize.Medium, "A walled tent; v1 dyed with a porch awning.", TentMedium, "tent", "camp");
			Piece(list, "TentLarge", StructureStyle.Hide, StructureSize.Medium, "A round pavilion with a pennant; v1 a yurt.", TentLarge, "tent", "camp");
			Piece(list, "LeanTo", StructureStyle.Timber, StructureSize.Medium, "A pole lean-to; v0 thatched, v1 hide with a side wall.", LeanTo, "camp", "tent");
			Piece(list, "CampfireRing", StructureStyle.Stone, StructureSize.Small, "A ring of stones round ash and logs; v1 with a cooking tripod.", CampfireRing, "camp", "fire")
				.Collides = false;
			Piece(list, "PalisadeSegment", StructureStyle.Timber, StructureSize.Medium, "Four metres of sharpened logs on two rails; v1 ragged.", PalisadeSegment,
				"camp", "wall", "palisade", "military").ModuleLength = 4f;
			Piece(list, "PalisadeGate", StructureStyle.Timber, StructureSize.Medium, "A palisade gateway four metres wide; v0 leaves open, v1 shut.", PalisadeGate,
				"camp", "wall", "palisade", "gate", "military").ModuleLength = 4f;
			Piece(list, "Crate", StructureStyle.Timber, StructureSize.Small, "A battened crate; v1 long.", Crate, "camp", "storage");
			Piece(list, "Barrel", StructureStyle.Timber, StructureSize.Small, "A hooped barrel; v1 lying on its side.", Barrel, "camp", "storage");
			Piece(list, "BannerPole", StructureStyle.Timber, StructureSize.Small, "A pole with a hanging banner; v1 a pennant.", BannerPole, "camp", "banner", "military");
			Piece(list, "Bedroll", StructureStyle.Hide, StructureSize.Small, "A blanket and pillow roll; v1 rolled up.", Bedroll, "camp", "bedding").Collides = false;
		}

		// ── Tents ─────────────────────────────────────────────────────

		private static void TentSmall(StructureDraft d)
		{
			bool walls = d.Variant == 1;
			StructureMaterial cloth = walls ? StructureMaterial.Dyed : StructureMaterial.Canvas;
			float half = walls ? 1.2f : 1.1f, length = walls ? 3.6f : 3f, ridge = walls ? 1.6f : 1.35f, wall = walls ? 0.4f : 0f;
			var outline = walls
				? new[] { new Vector2(-half, -0.05f), new Vector2(half, -0.05f), new Vector2(half, wall), new Vector2(0f, ridge), new Vector2(-half, wall) }
				: new[] { new Vector2(-half, -0.05f), new Vector2(half, -0.05f), new Vector2(0f, ridge) };
			d.Add(StructureShapes.Prism(outline, -length * 0.5f, length * 0.5f, cloth, 1));
			// Poles at the ends, standing a hand above the ridge, and a ridge pole.
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(0f, -0.1f, s * (length * 0.5f + 0.04f)), new Vector3(0f, ridge + 0.18f, s * (length * 0.5f + 0.04f)), 0.03f, 2 + s, 0f, StructureMaterial.Timber, 6);
			}
			if (d.Mid)
			{
				Log(d, new Vector3(0f, ridge + 0.02f, -length * 0.5f - 0.1f), new Vector3(0f, ridge + 0.02f, length * 0.5f + 0.1f), 0.025f, 4, 0f, StructureMaterial.Timber, 6);
			}
			if (d.Fine)
			{
				// The door flap, folded back on one side of the front.
				var flap = new[] { new Vector2(0f, 0f), new Vector2(half * 0.7f, 0f), new Vector2(0f, ridge * 0.9f) };
				StructureSolid f = StructureShapes.Prism(flap, -0.02f, 0.02f, cloth, 5, StructureRole.Detail);
				d.Add(f.Transform(Matrix4x4.TRS(new Vector3(0.05f, 0f, -length * 0.5f - 0.05f), Quaternion.Euler(0f, -35f, 0f), Vector3.one)));
				// A dark opening on the other side.
				var gap = new[] { new Vector2(-half * 0.55f, 0f), new Vector2(0f, 0f), new Vector2(0f, ridge * 0.85f) };
				d.Add(StructureShapes.Prism(gap, -length * 0.5f - 0.012f, -length * 0.5f + 0.01f, StructureMaterial.Ash, 6, StructureRole.Detail));
				// Guy stakes.
				for (int s = -1; s <= 1; s += 2)
				{
					for (int e = -1; e <= 1; e += 2)
					{
						Log(d, new Vector3(s * (half + 0.45f), -0.15f, e * length * 0.4f), new Vector3(s * (half + 0.42f), 0.12f, e * length * 0.4f), 0.025f, 20 + s + 3 * e, 0f, StructureMaterial.Timber, 4);
					}
				}
			}
		}

		private static void TentMedium(StructureDraft d)
		{
			bool dyed = d.Variant == 1;
			StructureMaterial cloth = dyed ? StructureMaterial.Dyed : StructureMaterial.Canvas;
			BuildHouse(d, new House
			{
				Width = 3.4f, Depth = 4.4f, WallHeight = 1.4f, Rise = 1.2f, Overhang = 0.18f, Wall = 0.05f, Plinth = 0f,
				WallMaterial = cloth, RoofMaterial = cloth, PlinthMaterial = StructureMaterial.Earth,
				DoorWidth = 1.1f, DoorHeight = 1.8f, DoorX = 0f, DoorMaterial = StructureMaterial.Ash, Key = 1,
			});
			// The pole at each gable's peak, and pegs under the eaves.
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(0f, -0.1f, s * 2.25f), new Vector3(0f, 2.85f, s * 2.25f), 0.04f, 100 + s, 0f, StructureMaterial.Timber, 6);
			}
			if (dyed)
			{
				// A porch awning on two poles over the door.
				d.Add(StructureShapes.Block(new Vector3(0f, 1.95f, -2.95f), new Vector3(2.4f, 0.04f, 1.4f), Quaternion.Euler(-12f, 0f, 0f), cloth, 110, StructureRole.Roof));
				for (int s = -1; s <= 1; s += 2)
				{
					Log(d, new Vector3(s * 1.1f, -0.1f, -3.55f), new Vector3(s * 1.1f, 1.85f, -3.55f), 0.04f, 112 + s, 0f, StructureMaterial.Timber, 6);
				}
			}
		}

		private static void TentLarge(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				int seg = d.Seg(20, 8);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(3f, -0.05f), new Vector2(3f, 1.85f) }, seg, StructureMaterial.Canvas, 1, false));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(3.25f, 1.7f), new Vector2(0f, 4.4f) }, seg, StructureMaterial.Dyed, 2, true, 0f, false, false, StructureRole.Roof));
				Log(d, new Vector3(0f, 4.2f, 0f), new Vector3(0f, 5.3f, 0f), 0.06f, 3, 0f, StructureMaterial.Timber, 6);
				if (d.Mid)
				{
					var pennant = new[] { new Vector2(0f, 4.95f), new Vector2(0.9f, 5.05f), new Vector2(0f, 5.25f) };
					d.Add(StructureShapes.Prism(pennant, -0.015f, 0.015f, StructureMaterial.Dyed, 4, StructureRole.Detail));
					// The door: a dark opening with its flaps tied back.
					d.Add(StructureShapes.Box(new Vector3(0f, 0.9f, -3.01f), new Vector3(1.2f, 1.8f, 0.04f), StructureMaterial.Ash, 5, StructureRole.Detail));
				}
				if (d.Fine)
				{
					for (int i = 0; i < 6; i++)
					{
						float a = (i + 0.5f) * Mathf.PI / 3f;
						var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
						Log(d, dir * 3.9f + Vector3.down * 0.15f, dir * 3.85f + Vector3.up * 0.15f, 0.03f, 20 + i, 0f, StructureMaterial.Timber, 4);
					}
				}
				return;
			}
			// A yurt: lattice wall, low dome, crown ring, timber door.
			int s = d.Seg(14, 8);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(2.6f, -0.05f), new Vector2(2.6f, 1.6f) }, s, StructureMaterial.Canvas, 1, false));
			var dome = new List<Vector2> { new Vector2(2.78f, 1.5f), new Vector2(1.6f, 2.35f) };
			if (d.Mid) dome.Add(new Vector2(0.9f, 2.65f));
			dome.Add(new Vector2(0.45f, 2.75f));
			dome.Add(new Vector2(0f, 2.75f));
			d.Add(StructureShapes.Lathe(dome, s, StructureMaterial.Canvas, 2, true, 0f, false, false, StructureRole.Roof));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f, 2.72f), new Vector2(0.55f, 2.72f), new Vector2(0.55f, 2.86f), new Vector2(0.4f, 2.86f) },
				d.Seg(10, 6), StructureMaterial.Timber, 3, true, 0f, true, false, StructureRole.Roof));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.85f, -2.6f), new Vector3(0.9f, 1.7f, 0.16f), StructureMaterial.Timber, 4, StructureRole.Detail)).Grain = Vector3.up;
			if (d.Mid)
			{
				// Two bands round the wall.
				for (int b = 0; b < 2; b++)
				{
					float y = 0.5f + b * 0.8f;
					d.Add(StructureShapes.Lathe(new[] { new Vector2(2.58f, y), new Vector2(2.64f, y), new Vector2(2.64f, y + 0.07f), new Vector2(2.58f, y + 0.07f) },
						s, StructureMaterial.Dyed, 5 + b, true, 0f, true, false, StructureRole.Detail));
				}
			}
		}

		private static void LeanTo(StructureDraft d)
		{
			bool hide = d.Variant == 1;
			const float width = 3f;
			float backY = 1f, frontY = 2.2f, backZ = 0.9f, frontZ = -0.9f;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (width * 0.5f - 0.15f);
				Log(d, new Vector3(x, -0.3f, backZ), new Vector3(x, backY, backZ), 0.07f, 1 + s, 0f, StructureMaterial.Timber, 6);
				Log(d, new Vector3(x, -0.3f, frontZ), new Vector3(x, frontY, frontZ), 0.08f, 4 + s, 0f, StructureMaterial.Timber, 6);
				Log(d, new Vector3(x, frontY - 0.05f, frontZ - 0.2f), new Vector3(x, backY - 0.05f, backZ + 0.25f), 0.06f, 7 + s, 0f, StructureMaterial.Timber, 6);
			}
			Log(d, new Vector3(-width * 0.5f - 0.1f, frontY + 0.02f, frontZ), new Vector3(width * 0.5f + 0.1f, frontY + 0.02f, frontZ), 0.07f, 10, 0f, StructureMaterial.Timber, 6);
			Log(d, new Vector3(-width * 0.5f - 0.1f, backY + 0.02f, backZ), new Vector3(width * 0.5f + 0.1f, backY + 0.02f, backZ), 0.06f, 11, 0f, StructureMaterial.Timber, 6);
			// The roof slab, from beyond the back rail to past the front one.
			var a = new Vector3(0f, backY - 0.1f, backZ + 0.35f);
			var b = new Vector3(0f, frontY + 0.18f, frontZ - 0.35f);
			float slope = Mathf.Atan2(b.y - a.y, a.z - b.z) * Mathf.Rad2Deg;
			StructureMaterial roof = hide ? StructureMaterial.Canvas : StructureMaterial.Thatch;
			float thick = hide ? 0.05f : 0.25f;
			StructureSolid slab = StructureShapes.Block((a + b) * 0.5f, new Vector3(width + 0.4f, thick, (b - a).magnitude), Quaternion.Euler(slope, 0f, 0f), roof, 12, StructureRole.Roof);
			slab.Grain = (b - a).normalized;
			d.Add(slab);
			if (hide)
			{
				// One side closed with hide, a triangle under the roof.
				var side = new[] { new Vector2(backZ, -0.05f), new Vector2(frontZ, -0.05f), new Vector2(frontZ, frontY), new Vector2(backZ, backY) };
				StructureSolid wall = StructureShapes.Prism(side, -0.025f, 0.025f, StructureMaterial.Canvas, 13);
				d.Add(wall.Transform(Matrix4x4.TRS(new Vector3(width * 0.5f - 0.1f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.one)));
			}
			else if (d.Mid)
			{
				// A bed of boughs under it.
				d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, 0.1f), new Vector3(width - 0.6f, 0.12f, 1.4f), StructureMaterial.Thatch, 14, StructureRole.Detail));
			}
		}

		// ── Fire, stores, banners, bedding ────────────────────────────

		private static void CampfireRing(StructureDraft d)
		{
			int stones = d.Lod == 0 ? 11 : d.Lod == 1 ? 9 : 7;
			for (int i = 0; i < stones; i++)
			{
				float a = (i + d.Range(i, 1, -0.2f, 0.2f)) * 2f * Mathf.PI / stones;
				float r = 0.72f + d.Range(i, 2, -0.05f, 0.05f);
				var size = new Vector3(d.Range(i, 3, 0.26f, 0.36f), d.Range(i, 4, 0.18f, 0.26f), d.Range(i, 5, 0.22f, 0.3f));
				d.Add(RoughStone(d, new Vector3(Mathf.Cos(a) * r, -0.06f, Mathf.Sin(a) * r), size, -a * Mathf.Rad2Deg, 100 + i, d.Fine ? 2 : 1));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.58f, -0.06f), new Vector2(0.58f, 0.02f), new Vector2(0.3f, 0.05f), new Vector2(0f, 0.05f) },
				d.Seg(12, 6), StructureMaterial.Ash, 1, true));
			int logs = d.Mid ? 4 : 3;
			for (int i = 0; i < logs; i++)
			{
				float a = i * 2f * Mathf.PI / logs + 0.4f;
				var foot = new Vector3(Mathf.Cos(a) * 0.48f, 0.02f, Mathf.Sin(a) * 0.48f);
				Log(d, foot, new Vector3(Mathf.Cos(a) * 0.05f, 0.42f, Mathf.Sin(a) * 0.05f), 0.05f, 50 + i, 0f, StructureMaterial.Timber, 5);
			}
			if (d.Variant == 1)
			{
				// A cooking tripod over it, and the pot.
				for (int i = 0; i < 3; i++)
				{
					float a = i * 2f * Mathf.PI / 3f;
					Log(d, new Vector3(Mathf.Cos(a) * 0.95f, -0.05f, Mathf.Sin(a) * 0.95f), new Vector3(0f, 1.45f, 0f), 0.03f, 60 + i, 0f, StructureMaterial.Timber, 5);
				}
				Log(d, new Vector3(0f, 1.42f, 0f), new Vector3(0f, 0.85f, 0f), 0.008f, 63, 0f, StructureMaterial.Iron, 4);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, 0.6f), new Vector2(0.22f, 0.68f), new Vector2(0.2f, 0.82f), new Vector2(0f, 0.82f) },
					d.Seg(10, 6), StructureMaterial.Iron, 64, true));
			}
		}

		private static void Crate(StructureDraft d)
		{
			Vector3 size = d.Variant == 0 ? new Vector3(0.8f, 0.8f, 0.8f) : new Vector3(1.3f, 0.65f, 0.7f);
			var centre = new Vector3(0f, size.y * 0.5f, 0f);
			d.Add(StructureShapes.Box(centre, size - new Vector3(0.04f, 0.04f, 0.04f), StructureMaterial.Timber, 1));
			if (!d.Mid)
			{
				return;
			}
			// Battens along every edge, a little proud.
			Vector3 h = size * 0.5f;
			const float w = 0.07f;
			int k = 10;
			for (int axis = 0; axis < 3; axis++)
			{
				int a = (axis + 1) % 3, b = (axis + 2) % 3;
				for (int sa = -1; sa <= 1; sa += 2)
				{
					for (int sb = -1; sb <= 1; sb += 2)
					{
						var p = Vector3.zero;
						p[a] = sa * (h[a] - w * 0.5f);
						p[b] = sb * (h[b] - w * 0.5f);
						var s = new Vector3(w, w, w);
						s[axis] = size[axis];
						StructureSolid batten = StructureShapes.Box(centre + p, s, StructureMaterial.Timber, k++, StructureRole.Detail);
						var grain = Vector3.zero;
						grain[axis] = 1f;
						batten.Grain = grain;
						d.Add(batten);
					}
				}
			}
			if (d.Fine)
			{
				// A diagonal brace on the front and the back.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(-h.x + w, w, s * (h.z + 0.005f)), new Vector3(h.x - w, size.y - w, s * (h.z + 0.005f)), 0.06f, 0.03f,
						StructureMaterial.Timber, 30 + s, StructureRole.Detail));
				}
			}
		}

		private static void Barrel(StructureDraft d)
		{
			int seg = d.Seg(16, 8);
			var profile = new List<Vector2> { new Vector2(0.27f, 0f), new Vector2(0.31f, 0.2f) };
			if (d.Mid) profile.Add(new Vector2(0.33f, 0.45f));
			profile.Add(new Vector2(0.31f, 0.7f));
			profile.Add(new Vector2(0.27f, 0.9f));
			int first = d.Solids.Count;
			d.Add(StructureShapes.Lathe(profile, seg, StructureMaterial.Timber, 1, true, 0f, false, true));
			if (d.Mid)
			{
				foreach (float y in new[] { 0.12f, 0.74f })
				{
					float r = y < 0.45f ? 0.296f : 0.296f;
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r - 0.02f, y), new Vector2(r + 0.012f, y), new Vector2(r + 0.012f, y + 0.05f), new Vector2(r - 0.02f, y + 0.05f) },
						seg, StructureMaterial.Iron, y < 0.45f ? 2 : 3, true, 0f, true, false, StructureRole.Detail));
				}
			}
			if (d.Variant == 1)
			{
				// On its side, resting on the bulge, with a chock under it.
				TransformSince(d, first, Matrix4x4.TRS(new Vector3(-0.45f, 0.33f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one));
				d.Add(StructureShapes.Block(new Vector3(-0.1f, 0.05f, 0.26f), new Vector3(0.3f, 0.1f, 0.08f), Quaternion.identity, StructureMaterial.Timber, 4));
			}
		}

		private static void BannerPole(StructureDraft d)
		{
			float top = d.Variant == 0 ? 4.2f : 5f;
			// A stone collar at the foot.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.28f, -0.2f), new Vector2(0.24f, 0.18f), new Vector2(0f, 0.22f) }, d.Seg(8, 5), StructureMaterial.Fieldstone, 1, false));
			Log(d, new Vector3(0f, -0.3f, 0f), new Vector3(0f, top, 0f), 0.06f, 2, 0f, StructureMaterial.Timber, 8);
			d.Add(StructureShapes.Sphere(new Vector3(0f, top + 0.07f, 0f), new Vector3(0.09f, 0.09f, 0.09f), d.Seg(8, 4), d.Fine ? 5 : 3, StructureMaterial.Iron, 3));
			if (d.Variant == 0)
			{
				float bar = top - 0.35f;
				d.Add(StructureShapes.Beam(new Vector3(-0.5f, bar, -0.06f), new Vector3(0.5f, bar, -0.06f), 0.06f, 0.06f, StructureMaterial.Timber, 4));
				var banner = new[] { new Vector2(-0.42f, bar), new Vector2(0.42f, bar), new Vector2(0.42f, bar - 1.5f), new Vector2(0f, bar - 1.75f), new Vector2(-0.42f, bar - 1.5f) };
				d.Add(StructureShapes.Prism(banner, -0.1f, -0.08f, StructureMaterial.Dyed, 5, StructureRole.Detail));
			}
			else
			{
				var pennant = new[] { new Vector2(0.05f, top - 0.1f), new Vector2(1.6f, top - 0.35f), new Vector2(0.05f, top - 0.75f) };
				d.Add(StructureShapes.Prism(pennant, -0.012f, 0.012f, StructureMaterial.Dyed, 5, StructureRole.Detail));
			}
		}

		private static void Bedroll(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.03f, 0f), new Vector3(0.8f, 0.07f, 1.9f), d.Bevel(0.02f), StructureMaterial.Canvas, 1));
				StructureSolid pillow = StructureShapes.Cylinder(0.11f, -0.36f, 0.36f, d.Seg(10, 6), StructureMaterial.Dyed, 2);
				d.Add(pillow.Transform(Matrix4x4.TRS(new Vector3(0f, 0.15f, 0.82f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				if (d.Fine)
				{
					// The blanket's top fold.
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.09f, -0.2f), new Vector3(0.78f, 0.05f, 1.2f), 0.015f, StructureMaterial.Dyed, 3, StructureRole.Detail));
				}
				return;
			}
			int seg = d.Seg(12, 6);
			StructureSolid roll = StructureShapes.Cylinder(0.17f, -0.42f, 0.42f, seg, StructureMaterial.Canvas, 1);
			d.Add(roll.Transform(Matrix4x4.TRS(new Vector3(0f, 0.17f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					StructureSolid strap = StructureShapes.Lathe(new[] { new Vector2(0.16f, -0.03f), new Vector2(0.185f, -0.03f), new Vector2(0.185f, 0.03f), new Vector2(0.16f, 0.03f) },
						seg, StructureMaterial.Timber, 2 + s, true, 0f, true);
					d.Add(strap.Transform(Matrix4x4.TRS(new Vector3(s * 0.25f, 0.17f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				}
			}
		}

		// ── Palisades ─────────────────────────────────────────────────

		private static void PalisadeSegment(StructureDraft d)
		{
			const float module = 4f;
			int logs = 13;
			float spacing = module / logs;
			bool ragged = d.Variant == 1;
			for (int i = 0; i < logs; i++)
			{
				float x = -module * 0.5f + spacing * (i + 0.5f);
				float r = d.Range(i, 1, 0.135f, 0.16f);
				float h = 3f + (ragged ? d.Range(i, 2, -0.45f, 0.3f) : d.Range(i, 2, -0.08f, 0.08f));
				Vector3 top = new Vector3(x + (ragged ? d.Range(i, 3, -0.08f, 0.08f) : 0f), h, ragged ? d.Range(i, 4, -0.06f, 0.06f) : 0f);
				Log(d, new Vector3(x, -0.5f, 0f), top, r, 1 + i, 0.35f, StructureMaterial.Timber, 7);
			}
			// Two rails behind, binding them.
			foreach (float y in new[] { 0.8f, 2.2f })
			{
				d.Add(StructureShapes.Beam(new Vector3(-module * 0.5f, y, 0.2f), new Vector3(module * 0.5f, y, 0.2f), 0.12f, 0.14f, StructureMaterial.Timber, y < 1f ? 50 : 51));
			}
		}

		private static void PalisadeGate(StructureDraft d)
		{
			bool open = d.Variant == 0;
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(s * 1.6f, -0.6f, 0f), new Vector3(s * 1.6f, 4f, 0f), 0.24f, 1 + s, 0.4f, StructureMaterial.Timber, 9);
				if (d.Mid)
				{
					// A short log closing each post to the module's edge.
					Log(d, new Vector3(s * 1.92f, -0.5f, 0f), new Vector3(s * 1.92f, 3.1f, 0f), 0.08f, 5 + s, 0.25f, StructureMaterial.Timber, 6);
				}
			}
			d.Add(StructureShapes.Beam(new Vector3(-1.95f, 3.45f, -0.05f), new Vector3(1.95f, 3.45f, -0.05f), 0.24f, 0.3f, StructureMaterial.Timber, 9));
			// The two leaves, hinged at the posts: each 1.36 wide, five planks and two battens.
			for (int s = -1; s <= 1; s += 2)
			{
				int first = d.Solids.Count;
				const float leaf = 1.36f;
				int planks = d.Fine ? 5 : d.Mid ? 3 : 1;
				float w = leaf / planks;
				for (int p = 0; p < planks; p++)
				{
					float x = -s * (w * (p + 0.5f));
					float top = 2.9f + (d.Fine ? d.Range(20 + p, 1, -0.06f, 0.06f) : 0f);
					d.Add(StructureShapes.Box(new Vector3(x, 0.05f + top * 0.5f, 0f), new Vector3(w - (d.Fine ? 0.01f : 0f), top, 0.08f), StructureMaterial.Timber, 20 + s * 10 + p)).Grain = Vector3.up;
				}
				if (d.Mid)
				{
					foreach (float y in new[] { 0.6f, 2.3f })
					{
						d.Add(StructureShapes.Box(new Vector3(-s * leaf * 0.5f, y, -0.07f), new Vector3(leaf - 0.1f, 0.18f, 0.06f), StructureMaterial.Timber, 60 + s * 3 + (y < 1f ? 0 : 1)));
					}
				}
				// Swing about the hinge at the post's inner face, outward (toward −z) when open.
				var hinge = new Vector3(s * 1.36f, 0f, 0f);
				float angle = open ? s * -75f : 0f;
				TransformSince(d, first, Matrix4x4.Translate(hinge) * Matrix4x4.Rotate(Quaternion.Euler(0f, angle, 0f)));
			}
		}
	}
}
#endif
