#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Sacred pieces (standing stones, altars, shrines, obelisks, statues, portals) and the dead (graves, crypts, bones, fences).</summary>
	public static partial class StructurePieces
	{
		private static void AddSacred(List<StructurePiece> list)
		{
			Piece(list, "StandingStone", StructureStyle.Stone, StructureSize.Small, "A rough menhir, 2.4–3.4 m, leaning a little.", StandingStone,
				"sacred", "standing-stone", "monument").Variants = 3;
			Piece(list, "CircleStone", StructureStyle.Stone, StructureSize.Medium, "A tall stone for a circle; v1 a trilithon.", CircleStone,
				"sacred", "standing-stone", "monument");
			Piece(list, "Altar", StructureStyle.Stone, StructureSize.Small, "A dressed altar on a step; v1 a rough slab on two stones.", Altar, "sacred", "altar");
			Piece(list, "Shrine", StructureStyle.Stone, StructureSize.Medium, "A small roofed shrine on a platform; v1 a wayside shrine on a post.", Shrine,
				"sacred", "shrine");
			Piece(list, "Obelisk", StructureStyle.Stone, StructureSize.Small, "A tapered shaft on a stepped base; v1 taller.", Obelisk, "sacred", "monument");
			Piece(list, "StatuePlinth", StructureStyle.Stone, StructureSize.Small, "An empty plinth; v1 round.", StatuePlinth, "sacred", "statue", "monument");
			Piece(list, "Statue", StructureStyle.Stone, StructureSize.Medium, "A robed figure on a plinth; v1 hooded, with a staff.", Statue,
				"sacred", "statue", "monument");
			Piece(list, "PortalArch", StructureStyle.Stone, StructureSize.Medium, "Two pillars and a lintel, 2.8 m clear; v1 pointed.", PortalArch, "portal", "sacred");
			Piece(list, "PortalRing", StructureStyle.Stone, StructureSize.Medium, "An upright ring of stones on a plinth; v1 flanked by runestones.", PortalRing,
				"portal", "sacred");
		}

		private static void AddDead(List<StructurePiece> list)
		{
			Piece(list, "Gravestone", StructureStyle.Stone, StructureSize.Small,
				"Six headstones: rounded, cross, small obelisk, leaning tablet, ringed cross, block.", Gravestone, "dead", "grave").Variants = 6;
			Piece(list, "GraveMound", StructureStyle.Earth, StructureSize.Small, "An earth mound over a grave; v1 a cairn.", GraveMound, "dead", "grave");
			Piece(list, "Mausoleum", StructureStyle.Stone, StructureSize.Building, "A gabled tomb with a columned porch; v1 domed.", Mausoleum, "dead", "crypt", "building");
			Piece(list, "CryptEntrance", StructureStyle.Stone, StructureSize.Medium, "A door frame set into a barrow; v1 with flanking walls and a roof slab.", CryptEntrance,
				"dead", "crypt");
			Piece(list, "BonePile", StructureStyle.Earth, StructureSize.Small, "Bones and a skull; v1 scattered, two skulls.", BonePile, "dead", "bones").Collides = false;
			Piece(list, "IronFence", StructureStyle.Iron, StructureSize.Small, "Three metres of spear-topped railing; v1 on a low stone wall.", IronFence,
				"dead", "fence", "wall").ModuleLength = 3f;
		}

		// ── Sacred ────────────────────────────────────────────────────

		private static void StandingStone(StructureDraft d)
		{
			float h = 2.4f + d.Variant * 0.5f + d.Range(1, 1, -0.2f, 0.2f);
			var size = new Vector3(d.Range(1, 2, 0.95f, 1.35f), h + 0.5f, d.Range(1, 3, 0.5f, 0.75f));
			StructureSolid s = RoughStone(d, new Vector3(0f, -0.5f, 0f), size, d.Range(1, 4, -20f, 20f), 1, 5, StructureMaterial.Monolith);
			d.Add(s.Transform(Lean(d, 1, 6f, Vector3.zero)));
		}

		private static void CircleStone(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				var size = new Vector3(1.4f, 5f, 0.8f);
				d.Add(RoughStone(d, new Vector3(0f, -0.6f, 0f), size, d.Range(1, 4, -10f, 10f), 1, 4, StructureMaterial.Monolith).Transform(Lean(d, 1, 3f, Vector3.zero)));
				return;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(RoughStone(d, new Vector3(s * 1.15f, -0.6f, 0f), new Vector3(1.1f, 4.6f, 0.9f), d.Range(2 + s, 4, -5f, 5f), 2 + s, 3, StructureMaterial.Monolith));
			}
			// The lintel: a long rough block across the two tops.
			StructureSolid lintel = RoughStone(d, Vector3.zero, new Vector3(3.6f, 0.75f, 0.95f), 0f, 5, 2, StructureMaterial.Monolith);
			d.Add(lintel.Transform(Matrix4x4.Translate(new Vector3(0f, 3.75f, 0f))));
		}

		private static void Altar(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.15f, 0f), new Vector3(2.3f, 0.5f, 1.5f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 0.9f, 0.85f), d.Bevel(0.04f), StructureMaterial.Monolith, 2));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.06f, 0f), new Vector3(1.75f, 0.12f, 1.05f), d.Bevel(0.025f), StructureMaterial.Monolith, 3));
				if (d.Mid)
				{
					// Two candle stubs (bone-pale wax) and a bowl.
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Cylinder(0.05f, 1.12f, 1.3f, d.Seg(8, 5), StructureMaterial.Bone, 4 + s, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(s * 0.6f, 0f, 0.25f))));
					}
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.1f, 1.12f), new Vector2(0.2f, 1.22f), new Vector2(0f, 1.2f) }, d.Seg(10, 6), StructureMaterial.Iron, 7, true, 0f, false, false, StructureRole.Detail));
				}
				return;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(RoughStone(d, new Vector3(s * 0.65f, -0.25f, 0f), new Vector3(0.45f, 1.1f, 0.9f), d.Range(2 + s, 1, -8f, 8f), 2 + s, 2, StructureMaterial.Monolith));
			}
			StructureSolid slab = RoughStone(d, Vector3.zero, new Vector3(2.1f, 0.3f, 1.2f), 0f, 5, 3, StructureMaterial.Monolith);
			d.Add(slab.Transform(Matrix4x4.TRS(new Vector3(0f, 0.82f, 0f), Quaternion.Euler(0f, 0f, d.Range(5, 1, -3f, 3f)), Vector3.one)));
		}

		private static void Shrine(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.1f, 0f), new Vector3(2.6f, 0.6f, 2.6f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.3f, -1.55f), new Vector3(1.2f, 0.2f, 0.5f), d.Bevel(0.03f), StructureMaterial.Stone, 2));
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.ChamferBox(new Vector3(sx * 1.05f, 1.2f, sz * 1.05f), new Vector3(0.26f, 2f, 0.26f), d.Bevel(0.03f), StructureMaterial.Monolith, 10 + sx + 3 * sz));
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 2.29f, 0f), new Vector3(2.5f, 0.18f, 2.5f), StructureMaterial.Timber, 20));
				GableRoof(d, 2.4f, 2.4f, 2.38f, 1f, 0.35f, 0.22f, StructureMaterial.Thatch, 21, Matrix4x4.identity);
				if (d.Mid)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.65f, 0.3f), new Vector3(0.8f, 0.9f, 0.6f), d.Bevel(0.03f), StructureMaterial.Monolith, 30, StructureRole.Detail));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.1f, 1.1f), new Vector2(0.22f, 1.22f), new Vector2(0f, 1.2f) }, d.Seg(10, 6), StructureMaterial.Iron, 31, true, 0f, false, false, StructureRole.Detail));
				}
				return;
			}
			// A wayside shrine: a post, a niche with a little roof, a step.
			d.Add(RoughStone(d, new Vector3(0f, -0.2f, 0f), new Vector3(0.7f, 0.4f, 0.7f), 15f, 1, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, 0f), new Vector3(0f, 1.35f, 0f), 0.16f, 0.16f, StructureMaterial.Timber, 2));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.62f, 0f), new Vector3(0.55f, 0.6f, 0.42f), d.Bevel(0.015f), StructureMaterial.Timber, 3));
			d.Add(StructureShapes.Box(new Vector3(0f, 1.6f, -0.2f), new Vector3(0.36f, 0.42f, 0.04f), StructureMaterial.Ash, 4, StructureRole.Detail));
			GableRoof(d, 0.55f, 0.42f, 1.92f, 0.28f, 0.1f, 0.05f, StructureMaterial.Timber, 5, Matrix4x4.identity);
		}

		private static void Obelisk(StructureDraft d)
		{
			bool tall = d.Variant == 1;
			int steps = tall ? 3 : 2;
			float y = -0.3f;
			for (int i = 0; i < steps; i++)
			{
				float w = (tall ? 2.6f : 1.9f) - i * 0.45f;
				float h = i == 0 ? 0.55f : 0.28f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, y + h * 0.5f, 0f), new Vector3(w, h, w), d.Bevel(0.025f), StructureMaterial.Stone, 1 + i));
				y += h;
			}
			float shaft = tall ? 8.5f : 5.5f, foot = tall ? 0.55f : 0.42f;
			var profile = new[] { new Vector2(foot * 1.414f, y), new Vector2(foot * 0.66f * 1.414f, y + shaft), new Vector2(0f, y + shaft + foot * 0.9f) };
			d.Add(StructureShapes.Lathe(profile, 4, StructureMaterial.Monolith, 10, false, Mathf.PI * 0.25f));
		}

		private static void StatuePlinth(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.1f, 0f), new Vector3(1.9f, 0.6f, 1.9f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.75f, 0f), new Vector3(1.35f, 1.1f, 1.35f), d.Bevel(0.04f), StructureMaterial.Stone, 2));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.38f, 0f), new Vector3(1.6f, 0.16f, 1.6f), d.Bevel(0.03f), StructureMaterial.Monolith, 3));
				return;
			}
			int seg = d.Seg(18, 8);
			var profile = new List<Vector2> { new Vector2(1f, -0.4f), new Vector2(1f, 0.2f), new Vector2(0.72f, 0.3f), new Vector2(0.66f, 1.25f), new Vector2(0.82f, 1.33f), new Vector2(0.82f, 1.46f), new Vector2(0f, 1.46f) };
			d.Add(StructureShapes.Lathe(profile, seg, StructureMaterial.Monolith, 1));
		}

		private static void Statue(StructureDraft d)
		{
			// On the square plinth, 1.46 m.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.1f, 0f), new Vector3(1.9f, 0.6f, 1.9f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.75f, 0f), new Vector3(1.35f, 1.1f, 1.35f), d.Bevel(0.04f), StructureMaterial.Stone, 2));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.38f, 0f), new Vector3(1.6f, 0.16f, 1.6f), d.Bevel(0.03f), StructureMaterial.Monolith, 3));
			Figure(d, new Vector3(0f, 1.46f, 0f), 1.15f, d.Variant == 1, 10);
		}

		/// <summary>
		/// A robed figure standing at <paramref name="foot"/>, <paramref name="scale"/> × 1.9 m tall, facing −z: a robe
		/// flaring to the ground, a torso with square shoulders, a neck and head, arms bent from the shoulders. Bare-headed
		/// with both hands on a sword planted point down; <paramref name="hooded"/>, open-faced in a hood with a staff in the
		/// right hand. About 800 triangles at level 0.
		/// </summary>
		/// <remarks>
		/// The parts are separate closed solids that overlap (robe into torso, arms into shoulders) rather than one welded
		/// skin: the silhouette — the gaps between arms and body, the narrow neck, the shoulder line — is what stops it
		/// reading as a turned chess piece, and it stays at every level because only facet counts fall.
		/// </remarks>
		internal static void Figure(StructureDraft d, Vector3 foot, float scale, bool hooded, int key)
		{
			const StructureMaterial stone = StructureMaterial.Monolith;
			int seg = d.Seg(14, 6), small = d.Seg(8, 5), rings = d.Fine ? 6 : 4;
			Matrix4x4 place = Matrix4x4.TRS(foot, Quaternion.identity, Vector3.one * scale);
			StructureSolid Put(StructureSolid s) => d.Add(s.Transform(place));

			// The robe: wide at the hem, gathered at the waist; a little deeper than a body so it hangs.
			var skirt = new List<Vector2> { new Vector2(0.36f, 0f), new Vector2(0.33f, 0.12f), new Vector2(0.26f, 0.6f), new Vector2(0.2f, 1f), new Vector2(0.19f, 1.08f) };
			if (!d.Mid)
			{
				skirt.RemoveAt(1);
			}
			Put(StructureShapes.Lathe(skirt, seg, stone, key).Transform(Matrix4x4.Scale(new Vector3(1.1f, 1f, 0.85f))));
			// The torso: chest to the shoulder line, wider than deep, closing over at the neck.
			var torso = new List<Vector2> { new Vector2(0.17f, 1f), new Vector2(0.2f, 1.26f), new Vector2(0.2f, 1.44f), new Vector2(0.11f, 1.54f), new Vector2(0f, 1.56f) };
			Put(StructureShapes.Lathe(torso, seg, stone, key + 1).Transform(Matrix4x4.Scale(new Vector3(1.2f, 1f, 0.72f))));
			Put(StructureShapes.Cylinder(0.055f, 1.5f, 1.68f, small, stone, key + 2));
			Put(StructureShapes.Sphere(new Vector3(0f, 1.77f, -0.01f), new Vector3(0.1f, 0.12f, 0.11f), seg, rings, stone, key + 3));
			if (hooded)
			{
				// A hood over the head and shoulders, open at the front so the face shows.
				StructureSolid hood = StructureShapes.Lathe(new[] { new Vector2(0.17f, 1.46f), new Vector2(0.16f, 1.74f), new Vector2(0.11f, 1.88f), new Vector2(0f, 1.93f) }, seg, stone, key + 4)
					.Transform(Matrix4x4.Translate(new Vector3(0f, 0f, 0.04f)));
				hood.Clip(Vector3.back, 0.05f);
				Put(hood);
			}
			// Shoulders, then the arms: upper arm and sleeved forearm, a hand at the end.
			for (int s = -1; s <= 1; s += 2)
			{
				int k = key + 10 + (s + 1) * 3; // 10..12 left, 16..18 right
				var shoulder = new Vector3(s * 0.22f, 1.43f, 0f);
				Put(StructureShapes.Sphere(shoulder, new Vector3(0.085f, 0.085f, 0.085f), small, d.Fine ? 5 : 4, stone, k));
				Vector3 elbow, hand;
				if (!hooded)
				{
					// Both hands on the sword's pommel, elbows out.
					elbow = new Vector3(s * 0.31f, 1.13f, -0.1f);
					hand = new Vector3(s * 0.055f, 1.02f, -0.31f);
				}
				else if (s > 0)
				{
					// The staff hand, out to the side at chest height.
					elbow = new Vector3(0.34f, 1.15f, -0.04f);
					hand = new Vector3(0.41f, 1.22f, -0.17f);
				}
				else
				{
					// The other hand at the waist, holding the robe.
					elbow = new Vector3(-0.3f, 1.12f, -0.06f);
					hand = new Vector3(-0.1f, 1.02f, -0.23f);
				}
				Put(StructureShapes.Rod(shoulder, elbow, 0.058f, small, stone, k + 1));
				Put(StructureShapes.Rod(elbow, hand, 0.066f, small, stone, k + 2));
				Put(StructureShapes.Sphere(hand, new Vector3(0.05f, 0.05f, 0.05f), d.Seg(6, 4), 4, stone, k + 3));
			}
			if (hooded)
			{
				Put(StructureShapes.Rod(new Vector3(0.41f, 0.02f, -0.17f), new Vector3(0.41f, 2.1f, -0.17f), 0.028f, d.Seg(6, 4), stone, key + 30));
				if (d.Mid)
				{
					// A knob at the staff's head.
					Put(StructureShapes.Sphere(new Vector3(0.41f, 2.12f, -0.17f), new Vector3(0.05f, 0.06f, 0.05f), d.Seg(6, 4), 4, stone, key + 31, StructureRole.Detail));
				}
			}
			else
			{
				// A sword planted point down: blade, cross-guard, grip and pommel under the hands.
				Put(StructureShapes.Box(new Vector3(0f, 0.5f, -0.33f), new Vector3(0.075f, 0.8f, 0.022f), stone, key + 30));
				Put(StructureShapes.Box(new Vector3(0f, 0.92f, -0.33f), new Vector3(0.3f, 0.045f, 0.05f), stone, key + 31));
				Put(StructureShapes.Rod(new Vector3(0f, 0.94f, -0.33f), new Vector3(0f, 1.06f, -0.33f), 0.022f, d.Seg(6, 4), stone, key + 32));
				if (d.Mid)
				{
					Put(StructureShapes.Sphere(new Vector3(0f, 1.08f, -0.33f), new Vector3(0.035f, 0.035f, 0.035f), d.Seg(6, 4), 4, stone, key + 33, StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				// Toes of the feet showing under the hem.
				for (int s = -1; s <= 1; s += 2)
				{
					Put(StructureShapes.Box(new Vector3(s * 0.09f, 0.035f, -0.31f), new Vector3(0.09f, 0.07f, 0.12f), stone, key + 35 + s, StructureRole.Detail));
				}
			}
		}

		private static void PortalArch(StructureDraft d)
		{
			// The opening is 2.8 m wide and 4.4 m high at every level (the last level is the collider).
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.25f, 0f), new Vector3(5.6f, 0.5f, 1.6f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 1.8f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 1.1f), d.Bevel(0.03f), StructureMaterial.Stone, 3 + s));
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 1.8f, 2.45f, 0f), new Vector3(0.8f, 4.3f, 0.8f), d.Bevel(0.04f), StructureMaterial.Stone, 6 + s));
			}
			if (d.Variant == 0)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * 1.8f, 4.75f, 0f), new Vector3(1.05f, 0.3f, 1.05f), d.Bevel(0.03f), StructureMaterial.Stone, 9 + s));
				}
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 5.25f, 0f), new Vector3(5f, 0.7f, 1.05f), d.Bevel(0.04f), StructureMaterial.Stone, 12));
				if (d.Mid)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 5.75f, 0f), new Vector3(1.2f, 0.3f, 1.1f), d.Bevel(0.03f), StructureMaterial.Stone, 13, StructureRole.Detail));
				}
				return;
			}
			// Pointed: two leaning blocks from the pillar heads meeting over the middle, and a keystone.
			for (int s = -1; s <= 1; s += 2)
			{
				var a = new Vector3(s * 1.8f, 4.6f, 0f);
				var b = new Vector3(s * 0.15f, 6.1f, 0f);
				d.Add(StructureShapes.Beam(a, b, 0.9f, 0.75f, StructureMaterial.Stone, 15 + s));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 6.25f, 0f), new Vector3(0.6f, 0.9f, 0.95f), d.Bevel(0.03f), StructureMaterial.Stone, 18));
		}

		private static void PortalRing(StructureDraft d)
		{
			bool flanked = d.Variant == 1;
			float inner = flanked ? 1.9f : 2f, outer = flanked ? 2.4f : 2.6f, centre = flanked ? 3.1f : 2.85f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.15f, 0f), new Vector3(2.4f, 0.7f, 1.4f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
			int stones = d.Lod == 0 ? 15 : d.Lod == 1 ? 13 : 11;
			for (int i = 0; i < stones; i++)
			{
				float a0 = -Mathf.PI * 0.5f + (i + 0.5f) * 2f * Mathf.PI / stones, a1 = a0 + 2f * Mathf.PI / stones;
				float gap = d.Fine ? 0.012f : 0f;
				StructureSolid block = RingBlock(Vector3.zero, inner, outer, -0.35f, 0.35f, a0 + gap, a1 - gap, StructureMaterial.Stone, 10 + i, StructureRole.Structure);
				// The ring stands in the x-y plane, facing −z: turn the lathe's y axis into z.
				d.Add(block.Transform(Matrix4x4.TRS(new Vector3(0f, centre, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
			// The key stone at the top, proud.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, centre + outer, 0f), new Vector3(0.6f, 0.6f, 0.85f), d.Bevel(0.03f), StructureMaterial.Stone, 40));
			if (flanked)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(RoughStone(d, new Vector3(s * 3.4f, -0.4f, 0.2f), new Vector3(0.8f, 3.4f, 0.55f), s * 8f, 50 + s, 3).Transform(Lean(d, 50 + s, 4f, new Vector3(s * 3.4f, 0f, 0.2f))));
				}
			}
			else
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * 1.45f, 0.75f, 0f), new Vector3(0.7f, 1.5f, 1.0f), d.Bevel(0.03f), StructureMaterial.Stone, 45 + s));
				}
			}
		}

		// ── The dead ──────────────────────────────────────────────────

		private static void Gravestone(StructureDraft d)
		{
			int first = d.Solids.Count;
			const StructureMaterial stone = StructureMaterial.Monolith;
			switch (d.Variant)
			{
				case 0:
				{
					// Rounded top.
					var outline = new List<Vector2> { new Vector2(-0.3f, -0.3f), new Vector2(0.3f, -0.3f), new Vector2(0.3f, 0.6f) };
					int arc = d.Fine ? 8 : 4;
					for (int i = 1; i < arc; i++)
					{
						float a = Mathf.PI * i / arc;
						outline.Add(new Vector2(0.3f * Mathf.Cos(a), 0.6f + 0.3f * Mathf.Sin(a)));
					}
					outline.Add(new Vector2(-0.3f, 0.6f));
					d.Add(StructureShapes.Prism(outline, -0.06f, 0.06f, stone, 1));
					break;
				}
				case 1:
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.35f, 0f), new Vector3(0.14f, 1.3f, 0.12f), d.Bevel(0.015f), stone, 1));
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.72f, 0f), new Vector3(0.62f, 0.14f, 0.12f), d.Bevel(0.015f), stone, 2));
					break;
				case 2:
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.05f, 0f), new Vector3(0.5f, 0.3f, 0.5f), d.Bevel(0.02f), stone, 1));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.24f, 0.1f), new Vector2(0.16f, 1.2f), new Vector2(0f, 1.36f) }, 4, stone, 2, false, Mathf.PI * 0.25f));
					break;
				case 3:
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.05f, 0f), new Vector3(0.75f, 0.25f, 0.35f), d.Bevel(0.02f), stone, 1));
					d.Add(StructureShapes.Block(new Vector3(0f, 0.42f, 0.05f), new Vector3(0.6f, 0.8f, 0.1f), Quaternion.Euler(-12f, 0f, 0f), stone, 2, StructureRole.Structure, d.Bevel(0.015f)));
					break;
				case 4:
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.45f, 0f), new Vector3(0.16f, 1.5f, 0.14f), d.Bevel(0.015f), stone, 1));
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 0.16f, 0.14f), d.Bevel(0.015f), stone, 2));
					int n = d.Seg(12, 8);
					for (int i = 0; i < n; i++)
					{
						float a0 = i * 2f * Mathf.PI / n, a1 = (i + 1) * 2f * Mathf.PI / n;
						StructureSolid seg = RingBlock(Vector3.zero, 0.2f, 0.27f, -0.05f, 0.05f, a0, a1, stone, 10 + i, StructureRole.Structure);
						d.Add(seg.Transform(Matrix4x4.TRS(new Vector3(0f, 0.85f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
					}
					break;
				}
				default:
				{
					StructureSolid block = StructureShapes.ChamferBox(new Vector3(0f, 0.15f, 0f), new Vector3(0.55f, 0.7f, 0.4f), d.Bevel(0.02f), stone, 1);
					// A sloped top, falling to the front.
					block.Clip(new Vector3(0f, 0.92f, -0.38f).normalized, Vector3.Dot(new Vector3(0f, 0.92f, -0.38f).normalized, new Vector3(0f, 0.42f, -0.2f)));
					d.Add(block);
					break;
				}
			}
			// Settled: a few degrees off true.
			TransformSince(d, first, Lean(d, 99, 5f, Vector3.zero) * Matrix4x4.Rotate(Quaternion.Euler(0f, d.Range(99, 5, -6f, 6f), 0f)));
		}

		private static void GraveMound(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				StructureSolid mound = StructureShapes.Sphere(new Vector3(0f, -0.25f, 0f), new Vector3(0.75f, 0.6f, 1.25f), d.Seg(12, 6), d.Fine ? 7 : 4, StructureMaterial.Earth, 1);
				// Only the top of the ellipsoid: the rest would be buried.
				mound.Clip(Vector3.down, 0.3f);
				d.Add(mound);
				return;
			}
			// A cairn: stones piled into a low cone.
			int stones = d.Lod == 0 ? 14 : d.Lod == 1 ? 10 : 7;
			for (int i = 0; i < stones; i++)
			{
				float t = (i + 0.5f) / stones;
				float a = i * 2.39996f;
				float r = 0.85f * Mathf.Sqrt(1f - t);
				float y = 0.75f * t - 0.12f;
				var size = new Vector3(d.Range(i, 1, 0.3f, 0.45f), d.Range(i, 2, 0.22f, 0.32f), d.Range(i, 3, 0.28f, 0.4f));
				d.Add(RoughStone(d, new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r * 1.5f), size, d.Rand(i, 4) * 360f, 10 + i, 2));
			}
		}

		private static void Mausoleum(StructureDraft d)
		{
			const StructureMaterial stone = StructureMaterial.Stone;
			// Two steps up to the floor, all round.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.2f, 0.2f), new Vector3(5.6f, 0.6f, 7f), d.Bevel(0.03f), stone, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.25f, 0.2f), new Vector3(5f, 0.3f, 6.4f), d.Bevel(0.03f), stone, 2));
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 2f, 0.9f), new Vector3(4.2f, 3.2f, 4.4f), d.Bevel(0.04f), stone, 3));
				// The porch: two columns and the pediment over them.
				for (int s = -1; s <= 1; s += 2)
				{
					Column(d, new Vector3(s * 1.55f, 0.4f, -2.2f), 3.2f, 0.22f, StructureMaterial.Monolith, 10 + s * 5);
				}
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 3.8f, 0.2f), new Vector3(4.5f, 0.4f, 5.8f), d.Bevel(0.03f), stone, 20));
				GableRoof(d, 4.5f, 5.8f, 4f, 1.1f, 0.12f, 0.22f, stone, 21, Matrix4x4.identity);
				Gable(d, 4.4f, 4f, 1.08f, -2.75f, 0.3f, stone, 23, Matrix4x4.identity);
				Gable(d, 4.4f, 4f, 1.08f, 3.15f, 0.3f, stone, 24, Matrix4x4.identity);
				d.Add(StructureShapes.Box(new Vector3(0f, 1.5f, -1.35f), new Vector3(1.2f, 2.1f, 0.1f), StructureMaterial.Iron, 25, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(0f, 2.65f, -1.38f), new Vector3(1.6f, 0.25f, 0.2f), d.Bevel(0.02f), stone, 26, StructureRole.Detail));
				}
				return;
			}
			// Domed: a cube, a drum, a dome, a finial.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.95f, 0.2f), new Vector3(4.4f, 3.1f, 4.4f), d.Bevel(0.04f), stone, 3));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 3.6f, 0.2f), new Vector3(4.7f, 0.3f, 4.7f), d.Bevel(0.03f), stone, 4));
			int seg = d.Seg(20, 8);
			var drum = new List<Vector2> { new Vector2(1.8f, 3.7f), new Vector2(1.8f, 4.4f), new Vector2(1.9f, 4.45f) };
			int rings = d.Fine ? 6 : 3;
			for (int i = 1; i < rings; i++)
			{
				float a = 0.5f * Mathf.PI * i / rings;
				drum.Add(new Vector2(1.9f * Mathf.Cos(a), 4.45f + 1.7f * Mathf.Sin(a)));
			}
			drum.Add(new Vector2(0f, 6.15f));
			d.Add(StructureShapes.Lathe(drum, seg, stone, 5).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, 0.2f))));
			d.Add(StructureShapes.Rod(new Vector3(0f, 6f, 0.2f), new Vector3(0f, 6.9f, 0.2f), 0.06f, d.Seg(6, 4), StructureMaterial.Iron, 6, StructureRole.Detail, 0.2f));
			d.Add(StructureShapes.Box(new Vector3(0f, 1.45f, -2.04f), new Vector3(1.2f, 2f, 0.1f), StructureMaterial.Iron, 7, StructureRole.Detail));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 2.6f, -2.1f), new Vector3(1.7f, 0.3f, 0.25f), d.Bevel(0.02f), stone, 8, StructureRole.Detail));
		}

		private static void CryptEntrance(StructureDraft d)
		{
			const StructureMaterial stone = StructureMaterial.Stone;
			// The barrow behind it.
			StructureSolid barrow = StructureShapes.Sphere(new Vector3(0f, -0.6f, 2.6f), new Vector3(3.6f, 3f, 4.2f), d.Seg(14, 8), d.Fine ? 8 : 5, StructureMaterial.Earth, 1);
			barrow.Clip(Vector3.down, 0.5f);
			d.Add(barrow);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 0.95f, 1.05f, -1.1f), new Vector3(0.55f, 2.7f, 0.8f), d.Bevel(0.03f), stone, 3 + s));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 2.65f, -1.1f), new Vector3(2.7f, 0.55f, 0.9f), d.Bevel(0.03f), stone, 6));
			Gable(d, 2.7f, 2.92f, 0.6f, -1.1f, 0.8f, stone, 7, Matrix4x4.identity);
			d.Add(StructureShapes.Box(new Vector3(0f, 1.1f, -0.85f), new Vector3(1.4f, 2.4f, 0.3f), StructureMaterial.Iron, 8, StructureRole.Detail));
			if (d.Variant == 1)
			{
				// Low walls flanking the approach and a slab roofing it.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * 1.45f, 0.55f, -2.4f), new Vector3(0.5f, 1.4f, 2.4f), d.Bevel(0.03f), stone, 10 + s));
				}
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.38f, -2.6f), new Vector3(3.5f, 0.28f, 2.2f), d.Bevel(0.03f), stone, 13, StructureRole.Roof));
			}
		}

		private static void BonePile(StructureDraft d)
		{
			bool scattered = d.Variant == 1;
			int bones = d.Lod == 0 ? 5 : d.Lod == 1 ? 4 : 3;
			for (int i = 0; i < bones; i++)
			{
				float length = d.Range(i, 1, 0.3f, 0.48f);
				float a = d.Rand(i, 2) * 360f;
				float spread = scattered ? 0.75f : 0.25f;
				var at = new Vector3(d.Range(i, 3, -spread, spread), 0.03f + (scattered ? 0f : i * 0.035f), d.Range(i, 4, -spread, spread));
				var profile = new List<Vector2> { new Vector2(0.026f, 0f), new Vector2(0.04f, 0.025f) };
				if (d.Mid) profile.Add(new Vector2(0.02f, 0.08f));
				if (d.Mid) profile.Add(new Vector2(0.02f, length - 0.08f));
				profile.Add(new Vector2(0.04f, length - 0.025f));
				profile.Add(new Vector2(0.026f, length));
				StructureSolid bone = StructureShapes.Lathe(profile, d.Seg(6, 4), StructureMaterial.Bone, 10 + i, true, 0f, false, true);
				d.Add(bone.Transform(Matrix4x4.TRS(at, Quaternion.Euler(0f, a, 90f + d.Range(i, 5, -12f, 12f)), Vector3.one)));
			}
			int skulls = scattered ? 2 : 1;
			for (int k = 0; k < skulls; k++)
			{
				var at = new Vector3(scattered ? (k == 0 ? -0.4f : 0.5f) : 0.12f, 0.09f + (scattered ? 0f : 0.1f), scattered ? (k == 0 ? 0.3f : -0.35f) : 0.05f);
				d.Add(StructureShapes.Sphere(at, new Vector3(0.085f, 0.095f, 0.11f), d.Seg(9, 5), d.Fine ? 6 : 4, StructureMaterial.Bone, 30 + k));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(at + new Vector3(0f, -0.06f, -0.06f), new Vector3(0.09f, 0.05f, 0.07f), StructureMaterial.Bone, 32 + k, StructureRole.Detail));
				}
			}
		}

		private static void IronFence(StructureDraft d)
		{
			const float module = 3f;
			bool wall = d.Variant == 1;
			float baseY = wall ? 0.55f : 0f;
			if (wall)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (baseY - 0.4f) * 0.5f, 0f), new Vector3(module, baseY + 0.4f, 0.4f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
			}
			float top = baseY + 1.6f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (module * 0.5f - 0.05f), (baseY - 0.3f + top + 0.1f) * 0.5f, 0f), new Vector3(0.1f, top + 0.1f - baseY + 0.3f, 0.1f), StructureMaterial.Iron, 2 + s));
			}
			foreach (float y in new[] { baseY + 0.15f, top - 0.2f })
			{
				d.Add(StructureShapes.Box(new Vector3(0f, y, 0f), new Vector3(module - 0.1f, 0.05f, 0.04f), StructureMaterial.Iron, y < 1f + baseY ? 5 : 6));
			}
			int bars = d.Lod == 0 ? 11 : d.Lod == 1 ? 8 : 5;
			for (int i = 0; i < bars; i++)
			{
				float x = -module * 0.5f + 0.1f + (module - 0.2f) * (i + 0.5f) / bars;
				if (d.Fine)
				{
					d.Add(StructureShapes.Rod(new Vector3(x, baseY - (wall ? 0.05f : 0.3f), 0f), new Vector3(x, top + 0.15f, 0f), 0.014f, 4, StructureMaterial.Iron, 10 + i, StructureRole.Structure, 0.12f));
				}
				else
				{
					d.Add(StructureShapes.Box(new Vector3(x, (baseY + top) * 0.5f, 0f), new Vector3(0.03f, top - baseY, 0.03f), StructureMaterial.Iron, 10 + i));
				}
			}
		}
	}
}
#endif
