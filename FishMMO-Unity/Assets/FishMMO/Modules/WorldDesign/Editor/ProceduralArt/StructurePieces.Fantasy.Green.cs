#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class StructurePieces
	{
		/// <summary>
		/// The fantasy wave's Green pack: its new pieces (2026-10-27, poi-fantasy-wave/WAVE.md). Every one is laid by id (=Talan …):
		/// none carries a tag, so no older query's candidates move.
		/// </summary>
		private static void AddFantasyGreen(List<StructurePiece> list)
		{
			// Woods.
			Piece(list, "Talan", StructureStyle.Timber, StructureSize.Building,
				"A talan, 14 m: a great trunk with a round plank platform lashed round it at 5.3 m, a stair winding up outside, a thatched round hut on it; v1 a second, smaller platform higher up under bare limbs.",
				Talan, "stilt");
			Piece(list, "RopeWalk", StructureStyle.Timber, StructureSize.Medium,
				"A rope walk, 8 m along z at deck level: plank treads slung between two lashed end posts, a hand-rope each side; v1 sagging more, with a third rope below.",
				RopeWalk, "crossing").Anchor = StructureAnchor.Deck;
			Piece(list, "TreeStair", StructureStyle.Timber, StructureSize.Medium,
				"A stair tower of lashed logs, 5.4 m: a stout post with plank treads winding up it to a small landing; v1 with a rope rail.",
				TreeStair, "stilt");
			Piece(list, "WebCurtain", StructureStyle.Hide, StructureSize.Medium,
				"A curtain of spider silk, 6 m wide and 5.5 m tall, strung between two dead trunks: radial strands and a ragged sheet; v1 torn.",
				WebCurtain, "crossing").Collides = false;
			Piece(list, "WebSheet", StructureStyle.Hide, StructureSize.Medium,
				"A funnel of spider silk, 7 m across, slung at head height between two dead stakes, its throat low in the middle.",
				WebSheet, "crossing").Collides = false;
			Piece(list, "Cocoon", StructureStyle.Hide, StructureSize.Small,
				"A silk cocoon, 1.5 m, wound tight round something: v0 standing against a stake, v1 lying with a bone showing.",
				Cocoon, "crossing");
			Piece(list, "HollowOak", StructureStyle.Timber, StructureSize.Building,
				"A hollow oak, 11 m: a great trunk flared into buttress roots, a tall arched hollow in its foot glowing with faerie light, bare limbs; v1 split and leaning.",
				HollowOak, "arch");
			// Jungle.
			Piece(list, "StepPyramid", StructureStyle.Stone, StructureSize.Stronghold,
				"A stepped pyramid, 19 m square and 11 m tall in five tiers, a steep stair up the front to a summit shrine with a lintelled door and braziers' stands; v1 with a rune band round each tier.",
				StepPyramid, "monument");
			Ruin(list, "RuinedStepPyramid", "StepPyramid", 2, 2, 0.45f, StructureSize.Stronghold, "A stepped pyramid with its shrine gone and its tiers cracked and fallen.", "ruin", "monument");
			Piece(list, "JaguarMaw", StructureStyle.Stone, StructureSize.Building,
				"A jaguar's maw: a grassed earth mound 11 m across with a stair up its front to a stone doorway whose lintel is a snarling mouth of fangs; v0 two fangs, v1 four and a carved brow.",
				JaguarMaw, "monument");
			// Wetland.
			Piece(list, "PileRoundHouse", StructureStyle.Timber, StructureSize.Building,
				"A crannog's round house on piles, wattle walls under a thatched cone, laid at deck level (the pier joins its door); its piles reach 3.2 m down to stand in shallows; v1 with a lashed outer rail.",
				PileRoundHouse, "stilt").Anchor = StructureAnchor.Deck;
			Piece(list, "ReedHall", StructureStyle.Timber, StructureSize.Building,
				"A reed hall: a long barrel-vaulted house of arched reed bundles, 4.4 m wide, 3.4 m high and 9 m long, bundle ends tied off at the front over a low door; v1 with a porch arch.",
				ReedHall, "stilt");
			Piece(list, "BasaltHall", StructureStyle.Stone, StructureSize.Building,
				"A fire giant's hall: a long dark basalt house, 15 m by 6.4 m, under a steep slab roof, a forge mouth glowing on the long side with a stone chimney above it; v1 with a second chimney.",
				BasaltHall, "monument");
			Ruin(list, "RuinedMinster", "Church", 2, 2, 0.5f, StructureSize.Building, "A minster church standing to a ragged half: the nave's walls broken and its roof gone (the cathedral's own ruin would spread past the kit's 40 m); laid scaled up.", "ruin", "monument");
		}

		// ── Shared bits ───────────────────────────────────────────────

		/// <summary>A thatched cone over a round wall, centred at <paramref name="at"/> (xz), its eave at <paramref name="y"/>.</summary>
		private static void GreenCone(StructureDraft d, Vector3 at, float radius, float y, float rise, int key)
		{
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Thatch, rise, radius);
			int seg = d.Seg(12, 8);
			using (d.Builds(RoofRoles))
			{
				StructureSolid s;
				if (roof.Flat)
				{
					s = StructureShapes.Lathe(new[] { new Vector2(0f, y), new Vector2(radius, y), new Vector2(radius, y + 0.25f), new Vector2(0f, y + 0.25f) },
						seg, StructureMaterial.Thatch, key, true, 0f, false, true, StructureRole.Roof);
				}
				else
				{
					float peak = y + (roof.Rise == rise ? rise : roof.Rise);
					s = StructureShapes.Lathe(new[] { new Vector2(radius, y), new Vector2(0f, peak) }, seg, StructureMaterial.Thatch, key, true, 0f, false, true, StructureRole.Roof);
				}
				d.Add(s.Transform(Matrix4x4.Translate(new Vector3(at.x, 0f, at.z))));
			}
		}

		/// <summary>Plank treads winding up round an axis at <paramref name="at"/>, turn° from <paramref name="startDeg"/>; the top tread at <paramref name="rise"/>.</summary>
		private static void GreenSpiral(StructureDraft d, Vector3 at, float radius, float rise, float turnDeg, float startDeg, int key)
		{
			int n = Mathf.CeilToInt(rise / 0.2f);
			using (d.Builds(StepRoles))
			{
				for (int i = 0; i < n; i++)
				{
					float a = (startDeg + turnDeg * i / n) * Mathf.Deg2Rad;
					float top = rise * (i + 1f) / n;
					var c = new Vector3(at.x + Mathf.Cos(a) * radius, top - 0.06f, at.z + Mathf.Sin(a) * radius);
					d.Add(StructureShapes.Block(c, new Vector3(1.1f, 0.12f, 0.95f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), StructureMaterial.Timber, key + i));
					if (i % 4 == 0)
					{
						// A post under every fourth tread, to the ground.
						d.Add(StructureShapes.Box(new Vector3(c.x, top * 0.5f - 0.1f, c.z), new Vector3(0.14f, top - 0.2f, 0.14f), StructureMaterial.Timber, key + 60 + i, StructureRole.Detail));
					}
				}
			}
		}

		/// <summary>A tapering trunk, flared at the foot.</summary>
		private static StructureSolid GreenTrunk(StructureDraft d, float foot, float waist, float top, float height, int key)
		{
			return d.Add(StructureShapes.Lathe(new[]
			{
				new Vector2(foot, -0.5f), new Vector2(foot * 0.8f, 0.9f), new Vector2(waist, height * 0.35f), new Vector2(waist * 0.9f, height * 0.7f), new Vector2(top, height),
			}, d.Seg(12, 6), StructureMaterial.Timber, key, true, 0f, false, true));
		}

		private static void Limb(StructureDraft d, Vector3 from, float yaw, float reach, float rise, float radius, int key)
		{
			Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
			Log(d, from, from + dir * reach + Vector3.up * rise, radius, key, 0.5f, StructureMaterial.Timber, 6, StructureRole.Detail);
		}

		// ── Woods ─────────────────────────────────────────────────────

		private static void Talan(StructureDraft d)
		{
			const float deck = 5.3f;
			float height = d.Variant == 0 ? 14f : 15.5f;
			GreenTrunk(d, 1.25f, 0.85f, 0.55f, height, 1);
			// The platform, braced off the trunk, with a rail round it.
			d.Add(StructureShapes.Cylinder(3.5f, deck - 0.25f, deck, d.Seg(16, 8), StructureMaterial.Timber, 2));
			for (int i = 0; i < 4; i++)
			{
				float a = i * Mathf.PI * 0.5f + 0.4f;
				Vector3 out3 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				d.Add(StructureShapes.Beam(out3 * 0.8f + Vector3.up * 3.2f, out3 * 3.2f + Vector3.up * (deck - 0.25f), 0.2f, 0.2f, StructureMaterial.Timber, 3 + i));
			}
			int posts = d.Fine ? 12 : 8;
			for (int i = 0; i < posts; i++)
			{
				float a = i * Mathf.PI * 2f / posts;
				if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0f)) < 28f) continue;
				d.Add(StructureShapes.Box(new Vector3(Mathf.Cos(a) * 3.35f, deck + 0.5f, Mathf.Sin(a) * 3.35f), new Vector3(0.1f, 1f, 0.1f), StructureMaterial.Timber, 10 + i, StructureRole.Detail));
			}
			GreenSpiral(d, Vector3.zero, 4.35f, deck, 270f, 0f, 100);
			// The hut at the platform's side, away from the trunk.
			const float hx = 0f, hz = 2.05f;
			d.Add(StructureShapes.Cylinder(1.15f, deck, deck + 2.3f, d.Seg(12, 7), StructureMaterial.Timber, 30)).Transform(Matrix4x4.Translate(new Vector3(hx, 0f, hz)));
			GreenCone(d, new Vector3(hx, 0f, hz), 1.65f, deck + 2.3f, 1.9f, 31);
			d.Emit(new Vector3(0f, deck + 0.9f, 3.0f), SceneEmitterKind.Lamp);
			// Bare limbs at the crown.
			for (int i = 0; i < 4; i++)
			{
				Limb(d, new Vector3(0f, height * 0.78f, 0f), i * 90f + d.Range(i, 1, -20f, 20f), 3.5f, 2.2f, 0.28f, 40 + i);
			}
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Cylinder(2.2f, 9.5f, 9.7f, d.Seg(12, 7), StructureMaterial.Timber, 50));
				for (int i = 0; i < 4; i++)
				{
					float a = i * Mathf.PI * 0.5f + 0.8f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.6f, 8.2f, Mathf.Sin(a) * 0.6f), new Vector3(Mathf.Cos(a) * 2.0f, 9.5f, Mathf.Sin(a) * 2.0f), 0.14f, 0.14f, StructureMaterial.Timber, 51 + i));
				}
			}
		}

		private static void RopeWalk(StructureDraft d)
		{
			bool slack = d.Variant == 1;
			const float len = 8f;
			float sag = slack ? 0.55f : 0.3f;
			int planks = d.Fine ? 17 : 11;
			for (int i = 0; i < planks; i++)
			{
				float t = i / (planks - 1f), z = (t - 0.5f) * (len - 0.5f);
				float y = -sag * (1f - Mathf.Pow(2f * t - 1f, 2f)) - 0.06f;
				d.Add(StructureShapes.Box(new Vector3(0f, y, z), new Vector3(1.1f, 0.07f, 0.42f), StructureMaterial.Timber, 1 + i));
			}
			for (int side = -1; side <= 1; side += 2)
			{
				float x = side * 0.62f;
				d.Add(StructureShapes.Box(new Vector3(x, 0.6f, -len * 0.5f), new Vector3(0.16f, 1.6f, 0.16f), StructureMaterial.Timber, 40 + side, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(x, 0.6f, len * 0.5f), new Vector3(0.16f, 1.6f, 0.16f), StructureMaterial.Timber, 43 + side, StructureRole.Detail));
				int bits = d.Mid ? 6 : 3;
				for (int j = 0; j < bits; j++)
				{
					float t0 = j / (float)bits, t1 = (j + 1f) / bits;
					Vector3 a = new Vector3(x, 0.95f - sag * (1f - Mathf.Pow(2f * t0 - 1f, 2f)) * 0.7f, (t0 - 0.5f) * len);
					Vector3 b = new Vector3(x, 0.95f - sag * (1f - Mathf.Pow(2f * t1 - 1f, 2f)) * 0.7f, (t1 - 0.5f) * len);
					d.Add(StructureShapes.Rod(a, b, 0.03f, 5, StructureMaterial.Timber, 60 + side * 7 + j, StructureRole.Detail));
				}
			}
			if (slack && d.Mid)
			{
				for (int j = 0; j < 4; j++)
				{
					float t0 = j / 4f, t1 = (j + 1f) / 4f;
					d.Add(StructureShapes.Rod(new Vector3(0f, -0.3f - sag * (1f - Mathf.Pow(2f * t0 - 1f, 2f)), (t0 - 0.5f) * len),
						new Vector3(0f, -0.3f - sag * (1f - Mathf.Pow(2f * t1 - 1f, 2f)), (t1 - 0.5f) * len), 0.03f, 5, StructureMaterial.Timber, 90 + j, StructureRole.Detail));
				}
			}
		}

		private static void TreeStair(StructureDraft d)
		{
			const float rise = 5.4f;
			d.Add(StructureShapes.Cylinder(0.42f, -0.5f, rise, d.Seg(10, 6), StructureMaterial.Timber, 1, true));
			GreenSpiral(d, Vector3.zero, 1.15f, rise, 360f, 0f, 100);
			d.Add(StructureShapes.Cylinder(1.5f, rise - 0.12f, rise, d.Seg(12, 7), StructureMaterial.Timber, 2));
			if (d.Variant == 1 && d.Mid)
			{
				for (int i = 0; i < 8; i++)
				{
					float a = i * Mathf.PI * 0.25f;
					d.Add(StructureShapes.Box(new Vector3(Mathf.Cos(a) * 1.45f, rise + 0.45f, Mathf.Sin(a) * 1.45f), new Vector3(0.07f, 0.9f, 0.07f), StructureMaterial.Timber, 10 + i, StructureRole.Detail));
				}
			}
		}

		private static void WebCurtain(StructureDraft d)
		{
			bool torn = d.Variant == 1;
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(s * 3f, -0.3f, 0f), new Vector3(s * 3.1f, 6f, 0f), 0.22f, 1 + s, 0.4f, StructureMaterial.Timber, 7);
			}
			// Radial strands from a hub, and spiral threads across them.
			var hub = new Vector3(0f, 3.2f, 0f);
			int spokes = d.Mid ? 9 : 6;
			var ends = new Vector3[spokes];
			for (int i = 0; i < spokes; i++)
			{
				float a = i * Mathf.PI * 2f / spokes;
				ends[i] = hub + new Vector3(Mathf.Cos(a) * 2.9f, Mathf.Sin(a) * 2.5f, 0f);
				if (torn && i % 3 == 1) ends[i] = hub + (ends[i] - hub) * 0.45f;
				d.Add(StructureShapes.Rod(hub, ends[i], 0.018f, 4, StructureMaterial.Canvas, 10 + i, StructureRole.Detail));
			}
			for (int ring = 1; ring <= 2; ring++)
			{
				for (int i = 0; i < spokes; i++)
				{
					Vector3 a = hub + (ends[i] - hub) * (ring / 2.3f), b = hub + (ends[(i + 1) % spokes] - hub) * (ring / 2.3f);
					d.Add(StructureShapes.Rod(a, b, 0.012f, 4, StructureMaterial.Canvas, 30 + ring * 12 + i, StructureRole.Detail));
				}
			}
			// The sheet itself: a thin pale pane across the middle.
			d.Add(StructureShapes.Box(hub + new Vector3(torn ? 0.5f : 0f, 0f, 0f), new Vector3(torn ? 3.4f : 4.6f, 3.8f, 0.015f), StructureMaterial.Canvas, 70, StructureRole.Detail));
		}

		private static void WebSheet(StructureDraft d)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(s * 3.4f, -0.3f, 0f), new Vector3(s * 3.4f, 2.8f, 0f), 0.16f, 1 + s, 0.3f, StructureMaterial.Timber, 6);
			}
			int n = d.Mid ? 7 : 4;
			// A sheet sagging to a throat in the middle: strips between a sagging centre line and the rim.
			for (int i = 0; i < n; i++)
			{
				float t = i / (n - 1f), x = (t - 0.5f) * 6.8f;
				float y = 1.6f + 0.9f * Mathf.Pow(2f * t - 1f, 2f);
				d.Add(StructureShapes.Box(new Vector3(x, y, 0f), new Vector3(6.8f / n + 0.05f, 0.012f, 3.2f - 1.6f * (1f - Mathf.Pow(2f * t - 1f, 2f))), StructureMaterial.Canvas, 10 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Rod(new Vector3(-3.4f, 2.7f, 0f), new Vector3(3.4f, 2.7f, 0f), 0.02f, 4, StructureMaterial.Canvas, 40, StructureRole.Detail));
		}

		private static void Cocoon(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				Log(d, new Vector3(0f, -0.2f, 0f), new Vector3(0f, 1.9f, 0f), 0.06f, 1, 0.1f, StructureMaterial.Timber, 5);
				d.Add(StructureShapes.Sphere(new Vector3(0f, 0.8f, 0.12f), new Vector3(0.38f, 0.78f, 0.36f), d.Seg(10, 6), d.Fine ? 6 : 4, StructureMaterial.Canvas, 2));
				return;
			}
			var cocoon = StructureShapes.Sphere(Vector3.zero, new Vector3(0.36f, 0.34f, 0.8f), d.Seg(10, 6), d.Fine ? 6 : 4, StructureMaterial.Canvas, 2);
			d.Add(cocoon.Transform(Matrix4x4.TRS(new Vector3(0f, 0.3f, 0f), Quaternion.Euler(0f, 25f, 0f), Vector3.one)));
			d.Add(StructureShapes.Rod(new Vector3(0.2f, 0.45f, 0.65f), new Vector3(0.45f, 0.6f, 1.15f), 0.04f, 5, StructureMaterial.Bone, 3, StructureRole.Detail));
		}

		private static void HollowOak(StructureDraft d)
		{
			bool split = d.Variant == 1;
			float height = 11f;
			StructureSolid trunk = GreenTrunk(d, 1.9f, 1.45f, 1.0f, height, 1);
			if (split)
			{
				trunk.Transform(Matrix4x4.Rotate(Quaternion.Euler(4f, 0f, -5f)));
			}
			int roots = d.Fine ? 6 : 4;
			for (int i = 0; i < roots; i++)
			{
				float a = i * 360f / roots + 18f;
				Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
				Log(d, dir * 0.9f + Vector3.up * 1.5f, dir * 3.4f + Vector3.down * 0.3f, 0.32f, 10 + i, 0.4f, StructureMaterial.Timber, 6);
			}
			// The hollow in its foot, facing −z: a dark arched niche with a stone-set sill and a cool light inside.
			d.Add(StructureShapes.Box(new Vector3(0f, 1.0f, -1.55f), new Vector3(1.0f, 2.0f, 0.5f), StructureMaterial.Ash, 20, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, 2.1f, -1.6f), new Vector3(1.3f, 0.2f, 0.5f), StructureMaterial.Timber, 21, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.08f, -1.9f), new Vector3(1.3f, 0.2f, 0.7f), StructureMaterial.Fieldstone, 22));
			d.Emit(new Vector3(0f, 1.0f, -1.35f), SceneEmitterKind.Glow);
			for (int i = 0; i < 5; i++)
			{
				Limb(d, new Vector3(0f, height * 0.8f, 0f), i * 72f + d.Range(i, 2, -15f, 15f), 4f, split ? 1.0f : 2.4f, 0.3f, 30 + i);
			}
		}

		// ── Jungle ────────────────────────────────────────────────────

		private static void StepPyramid(StructureDraft d)
		{
			bool runes = d.Variant == 1;
			float[] half = { 9.5f, 7.8f, 6.1f, 4.4f, 2.7f };
			const float tier = 2.1f;
			for (int i = 0; i < half.Length; i++)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, tier * (i + 0.5f) - 0.3f, 0f), new Vector3(half[i] * 2f, tier + 0.6f, half[i] * 2f), d.Bevel(0.05f), StructureMaterial.Stone, 1 + i));
				if (runes && d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, tier * (i + 0.5f), -half[i] - 0.015f), new Vector3(half[i] * 2f - 0.8f, 0.28f, 0.04f), StructureMaterial.Monolith, 10 + i, StructureRole.Detail));
				}
			}
			float top = tier * half.Length;
			// The great stair climbs the front from the ground to the top tier's edge, 0.25 m a step, a solid flight cut into the tiers.
			float edge = -half[half.Length - 1];
			int stairs = Mathf.CeilToInt(top / 0.25f);
			using (d.Builds(StepRoles))
			{
				for (int i = 0; i < stairs; i++)
				{
					float z1 = edge - (stairs - 1 - i) * 0.3f, y = top * (i + 1f) / stairs;
					d.Add(StructureShapes.BoxBetween(new Vector3(-1.6f, -0.3f, z1 - 0.3f), new Vector3(1.6f, y, z1), StructureMaterial.Stone, 100 + i));
				}
			}
			// Summit shrine: a squat stone room with a lintelled doorway and a roof slab.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 1.2f, 0.4f), new Vector3(3.6f, 2.4f, 3.2f), d.Bevel(0.04f), StructureMaterial.Stone, 20));
			d.Add(StructureShapes.Box(new Vector3(0f, top + 1.0f, -1.28f), new Vector3(1.0f, 1.8f, 0.2f), StructureMaterial.Ash, 21, StructureRole.Detail));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 2.6f, 0.4f), new Vector3(4.2f, 0.4f, 3.8f), d.Bevel(0.04f), StructureMaterial.Monolith, 22, StructureRole.Roof));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 3.15f, 0.4f), new Vector3(2.0f, 0.7f, 1.8f), d.Bevel(0.04f), StructureMaterial.Stone, 23, StructureRole.Roof));
			// Brazier stands at the stair's head.
			for (int s = -1; s <= 1; s += 2)
			{
				var at = Matrix4x4.Translate(new Vector3(s * 2.2f, 0f, -2.1f));
				d.Add(StructureShapes.Cylinder(0.3f, top, top + 0.8f, d.Seg(8, 5), StructureMaterial.Monolith, 30 + s).Transform(at));
				d.Add(StructureShapes.Cylinder(0.45f, top + 0.8f, top + 0.95f, d.Seg(8, 5), StructureMaterial.Ash, 33 + s, true, StructureRole.Detail).Transform(at));
				d.Emit(new Vector3(s * 2.2f, top + 0.95f, -2.1f), SceneEmitterKind.Fire);
			}
		}

		private static void JaguarMaw(StructureDraft d)
		{
			bool four = d.Variant == 1;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.4f), new Vector2(5.6f, -0.4f), new Vector2(5.2f, 0.7f), new Vector2(3.8f, 2.0f), new Vector2(2.0f, 2.8f), new Vector2(0f, 3.0f) },
				d.Seg(16, 8), StructureMaterial.Earth, 1));
			float zf = -4.6f;
			Steps(d, new Vector3(0f, 0f, zf - 0.2f), Vector3.back, 2.8f, 1.9f, StructureMaterial.Stone, 100);
			// The doorway: two jambs, a lintel of the maw, a dark passage behind.
			d.Add(StructureShapes.ChamferBox(new Vector3(-1.5f, 1.9f, zf + 0.9f), new Vector3(0.9f, 3.0f, 1.4f), d.Bevel(0.04f), StructureMaterial.Stone, 20));
			d.Add(StructureShapes.ChamferBox(new Vector3(1.5f, 1.9f, zf + 0.9f), new Vector3(0.9f, 3.0f, 1.4f), d.Bevel(0.04f), StructureMaterial.Stone, 21));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 3.75f, zf + 0.9f), new Vector3(3.9f, 0.7f, 1.4f), d.Bevel(0.04f), StructureMaterial.Monolith, 22));
			d.Add(StructureShapes.Box(new Vector3(0f, 1.9f, zf + 1.7f), new Vector3(2.1f, 3.0f, 0.2f), StructureMaterial.Ash, 23, StructureRole.Detail));
			// Fangs hang from the lintel; a brow ridge over it.
			int fangs = four ? 4 : 2;
			for (int i = 0; i < fangs; i++)
			{
				float x = fangs == 2 ? (i == 0 ? -1.05f : 1.05f) : -1.3f + i * 0.87f;
				float len = fangs == 4 && (i == 1 || i == 2) ? 0.9f : 1.3f;
				d.Add(StructureShapes.Rod(new Vector3(x, 3.45f, zf + 0.25f), new Vector3(x, 3.45f - len, zf + 0.25f), 0.17f, 6, StructureMaterial.Bone, 30 + i, StructureRole.Detail));
			}
			if (four)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 4.3f, zf + 0.9f), new Vector3(4.5f, 0.5f, 1.2f), d.Bevel(0.04f), StructureMaterial.Monolith, 40, StructureRole.Detail));
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Emit(new Vector3(s * 2.3f, 2.3f, zf - 0.2f), SceneEmitterKind.Fire);
				d.Add(StructureShapes.Cylinder(0.22f, 1.9f, 2.4f, d.Seg(8, 5), StructureMaterial.Stone, 50 + s).Transform(Matrix4x4.Translate(new Vector3(s * 2.3f, 0f, zf - 0.2f))));
			}
		}

		// ── Wetland ───────────────────────────────────────────────────

		private static void PileRoundHouse(StructureDraft d)
		{
			const float deck = 0f, inner = 3.1f, door = 1f;
			int seg = d.Seg(14, 8);
			d.Add(StructureShapes.Cylinder(inner + 0.9f, deck - 0.25f, deck, seg, StructureMaterial.Timber, 1));
			int piles = d.Fine ? 12 : 8;
			for (int i = 0; i < piles; i++)
			{
				float a = i * Mathf.PI * 2f / piles;
				Log(d, new Vector3(Mathf.Cos(a) * (inner + 0.6f), -3.2f, Mathf.Sin(a) * (inner + 0.6f)), new Vector3(Mathf.Cos(a) * (inner + 0.6f), deck - 0.2f, Mathf.Sin(a) * (inner + 0.6f)), 0.15f, 10 + i, 0f, StructureMaterial.Timber, 6);
			}
			using (d.Builds(WallRoles))
			{
				RingWallWithDoor(d, Vector3.zero, inner, inner + 0.14f, deck, deck + 2.6f, seg, 1.5f * Mathf.PI, door, deck + 2.2f, StructureMaterial.Timber, 30);
			}
			HangDoor(d, new Vector3(0f, deck, -Mathf.Sqrt(inner * inner - door * door * 0.25f) + 0.05f), Vector3.right, Vector3.forward, door, 2.2f, StructureDoorKind.Planks);
			d.RoundRoom(new Vector3(0f, deck, 0f), inner, 2.6f, StructureRoomKind.Dwelling);
			GreenCone(d, Vector3.zero, inner + 0.9f, deck + 2.6f, 3.2f, 31);
			if (d.Variant == 1 && d.Mid)
			{
				for (int i = 0; i < 10; i++)
				{
					float a = (30f + i * 30f) * Mathf.Deg2Rad;
					d.Add(StructureShapes.Box(new Vector3(Mathf.Cos(a) * (inner + 0.8f), deck + 0.5f, Mathf.Sin(a) * (inner + 0.8f)), new Vector3(0.08f, 1f, 0.08f), StructureMaterial.Timber, 60 + i, StructureRole.Detail));
				}
			}
		}

		private static void ReedHall(StructureDraft d)
		{
			bool porch = d.Variant == 1;
			const float w = 2.2f, h = 3.4f, len = 9f;
			int arc = d.Fine ? 8 : 5;
			var outline = new List<Vector2> { new Vector2(-w, 0f) };
			for (int i = 0; i <= arc; i++)
			{
				float a = Mathf.PI * (1f - i / (float)arc);
				outline.Add(new Vector2(Mathf.Cos(a) * w, Mathf.Sin(a) * h));
			}
			outline.Add(new Vector2(w, 0f));
			d.Add(StructureShapes.Prism(outline, -len * 0.5f, len * 0.5f, StructureMaterial.Thatch, 1));
			// Arched ribs standing out of the thatch, bundle ends tied and fanned at the front.
			int ribs = d.Mid ? 6 : 3;
			for (int r = 0; r < ribs; r++)
			{
				float z = Mathf.Lerp(-len * 0.5f + 0.4f, len * 0.5f - 0.4f, r / (ribs - 1f));
				for (int i = 0; i < arc; i++)
				{
					float a0 = Mathf.PI * (1f - i / (float)arc), a1 = Mathf.PI * (1f - (i + 1f) / arc);
					d.Add(StructureShapes.Rod(new Vector3(Mathf.Cos(a0) * (w + 0.07f), Mathf.Sin(a0) * (h + 0.07f), z), new Vector3(Mathf.Cos(a1) * (w + 0.07f), Mathf.Sin(a1) * (h + 0.07f), z), 0.07f, 5, StructureMaterial.Timber, 20 + r * 10 + i, StructureRole.Detail));
				}
			}
			float fz = -len * 0.5f;
			d.Add(StructureShapes.Box(new Vector3(0f, 1.1f, fz - 0.03f), new Vector3(1.2f, 2.2f, 0.12f), StructureMaterial.Ash, 90, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, 2.3f, fz - 0.06f), new Vector3(1.5f, 0.18f, 0.16f), StructureMaterial.Timber, 91, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, -0.05f, 0f), new Vector3(w * 2f + 0.5f, 0.3f, len + 0.5f), StructureMaterial.Timber, 92));
			if (porch)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					Log(d, new Vector3(s * 1.2f, -0.1f, fz - 1.4f), new Vector3(s * 1.2f, 2.6f, fz - 1.4f), 0.1f, 95 + s, 0f, StructureMaterial.Timber, 6);
				}
				d.Add(StructureShapes.Beam(new Vector3(-1.4f, 2.6f, fz - 1.4f), new Vector3(1.4f, 2.6f, fz - 1.4f), 0.14f, 0.14f, StructureMaterial.Timber, 98, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, 2.75f, fz - 0.7f), new Vector3(3f, 0.1f, 1.5f), StructureMaterial.Thatch, 99, StructureRole.Roof));
			}
			d.Emit(new Vector3(0f, h + 0.1f, 0f), SceneEmitterKind.Smoke);
		}

		// ── Volcanic ──────────────────────────────────────────────────

		private static void BasaltHall(StructureDraft d)
		{
			const float w = 3.2f, wallH = 3.2f, len = 15f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, wallH * 0.5f - 0.2f, 0f), new Vector3(w * 2f, wallH + 0.4f, len), d.Bevel(0.08f), StructureMaterial.Fieldstone, 1));
			var gable = new List<Vector2> { new Vector2(-w - 0.5f, 0f), new Vector2(w + 0.5f, 0f), new Vector2(0f, 3.1f) };
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Prism(gable, -len * 0.5f - 0.5f, len * 0.5f + 0.5f, StructureMaterial.Stone, 2, StructureRole.Roof).Transform(Matrix4x4.Translate(new Vector3(0f, wallH, 0f))));
			}
			float fz = -len * 0.5f;
			d.Add(StructureShapes.Box(new Vector3(0f, 1.3f, fz - 0.03f), new Vector3(1.6f, 2.6f, 0.14f), StructureMaterial.Ash, 10, StructureRole.Detail));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 2.7f, fz - 0.1f), new Vector3(2.3f, 0.4f, 0.3f), d.Bevel(0.03f), StructureMaterial.Monolith, 11, StructureRole.Detail));
			// The forge mouth in the long side and its chimney above.
			for (int c = 0; c < (d.Variant == 1 ? 2 : 1); c++)
			{
				float z = c == 0 ? 1.5f : -3.5f;
				d.Add(StructureShapes.ChamferBox(new Vector3(w + 0.55f, 1.2f, z), new Vector3(1.2f, 2.4f, 2.0f), d.Bevel(0.05f), StructureMaterial.Stone, 20 + c));
				d.Add(StructureShapes.Box(new Vector3(w + 1.17f, 0.8f, z), new Vector3(0.06f, 0.9f, 1.0f), StructureMaterial.Ash, 22 + c, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.5f, 2.4f, wallH + 3.4f, d.Seg(8, 5), StructureMaterial.Stone, 24 + c).Transform(Matrix4x4.Translate(new Vector3(w + 0.55f, 0f, z))));
				d.Emit(new Vector3(w + 1.0f, 0.8f, z), SceneEmitterKind.Forge);
				d.Emit(new Vector3(w + 0.55f, wallH + 3.5f, z), SceneEmitterKind.Smoke);
			}
			d.Add(StructureShapes.Box(new Vector3(0f, -0.05f, fz - 1.0f), new Vector3(3f, 0.3f, 2f), StructureMaterial.Stone, 30));
		}
	}
}
#endif
