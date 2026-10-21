#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Medieval light sources (2026-10-12: "We need torches, light posts, braziers, and other light sources"): a torch
	/// on a pole, a torch in an iron sconce for a wall, and a brazier. Each gives off its flame (SceneEmitterKind.Torch for a
	/// torch, Fire for a brazier's bowl), which the client lights (SiteLightPool) and burns (SiteEmbers' flames).
	/// </summary>
	public static partial class StructurePieces
	{
		/// <summary>A wall torch's flame stands this far out from the wall and this high over its fixing point, metres.</summary>
		public const float WallTorchReach = 0.32f, WallTorchRise = 0.42f;

		private static void AddLights(List<StructurePiece> list)
		{
			Piece(list, "TorchPost", StructureStyle.Timber, StructureSize.Small,
				"A torch on a pole, 2.4 m: v0 a pitch-wrapped torch in an iron cup, v1 an iron cresset basket of burning wood.",
				TorchPost, "light", "torch", "camp", "town");
			Piece(list, "WallTorch", StructureStyle.Iron, StructureSize.Small,
				"A torch in an iron sconce, its back plate at z = 0 against a wall, the torch leaning out along −z; v1 a ring bracket.",
				WallTorch, "light", "torch").Collides = false;
			Piece(list, "Brazier", StructureStyle.Iron, StructureSize.Small,
				"A fire bowl: v0 an iron brazier on three splayed legs, v1 a stone bowl on a pedestal.",
				Brazier, "light", "military", "sacred");
			// The lights of the biome suite (2026-10-16, I6c): what a biome's streets, gates, groves, caves and mines may be lit
			// with besides the lantern post, the torch post and the brazier (BiomeWayLightKind names each by its id). They are laid
			// by id only (=Cresset…), so none carries a tag a template's slot queries by ("fire" is a camp's fire or a kiln): a new
			// light joining a query's candidates would move every pick the query makes in every site.
			Piece(list, "Cresset", StructureStyle.Timber, StructureSize.Small,
				"A cresset, 3.4 m: an iron fire basket of burning resin-pine knots high on a pole: v0 on a squared timber pole, v1 on an iron pole in a fieldstone footing.",
				Cresset, "light", "torch", "military", "town");
			Piece(list, "CrookLantern", StructureStyle.Timber, StructureSize.Small,
				"An iron lantern hung on a short chain from a bent oak shepherd's-crook post, 3 m: v0 a square lantern, v1 a round horn lantern and an iron ferrule on the post.",
				CrookLantern, "light", "lantern", "town");
			Piece(list, "Glowjar", StructureStyle.Timber, StructureSize.Small,
				"A glowjar, 1.9 m: faerie light in a stoppered glass jar on a crooked stake: v0 hung from a cord off its arm, v1 set in an iron cage on its top.",
				Glowjar, "light", "lantern", "sacred");
			Piece(list, CrystalPostId, StructureStyle.Stone, StructureSize.Small,
				"A crystal of the biome's own kind in an iron cradle (the site lays the crystal at the cradle, StructurePieces.TryCrystalSocket): v0 on a carved stone pillar, 1.8 m; v1 on an iron-banded timber post, 1.75 m.",
				CrystalPost, "light", "sacred", "town");
			Piece(list, "RuneStone", StructureStyle.Stone, StructureSize.Small,
				"A standing stone carved with runes that glow: v0 a tall slab, 2.3 m, a column of runes down its face; v1 a broad stone, 1.5 m, on a cairn, a band of runes round it.",
				RuneStone, "light", "standing-stone", "sacred", "monument");
			Piece(list, "HangingBracket", StructureStyle.Iron, StructureSize.Small,
				"A wrought-iron bracket, its back plate at z = 0 against a wall, a scrolled arm 0.6 m out along −z with a lantern hung from its end: v0 a square lantern, v1 a round one under a twisted arm.",
				HangingBracket, "light", "lantern", "town", "cave", "mine").Collides = false;
		}

		/// <summary>The crystal post's piece id.</summary>
		public const string CrystalPostId = "CrystalPost";

		/// <summary>
		/// The tint of a piece's cold glow (SceneEmitterKind.Glow, 2026-10-18, I6g): a rune stone's rune-light, a glowjar's
		/// faerie-light; null for a crystal post, whose glow is its crystal's (the site's biome's, laid by the site: SceneGlow.WithCrystalHue),
		/// and for a piece with no glow.
		/// </summary>
		public static SceneGlowHue? GlowHueOf(string pieceId)
		{
			switch (pieceId)
			{
				case "RuneStone": return SceneGlowHue.Rune;
				// A dungeon door's rune frieze or lintel (StructurePieces.Dungeon.cs).
				case "DungeonPortal": case "DungeonGate": return SceneGlowHue.Rune;
				// The wizard's tower's door runes and finial, the scrying focus, the observatory's rune star (2026-10-19).
				case "WizardTower": case "ArcaneFocus": case "Observatory": return SceneGlowHue.Rune;
				case "Glowjar": return SceneGlowHue.Faerie;
				default: return null;
			}
		}

		/// <summary>A crystal post's cradle over its foot, per variant, metres: where the crystal is laid.</summary>
		private static readonly float[] CrystalPostCradles = { 1.82f, 1.76f };

		/// <summary>The scale a crystal post lays a cave crystal at in its cradle (the crystals are about a metre across).</summary>
		public const float CrystalPostScale = 0.36f;

		/// <summary>
		/// Where a piece holds a crystal of its site's biome's own kind (the crystal post's cradle, in the piece's frame) and the
		/// scale the crystal is laid at; false for every other piece (2026-10-16, I6c). The crystal is not the piece's own mesh: a
		/// kit piece is built once for every biome, and the crystal is the biome's (PointOfInterestSiteContext.AddProp lays it).
		/// </summary>
		public static bool TryCrystalSocket(StructurePiece piece, int variant, out Vector3 socket, out float scale)
		{
			socket = default;
			scale = 0f;
			if (piece == null || piece.Id != CrystalPostId || variant < 0 || variant >= piece.Variants)
			{
				return false;
			}
			socket = new Vector3(0f, CrystalPostCradles[variant] - 0.06f, 0f);
			scale = CrystalPostScale;
			return true;
		}

		/// <summary>A cresset: an iron fire basket on a tall pole, burning resin-pine knots (a fire, so it lights, smokes and throws embers).</summary>
		private static void Cresset(StructureDraft d)
		{
			const float height = 3.1f;
			int seg = d.Seg(10, 6);
			bool iron = d.Variant == 1;
			if (iron)
			{
				// A footing of rough stone, the iron pole leaded into it.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.5f, 0.5f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
				d.Add(StructureShapes.Rod(new Vector3(0f, 0.25f, 0f), new Vector3(0f, height, 0f), 0.045f, d.Seg(8, 5), StructureMaterial.Iron, 2));
			}
			else
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.4f, 0f), new Vector3(0f, height, 0f), 0.15f, 0.15f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
				d.Add(StructureShapes.Cylinder(0.11f, height - 0.16f, height - 0.04f, seg, StructureMaterial.Iron, 2, false, StructureRole.Detail));
			}
			// The basket: staves flaring from a ring at its foot to its lip, two hoops, a spike up from each stave on the iron one.
			int staves = 6;
			float footR = 0.1f, lipR = 0.3f, deep = 0.42f;
			for (int i = 0; i < staves; i++)
			{
				float a = i * Mathf.PI * 2f / staves;
				var foot = new Vector3(Mathf.Cos(a) * footR, height, Mathf.Sin(a) * footR);
				var lip = new Vector3(Mathf.Cos(a) * lipR, height + deep, Mathf.Sin(a) * lipR);
				d.Add(StructureShapes.Beam(foot, lip, 0.026f, 0.026f, StructureMaterial.Iron, 10 + i, StructureRole.Detail));
				if (iron && d.Mid)
				{
					d.Add(StructureShapes.Rod(lip, lip + new Vector3(Mathf.Cos(a) * 0.03f, 0.12f, Mathf.Sin(a) * 0.03f), 0.012f, 4, StructureMaterial.Iron, 20 + i, StructureRole.Detail, 0.06f));
				}
			}
			d.Add(StructureShapes.Cylinder(lipR + 0.005f, height + deep - 0.04f, height + deep, seg, StructureMaterial.Iron, 30, true, StructureRole.Detail));
			d.Add(StructureShapes.Cylinder(footR + 0.06f, height + 0.12f, height + 0.15f, seg, StructureMaterial.Iron, 31, true, StructureRole.Detail));
			d.Add(StructureShapes.Cylinder(footR + 0.01f, height - 0.02f, height + 0.02f, seg, StructureMaterial.Iron, 32, true, StructureRole.Detail));
			// The knots heaped in it, charred.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.12f, height + 0.04f), new Vector2(0.26f, height + deep - 0.06f), new Vector2(0.12f, height + deep + 0.04f), new Vector2(0f, height + deep + 0.06f) },
				seg, StructureMaterial.Ash, 33, true, 0f, false, false, StructureRole.Detail));
			d.Emit(new Vector3(0f, height + deep + 0.05f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>An iron lantern on a short chain from a bent oak crook post (its crook bending out over −z).</summary>
		private static void CrookLantern(StructureDraft d)
		{
			const float height = 2.7f, bend = 0.34f;
			bool round = d.Variant == 1;
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.4f, 0f), new Vector3(0f, height, 0f), 0.13f, 0.13f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
			// The crook: up, over and down in an arc of short straight lengths (an oak bough steamed round).
			int steps = d.Fine ? 6 : 3;
			Vector3 last = new Vector3(0f, height - 0.02f, 0f);
			for (int i = 1; i <= steps; i++)
			{
				float t = Mathf.PI * i / steps;
				var next = new Vector3(0f, height + bend * Mathf.Sin(t), -bend + bend * Mathf.Cos(t));
				d.Add(StructureShapes.Beam(last, next, 0.1f - 0.035f * i / steps, 0.1f - 0.035f * i / steps, StructureMaterial.Timber, 1 + i)).Grain = (next - last).normalized;
				last = next;
			}
			// The crook's tip turned down a hand's breadth, the hook under it.
			var tip = new Vector3(0f, height - 0.12f, -2f * bend);
			d.Add(StructureShapes.Beam(last, tip, 0.06f, 0.06f, StructureMaterial.Timber, 10));
			d.Add(StructureShapes.Beam(tip, tip + Vector3.down * 0.06f, 0.012f, 0.012f, StructureMaterial.Iron, 11, StructureRole.Detail));
			Vector3 foot = tip + Vector3.down * 0.6f;
			Chain(d, tip + Vector3.down * 0.06f, foot + Vector3.up * 0.38f, 20);
			Lantern(d, foot, round, 100);
			if (round)
			{
				d.Add(StructureShapes.Cylinder(0.085f, height - 0.9f, height - 0.82f, d.Seg(8, 4), StructureMaterial.Iron, 12, false, StructureRole.Detail));
			}
		}

		/// <summary>A glowjar: faerie light (a glowing mote, Faelight: a cold glow, SceneEmitterKind.Glow) in a stoppered glass jar on a crooked stake.</summary>
		private static void Glowjar(StructureDraft d)
		{
			int seg = d.Seg(10, 6);
			// The stake, crooked in two lengths, and its arm.
			var a = new Vector3(0f, -0.35f, 0f);
			var b = new Vector3(0.05f, 1.05f, 0.02f);
			var c = new Vector3(-0.02f, 1.85f, -0.04f);
			d.Add(StructureShapes.Beam(a, b, 0.07f, 0.07f, StructureMaterial.Timber, 1)).Grain = (b - a).normalized;
			d.Add(StructureShapes.Beam(b, c, 0.06f, 0.06f, StructureMaterial.Timber, 2)).Grain = (c - b).normalized;
			Vector3 jar;
			if (d.Variant == 0)
			{
				var arm = new Vector3(-0.02f, 1.76f, -0.42f);
				d.Add(StructureShapes.Beam(c + Vector3.down * 0.1f, arm, 0.04f, 0.04f, StructureMaterial.Timber, 3));
				jar = arm + Vector3.down * 0.42f;
				// The cord it hangs by.
				d.Add(StructureShapes.Beam(arm, jar + Vector3.up * 0.2f, 0.012f, 0.012f, StructureMaterial.Canvas, 4, StructureRole.Detail));
			}
			else
			{
				jar = c + Vector3.up * 0.02f;
				// An iron cage of four bars over a cup, the jar in it.
				d.Add(StructureShapes.Cylinder(0.085f, jar.y - 0.02f, jar.y + 0.02f, seg, StructureMaterial.Iron, 3, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(jar.x, 0f, jar.z))));
				for (int i = 0; i < 4; i++)
				{
					float t = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
					var foot = jar + new Vector3(Mathf.Cos(t) * 0.085f, 0f, Mathf.Sin(t) * 0.085f);
					d.Add(StructureShapes.Beam(foot, foot + Vector3.up * 0.25f + new Vector3(-Mathf.Cos(t) * 0.05f, 0f, -Mathf.Sin(t) * 0.05f), 0.012f, 0.012f, StructureMaterial.Iron, 4 + i, StructureRole.Detail));
				}
			}
			Matrix4x4 at = Matrix4x4.Translate(jar);
			// The jar: a bellied glass body, a neck, a wooden stopper; the mote of faerie light inside it.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.05f, 0f), new Vector2(0.075f, 0.06f), new Vector2(0.07f, 0.13f), new Vector2(0.035f, 0.17f), new Vector2(0.035f, 0.19f) },
				seg, StructureMaterial.Glass, 10, true, 0f, false, false, StructureRole.Detail).Transform(at));
			d.Add(StructureShapes.Cylinder(0.03f, 0.18f, 0.22f, d.Seg(8, 4), StructureMaterial.Timber, 11, false, StructureRole.Detail).Transform(at));
			d.Add(StructureShapes.Sphere(jar + Vector3.up * 0.075f, new Vector3(0.035f, 0.04f, 0.035f), d.Seg(8, 5), d.Fine ? 5 : 3, StructureMaterial.Faelight, 12, StructureRole.Detail));
			d.Emit(jar + Vector3.up * 0.075f, SceneEmitterKind.Glow);
		}

		/// <summary>
		/// A crystal post: an iron cradle on a carved stone pillar (v0) or an iron-banded timber post (v1). The crystal is the site
		/// biome's own and laid by the site in the cradle (<see cref="TryCrystalSocket"/>); its light a cold glow (SceneEmitterKind.Glow,
		/// 2026-10-18) in the crystal's hue, set where it is laid (<see cref="GlowHueOf"/>).
		/// </summary>
		private static void CrystalPost(StructureDraft d)
		{
			float cradle = CrystalPostCradles[Mathf.Clamp(d.Variant, 0, CrystalPostCradles.Length - 1)];
			int seg = d.Seg(10, 6);
			float top;
			if (d.Variant == 0)
			{
				// A squared pillar with a moulded cap, carved in one piece, on a plinth.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.15f, 0f), new Vector3(0.5f, 0.4f, 0.5f), d.Bevel(0.04f), StructureMaterial.Monolith, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.75f, 0f), new Vector3(0.3f, 1.4f, 0.3f), d.Bevel(0.03f), StructureMaterial.Monolith, 2));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.52f, 0f), new Vector3(0.4f, 0.14f, 0.4f), d.Bevel(0.03f), StructureMaterial.Monolith, 3));
				top = 1.59f;
			}
			else
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.4f, 0f), new Vector3(0f, 1.55f, 0f), 0.16f, 0.16f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
				for (int i = 0; i < 2; i++)
				{
					float y = 0.6f + i * 0.7f;
					d.Add(StructureShapes.Box(new Vector3(0f, y, 0f), new Vector3(0.18f, 0.05f, 0.18f), StructureMaterial.Iron, 2 + i, StructureRole.Detail));
				}
				top = 1.55f;
			}
			// The cradle: a cup on a stem and four claws curling up round where the crystal sits.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.05f, top), new Vector2(0.06f, top + 0.08f), new Vector2(0.15f, cradle - 0.06f), new Vector2(0.13f, cradle - 0.04f), new Vector2(0f, cradle - 0.07f) },
				seg, StructureMaterial.Iron, 10, true, 0f, false, false));
			for (int i = 0; i < 4; i++)
			{
				float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
				var rim = new Vector3(Mathf.Cos(a) * 0.14f, cradle - 0.06f, Mathf.Sin(a) * 0.14f);
				var tipAt = new Vector3(Mathf.Cos(a) * 0.08f, cradle + 0.2f, Mathf.Sin(a) * 0.08f);
				Vector3 knee = Vector3.Lerp(rim, tipAt, 0.5f) + new Vector3(Mathf.Cos(a) * 0.05f, 0f, Mathf.Sin(a) * 0.05f);
				d.Add(StructureShapes.Beam(rim, knee, 0.022f, 0.022f, StructureMaterial.Iron, 11 + 2 * i, StructureRole.Detail));
				d.Add(StructureShapes.Beam(knee, tipAt, 0.018f, 0.018f, StructureMaterial.Iron, 12 + 2 * i, StructureRole.Detail));
			}
			d.Emit(new Vector3(0f, cradle + 0.16f, 0f), SceneEmitterKind.Glow);
		}

		/// <summary>
		/// A rune stone: a standing stone with runes cut into it and filled with a glowing lacquer (Runelight), giving off a cold
		/// glow (SceneEmitterKind.Glow, 2026-10-18) that shows faintly from before dusk. The runes are strokes of the elder
		/// futhark's shapes, set proud of the face by a few millimetres.
		/// </summary>
		private static void RuneStone(StructureDraft d)
		{
			bool tall = d.Variant == 0;
			float height = tall ? 2.3f : 1.5f, width = tall ? 0.8f : 1.1f, thick = tall ? 0.34f : 0.55f;
			// Sunk well into the ground (a broad stone's foot hidden in its cairn).
			const float baseY = -0.45f;
			if (!tall)
			{
				// A cairn of rough stones it stands in.
				for (int i = 0; i < 6; i++)
				{
					float a = i * Mathf.PI / 3f + d.Range(40 + i, 1, -0.2f, 0.2f);
					var c = new Vector3(Mathf.Cos(a) * 0.75f, 0.12f, Mathf.Sin(a) * 0.55f);
					d.Add(StructureShapes.Block(c, new Vector3(d.Range(40 + i, 2, 0.35f, 0.5f), 0.32f, d.Range(40 + i, 3, 0.3f, 0.45f)), Quaternion.Euler(0f, a * Mathf.Rad2Deg, d.Range(40 + i, 4, -8f, 8f)),
						StructureMaterial.Fieldstone, 40 + i, StructureRole.Structure, d.Bevel(0.04f)));
				}
			}
			// The stone: a slab tapering to a rounded crown, its faces flat as a dressed menhir's.
			float half = width * 0.5f;
			var outline = new[]
			{
				new Vector2(-half, baseY), new Vector2(half, baseY), new Vector2(half * 0.86f, height * 0.78f), new Vector2(half * 0.5f, height * 0.96f),
				new Vector2(0f, height), new Vector2(-half * 0.55f, height * 0.95f), new Vector2(-half * 0.9f, height * 0.8f),
			};
			d.Add(StructureShapes.Prism(outline, -thick * 0.5f, thick * 0.5f, StructureMaterial.Monolith, 1));
			// The runes on its face (−z): a column down a tall slab, a band across a broad one.
			int runes = tall ? 6 : 5;
			float z = -thick * 0.5f - 0.004f;
			for (int r = 0; r < runes; r++)
			{
				Vector2 centre = tall ? new Vector2(0f, height * 0.78f - r * 0.27f) : new Vector2((r - (runes - 1) * 0.5f) * 0.19f, height * 0.6f);
				// Each rune a stave and one or two twigs (ᚠ ᚱ ᚦ ᚨ ᛉ shapes, by its number).
				float s = 0.09f;
				Rune(d, centre, s, r, z, 100 + 10 * r);
			}
			d.Emit(new Vector3(0f, tall ? height * 0.55f : height * 0.6f, -thick * 0.5f - 0.35f), SceneEmitterKind.Glow);
		}

		/// <summary>One rune of three strokes at most, flat on the face at <paramref name="z"/>, its stave <paramref name="size"/> tall either side of the centre.</summary>
		private static void Rune(StructureDraft d, Vector2 centre, float size, int which, float z, int key)
		{
			void Stroke(Vector2 a, Vector2 b, int k)
			{
				Vector2 mid = (a + b) * 0.5f, run = b - a;
				float angle = Mathf.Atan2(run.y, run.x) * Mathf.Rad2Deg;
				d.Add(StructureShapes.Block(new Vector3(mid.x, mid.y, z), new Vector3(run.magnitude + 0.016f, 0.016f, 0.008f), Quaternion.Euler(0f, 0f, angle), StructureMaterial.Runelight, k, StructureRole.Detail));
			}
			Vector2 top = centre + new Vector2(0f, size), bottom = centre - new Vector2(0f, size);
			Stroke(bottom, top, key);
			switch (which % 5)
			{
				case 0: // ᚠ: two twigs up to the right.
					Stroke(centre + new Vector2(0f, size * 0.5f), centre + new Vector2(size * 0.6f, size), key + 1);
					Stroke(centre, centre + new Vector2(size * 0.6f, size * 0.45f), key + 2);
					break;
				case 1: // ᚱ: a bow and a leg.
					Stroke(top, centre + new Vector2(size * 0.55f, size * 0.5f), key + 1);
					Stroke(centre + new Vector2(size * 0.55f, size * 0.5f), centre, key + 2);
					break;
				case 2: // ᚦ: a thorn.
					Stroke(centre + new Vector2(0f, size * 0.5f), centre + new Vector2(size * 0.55f, 0f), key + 1);
					Stroke(centre + new Vector2(size * 0.55f, 0f), centre - new Vector2(0f, size * 0.5f), key + 2);
					break;
				case 3: // ᚨ: two twigs down to the right.
					Stroke(centre + new Vector2(0f, size * 0.8f), centre + new Vector2(size * 0.6f, size * 0.3f), key + 1);
					Stroke(centre + new Vector2(0f, size * 0.3f), centre + new Vector2(size * 0.6f, -size * 0.2f), key + 2);
					break;
				default: // ᛉ: a fork up both ways.
					Stroke(centre + new Vector2(0f, size * 0.2f), centre + new Vector2(-size * 0.6f, size), key + 1);
					Stroke(centre + new Vector2(0f, size * 0.2f), centre + new Vector2(size * 0.6f, size), key + 2);
					break;
			}
		}

		/// <summary>A wrought-iron bracket from a wall (its back plate at z = 0), a lantern hung from its scrolled arm's end.</summary>
		private static void HangingBracket(StructureDraft d)
		{
			const float reach = 0.6f;
			bool round = d.Variant == 1;
			d.Add(StructureShapes.Box(new Vector3(0f, 0f, 0.008f), new Vector3(0.12f, 0.36f, 0.016f), StructureMaterial.Iron, 1));
			// The arm out from the plate's top, a brace up to it from the plate's foot, and the scroll between them.
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.14f, 0f), new Vector3(0f, 0.14f, -reach), 0.026f, 0.026f, StructureMaterial.Iron, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.15f, 0f), new Vector3(0f, 0.12f, -reach * 0.62f), 0.02f, 0.02f, StructureMaterial.Iron, 3));
			if (d.Mid)
			{
				int steps = d.Fine ? 6 : 4;
				Vector3 last = new Vector3(0f, 0.12f, -reach * 0.3f);
				for (int i = 1; i <= steps; i++)
				{
					float t = Mathf.PI * 1.5f * i / steps;
					float r = 0.07f * (1f - 0.5f * i / steps);
					var next = new Vector3(0f, 0.05f + r * Mathf.Cos(t), -reach * 0.3f - r * Mathf.Sin(t));
					d.Add(StructureShapes.Beam(last, next, 0.012f, 0.012f, StructureMaterial.Iron, 10 + i, StructureRole.Detail));
					last = next;
				}
				if (round)
				{
					// A finial on the arm's end, twisted.
					d.Add(StructureShapes.Block(new Vector3(0f, 0.14f, -reach - 0.03f), new Vector3(0.04f, 0.04f, 0.04f), Quaternion.Euler(45f, 0f, 45f), StructureMaterial.Iron, 20, StructureRole.Detail));
				}
			}
			Vector3 hook = new Vector3(0f, 0.12f, -reach + 0.04f);
			Vector3 foot = hook + Vector3.down * 0.62f;
			Chain(d, hook, foot + Vector3.up * 0.38f, 30);
			Lantern(d, foot, round, 100);
		}

		private static void TorchPost(StructureDraft d)
		{
			const float height = 2.4f;
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.35f, 0f), new Vector3(0f, height, 0f), 0.11f, 0.11f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
			if (d.Variant == 1)
			{
				// A cresset: an iron basket of four staves and two hoops, wood burning in it.
				int seg = d.Seg(8, 4);
				for (int i = 0; i < 4; i++)
				{
					float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
					var foot = new Vector3(Mathf.Cos(a) * 0.08f, height - 0.02f, Mathf.Sin(a) * 0.08f);
					var lip = new Vector3(Mathf.Cos(a) * 0.2f, height + 0.32f, Mathf.Sin(a) * 0.2f);
					d.Add(StructureShapes.Beam(foot, lip, 0.025f, 0.025f, StructureMaterial.Iron, 2 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Cylinder(0.205f, height + 0.27f, height + 0.31f, seg, StructureMaterial.Iron, 8, true, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.14f, height + 0.1f, height + 0.13f, seg, StructureMaterial.Iron, 9, true, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.13f, height + 0.05f, height + 0.22f, seg, StructureMaterial.Ash, 10, false, StructureRole.Detail));
				d.Emit(new Vector3(0f, height + 0.22f, 0f), SceneEmitterKind.Torch);
				return;
			}
			// The cup and the torch in it, its head bound in pitch-soaked rag.
			d.Add(StructureShapes.Cylinder(0.07f, height - 0.05f, height + 0.08f, d.Seg(8, 4), StructureMaterial.Iron, 2, false, StructureRole.Detail));
			Torch(d, new Vector3(0f, height - 0.1f, 0f), Vector3.up, 3);
		}

		private static void WallTorch(StructureDraft d) => MountedTorch(d, Matrix4x4.identity, d.Variant == 1, 1);

		/// <summary>
		/// A torch in an iron sconce, built in the frame <paramref name="place"/> (its back plate at the origin against a wall
		/// facing −z): the wall torch piece's, and a building's own where it has torches of its own (a gatehouse's).
		/// </summary>
		internal static void MountedTorch(StructureDraft d, Matrix4x4 place, bool ring, int key)
		{
			d.Add(StructureShapes.Box(new Vector3(0f, 0f, 0.01f), new Vector3(0.12f, 0.24f, 0.02f), StructureMaterial.Iron, key).Transform(place));
			var ringAt = new Vector3(0f, ring ? 0.02f : 0.05f, -0.16f);
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.08f, 0f), ringAt + Vector3.down * 0.05f, 0.03f, 0.03f, StructureMaterial.Iron, key + 1).Transform(place));
			if (!ring)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.09f, 0f), ringAt + Vector3.up * 0.03f, 0.025f, 0.025f, StructureMaterial.Iron, key + 2, StructureRole.Detail).Transform(place));
			}
			d.Add(StructureShapes.Cylinder(0.05f, ringAt.y - 0.03f, ringAt.y + 0.03f, d.Seg(8, 4), StructureMaterial.Iron, key + 3, true)
				.Transform(place * Matrix4x4.Translate(new Vector3(0f, 0f, ringAt.z))));
			// The torch from below the ring up and out to its head.
			Vector3 lean = new Vector3(0f, 0.93f, -0.37f).normalized;
			Vector3 foot = ringAt - lean * 0.2f, head = foot + lean * 0.48f;
			d.Add(StructureShapes.Beam(foot, head, 0.04f, 0.04f, StructureMaterial.Timber, key + 4).Transform(place)).Grain = place.MultiplyVector(lean);
			d.Add(StructureShapes.Beam(head - lean * 0.02f, head + lean * 0.13f, 0.075f, 0.075f, StructureMaterial.Canvas, key + 5, StructureRole.Detail).Transform(place));
			d.Emit(place.MultiplyPoint3x4(head + lean * 0.14f), SceneEmitterKind.Torch);
		}

		/// <summary>A torch: a shaft along <paramref name="up"/> from its foot, a rag-bound head, and its flame given off above it.</summary>
		private static void Torch(StructureDraft d, Vector3 foot, Vector3 up, int key)
		{
			Vector3 head = foot + up * 0.48f;
			d.Add(StructureShapes.Beam(foot, head, 0.04f, 0.04f, StructureMaterial.Timber, key)).Grain = up;
			d.Add(StructureShapes.Beam(head - up * 0.02f, head + up * 0.13f, 0.075f, 0.075f, StructureMaterial.Canvas, key + 1, StructureRole.Detail));
			d.Emit(head + up * 0.14f, SceneEmitterKind.Torch);
		}

		private static void Brazier(StructureDraft d)
		{
			int seg = d.Seg(14, 6);
			if (d.Variant == 1)
			{
				// A stone fire bowl on a pedestal: the shaft, the bowl, the coals.
				d.Add(StructureShapes.Cylinder(0.24f, -0.2f, 0.12f, seg, StructureMaterial.Stone, 1, true));
				d.Add(StructureShapes.Cylinder(0.13f, 0.12f, 0.75f, seg, StructureMaterial.Stone, 2, false));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.13f, 0.72f), new Vector2(0.42f, 0.98f), new Vector2(0.46f, 1.05f), new Vector2(0.38f, 1.05f), new Vector2(0.12f, 0.86f) },
					seg, StructureMaterial.Stone, 3, true, 0f, true, false));
				d.Add(StructureShapes.Cylinder(0.36f, 0.94f, 1.0f, seg, StructureMaterial.Ash, 4, true, StructureRole.Detail));
				d.Emit(new Vector3(0f, 1.0f, 0f), SceneEmitterKind.Fire);
				return;
			}
			// An iron bowl on three splayed legs, a ring tying them.
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				var foot = new Vector3(Mathf.Cos(a) * 0.38f, -0.05f, Mathf.Sin(a) * 0.38f);
				var top = new Vector3(Mathf.Cos(a) * 0.22f, 0.78f, Mathf.Sin(a) * 0.22f);
				d.Add(StructureShapes.Beam(foot, top, 0.04f, 0.04f, StructureMaterial.Iron, 1 + i));
			}
			d.Add(StructureShapes.Cylinder(0.3f, 0.4f, 0.44f, seg, StructureMaterial.Iron, 5, true, StructureRole.Detail));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.05f, 0.74f), new Vector2(0.3f, 0.8f), new Vector2(0.4f, 0.98f), new Vector2(0.35f, 0.98f), new Vector2(0.05f, 0.8f) },
				seg, StructureMaterial.Iron, 6, true, 0f, true, false));
			d.Add(StructureShapes.Cylinder(0.32f, 0.88f, 0.94f, seg, StructureMaterial.Ash, 7, true, StructureRole.Detail));
			d.Emit(new Vector3(0f, 0.94f, 0f), SceneEmitterKind.Fire);
		}
	}
}
#endif
