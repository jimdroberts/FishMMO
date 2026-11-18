#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The country bridges the biome drafts name (2026-10-26, phase I2l-b; Biomes.BiomeCrossingStyle, decisions 49, 52, 59, 61, 78, 79):
	/// the liana bridge, the corbelled laterite bridge, the covered wind-and-rain bridge, the cantilever bridge, the Goblins' Bridge and
	/// the snow bridge. Each is laid by a Bridge template of its style (PointOfInterestTemplateSpecs.Works, by id) as the kit's timber
	/// span is: along z, the walking surface's top at y = 0 where it meets the next module or the bank, across x, stretched along z to the
	/// crossing. The modular ones (laterite, covered, slabs) stand a pier at their −z end and are laid end to end; the single ones (liana,
	/// cantilever, snow) are one piece bank to bank, each a module as long as the spans it is built for (PathCrossings.StyleSpan).
	/// </summary>
	/// <remarks>
	/// Every piece carries the tag "crossing" alone, so no older query ("bridge", "water") draws one: a timber bridge stays a timber bridge.
	/// The coarsest level is the collider: a continuous walk with its rails or parapets.
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>How long one liana bridge is between its anchor trees, metres (unstretched).</summary>
		public const float LianaBridgeLength = 24f;

		/// <summary>How long one cantilever bridge is bank seat to bank seat, metres (unstretched).</summary>
		public const float CantileverBridgeLength = 18f;

		/// <summary>How long one snow bridge is lip to lip, metres (unstretched).</summary>
		public const float SnowBridgeLength = 10f;

		/// <summary>The modules of the laid-end-to-end styles, metres along z.</summary>
		public const float LateriteModule = 4f, CoveredModule = 6f, GoblinSlabModule = 3.5f;

		private static void AddCrossings(List<StructurePiece> list)
		{
			StructurePiece liana = Piece(list, "LianaBridge", StructureStyle.Timber, StructureSize.Building,
				$"A liana bridge {LianaBridgeLength:0} m between anchor trees: a walk of lashed sticks hung from twisted vine cables, V-ties up to vine hand lines; v1 sags deeper, with fewer ties.",
				LianaBridge, "crossing");
			liana.Anchor = StructureAnchor.Deck;
			liana.ModuleLength = LianaBridgeLength;

			StructurePiece laterite = Piece(list, "LateriteSpan", StructureStyle.Stone, StructureSize.Medium,
				$"{LateriteModule:0} m of corbelled laterite bridge along z: a block pier at its −z end, its faces stepping in course by course to close a narrow opening under a flat slab deck; v0 a low balustrade on posts, v1 a plain kerb.",
				LateriteSpan, "crossing");
			laterite.Anchor = StructureAnchor.Deck;
			laterite.ModuleLength = LateriteModule;

			StructurePiece covered = Piece(list, "CoveredSpan", StructureStyle.Timber, StructureSize.Building,
				$"{CoveredModule:0} m of covered wind-and-rain bridge along z: a fieldstone pier at its −z end, stacked log beams, a plank deck between posts with benches along the sides, under a pitched board roof; v1 raises a pavilion's second roof over its middle.",
				CoveredSpan, "crossing");
			covered.Anchor = StructureAnchor.Deck;
			covered.ModuleLength = CoveredModule;

			StructurePiece cantilever = Piece(list, "CantileverBridge", StructureStyle.Timber, StructureSize.Building,
				$"A cantilever bridge {CantileverBridgeLength:0} m bank to bank: a fieldstone and log crib on each bank, three courses of beams each reaching out past the one below, long beams over the gap between them, a plank deck with hand rails; v1 two courses, rough poles.",
				CantileverBridge, "crossing");
			cantilever.Anchor = StructureAnchor.Deck;
			cantilever.ModuleLength = CantileverBridgeLength;

			StructurePiece slab = Piece(list, "GoblinSlab", StructureStyle.Stone, StructureSize.Medium,
				$"{GoblinSlabModule:0.0} m of the Goblins' Bridge along z: one great rough slab laid over a pier of piled boulders at its −z end; v1 a narrower slab on two boulders.",
				GoblinSlab, "crossing");
			slab.Anchor = StructureAnchor.Deck;
			slab.ModuleLength = GoblinSlabModule;

			StructurePiece snow = Piece(list, "SnowBridge", StructureStyle.Stone, StructureSize.Medium,
				$"A snow bridge {SnowBridgeLength:0} m lip to lip: a hump of hard-packed wind snow over a narrow channel, arched underneath and hung with drips, its lips spread onto both banks; v1 thinner and sagging.",
				SnowBridge, "crossing");
			snow.Anchor = StructureAnchor.Deck;
			snow.ModuleLength = SnowBridgeLength;
		}

		// ── The liana bridge (Jungle, decision 52) ────────────────────

		/// <summary>
		/// A liana bridge: cross sticks lashed onto two foot cables hung tree to tree in a curve (a twenty-eighth of the span down in
		/// the middle, a twenty-second on v1: the walk off each end stays under 10°), two hand cables a metre above, each a pair of
		/// twisted vines, V-ties from the hand cables down to the foot cables, two anchor trunks with a lashed crossbar sunk into each
		/// bank past the ends and a few sticks of landing. The coarsest level is a continuous walk with its hand cables.
		/// </summary>
		private static void LianaBridge(StructureDraft d)
		{
			const float Walk = 0.9f, HandAbove = 1.0f, Post = 0.7f;
			float length = LianaBridgeLength, half = length * 0.5f;
			float sag = length / (d.Variant == 0 ? 28f : 22f);
			float Deck(float z) => -sag * (1f - 4f * z * z / (length * length));

			using (d.Builds(DeckRoles))
			{
				if (d.Lod == 2)
				{
					const int Slabs = 12;
					for (int k = 0; k < Slabs; k++)
					{
						float z0 = -half + length * k / Slabs, z1 = -half + length * (k + 1) / Slabs;
						d.Add(StructureShapes.Beam(new Vector3(0f, Deck(z0) - 0.05f, z0 - 0.05f), new Vector3(0f, Deck(z1) - 0.05f, z1 + 0.05f), Walk, 0.1f, StructureMaterial.Timber, 1 + k));
					}
				}
				else
				{
					// Sticks across, each a little crooked, at a hand's spacing; bundles of three at level 1.
					float pitch = d.Fine ? 0.34f : 1.0f;
					int sticks = Mathf.RoundToInt(length / pitch);
					pitch = length / sticks;
					for (int k = 0; k < sticks; k++)
					{
						float z = -half + pitch * (k + 0.5f);
						float lean = d.Fine ? d.Range(k, 5, -0.06f, 0.06f) : 0f;
						float y = Deck(z) - 0.04f;
						d.Add(StructureShapes.Beam(new Vector3(-Walk * 0.5f - 0.06f, y, z - lean), new Vector3(Walk * 0.5f + 0.06f, y, z + lean), d.Fine ? 0.07f : pitch - 0.08f, 0.07f, StructureMaterial.Timber, 1 + k));
					}
				}
			}

			// The cables: the foot pair under the sticks' ends, the hand pair above, each a chain of short runs; a second, thinner vine
			// twisted along each hand cable at the finer levels.
			int runs = d.Fine ? 16 : d.Mid ? 10 : 8;
			float cable = d.Lod == 2 ? 0.12f : 0.07f;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * Walk * 0.5f;
				for (int k = 0; k < runs; k++)
				{
					float z0 = -half + length * k / runs, z1 = -half + length * (k + 1) / runs;
					d.Add(StructureShapes.Beam(new Vector3(x, Deck(z0) + HandAbove, z0), new Vector3(x, Deck(z1) + HandAbove, z1), cable, cable, StructureMaterial.Timber, 400 + s * 50 + k));
					if (d.Lod < 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(x, Deck(z0) - 0.1f, z0), new Vector3(x, Deck(z1) - 0.1f, z1), cable, cable, StructureMaterial.Timber, 500 + s * 50 + k));
					}
					if (d.Fine)
					{
						// The twist: a thinner vine wound round the hand cable, crossing it at each run's middle.
						float w0 = (k % 2 == 0 ? 1f : -1f) * 0.05f;
						d.Add(StructureShapes.Beam(new Vector3(x + w0, Deck(z0) + HandAbove + w0, z0), new Vector3(x - w0, Deck(z1) + HandAbove - w0, z1), 0.035f, 0.035f, StructureMaterial.Timber, 550 + s * 50 + k, StructureRole.Detail));
					}
				}
				// V-ties: from each foot point up to the hand cable either side of it, a lattice down the bridge's side.
				if (d.Mid)
				{
					float every = d.Variant == 0 ? 1.5f : 2.4f;
					int ties = Mathf.RoundToInt(length / every);
					for (int k = 1; k < ties; k++)
					{
						float z = -half + length * k / ties;
						float spread = 0.5f * length / ties;
						Vector3 foot = new Vector3(x, Deck(z) - 0.1f, z);
						d.Add(StructureShapes.Beam(foot, new Vector3(x, Deck(z - spread) + HandAbove, z - spread), 0.03f, 0.03f, StructureMaterial.Timber, 600 + s * 70 + k, StructureRole.Detail));
						if (d.Fine)
						{
							d.Add(StructureShapes.Beam(foot, new Vector3(x, Deck(z + spread) + HandAbove, z + spread), 0.03f, 0.03f, StructureMaterial.Timber, 640 + s * 70 + k, StructureRole.Detail));
						}
					}
				}
				// The anchor trunks past each end, sunk into the bank, rising past the hand cables.
				for (int e = -1; e <= 1; e += 2)
				{
					float z = e * (half + Post);
					d.Add(StructureShapes.Beam(new Vector3(s * (Walk * 0.5f + 0.2f), -1.3f, z), new Vector3(s * (Walk * 0.5f + 0.16f), HandAbove + 0.9f, z), 0.3f, 0.3f, StructureMaterial.Timber, 700 + s * 10 + e));
				}
			}
			for (int e = -1; e <= 1; e += 2)
			{
				float z = e * (half + Post);
				// The lashed crossbar the hand cables are tied over, and its lashing.
				d.Add(StructureShapes.Beam(new Vector3(-Walk * 0.5f - 0.4f, HandAbove + 0.25f, z), new Vector3(Walk * 0.5f + 0.4f, HandAbove + 0.25f, z), 0.14f, 0.14f, StructureMaterial.Timber, 720 + e));
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * (Walk * 0.5f + 0.18f), HandAbove + 0.25f, z), new Vector3(0.36f, 0.2f, 0.36f), StructureMaterial.Canvas, 725 + e * 3 + s, StructureRole.Detail));
					}
				}
				// The landing: a few sticks from the end back onto the bank.
				d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, e * (half + Post * 0.5f)), new Vector3(Walk + 0.1f, 0.08f, Post + 0.1f), StructureMaterial.Timber, 730 + e));
			}
		}

		// ── The corbelled laterite bridge (Jungle Temple, decision 59) ─

		/// <summary>
		/// A module of corbelled laterite bridge: a block pier at the module's −z end, from the bed up to the deck, whose faces step out
		/// course by course over the opening either side (the corbel arch of the royal roads' bridges: no true arch, each course an
		/// overhang), a flat slab deck over it all, and a low balustrade on posts (v0) or a plain kerb (v1). The steps of the next
		/// module's pier close this module's opening.
		/// </summary>
		private static void LateriteSpan(StructureDraft d)
		{
			const float Width = 3.6f, Slab = 0.4f, Pier = 1.4f, Course = 0.36f, Step = 0.24f, Foot = -5.8f;
			float half = LateriteModule * 0.5f;
			bool balustrade = d.Variant == 0;
			StructureMaterial stone = StructureMaterial.Stone;

			// The deck: slabs across, the walk's top at 0.
			int slabs = d.Fine ? 4 : 1;
			for (int k = 0; k < slabs; k++)
			{
				float z0 = -half + LateriteModule * k / slabs, z1 = -half + LateriteModule * (k + 1) / slabs;
				float gap = d.Fine ? 0.02f : 0f;
				d.Add(StructureShapes.BoxBetween(new Vector3(-Width * 0.5f, -Slab, z0 + gap), new Vector3(Width * 0.5f, 0f, z1 - gap), stone, 1 + k));
			}

			// The pier, centred on the module's −z end, and its corbel courses stepping out both ways toward the openings.
			float pz = -half;
			float soffit = -Slab;
			int courses = d.Fine ? 4 : d.Mid ? 3 : 2;
			float stepBy = d.Fine ? Step : Step * 4f / courses;
			float courseH = d.Fine ? Course : Course * 4f / courses;
			float springing = soffit - courses * courseH;
			d.Add(StructureShapes.BoxBetween(new Vector3(-Width * 0.5f + 0.1f, Foot, pz - Pier * 0.5f), new Vector3(Width * 0.5f - 0.1f, springing, pz + Pier * 0.5f), stone, 20));
			for (int c = 0; c < courses; c++)
			{
				float y0 = springing + c * courseH, y1 = y0 + courseH;
				float reach = Pier * 0.5f + (c + 1) * stepBy;
				d.Add(StructureShapes.BoxBetween(new Vector3(-Width * 0.5f + 0.1f, y0, pz - reach), new Vector3(Width * 0.5f - 0.1f, y1, pz + reach), stone, 30 + c));
			}
			// A cutwater on the pier's upstream and downstream faces: the blocks brought to a blunt point against the current.
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(Cutwater(s, Width * 0.5f - 0.1f, pz, Pier * 0.5f, 0.6f, Foot, springing - 0.2f, stone, 40 + s));
				}
			}

			// Along both edges: the balustrade's posts and rail (v0), or a kerb of blocks (v1).
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (Width * 0.5f - 0.18f);
				if (balustrade)
				{
					d.Add(StructureShapes.Box(new Vector3(x, 0.62f, 0f), new Vector3(0.26f, 0.22f, LateriteModule), StructureMaterial.Monolith, 50 + s));
					int posts = d.Fine ? 4 : d.Mid ? 2 : 1;
					for (int k = 0; k < posts; k++)
					{
						float z = -half + LateriteModule * (k + 0.5f) / posts;
						d.Add(StructureShapes.Box(new Vector3(x, 0.26f, z), new Vector3(0.3f, 0.52f, 0.3f), StructureMaterial.Monolith, 60 + s * 8 + k));
					}
				}
				else
				{
					d.Add(StructureShapes.Box(new Vector3(x, 0.17f, 0f), new Vector3(0.36f, 0.34f, LateriteModule), stone, 70 + s));
				}
			}
		}

		/// <summary>
		/// A pier's cutwater: a triangle in plan on the pier's face at x = side·<paramref name="face"/>, its base the pier's length along z
		/// (<paramref name="zCentre"/> ± <paramref name="halfLength"/>), its point <paramref name="point"/> out across the current, standing
		/// from <paramref name="y0"/> to <paramref name="y1"/>.
		/// </summary>
		private static StructureSolid Cutwater(int side, float face, float zCentre, float halfLength, float point, float y0, float y1, StructureMaterial material, int key)
		{
			// Drawn in (x, −z) and stood up a quarter turn about x, so the prism's own z (y0 … y1) is the world's y.
			var outline = new[] { new Vector2(side * face, halfLength), new Vector2(side * (face + point), 0f), new Vector2(side * face, -halfLength) };
			return StructureShapes.Prism(outline, y0, y1, material, key).Transform(Matrix4x4.TRS(new Vector3(0f, 0f, zCentre), Quaternion.Euler(-90f, 0f, 0f), Vector3.one));
		}

		// ── The covered wind-and-rain bridge (Karst, decision 61) ─────

		/// <summary>
		/// A module of covered wind-and-rain bridge: a fieldstone pier with cutwaters at the module's −z end, two courses of stacked
		/// log beams over it reaching out along the bridge, a plank deck between posts (benches and a rail between them along both
		/// sides), and a pitched board roof over all from post to post; v1 raises a pavilion's second roof on short posts over the
		/// module's middle, so a bridge of them carries its towers.
		/// </summary>
		private static void CoveredSpan(StructureDraft d)
		{
			const float Width = 3.4f, PostH = 2.4f, Eave = 0.45f, Rise = 1.1f, Foot = -5.8f;
			float half = CoveredModule * 0.5f;
			bool pavilion = d.Variant == 1;

			// The deck's planks across, on two stringers.
			using (d.Builds(DeckRoles))
			{
				if (d.Fine)
				{
					int planks = Mathf.RoundToInt(CoveredModule / 0.3f);
					float pitch = CoveredModule / planks;
					for (int i = 0; i < planks; i++)
					{
						float z = -half + pitch * (i + 0.5f);
						d.Add(StructureShapes.Box(new Vector3(d.Range(i, 1, -0.03f, 0.03f), -0.05f, z), new Vector3(Width, 0.1f, pitch - 0.02f), StructureMaterial.Timber, 1 + i)).Grain = Vector3.right;
					}
				}
				else
				{
					d.Add(StructureShapes.Box(new Vector3(0f, -0.05f, 0f), new Vector3(Width, 0.1f, CoveredModule), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				}
			}

			// The stacked log beams under the deck, the lower course shorter (reaching out from the pier), the upper the full module.
			int beams = d.Mid ? 4 : 2;
			for (int b = 0; b < beams; b++)
			{
				float x = (b - (beams - 1) * 0.5f) * (Width - 0.6f) / Mathf.Max(1, beams - 1);
				d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, -half), new Vector3(x, -0.3f, half), 0.32f, 0.36f, StructureMaterial.Timber, 100 + b));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, -0.68f, -half - 1.6f), new Vector3(x, -0.68f, -half + 1.6f), 0.3f, 0.34f, StructureMaterial.Timber, 110 + b));
				}
			}

			// The pier at the −z end: fieldstone from the bed to the beams, pointed against the current both ways.
			float pierTop = d.Mid ? -0.85f : -0.48f;
			d.Add(StructureShapes.BoxBetween(new Vector3(-Width * 0.5f, Foot, -half - 0.9f), new Vector3(Width * 0.5f, pierTop, -half + 0.9f), StructureMaterial.Fieldstone, 120));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(Cutwater(s, Width * 0.5f, -half, 0.9f, 1.0f, Foot, pierTop - 0.3f, StructureMaterial.Fieldstone, 125 + s));
				}
			}

			// The posts down both sides, the rail and the bench between them, the plate the roof sits on.
			int bays = d.Fine ? 4 : d.Mid ? 2 : 1;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (Width * 0.5f - 0.1f);
				// The module's +z post is the next module's −z post: each module lays its −z post and those between.
				for (int k = 0; k < bays; k++)
				{
					float z = -half + CoveredModule * k / bays;
					d.Add(StructureShapes.Box(new Vector3(x, PostH * 0.5f, z + 0.1f), new Vector3(0.2f, PostH, 0.2f), StructureMaterial.Timber, 200 + s * 10 + k));
				}
				d.Add(StructureShapes.Box(new Vector3(x, PostH + 0.08f, 0f), new Vector3(0.24f, 0.16f, CoveredModule), StructureMaterial.Timber, 230 + s));
				d.Add(StructureShapes.Box(new Vector3(x, 0.95f, 0f), new Vector3(0.1f, 0.12f, CoveredModule), StructureMaterial.Timber, 240 + s));
				if (d.Mid)
				{
					// The bench along the side, on the deck, below the rail.
					d.Add(StructureShapes.Box(new Vector3(x - s * 0.28f, 0.42f, 0f), new Vector3(0.36f, 0.06f, CoveredModule - 0.1f), StructureMaterial.Timber, 250 + s, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(x - s * 0.12f, 0.2f, 0f), new Vector3(0.06f, 0.4f, CoveredModule - 0.1f), StructureMaterial.Timber, 255 + s, StructureRole.Detail));
				}
			}

			// The roof: two board slopes from eaves past the posts up to a ridge, a ridge beam along it.
			float eaveY = PostH + 0.16f, ridgeY = eaveY + Rise, eaveX = Width * 0.5f + Eave;
			float slope = Mathf.Sqrt(eaveX * eaveX + Rise * Rise);
			float pitchDeg = Mathf.Atan2(Rise, eaveX) * Mathf.Rad2Deg;
			for (int s = -1; s <= 1; s += 2)
			{
				var centre = new Vector3(s * eaveX * 0.5f, 0.5f * (eaveY + ridgeY), 0f);
				d.Add(StructureShapes.Block(centre, new Vector3(slope + 0.05f, 0.1f, CoveredModule + 0.04f), Quaternion.Euler(0f, 0f, s * pitchDeg), StructureMaterial.Timber, 300 + s)).Grain = Vector3.forward;
			}
			d.Add(StructureShapes.Box(new Vector3(0f, ridgeY + 0.04f, 0f), new Vector3(0.24f, 0.16f, CoveredModule + 0.04f), StructureMaterial.Timber, 310));
			if (d.Mid)
			{
				// Tie beams across under the roof at the posts.
				d.Add(StructureShapes.Box(new Vector3(0f, PostH + 0.08f, -half + 0.1f), new Vector3(Width, 0.16f, 0.18f), StructureMaterial.Timber, 320));
			}
			if (pavilion)
			{
				// The pavilion: short posts on the ridge's flanks carrying a second, steeper roof over the module's middle third.
				float pz = CoveredModule / 3f, py = ridgeY + 0.45f, px = Width * 0.32f, prise = 0.9f;
				for (int s = -1; s <= 1; s += 2)
				{
					for (int e = -1; e <= 1; e += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * px, ridgeY - 0.25f + 0.35f, e * pz * 0.5f), new Vector3(0.16f, 0.7f + 0.5f, 0.16f), StructureMaterial.Timber, 400 + s * 10 + e));
					}
					float ex = px + 0.45f;
					float pslope = Mathf.Sqrt(ex * ex + prise * prise), pdeg = Mathf.Atan2(prise, ex) * Mathf.Rad2Deg;
					d.Add(StructureShapes.Block(new Vector3(s * ex * 0.5f, py + prise * 0.5f, 0f), new Vector3(pslope + 0.05f, 0.1f, pz + 0.5f), Quaternion.Euler(0f, 0f, s * pdeg), StructureMaterial.Timber, 420 + s)).Grain = Vector3.forward;
				}
				d.Add(StructureShapes.Box(new Vector3(0f, py + prise + 0.05f, 0f), new Vector3(0.2f, 0.14f, pz + 0.5f), StructureMaterial.Timber, 430));
				if (d.Fine)
				{
					// The finial at the ridge's middle.
					d.Add(StructureShapes.Box(new Vector3(0f, py + prise + 0.35f, 0f), new Vector3(0.14f, 0.5f, 0.14f), StructureMaterial.Timber, 431, StructureRole.Detail));
				}
			}
		}

		// ── The cantilever bridge (Rocky Terrain, decision 78) ────────

		/// <summary>
		/// A cantilever bridge: on each bank a crib (a fieldstone seat with logs laid crosswise on it), courses of beams on the crib each
		/// reaching out over the gap past the one below, long beams across the gap that is
		/// left from the top course to the other, a plank deck the whole length with a hand rail on posts. Three courses on v0, two of
		/// rough poles on v1. The deck is level at y = 0; the courses rise to it from below.
		/// </summary>
		private static void CantileverBridge(StructureDraft d)
		{
			const float Width = 2.4f, Crib = 2.6f, Seat = -3.4f;
			float length = CantileverBridgeLength, half = length * 0.5f;
			bool rough = d.Variant == 1;
			int courses = rough ? 2 : 3;
			float reach = rough ? 2.6f : 2.0f;
			float beam = rough ? 0.26f : 0.3f;

			using (d.Builds(DeckRoles))
			{
				if (d.Fine)
				{
					int planks = Mathf.RoundToInt(length / 0.32f);
					float pitch = length / planks;
					for (int i = 0; i < planks; i++)
					{
						float z = -half + pitch * (i + 0.5f);
						d.Add(StructureShapes.Box(new Vector3(d.Range(i, 1, -0.03f, 0.03f), -0.05f, z), new Vector3(Width + d.Range(i, 2, -0.06f, 0.06f), 0.1f, pitch - 0.03f), StructureMaterial.Timber, 1 + i)).Grain = Vector3.right;
					}
				}
				else
				{
					d.Add(StructureShapes.Box(new Vector3(0f, -0.05f, 0f), new Vector3(Width, 0.1f, length), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				}
			}

			int across = d.Mid ? 3 : 2;
			float courseH = (beam + 0.04f);
			// The top course's tip on each side; the long beams span between them.
			float tip = half - Crib - courses * reach;
			for (int e = -1; e <= 1; e += 2)
			{
				float cribIn = e * (half - Crib), cribOut = e * half;
				// The fieldstone seat on the bank.
				// Two layers of crosswise logs on the seat, the lowest course of beams on them.
				const float Log = 0.34f;
				float cribTop = -0.12f - courses * courseH;
				float seatTop = cribTop - 2f * Log;
				d.Add(StructureShapes.BoxBetween(new Vector3(-Width * 0.5f - 0.4f, Seat, Mathf.Min(cribIn, cribOut)), new Vector3(Width * 0.5f + 0.4f, seatTop, Mathf.Max(cribIn, cribOut)), StructureMaterial.Fieldstone, 100 + e));
				int logs = d.Fine ? 4 : 2;
				for (int layer = 0; layer < (d.Mid ? 2 : 1); layer++)
				{
					float y = seatTop + Log * (layer + 0.5f);
					for (int k = 0; k < logs; k++)
					{
						float z = e * (half - Crib * (k + 0.5f) / logs);
						d.Add(StructureShapes.Beam(new Vector3(-Width * 0.5f - 0.35f, y, z), new Vector3(Width * 0.5f + 0.35f, y, z), Log, Log, StructureMaterial.Timber, 110 + e * 20 + layer * 8 + k));
					}
				}
				// The courses: each starts at the crib's back and reaches one reach further out over the gap than the one below.
				for (int c = 0; c < courses; c++)
				{
					float y = cribTop + c * courseH + beam * 0.5f;
					float outTo = e * (half - Crib - (c + 1) * reach);
					for (int b = 0; b < across; b++)
					{
						float x = (b - (across - 1) * 0.5f) * (Width - beam) / Mathf.Max(1, across - 1);
						float wob = rough && d.Fine ? d.Range(c * 10 + b, 7 + e, -0.04f, 0.04f) : 0f;
						d.Add(StructureShapes.Beam(new Vector3(x + wob, y, cribOut), new Vector3(x - wob, y, outTo), beam, beam, StructureMaterial.Timber, 200 + (e + 1) * 50 + c * 10 + b));
					}
				}
			}
			// The long beams across the gap, from one top course's tip to the other's, under the deck.
			for (int b = 0; b < across; b++)
			{
				float x = (b - (across - 1) * 0.5f) * (Width - beam) / Mathf.Max(1, across - 1);
				d.Add(StructureShapes.Beam(new Vector3(x, -0.12f - beam * 0.5f, -tip - reach), new Vector3(x, -0.12f - beam * 0.5f, tip + reach), beam, beam, StructureMaterial.Timber, 320 + b));
			}
			// The hand rails on posts along both sides.
			int posts = d.Fine ? 7 : d.Mid ? 4 : 2;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (Width * 0.5f - 0.06f);
				d.Add(StructureShapes.Beam(new Vector3(x, 1.0f, -half), new Vector3(x, 1.0f, half), 0.1f, 0.1f, StructureMaterial.Timber, 400 + s));
				for (int k = 0; k < posts; k++)
				{
					float z = -half + 0.3f + (length - 0.6f) * k / Mathf.Max(1, posts - 1);
					d.Add(StructureShapes.Box(new Vector3(x, 0.5f, z), new Vector3(0.12f, 1.0f, 0.12f), StructureMaterial.Timber, 410 + s * 20 + k));
				}
			}
		}

		// ── The Goblins' Bridge (Scree, decision 79) ──────────────────

		/// <summary>
		/// A module of the Goblins' Bridge: one great rough slab, its top worn level enough to walk (a few centimetres of lean), laid
		/// over a pier of boulders piled from the bed at the module's −z end; v0 a broad slab on three boulders, v1 a narrower one on two.
		/// Laid end to end the slabs make a crooked clapper walk over a scree stream.
		/// </summary>
		private static void GoblinSlab(StructureDraft d)
		{
			bool broad = d.Variant == 0;
			float width = broad ? 1.7f : 1.3f, thick = 0.42f, half = GoblinSlabModule * 0.5f;
			// The slab: its top at 0 at both ends (where the next one meets it), rolled a little about its length.
			float roll = d.Range(1, 1, -1.6f, 1.6f);
			float bevel = d.Bevel(0.06f);
			StructureSolid top = bevel > 0f
				? StructureShapes.ChamferBox(new Vector3(0f, -thick * 0.5f, 0f), new Vector3(width, thick, GoblinSlabModule - 0.04f), bevel, StructureMaterial.Fieldstone, 1)
				: StructureShapes.Box(new Vector3(0f, -thick * 0.5f, 0f), new Vector3(width, thick, GoblinSlabModule - 0.04f), StructureMaterial.Fieldstone, 1);
			d.Add(top.Transform(Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, roll))));
			// The pier: boulders piled under the −z end, the top one bedding the slab, the lowest down in the stream bed.
			int boulders = broad ? 3 : 2;
			int seg = d.Seg(8, 5), rings = d.Lod == 2 ? 3 : d.Mid ? 4 : 5;
			float y = -thick - 0.55f;
			for (int k = 0; k < boulders; k++)
			{
				float r = (broad ? 0.95f : 0.8f) + 0.15f * k;
				var radii = new Vector3(r * d.Range(10 + k, 2, 0.95f, 1.2f), r * d.Range(10 + k, 3, 0.7f, 0.9f), r * d.Range(10 + k, 4, 0.8f, 1.0f));
				var at = new Vector3(d.Range(10 + k, 5, -0.15f, 0.15f), y, -half + d.Range(10 + k, 6, -0.12f, 0.12f));
				d.Add(StructureShapes.Sphere(at, radii, seg, rings, StructureMaterial.Fieldstone, 10 + k));
				y -= radii.y * 1.7f;
				if (y < -5.2f)
				{
					break;
				}
			}
			if (d.Fine)
			{
				// Chock stones wedged under the slab's edges where it rides the pier.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(new Vector3(s * width * 0.4f, -thick - 0.12f, -half + 0.35f), new Vector3(0.32f, 0.22f, 0.28f),
						Quaternion.Euler(d.Range(20 + s, 1, -15f, 15f), d.Range(20 + s, 2, 0f, 90f), 0f), StructureMaterial.Fieldstone, 20 + s, StructureRole.Detail));
				}
			}
		}

		// ── The snow bridge (Glacier, decision 49) ───────────────────

		/// <summary>
		/// A snow bridge: wind-packed snow lying over a narrow channel, its walked top a low hump (a quarter of a metre up in the
		/// middle) meeting the banks at y = 0, its underside an arch eaten out by the water (springing 1.6 m down at the ends, the crown
		/// 0.7 m under the top; v1 thinner, its crown 0.45 m under the top), its lips spread a metre onto both banks, drips of hard snow
		/// hanging under the arch's crown. Built in the kit's plaster, white.
		/// </summary>
		private static void SnowBridge(StructureDraft d)
		{
			const float Width = 2.6f, Lip = 1.0f, Spring = -1.6f;
			float length = SnowBridgeLength, half = length * 0.5f;
			float crown = d.Variant == 0 ? -0.7f : -0.45f, hump = d.Variant == 0 ? 0.25f : 0.18f;
			StructureMaterial snow = StructureMaterial.Plaster;
			Quaternion across = Quaternion.Euler(0f, 90f, 0f);
			float Top(float z) => hump * (1f - 4f * z * z / (length * length));
			float Under(float z)
			{
				float t = Mathf.Clamp01(Mathf.Abs(z) / half);
				return Mathf.Lerp(crown, Spring, t * t);
			}
			int count = d.Fine ? 12 : d.Mid ? 8 : 4;
			for (int k = 0; k < count; k++)
			{
				float z0 = -half + length * k / count, z1 = -half + length * (k + 1) / count;
				var outline = new[] { new Vector2(z0, Under(z0)), new Vector2(z1, Under(z1)), new Vector2(z1, Top(z1)), new Vector2(z0, Top(z0)) };
				d.Add(Across(outline, Width * 0.5f, across, snow, 1 + k));
			}
			// The lips onto both banks: wedges from the top line out and down into the snow on the ground.
			for (int e = -1; e <= 1; e += 2)
			{
				float z0 = e * half, z1 = e * (half + Lip);
				var outline = e > 0
					? new[] { new Vector2(z0, Spring * 0.5f), new Vector2(z1, -0.15f), new Vector2(z1, -0.02f), new Vector2(z0, 0f) }
					: new[] { new Vector2(z1, -0.15f), new Vector2(z0, Spring * 0.5f), new Vector2(z0, 0f), new Vector2(z1, -0.02f) };
				d.Add(Across(outline, Width * 0.5f + 0.3f, across, snow, 40 + e));
			}
			// Cornices of drift along both edges, a little proud of the top.
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					int runs = d.Fine ? 6 : 3;
					for (int k = 0; k < runs; k++)
					{
						float z0 = -half + length * k / runs, z1 = -half + length * (k + 1) / runs;
						var outline = new[] { new Vector2(z0, Top(z0) - 0.3f), new Vector2(z1, Top(z1) - 0.3f), new Vector2(z1, Top(z1) + 0.12f), new Vector2(z0, Top(z0) + 0.12f) };
						StructureSolid ridge = Across(outline, 0.18f, across, snow, 60 + s * 10 + k, StructureRole.Detail);
						d.Add(ridge.Transform(Matrix4x4.Translate(new Vector3(s * (Width * 0.5f - 0.05f), 0f, 0f))));
					}
				}
			}
			// Drips of hard snow under the crown.
			if (d.Fine)
			{
				for (int k = 0; k < 7; k++)
				{
					float z = -half * 0.6f + length * 0.6f * k / 6f + d.Range(k, 1, -0.2f, 0.2f);
					float x = d.Range(k, 2, -Width * 0.4f, Width * 0.4f);
					float drop = d.Range(k, 3, 0.18f, 0.45f);
					float y = Under(z) + 0.05f;
					d.Add(StructureShapes.Rod(new Vector3(x, y, z), new Vector3(x, y - drop, z), 0.05f, 5, snow, 80 + k, StructureRole.Detail, drop * 0.6f));
				}
			}
		}

		/// <summary><see cref="Across(Vector2[], float, Quaternion, StructureMaterial, int)"/> in a role.</summary>
		private static StructureSolid Across(Vector2[] outline, float halfWidth, Quaternion across, StructureMaterial material, int key, StructureRole role)
		{
			var local = new Vector2[outline.Length];
			for (int i = 0; i < outline.Length; i++)
			{
				local[i] = new Vector2(-outline[i].x, outline[i].y);
			}
			return StructureShapes.Prism(local, -halfWidth, halfWidth, material, key, role).Transform(Matrix4x4.TRS(Vector3.zero, across, Vector3.one));
		}
	}
}
#endif
