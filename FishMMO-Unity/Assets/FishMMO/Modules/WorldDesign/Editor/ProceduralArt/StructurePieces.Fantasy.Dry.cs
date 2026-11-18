#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The fantasy wave's Dry pack: its new pieces (2026-10-27, poi-fantasy-wave/WAVE.md): the kraal's round hut, the baobab, the
	/// pyramid, the sphinx, and the four set into rock or hill (a rock-cut tomb front, a cliff palace's alcove, a mountain king's
	/// carved gate, the colossal kings of a cliff relief).
	/// </summary>
	/// <remarks>
	/// Each is laid by its id (the templates' "=Id" slots) and carries only tags of its own ("monument", "dead", "bothy"), so no
	/// older query lays one. The four set into rock keep a dungeon entrance's frame: the origin is the middle of the front on the
	/// ground line, the forecourt runs out down −z and the rear mass (the cliff's own face, drawn so the piece stands against
	/// any ground) back up +z; what a player walks through stays open at every level, since the last level is the collider.
	/// </remarks>
	public static partial class StructurePieces
	{
		private static void AddFantasyDry(List<StructurePiece> list)
		{
			Piece(list, "RoundHut", StructureStyle.Timber, StructureSize.Medium,
				"A kraal's round hut with a door and a smoke-dark inside: v0 a daub wall 4 m across under a conical thatch roof to the ground's edge; v1 a larger hut 5 m across with a ring of timber veranda posts carrying the eaves and a thatched crown knot.",
				RoundHut, "bothy").Variants = 2;
			Piece(list, "Baobab", StructureStyle.Earth, StructureSize.Building,
				"A baobab, the great bottle tree of the dry grass: v0 one swollen trunk 6 m across in the belly, a few thick bare limbs forking from its crown and a hollow in its side; v1 two trunks grown together, the hollow big enough to shelter in.",
				Baobab, "monument").Variants = 2;
			Piece(list, "Pyramid", StructureStyle.Stone, StructureSize.Building,
				"A king's tomb, 18 m across: v0 smooth-sided under a capstone of carved stone; v1 stepped in five tiers with a little shrine on the top; both with a mortuary temple at the foot of the east face and a stone causeway running out, an obelisk either side of its end.",
				PyramidPiece, "monument", "dead").Variants = 2;
			Piece(list, "Sphinx", StructureStyle.Stone, StructureSize.Building,
				"A sphinx half sunk in the sand, 16 m long on its sunken plinth: a lion's body couched with its paws before it, v0 a king's head under the striped headcloth, v1 a hawk's head with the beak.",
				SphinxPiece, "monument").Variants = 2;
			Piece(list, "RockCutFacade", StructureStyle.Stone, StructureSize.Building,
				"A tomb front cut into a cliff, anchored at its threshold: v0 two storeys of columns, a broken pediment, a round tholos over the dark door and steps up to it; v1 a row of three doors under a stepped cornice between two pilaster stacks, the middle door the great one. The cliff's own mass stands behind.",
				RockCutFacadePiece, "monument", "dead").Variants = 2;
			Piece(list, "AlcoveHouse", StructureStyle.Stone, StructureSize.Building,
				"A cliff dwelling built into an overhang, anchored at the cliff: stone rooms in two tiers under the lip of the rock, small doors and T-shaped windows, timber ladder poles standing out; v0 four rooms below, three above and a round tower; v1 three rooms, a square watch tower and a timber gallery.",
				AlcoveHousePiece, "monument").Variants = 2;
			Piece(list, "CarvedHallFront", StructureStyle.Stone, StructureSize.Building,
				"A mountain king's gate, cut into the rock: a great doorway 5 m wide into a passage that ends in black, a rune-cut lintel and iron doors swung back, two buttress towers with burning fire bowls; v0 a square dwarven gate, v1 the same under a pointed head with a crowned king 4.6 m tall standing before each buttress.",
				CarvedHallFrontPiece, "monument").Variants = 2;
			Piece(list, "CliffRelief", StructureStyle.Stone, StructureSize.Building,
				"Giant kings carved into a cliff, anchored at the cliff: v0 two colossal robed kings 14 m tall on their pedestals, hands on the pommels of swords planted point down; v1 three hooded kings with staves, a little smaller, shoulder to shoulder. A slab of the cliff stands behind them.",
				CliffReliefPiece, "monument").Variants = 2;
		}

		// ── The kraal's hut ───────────────────────────────────────────

		private static void RoundHut(StructureDraft d)
		{
			bool large = d.Variant == 1;
			float inner = large ? 2.2f : 1.7f, outer = inner + 0.38f, wallTop = large ? 2f : 1.8f;
			int seg = d.Seg(large ? 18 : 14, 10);
			RingWallWithDoor(d, Vector3.zero, inner, outer, -0.2f, wallTop, seg, 1.5f * Mathf.PI, 0.95f, 1.65f, StructureMaterial.Plaster, 1);
			float eave = outer + (large ? 0.9f : 0.55f), rise = large ? 2.6f : 2.2f;
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Thatch, rise, eave);
			if (roof.Flat)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, wallTop - 0.1f), new Vector2(eave, wallTop - 0.1f), new Vector2(eave, wallTop + 0.15f), new Vector2(0f, wallTop + 0.15f) },
					d.Seg(18, 10), StructureMaterial.Thatch, 10, true, 0f, false, false, StructureRole.Roof));
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, wallTop - 0.1f), new Vector2(eave, wallTop - 0.1f), new Vector2(eave, wallTop + 0.08f), new Vector2(0.18f, wallTop + roof.Rise), new Vector2(0f, wallTop + roof.Rise + 0.18f) },
					d.Seg(18, 10), StructureMaterial.Thatch, 10, true, 0f, false, false, StructureRole.Roof));
			}
			// The door's leaf, set open against the wall; the timber lintel over the doorway.
			d.Add(StructureShapes.Block(new Vector3(0.55f, 0.8f, -outer - 0.05f), new Vector3(0.08f, 1.6f, 0.9f), Quaternion.Euler(0f, -70f, 0f), StructureMaterial.Timber, 12, StructureRole.Detail));
			if (large)
			{
				int posts = d.Fine ? 8 : 6;
				for (int i = 0; i < posts; i++)
				{
					float a = (i + 0.5f) * 2f * Mathf.PI / posts;
					var at = new Vector3(Mathf.Sin(a) * (eave - 0.2f), 0f, Mathf.Cos(a) * (eave - 0.2f));
					if (Mathf.Abs(at.x) < 0.9f && at.z < 0f)
					{
						continue;
					}
					d.Add(StructureShapes.Rod(at + Vector3.down * 0.2f, at + Vector3.up * (wallTop - 0.05f), 0.07f, d.Seg(6, 4), StructureMaterial.Timber, 20 + i));
				}
			}
			else if (d.Mid)
			{
				// Thorns of the pen's fence lean against the wall: a pot and a stack of firewood at the door.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, 0f), new Vector2(0.3f, 0.25f), new Vector2(0.18f, 0.45f), new Vector2(0f, 0.45f) }, d.Seg(8, 5), StructureMaterial.Plaster, 30, true, 0f, false, false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(new Vector3(-1.1f, 0f, -outer - 0.45f))));
			}
		}

		// ── The baobab ────────────────────────────────────────────────

		private static void Baobab(StructureDraft d)
		{
			bool twin = d.Variant == 1;
			int seg = d.Seg(16, 8);
			var offsets = twin ? new[] { new Vector3(-1.6f, 0f, 0.2f), new Vector3(1.7f, 0f, -0.1f) } : new[] { Vector3.zero };
			for (int t = 0; t < offsets.Length; t++)
			{
				float s = twin ? 0.78f : 1f, lean = twin ? (t == 0 ? -1f : 1f) : 0f;
				var trunk = new List<Vector2>
				{
					new Vector2(3.5f * s, -0.4f), new Vector2(3.1f * s, 0.8f), new Vector2(2.9f * s, 2.4f), new Vector2(3.1f * s, 4.2f), new Vector2(2.7f * s, 6f), new Vector2(2.0f * s, 7.4f), new Vector2(0f, 7.8f * s + 0.2f),
				};
				if (!d.Mid)
				{
					trunk.RemoveAt(1);
				}
				d.Add(StructureShapes.Lathe(trunk, seg, StructureMaterial.Earth, 1 + t * 40).Transform(Matrix4x4.Translate(offsets[t])));
				// Limbs: thick, bare, forking from the crown.
				int limbs = twin ? 4 : 6;
				for (int i = 0; i < limbs; i++)
				{
					float yaw = (i + d.Rand(i + t * 9, 1) * 0.6f) * 360f / limbs;
					Quaternion q = Quaternion.Euler(0f, yaw, 0f);
					float reach = d.Range(i + t * 9, 2, 3.2f, 4.6f) * s, rise = d.Range(i + t * 9, 3, 2.4f, 4f) * s;
					Vector3 root = offsets[t] + new Vector3(0f, 7f * s, 0f) + q * (Vector3.forward * (1.1f * s)) + Vector3.right * lean * 0.2f;
					Vector3 elbow = root + q * (Vector3.forward * reach * 0.55f) + Vector3.up * rise * 0.45f;
					Vector3 tip = elbow + q * (Quaternion.Euler(0f, d.Range(i + t * 9, 4, -25f, 25f), 0f) * Vector3.forward * reach * 0.5f) + Vector3.up * rise * 0.6f;
					d.Add(StructureShapes.Rod(root, elbow, 0.5f * s, d.Seg(8, 5), StructureMaterial.Earth, 100 + i * 6 + t * 40, StructureRole.Structure));
					d.Add(StructureShapes.Rod(elbow, tip, 0.3f * s, d.Seg(8, 5), StructureMaterial.Earth, 101 + i * 6 + t * 40, StructureRole.Structure, 0.2f));
					if (d.Mid)
					{
						Vector3 twig = elbow + q * (Quaternion.Euler(0f, 35f, 0f) * Vector3.forward * reach * 0.35f) + Vector3.up * rise * 0.25f;
						d.Add(StructureShapes.Rod(elbow, twig, 0.16f * s, d.Seg(6, 4), StructureMaterial.Timber, 102 + i * 6 + t * 40, StructureRole.Detail, 0.15f));
					}
				}
			}
			// The hollow in its side, facing the way in (−z): dark, and a step of bare roots.
			float front = twin ? 2.75f : 3.0f;
			d.Add(StructureShapes.Box(new Vector3(twin ? 0f : 0f, 1.3f, -front + 0.3f), new Vector3(twin ? 1.9f : 1.2f, 2.4f, 0.5f), StructureMaterial.Ash, 300, StructureRole.Detail));
			if (d.Mid)
			{
				for (int s2 = -1; s2 <= 1; s2 += 2)
				{
					d.Add(StructureShapes.Rod(new Vector3(s2 * 0.9f, 0.1f, -front - 0.3f), new Vector3(s2 * 1.7f, 0f, -front - 1.6f), 0.28f, d.Seg(6, 4), StructureMaterial.Earth, 310 + s2, StructureRole.Detail, 0.2f));
				}
			}
		}

		// ── The pyramid ───────────────────────────────────────────────

		private static void PyramidPiece(StructureDraft d)
		{
			const StructureMaterial stone = StructureMaterial.Stone;
			bool stepped = d.Variant == 1;
			float half = stepped ? 8f : 9f, z0 = 4f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.2f, z0), new Vector3(half * 2f + 1.6f, 0.7f, half * 2f + 1.6f), d.Bevel(0.04f), stone, 1));
			if (!stepped)
			{
				float h = 13f, r = half * 1.4142f, capH = 1.5f;
				float rCap = r * capH / h;
				d.Add(StructureShapes.Lathe(new[] { new Vector2(r, 0.1f), new Vector2(rCap * 1.0f, h - capH) }, 4, stone, 2, false, Mathf.PI * 0.25f).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, z0))));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(rCap * 1.02f, h - capH), new Vector2(0f, h + 0.1f) }, 4, StructureMaterial.Monolith, 3, false, Mathf.PI * 0.25f).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, z0))));
			}
			else
			{
				for (int i = 0; i < 5; i++)
				{
					float w = half * 2f - i * 3.2f;
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.15f + i * 2.5f + 1.25f, z0), new Vector3(w, 2.5f, w), d.Bevel(0.04f), stone, 2 + i));
				}
				float top = 0.15f + 5 * 2.5f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.9f, z0), new Vector3(3.6f, 1.8f, 3.6f), d.Bevel(0.04f), StructureMaterial.Monolith, 8));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(2.7f, top + 1.8f), new Vector2(0f, top + 3.2f) }, 4, stone, 9, false, Mathf.PI * 0.25f).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, z0))));
				// The stair up the east face, steps cut in the tiers.
				Steps(d, new Vector3(0f, 0f, z0 - half + 3.2f), Vector3.back, 2f, 2.5f, stone, 12);
			}
			// The mortuary temple at the foot of the east face: a pillared porch, a dark door, a flat roof.
			float tz = z0 - half - 3.2f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.6f, tz + 1.2f), new Vector3(8f, 3.4f, 3.6f), d.Bevel(0.03f), stone, 20));
			d.Add(StructureShapes.Box(new Vector3(0f, 1.1f, tz - 0.6f), new Vector3(1.5f, 2.2f, 0.3f), StructureMaterial.Ash, 21, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 2.6f, 1.6f, tz - 0.8f), new Vector3(0.7f, 3.2f, 0.7f), d.Bevel(0.03f), StructureMaterial.Monolith, 22 + s));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 3.35f, tz - 0.6f), new Vector3(7f, 0.4f, 2.4f), d.Bevel(0.03f), stone, 25));
			// The causeway: a low walled way running out, an obelisk at either side of its end.
			float cz = tz - 3.4f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.05f, cz + 0.6f), new Vector3(3f, 0.3f, 7f), d.Bevel(0.02f), stone, 30));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 1.65f, 0.35f, cz + 0.6f), new Vector3(0.3f, 0.8f, 7f), d.Bevel(0.02f), stone, 32 + s));
				float oz = cz - 2.2f;
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 3f, 0.1f, oz), new Vector3(1.1f, 0.4f, 1.1f), d.Bevel(0.02f), stone, 36 + s));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f * 1.414f, 0.3f), new Vector2(0.26f * 1.414f, 4.2f), new Vector2(0f, 4.7f) }, 4, StructureMaterial.Monolith, 40 + s, false, Mathf.PI * 0.25f)
					.Transform(Matrix4x4.Translate(new Vector3(s * 3f, 0f, oz))));
			}
		}

		// ── The sphinx ────────────────────────────────────────────────

		private static void SphinxPiece(StructureDraft d)
		{
			const StructureMaterial carved = StructureMaterial.Monolith;
			bool hawk = d.Variant == 1;
			int seg = d.Seg(14, 8), rings = d.Fine ? 7 : 4;
			// The sunken plinth it lies on, sand to its rim.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.5f, 0.6f), new Vector3(7f, 1.4f, 17f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
			// Body, haunch and chest.
			StructureSolid body = StructureShapes.Sphere(new Vector3(0f, 1.7f, 3.2f), new Vector3(1.9f, 1.7f, 4.4f), seg, rings, carved, 2);
			body.Clip(Vector3.down, -0.15f);
			d.Add(body);
			for (int s = -1; s <= 1; s += 2)
			{
				StructureSolid haunch = StructureShapes.Sphere(new Vector3(s * 1.0f, 1.4f, 5.6f), new Vector3(1.0f, 1.3f, 1.9f), seg, rings, carved, 4 + s);
				haunch.Clip(Vector3.down, -0.15f);
				d.Add(haunch);
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 0.85f, 0.8f, -2.9f), new Vector3(1f, 1.6f, 5.4f), d.Bevel(0.06f), carved, 8 + s));
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 0.85f, 0.3f, -5.5f), new Vector3(1.15f, 0.6f, 1.1f), d.Bevel(0.05f), carved, 12 + s));
			}
			d.Add(StructureShapes.Sphere(new Vector3(0f, 2.7f, -0.9f), new Vector3(1.7f, 2.1f, 2.0f), seg, rings, carved, 16));
			d.Add(StructureShapes.Cylinder(0.95f, 3.2f, 5.3f, seg, carved, 17).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, -1.9f))));
			// The head, the headdress (v0) or the beak (v1), the tail down the flank.
			d.Add(StructureShapes.Sphere(new Vector3(0f, 5.8f, -2f), new Vector3(0.95f, 1.15f, 1.05f), seg, rings, carved, 18));
			if (!hawk)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(new Vector3(s * 1.15f, 5.2f, -1.6f), new Vector3(0.4f, 2.6f, 1.6f), Quaternion.Euler(0f, s * 8f, 0f), carved, 20 + s, StructureRole.Structure, d.Bevel(0.04f)));
				}
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 6.85f, -1.9f), new Vector3(2.3f, 0.5f, 2.1f), d.Bevel(0.04f), carved, 23));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 5.65f, -3.05f), new Vector3(0.26f, 0.5f, 0.3f), carved, 24, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0f, 5.2f, -2.75f), new Vector3(0.7f, 0.18f, 0.3f), carved, 25, StructureRole.Detail));
				}
			}
			else
			{
				d.Add(StructureShapes.Rod(new Vector3(0f, 5.8f, -2.7f), new Vector3(0f, 5.0f, -3.9f), 0.32f, d.Seg(8, 5), carved, 24, StructureRole.Structure, 0.2f));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(new Vector3(s * 1.05f, 5.0f, -1.5f), new Vector3(0.45f, 2.8f, 1.8f), Quaternion.Euler(0f, s * 6f, 0f), carved, 20 + s, StructureRole.Structure, d.Bevel(0.04f)));
				}
			}
			d.Add(StructureShapes.Rod(new Vector3(1.7f, 0.9f, 5.5f), new Vector3(2.1f, 0.5f, 8.1f), 0.2f, d.Seg(6, 4), carved, 30, StructureRole.Detail, 0.1f));
		}

		// ── Set into rock: the tomb front ─────────────────────────────

		private static void RockCutFacadePiece(StructureDraft d)
		{
			const StructureMaterial stone = StructureMaterial.Stone;
			// The cliff's own mass behind the front, so it stands against any ground.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 6f, 3.6f), new Vector3(17f, 12.4f, 5f), d.Bevel(0.08f), StructureMaterial.Fieldstone, 1));
			// Steps and the court before it.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.05f, -1.2f), new Vector3(13f, 0.5f, 3f), d.Bevel(0.03f), stone, 2));
			if (d.Variant == 0)
			{
				// Two storeys: the lower order of four columns, the upper of two flanking a tholos, over a dark door.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 2.9f, 0.1f), new Vector3(11f, 6.2f, 0.5f), d.Bevel(0.04f), stone, 3));
				d.Add(StructureShapes.Box(new Vector3(0f, 1.9f, -0.2f), new Vector3(2.1f, 3.8f, 0.2f), StructureMaterial.Ash, 4, StructureRole.Detail));
				for (int i = 0; i < 4; i++)
				{
					Column(d, new Vector3(-4.2f + i * 2.8f, 0.2f, -1.2f), 5.4f, 0.4f, StructureMaterial.Monolith, 10 + i * 4);
				}
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 5.85f, -0.7f), new Vector3(11.6f, 0.6f, 1.9f), d.Bevel(0.04f), stone, 30));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 8.7f, 0.3f), new Vector3(9f, 5f, 0.6f), d.Bevel(0.04f), stone, 31));
				int pairs = d.Mid ? 1 : 0;
				for (int s = -1; s <= 1; s += pairs > 0 ? 2 : 4)
				{
					if (s == 0)
					{
						continue;
					}
					Column(d, new Vector3(s * 3.6f, 6.15f, -0.5f), 4.3f, 0.3f, StructureMaterial.Monolith, 40 + s * 4);
				}
				var tholos = new List<Vector2> { new Vector2(1.7f, 6.15f), new Vector2(1.7f, 8.4f), new Vector2(1.95f, 8.5f) };
				int ring = d.Fine ? 5 : 3;
				for (int i = 1; i < ring; i++)
				{
					float a = 0.5f * Mathf.PI * i / ring;
					tholos.Add(new Vector2(1.95f * Mathf.Cos(a), 8.5f + 1.7f * Mathf.Sin(a)));
				}
				tholos.Add(new Vector2(0f, 10.2f));
				d.Add(StructureShapes.Lathe(tholos, d.Seg(16, 8), stone, 50).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, -0.4f))));
				d.Add(StructureShapes.Box(new Vector3(0f, 7.2f, -2.3f), new Vector3(1.2f, 1.9f, 0.2f), StructureMaterial.Ash, 51, StructureRole.Detail));
				RuneRow(d, 5.4f, 3.4f, 5, -2.15f, 60);
			}
			else
			{
				// Three doors between two pilaster stacks, a stepped cornice over all.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 3.4f, 0.6f), new Vector3(14f, 6.8f, 1.6f), d.Bevel(0.04f), stone, 3));
				for (int i = 0; i < 3; i++)
				{
					float x = (i - 1) * 4.2f, w = i == 1 ? 2.4f : 1.7f, h = i == 1 ? 4.4f : 3.2f;
					d.Add(StructureShapes.Box(new Vector3(x, h * 0.5f, -0.25f), new Vector3(w, h, 0.2f), StructureMaterial.Ash, 4 + i, StructureRole.Detail));
					d.Add(StructureShapes.ChamferBox(new Vector3(x, h + 0.2f, -0.3f), new Vector3(w + 0.9f, 0.4f, 0.4f), d.Bevel(0.02f), StructureMaterial.Monolith, 8 + i));
				}
				for (int s = -1; s <= 1; s += 2)
				{
					for (int t = 0; t < 3; t++)
					{
						d.Add(StructureShapes.ChamferBox(new Vector3(s * 6.3f, 1.2f + t * 2.3f, -0.5f), new Vector3(1.1f - t * 0.2f, 2.3f, 0.9f), d.Bevel(0.03f), stone, 12 + s + t * 3));
					}
				}
				for (int t = 0; t < 3; t++)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 6.9f + t * 0.45f, 0.3f - t * 0.1f), new Vector3(14.6f - t * 1.6f, 0.45f, 2.4f - t * 0.4f), d.Bevel(0.03f), stone, 20 + t));
				}
				RuneRow(d, 5.5f, 5.5f, 7, -0.62f, 30);
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.1f, -2.4f), new Vector3(3.4f, 0.4f, 1.3f), d.Bevel(0.02f), stone, 40));
			}
		}

		// ── Set into rock: a cliff dwelling ───────────────────────────

		private static void AlcoveHousePiece(StructureDraft d)
		{
			const StructureMaterial stone = StructureMaterial.Stone;
			bool watch = d.Variant == 1;
			// The cave's back wall and the lip of rock over the dwellings.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 5.8f, 1.7f), new Vector3(21f, 11.6f, 1.5f), d.Bevel(0.08f), StructureMaterial.Fieldstone, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 10.4f, -1.6f), new Vector3(22f, 2.4f, 9.4f), d.Bevel(0.1f), StructureMaterial.Fieldstone, 2));
			d.Add(RoughStone(d, new Vector3(-9.6f, 8.4f, -3.2f), new Vector3(2.2f, 3.2f, 2.2f), 20f, 3, 3));
			d.Add(RoughStone(d, new Vector3(9.6f, 8.4f, -3.2f), new Vector3(2.2f, 3.2f, 2.2f), -15f, 4, 3));
			int lower = watch ? 3 : 4;
			float roomW = 4.2f;
			for (int i = 0; i < lower; i++)
			{
				float x = (i - (lower - 1) * 0.5f) * roomW + (watch ? 1.6f : 0f);
				d.Add(StructureShapes.ChamferBox(new Vector3(x, 1.4f, -0.9f), new Vector3(roomW - 0.15f, 2.8f, 3.4f), d.Bevel(0.04f), stone, 10 + i * 4));
				d.Add(StructureShapes.Box(new Vector3(x, 0.85f, -2.62f), new Vector3(0.9f, 1.6f, 0.1f), StructureMaterial.Ash, 11 + i * 4, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(x + 1.2f, 2.0f, -2.62f), new Vector3(0.45f, 0.4f, 0.1f), StructureMaterial.Ash, 12 + i * 4, StructureRole.Detail));
			}
			int upper = lower - 1;
			for (int i = 0; i < upper; i++)
			{
				float x = (i - (upper - 1) * 0.5f) * roomW + (watch ? 1.6f : 0f);
				d.Add(StructureShapes.ChamferBox(new Vector3(x, 4.0f, -0.4f), new Vector3(roomW - 0.3f, 2.5f, 2.6f), d.Bevel(0.04f), stone, 40 + i * 4));
				d.Add(StructureShapes.Box(new Vector3(x, 3.6f, -1.72f), new Vector3(0.8f, 1.4f, 0.1f), StructureMaterial.Ash, 41 + i * 4, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(x + 1.1f, 4.4f, -1.72f), new Vector3(0.5f, 0.35f, 0.1f), StructureMaterial.Ash, 42 + i * 4, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(x + 1.1f, 4.12f, -1.72f), new Vector3(0.18f, 0.3f, 0.1f), StructureMaterial.Ash, 43 + i * 4, StructureRole.Detail));
			}
			if (!watch)
			{
				// The round tower at the end: tall enough to carry the ladder poles up over the roofs.
				d.Add(StructureShapes.Cylinder(1.8f, -0.3f, 6.3f, d.Seg(16, 8), stone, 70).Transform(Matrix4x4.Translate(new Vector3(8.6f, 0f, -2.4f))));
				d.Add(StructureShapes.Box(new Vector3(8.6f, 1.0f, -4.17f), new Vector3(0.9f, 1.8f, 0.1f), StructureMaterial.Ash, 71, StructureRole.Detail));
			}
			else
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(-8.2f, 4.0f, -1.0f), new Vector3(3.2f, 8.6f, 3.2f), d.Bevel(0.04f), stone, 72));
				d.Add(StructureShapes.Box(new Vector3(-8.2f, 1.0f, -2.67f), new Vector3(0.9f, 1.8f, 0.1f), StructureMaterial.Ash, 73, StructureRole.Detail));
				// A timber gallery along the upper rooms.
				d.Add(StructureShapes.Box(new Vector3(1.6f, 2.95f, -2.9f), new Vector3(8.6f, 0.15f, 1.1f), StructureMaterial.Timber, 74, StructureRole.Detail));
				for (int i = 0; i < 3; i++)
				{
					d.Add(StructureShapes.Rod(new Vector3(-2.5f + i * 4.1f, 0f, -3.3f), new Vector3(-2.5f + i * 4.1f, 2.9f, -3.3f), 0.08f, d.Seg(6, 4), StructureMaterial.Timber, 75 + i, StructureRole.Detail));
				}
			}
			// Ladder poles standing from the lower roofs to the upper doors.
			if (d.Mid)
			{
				float lx = watch ? 4.5f : -2.1f;
				d.Add(StructureShapes.Rod(new Vector3(lx, 0f, -3.1f), new Vector3(lx + 0.3f, 4.6f, -2.0f), 0.07f, d.Seg(6, 4), StructureMaterial.Timber, 80, StructureRole.Detail, 0.1f));
				d.Add(StructureShapes.Rod(new Vector3(lx + 0.5f, 0f, -3.1f), new Vector3(lx + 0.8f, 4.6f, -2.0f), 0.07f, d.Seg(6, 4), StructureMaterial.Timber, 81, StructureRole.Detail, 0.1f));
			}
		}

		// ── Set into rock: the mountain king's gate ───────────────────

		private static void CarvedHallFrontPiece(StructureDraft d)
		{
			const StructureMaterial stone = StructureMaterial.Stone;
			bool pointed = d.Variant == 1;
			float half = 2.5f, springs = pointed ? 6.2f : 6f;
			// The mass either side of the door and over it, five metres deep: the doorway is open through it to a passage that ends in black.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * (half + 3.2f), 5.5f, 2.6f), new Vector3(6.4f, 11f, 5.2f), d.Bevel(0.06f), stone, 1 + s));
			}
			if (!pointed)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 8.2f, 2.6f), new Vector3(half * 2f, 5.2f, 5.2f), d.Bevel(0.05f), stone, 3));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 5.65f, -0.1f), new Vector3(half * 2f + 1.2f, 0.7f, 0.7f), d.Bevel(0.03f), StructureMaterial.Monolith, 4));
			}
			else
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * half, springs - 0.3f, 2.6f), new Vector3(s * 0.3f, springs + 2.6f, 2.6f), 1.1f, 5.2f, stone, 5 + s));
				}
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 10.2f, 2.6f), new Vector3(4.4f, 1.6f, 5.2f), d.Bevel(0.04f), stone, 7));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, 3f, 5.1f), new Vector3(half * 2f, 6f, 0.2f), StructureMaterial.Ash, 8, StructureRole.Detail));
			// The iron doors, swung back along the passage walls.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Block(new Vector3(s * (half - 0.2f), 2.7f, 1.4f), new Vector3(0.14f, 5.4f, 2.2f), Quaternion.identity, StructureMaterial.Iron, 9 + s, StructureRole.Detail));
			}
			// Steps up to the sill.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.15f, -1.4f), new Vector3(9.5f, 0.5f, 3.2f), d.Bevel(0.03f), stone, 12));
			// The buttress towers either side of the front, a fire bowl on each head.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 8.8f, 4.9f, -1.4f), new Vector3(3.6f, 9.8f, 3.6f), d.Bevel(0.05f), stone, 20 + s));
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 8.8f, 9.95f, -1.4f), new Vector3(4.1f, 0.5f, 4.1f), d.Bevel(0.03f), StructureMaterial.Monolith, 22 + s));
				FireBowl(d, new Vector3(s * 8.8f, 10.55f, -1.4f), true, 24 + s * 2);
			}
			if (pointed)
			{
				// A king in stone before each buttress, his hands on the pommel of his sword.
				for (int s = -1; s <= 1; s += 2)
				{
					Figure(d, new Vector3(s * 8.8f, 0f, -3.4f), 2.4f, false, 100 + s * 50);
				}
			}
			RuneRow(d, 6.4f, half - 0.3f, 7, -0.2f, 40);
			if (!pointed)
			{
				RuneRow(d, 5.65f, half + 0.3f, 3, -0.46f, 50);
			}
		}

		// ── Set into rock: the colossal kings ────────────────────────

		private static void CliffReliefPiece(StructureDraft d)
		{
			bool three = d.Variant == 1;
			// The slab of cliff the kings are cut from, stood behind them.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 9f, 3.2f), new Vector3(three ? 22f : 17f, 18f, 2.8f), d.Bevel(0.1f), StructureMaterial.Fieldstone, 1));
			int n = three ? 3 : 2;
			float scale = three ? 5.2f : 6.3f, spacing = three ? 7f : 8f;
			for (int i = 0; i < n; i++)
			{
				float x = (i - (n - 1) * 0.5f) * spacing;
				d.Add(StructureShapes.ChamferBox(new Vector3(x, 1.2f, 0.2f), new Vector3(scale * 0.9f, 2.4f, scale * 0.9f), d.Bevel(0.06f), StructureMaterial.Stone, 10 + i * 3));
				d.Add(StructureShapes.ChamferBox(new Vector3(x, 8f, 2.2f), new Vector3(scale * 0.8f, 14f, 1.4f), d.Bevel(0.06f), StructureMaterial.Monolith, 11 + i * 3));
				Figure(d, new Vector3(x, 2.35f, 0.2f), scale, three, 100 + i * 60);
			}
		}
	}
}
#endif
