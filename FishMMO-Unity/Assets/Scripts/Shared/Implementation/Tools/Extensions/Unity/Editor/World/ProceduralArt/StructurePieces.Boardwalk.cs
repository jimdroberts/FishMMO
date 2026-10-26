#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Boardwalks (2026-10-18, phase I2i, decision 27: "raised boardwalks as real walkable structures along swamp ways and between
	/// village houses"): a plank deck on stringers with a pole handrail, four metres a module, and the bent of two driven piles
	/// under each joint, laid along a way by PathBoardwalks the way a moat's bridge is laid (PropsFeature.LayMoatBridge).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The record.</b> Timber trackways were laid across fen and bog from the Neolithic on (the Sweet Track, 3807 BC; the
	/// Corlea and Wittemoor plank roads of the Iron Age), and medieval towns in marsh and lake country (Novgorod, the crannog
	/// villages, the Frisian terps) walked their streets on split-log and plank decking renewed layer on layer. A raised walk on
	/// piles with a handrail is how such ways cross ground too soft and wet to carry a bank.
	/// </para>
	/// <para>
	/// <b>Frames.</b> Both pieces stand at deck level (<see cref="StructureAnchor.Deck"/>): y = 0 is the walking surface, the
	/// module runs along z from −<see cref="BoardwalkModule"/>/2 to +, and is <see cref="BoardwalkWidth"/> across x. A way's width
	/// scales x, its spacing z; the bent is scaled in y to reach the ground (<see cref="BoardwalkBentScale"/>), as a trestle's is.
	/// The planks are the deck's floor (palette role Floor), every other timber the palette's woodwork.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>A boardwalk module's length along its way, metres.</summary>
		public const float BoardwalkModule = 4f;
		/// <summary>A boardwalk's deck width as built, metres (a way's own width scales it).</summary>
		public const float BoardwalkWidth = 1.8f;
		/// <summary>How far a bent reaches below the deck as built, metres (scaled in y to the ground).</summary>
		public const float BoardwalkBentDepth = 1.2f;
		/// <summary>The handrail's height over the deck, metres.</summary>
		public const float BoardwalkRailHeight = 0.95f;
		/// <summary>How far a pile is driven into the ground past its foot, metres.</summary>
		public const float BoardwalkPileSink = 0.3f;

		/// <summary>The y scale that brings a bent's piles from a deck <paramref name="height"/> metres over the ground into it, driven <see cref="BoardwalkPileSink"/> deeper.</summary>
		public static float BoardwalkBentScale(float height) => Mathf.Max(0.15f, (Mathf.Max(0f, height) + BoardwalkPileSink) / BoardwalkBentDepth);

		private static void AddBoardwalk(List<StructurePiece> list)
		{
			StructurePiece span = Piece(list, "BoardwalkSpan", StructureStyle.Timber, StructureSize.Medium,
				"Four metres of a raised plank walk along z, its deck at y = 0: split planks across two stringers, a pole handrail on stout posts; v0 railed both sides, v1 one side (+x); v2 passerella (1.2 m, one rail), v3 landing walk (1.4 m, no rail), v4 Bohlensteg (two planks lengthwise, 0.6 m), v5 pole-walk (two poles, 0.5 m). Laid end to end along a way, v0/v1 scaled across to its width.",
				BoardwalkSpan, "boardwalk");
			span.Anchor = StructureAnchor.Deck;
			span.ModuleLength = BoardwalkModule;
			// The country styles (2026-10-25, I2i-b) as variants 2–5 (BoardwalkStyleVariant): v0 and v1 build as before.
			span.Variants = BoardwalkVariants;
			StructurePiece bent = Piece(list, "BoardwalkBent", StructureStyle.Timber, StructureSize.Medium,
				"A boardwalk's bent across x at z = 0, under a joint of two spans: a cap under the stringers on two driven round piles 1.2 m down; v1 braced; v2 passerella stubs, v3 forked posts and a crosspole, v4 sleeper on stakes, v5 forked stakes (v2, v4, v5 0.6 m down). Scale y to the ground.",
				BoardwalkBent, "boardwalk");
			bent.Anchor = StructureAnchor.Deck;
			bent.Variants = BoardwalkVariants;
		}

		/// <summary>
		/// A boardwalk module: planks across two stringers (the floor), a handrail of round poles lashed to square posts at the
		/// module's start (the next module's posts close the rail), on both sides (v0) or the +x side (v1).
		/// </summary>
		private static void BoardwalkSpan(StructureDraft d)
		{
			if (d.Variant >= 2)
			{
				StyledSpan(d);
				return;
			}
			const float width = BoardwalkWidth, length = BoardwalkModule, half = length * 0.5f, thick = 0.07f;
			using (d.Builds(DeckRoles))
			{
				if (d.Lod == 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, -0.5f * thick, 0f), new Vector3(width, thick, length), StructureMaterial.Timber, 1));
				}
				else
				{
					int planks = d.Fine ? 14 : 7;
					float pitch = length / planks;
					for (int k = 0; k < planks; k++)
					{
						// Split, not sawn: each plank a little its own width and length, a finger's gap between.
						float w = width + (d.Fine ? d.Range(k, 3, -0.08f, 0.06f) : 0f);
						float sx = d.Fine ? d.Range(k, 4, -0.04f, 0.04f) : 0f;
						d.Add(StructureShapes.Box(new Vector3(sx, -0.5f * thick, -half + pitch * (k + 0.5f)), new Vector3(w, thick, pitch - 0.025f),
							StructureMaterial.Timber, 10 + k)).Grain = Vector3.right;
					}
				}
			}
			for (int s = -1; s <= 1; s += 2)
			{
				// The stringers, the length of the module, under the planks' ends.
				d.Add(StructureShapes.Box(new Vector3(s * (0.5f * width - 0.22f), -thick - 0.11f, 0f), new Vector3(0.16f, 0.22f, length), StructureMaterial.Timber, 30 + s)).Grain = Vector3.forward;
				if (d.Variant == 1 && s < 0)
				{
					continue;
				}
				float x = s * (0.5f * width - 0.06f);
				// A post at the module's start, through the deck's edge down to the stringer, and the rail poles to the next.
				d.Add(StructureShapes.Box(new Vector3(x, 0.5f * BoardwalkRailHeight - 0.15f, -half + 0.06f), new Vector3(0.1f, BoardwalkRailHeight + 0.3f, 0.1f),
					StructureMaterial.Timber, 35 + s)).Grain = Vector3.up;
				d.Add(StructureShapes.Rod(new Vector3(x, BoardwalkRailHeight - 0.04f, -half + 0.06f), new Vector3(x, BoardwalkRailHeight - 0.04f, half + 0.06f),
					0.045f, d.Seg(6), StructureMaterial.Timber, 40 + s));
				if (d.Mid)
				{
					d.Add(StructureShapes.Rod(new Vector3(x, 0.45f, -half + 0.06f), new Vector3(x, 0.45f, half + 0.06f), 0.035f, d.Seg(6),
						StructureMaterial.Timber, 44 + s, StructureRole.Detail));
				}
				if (d.Fine)
				{
					// The lashing at the post's head.
					d.Add(StructureShapes.Box(new Vector3(x, BoardwalkRailHeight - 0.04f, -half + 0.06f), new Vector3(0.13f, 0.06f, 0.13f),
						StructureMaterial.Canvas, 48 + s, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// A boardwalk's bent at z = 0: a cap across under both stringers on two round piles driven <see cref="BoardwalkBentDepth"/>
		/// down from the deck; v1 a diagonal brace between the piles.
		/// </summary>
		private static void BoardwalkBent(StructureDraft d)
		{
			if (d.Variant >= 2)
			{
				StyledBent(d);
				return;
			}
			const float width = BoardwalkWidth, deck = 0.07f;
			float capY = -deck - 0.22f - 0.08f;
			d.Add(StructureShapes.Box(new Vector3(0f, capY, 0f), new Vector3(width - 0.1f, 0.16f, 0.2f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			float pileX = 0.5f * width - 0.22f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Rod(new Vector3(s * pileX, -BoardwalkBentDepth, 0f), new Vector3(s * pileX, capY - 0.08f, 0f), 0.09f, d.Seg(8),
					StructureMaterial.Timber, 2 + s, StructureRole.Structure, tipLength: 0f));
			}
			if (d.Variant == 1 && d.Mid)
			{
				d.Add(StructureShapes.Beam(new Vector3(-pileX, capY - 0.12f, 0.12f), new Vector3(pileX, -BoardwalkBentDepth + 0.25f, 0.12f), 0.07f, 0.05f,
					StructureMaterial.Timber, 10));
			}
		}

		// ── Country styles (2026-10-25, phase I2i-b) ──────────────────────
		// The walks the biome drafts name, one variant of each piece a style (BiomeBoardwalkStyle): the passerella of the Lombard
		// meadows, the landing walk of the prairie rivers, the Alpine Bohlensteg over a fen, the coast's pole-walk through the
		// mangal. Each is built at its own width and depth (BoardwalkStyleShape), so a way lays it unscaled across and its bents
		// scale y from their own depth. Walkable decks as the plank walk's: baked props, collided with and walked on the NavMesh.

		/// <summary>How many variants each boardwalk piece has: the plank walk's two and one per country style.</summary>
		public const int BoardwalkVariants = 6;

		/// <summary>How a style's walk is laid: the variants of the span and the bent, how high and how wide its deck, its ramps and bents.</summary>
		public struct BoardwalkStyleShape
		{
			/// <summary>The span's variant (a footway's planked span is v1, railed on one side).</summary>
			public int Span;
			/// <summary>The bent's variant; the plank walk braces every third bent (v1).</summary>
			public int Bent;
			/// <summary>Whether every third bent is the braced v1 (the plank walk only).</summary>
			public bool Braced;
			/// <summary>How high the deck stands over the highest ground under it, metres.</summary>
			public float Rise;
			/// <summary>How far each end ramps down to the ground, metres.</summary>
			public float Ramp;
			/// <summary>The deck's own width as built, metres; 0 scales the plank walk across to the way's width.</summary>
			public float Width;
			/// <summary>Under this clearance a station has no bent, metres.</summary>
			public float BentClear;
			/// <summary>How far the bent reaches below the deck as built, metres.</summary>
			public float BentDepth;
		}

		/// <summary>The variant both pieces build a country style as (2–5); the plank walk's own are 0 and 1.</summary>
		public static int BoardwalkStyleVariant(BiomeBoardwalkStyle style) => style == BiomeBoardwalkStyle.Planked ? 0 : 1 + (int)style;

		/// <summary>The shape of a style's walk; an unknown style is the plank walk's (I2i's constants, unchanged).</summary>
		public static BoardwalkStyleShape BoardwalkShape(BiomeBoardwalkStyle style, bool footway)
		{
			switch (style)
			{
				case BiomeBoardwalkStyle.Passerella:
					return new BoardwalkStyleShape { Span = 2, Bent = 2, Rise = 0.3f, Ramp = 1.5f, Width = PasserellaWidth, BentClear = 0.12f, BentDepth = LowBentDepth };
				case BiomeBoardwalkStyle.LandingWalk:
					return new BoardwalkStyleShape { Span = 3, Bent = 3, Rise = 0.8f, Ramp = 3.5f, Width = LandingWalkWidth, BentClear = 0.25f, BentDepth = BoardwalkBentDepth };
				case BiomeBoardwalkStyle.Bohlensteg:
					return new BoardwalkStyleShape { Span = 4, Bent = 4, Rise = 0.3f, Ramp = 1.2f, Width = BohlenstegWidth, BentClear = 0.1f, BentDepth = LowBentDepth };
				case BiomeBoardwalkStyle.PoleWalk:
					return new BoardwalkStyleShape { Span = 5, Bent = 5, Rise = 0.25f, Ramp = 1f, Width = PoleWalkWidth, BentClear = 0.08f, BentDepth = LowBentDepth };
				default:
					return new BoardwalkStyleShape { Span = footway ? 1 : 0, Bent = 0, Braced = true, Rise = 0.9f, Ramp = 4.5f, Width = 0f, BentClear = 0.3f,
						BentDepth = BoardwalkBentDepth };
			}
		}

		/// <summary>The y scale that brings a bent built <paramref name="depth"/> deep from a deck <paramref name="height"/> metres over the ground into it.</summary>
		public static float BoardwalkBentScale(float height, float depth) => Mathf.Max(0.15f, (Mathf.Max(0f, height) + BoardwalkPileSink) / Mathf.Max(0.1f, depth));

		/// <summary>The widths the country walks are built at, metres.</summary>
		public const float PasserellaWidth = 1.2f, LandingWalkWidth = 1.4f, BohlenstegWidth = 0.6f, PoleWalkWidth = 0.5f;
		/// <summary>How far the low walks' bents reach below the deck as built, metres.</summary>
		public const float LowBentDepth = 0.6f;

		/// <summary>The country styles' spans (v2–v5), the deck at y = 0 along z.</summary>
		private static void StyledSpan(StructureDraft d)
		{
			const float length = BoardwalkModule, half = length * 0.5f;
			switch (d.Variant)
			{
				case 2:
				{
					// Passerella: sawn oak planks across two stringers, one pole rail on the +x side.
					const float width = PasserellaWidth, thick = 0.06f;
					Planks(d, width, thick, 0.04f, 0.03f);
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * (0.5f * width - 0.18f), -thick - 0.07f, 0f), new Vector3(0.12f, 0.14f, length), StructureMaterial.Timber, 30 + s)).Grain = Vector3.forward;
					}
					float x = 0.5f * width - 0.05f;
					d.Add(StructureShapes.Box(new Vector3(x, 0.45f - 0.1f, -half + 0.06f), new Vector3(0.08f, 0.9f + 0.2f, 0.08f), StructureMaterial.Timber, 35)).Grain = Vector3.up;
					d.Add(StructureShapes.Rod(new Vector3(x, 0.86f, -half + 0.06f), new Vector3(x, 0.86f, half + 0.06f), 0.04f, d.Seg(6), StructureMaterial.Timber, 40));
					break;
				}
				case 3:
				{
					// Landing walk: split cottonwood planks across two round pole stringers, rawhide lashings at the ends; no rail.
					const float width = LandingWalkWidth, thick = 0.07f;
					Planks(d, width, thick, 0.12f, 0.07f);
					for (int s = -1; s <= 1; s += 2)
					{
						float x = s * (0.5f * width - 0.2f);
						d.Add(StructureShapes.Rod(new Vector3(x, -thick - 0.07f, -half), new Vector3(x, -thick - 0.07f, half), 0.07f, d.Seg(7), StructureMaterial.Timber, 30 + s));
						if (d.Fine)
						{
							d.Add(StructureShapes.Box(new Vector3(x, -thick - 0.02f, -half + 0.2f), new Vector3(0.16f, 0.05f, 0.08f), StructureMaterial.Canvas, 48 + s,
								StructureRole.Detail));
						}
					}
					break;
				}
				case 4:
				{
					// Bohlensteg: two split-larch planks laid lengthwise side by side, a finger's gap between, lapping the next module.
					const float plank = 0.29f, thick = 0.06f;
					if (d.Lod == 2)
					{
						using (d.Builds(DeckRoles))
						{
							d.Add(StructureShapes.Box(new Vector3(0f, -0.5f * thick, 0.03f), new Vector3(BohlenstegWidth, thick, length + 0.06f), StructureMaterial.Timber, 1));
						}
						break;
					}
					using (d.Builds(DeckRoles))
					{
						for (int s = -1; s <= 1; s += 2)
						{
							float w = plank + (d.Fine ? d.Range(s + 1, 3, -0.03f, 0.02f) : 0f);
							d.Add(StructureShapes.Box(new Vector3(s * (0.5f * plank + 0.01f), -0.5f * thick, 0.03f + (d.Fine ? d.Range(s + 1, 4, -0.02f, 0.02f) : 0f)),
								new Vector3(w, thick, length + 0.06f), StructureMaterial.Timber, 10 + s)).Grain = Vector3.forward;
						}
					}
					break;
				}
				default:
				{
					// Pole-walk: two bark-stripped poles side by side along the way, their tops the walking line, lapping the next pair.
					const float radius = 0.08f;
					for (int s = -1; s <= 1; s += 2)
					{
						float x = s * (radius + 0.01f), z0 = -half + (s > 0 ? 0.08f : 0f);
						d.Add(StructureShapes.Rod(new Vector3(x, -radius, z0), new Vector3(x, -radius, z0 + length + 0.15f), radius, d.Seg(8), StructureMaterial.Timber, 10 + s));
					}
					break;
				}
			}
		}

		/// <summary>A style's planks across its width (the deck's floor), as the plank walk's: split, each its own width and set.</summary>
		private static void Planks(StructureDraft d, float width, float thick, float ragged, float shift)
		{
			const float length = BoardwalkModule, half = length * 0.5f;
			using (d.Builds(DeckRoles))
			{
				if (d.Lod == 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, -0.5f * thick, 0f), new Vector3(width, thick, length), StructureMaterial.Timber, 1));
					return;
				}
				int planks = d.Fine ? 14 : 7;
				float pitch = length / planks;
				for (int k = 0; k < planks; k++)
				{
					float w = width + (d.Fine ? d.Range(k, 3, -ragged, 0.5f * ragged) : 0f);
					float sx = d.Fine ? d.Range(k, 4, -shift, shift) : 0f;
					d.Add(StructureShapes.Box(new Vector3(sx, -0.5f * thick, -half + pitch * (k + 0.5f)), new Vector3(w, thick, pitch - 0.025f),
						StructureMaterial.Timber, 10 + k)).Grain = Vector3.right;
				}
			}
		}

		/// <summary>The country styles' bents (v2–v5) at z = 0 under a joint, reaching their style's depth below the deck.</summary>
		private static void StyledBent(StructureDraft d)
		{
			switch (d.Variant)
			{
				case 2:
				{
					// Passerella: a cap on two squared oak stubs.
					const float width = PasserellaWidth;
					float capY = -0.06f - 0.14f - 0.06f;
					d.Add(StructureShapes.Box(new Vector3(0f, capY, 0f), new Vector3(width - 0.1f, 0.12f, 0.16f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
					for (int s = -1; s <= 1; s += 2)
					{
						float x = s * (0.5f * width - 0.18f), top = capY - 0.06f;
						d.Add(StructureShapes.Box(new Vector3(x, 0.5f * (top - LowBentDepth), 0f), new Vector3(0.12f, top + LowBentDepth, 0.12f), StructureMaterial.Timber, 2 + s)).Grain = Vector3.up;
					}
					break;
				}
				case 3:
				{
					// Landing walk: two forked cottonwood posts, a crosspole lying in their crotches, lashed.
					const float width = LandingWalkWidth;
					float poleY = -0.07f - 0.14f - 0.06f;
					d.Add(StructureShapes.Rod(new Vector3(-0.5f * width + 0.05f, poleY, 0f), new Vector3(0.5f * width - 0.05f, poleY, 0f), 0.06f, d.Seg(7), StructureMaterial.Timber, 1));
					for (int s = -1; s <= 1; s += 2)
					{
						float x = s * (0.5f * width - 0.2f), crotch = poleY - 0.1f;
						d.Add(StructureShapes.Rod(new Vector3(x, -BoardwalkBentDepth, 0f), new Vector3(x, crotch, 0f), 0.07f, d.Seg(7), StructureMaterial.Timber, 2 + s));
						if (d.Mid)
						{
							// The fork's two tines, up either side of the crosspole.
							for (int t = -1; t <= 1; t += 2)
							{
								d.Add(StructureShapes.Rod(new Vector3(x, crotch, 0f), new Vector3(x, poleY + 0.06f, t * 0.09f), 0.035f, d.Seg(5), StructureMaterial.Timber, 6 + s + t,
									StructureRole.Detail));
							}
						}
						if (d.Fine)
						{
							d.Add(StructureShapes.Box(new Vector3(x, poleY, 0f), new Vector3(0.1f, 0.06f, 0.16f), StructureMaterial.Canvas, 12 + s, StructureRole.Detail));
						}
					}
					break;
				}
				case 4:
				{
					// Bohlensteg: a sleeper across under both planks on two driven stakes beside them.
					float sleeperY = -0.06f - 0.04f;
					d.Add(StructureShapes.Box(new Vector3(0f, sleeperY, 0f), new Vector3(BohlenstegWidth + 0.12f, 0.08f, 0.12f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
					for (int s = -1; s <= 1; s += 2)
					{
						float x = s * (0.5f * BohlenstegWidth + 0.03f), top = sleeperY - 0.04f;
						d.Add(StructureShapes.Box(new Vector3(x, 0.5f * (top - LowBentDepth), 0f), new Vector3(0.06f, top + LowBentDepth, 0.06f), StructureMaterial.Timber, 2 + s)).Grain = Vector3.up;
					}
					break;
				}
				default:
				{
					// Pole-walk: a forked stake under each pole, the pole lying in its crotch; the two stakes a hand apart along the
					// way, as they were driven where each pole's end lapped the next.
					const float radius = 0.08f;
					for (int s = -1; s <= 1; s += 2)
					{
						float x = s * (radius + 0.01f), z = s * 0.06f, crotch = -2f * radius - 0.03f;
						d.Add(StructureShapes.Rod(new Vector3(x, -LowBentDepth, z), new Vector3(x, crotch, z), 0.04f, d.Seg(6), StructureMaterial.Timber, 2 + s));
						if (d.Mid)
						{
							for (int t = -1; t <= 1; t += 2)
							{
								d.Add(StructureShapes.Rod(new Vector3(x, crotch, z), new Vector3(x + t * 0.07f, -radius, z), 0.02f, d.Seg(5), StructureMaterial.Timber, 6 + s + t,
									StructureRole.Detail));
							}
						}
					}
					break;
				}
			}
		}
	}
}
#endif
