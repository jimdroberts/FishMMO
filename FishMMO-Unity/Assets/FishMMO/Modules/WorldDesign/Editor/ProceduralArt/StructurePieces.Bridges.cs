#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Gorge crossings (2026-10-12: a gorge is not crossed on plank spans with nothing under them): a stone arch for a
	/// gorge a town keeps, a rope-and-plank bridge for a wild or a wide one. Each is one piece from rim to rim, laid by
	/// PropsFeature when its crossing is a gorge's (TryGorgeCrossing) and stretched along z to the lips' distance, as a
	/// bridge span is: across is x, along z, the deck's top at y = 0 at both ends, which land on the rims.
	/// </summary>
	public static partial class StructurePieces
	{
		/// <summary>The stone arches' lengths lip to lip (their ModuleLength): the one nearest a gorge is stretched to it.</summary>
		public static readonly float[] ArchBridgeLengths = { 8f, 14f, 20f, 28f };

		/// <summary>The rope bridges' lengths between their anchor posts: the one nearest a gorge is stretched to it, up to PathRouter's 40 m.</summary>
		public static readonly float[] RopeBridgeLengths = { 16f, 26f, 36f };

		/// <summary>How far an arch's abutments and deck run on past each lip into the rim, metres (unstretched).</summary>
		public const float ArchAbutmentRun = 1.5f;

		private static void AddBridges(List<StructurePiece> list)
		{
			foreach (float length in ArchBridgeLengths)
			{
				float l = length;
				var arch = Piece(list, ArchBridgeId(l), StructureStyle.Stone, StructureSize.Building,
					$"A single segmental stone arch {l:0} m from lip to lip, springing from abutments set into each rim, with spandrel walls and parapets; v0 dressed ashlar with a coping, v1 rubble with a low parapet.",
					d => ArchBridge(d, l), "gorge", "arch");
				arch.Anchor = StructureAnchor.Deck;
				arch.ModuleLength = l;
			}
			foreach (float length in RopeBridgeLengths)
			{
				float l = length;
				var rope = Piece(list, RopeBridgeId(l), StructureStyle.Timber, StructureSize.Building,
					$"A plank walk hung from ropes across a gorge, {l:0} m between its anchor posts and sagging a thirtieth of that in the middle; v1 sags more, with fewer ties.",
					d => RopeBridge(d, l), "gorge", "rope");
				rope.Anchor = StructureAnchor.Deck;
				rope.ModuleLength = l;
			}
		}

		/// <summary>The piece id of the rope bridge of a length (one of <see cref="RopeBridgeLengths"/>).</summary>
		public static string RopeBridgeId(float length) => $"RopeBridge{Mathf.RoundToInt(length)}";

		/// <summary>The piece id of the stone arch of a length (one of <see cref="ArchBridgeLengths"/>).</summary>
		public static string ArchBridgeId(float length) => $"ArchBridge{Mathf.RoundToInt(length)}";

		/// <summary>
		/// A segmental arch: the deck level from end to end (the lips at z = ±length/2), its ring of voussoirs springing from
		/// abutments a metre inside each lip with a rise of a seventh of its span (the flat arch medieval builders threw over
		/// a gorge), the spandrels filled up to the deck, parapets along both sides. The abutments run on into the rims, so
		/// the deck meets the ground past each lip and the arch's feet are bedded in the walls, never hanging in the air.
		/// </summary>
		private static void ArchBridge(StructureDraft d, float length)
		{
			bool ashlar = d.Variant == 0;
			StructureMaterial stone = ashlar ? StructureMaterial.Stone : StructureMaterial.Fieldstone;
			const float Width = 4f, Slab = 0.35f, Fill = 0.15f, Inset = 1f;
			float ring = ashlar ? 0.7f : 0.6f;
			float half = length * 0.5f, end = half + ArchAbutmentRun;
			float clear = length - 2f * Inset, rise = clear / 7f;
			// The intrados: a circle's arc through both springings and the crown.
			float crown = -(Slab + Fill + ring), spring = crown - rise;
			float radius = (clear * clear * 0.25f + rise * rise) / (2f * rise), centreY = crown - radius;
			float sweep = Mathf.Asin(clear * 0.5f / radius);

			// The deck, over the arch and on into both rims.
			d.Add(StructureShapes.Box(new Vector3(0f, -Slab * 0.5f, 0f), new Vector3(Width, Slab, 2f * end), stone, 1));
			// The abutments: from the springing (and a metre below it) out past the lip, bedded in the rim's rock.
			float foot = spring - 1f;
			for (int s = -1; s <= 1; s += 2)
			{
				float z0 = s * (clear * 0.5f), z1 = s * end;
				d.Add(StructureShapes.BoxBetween(new Vector3(-Width * 0.5f, foot, Mathf.Min(z0, z1)), new Vector3(Width * 0.5f, -Slab, Mathf.Max(z0, z1)), stone, 10 + s));
			}

			// The ring and the spandrels over it, segment by segment: each voussoir an exact wedge of the ring, each spandrel
			// the wall from the ring's back up to the deck.
			int count = d.Fine ? 17 : d.Mid ? 11 : 7;
			Quaternion across = Quaternion.Euler(0f, 90f, 0f);
			Vector2 Arc(float angle, float r) => new Vector2(r * Mathf.Sin(angle), centreY + r * Mathf.Cos(angle));
			for (int k = 0; k < count; k++)
			{
				float a0 = -sweep + 2f * sweep * k / count, a1 = -sweep + 2f * sweep * (k + 1) / count;
				Vector2 i0 = Arc(a0, radius), i1 = Arc(a1, radius), e0 = Arc(a0, radius + ring), e1 = Arc(a1, radius + ring);
				// The keystone stands proud of the ring on a dressed arch.
				float proud = ashlar && d.Fine && k == count / 2 ? 0.08f : 0f;
				d.Add(Across(new[] { i0, i1, e1, e0 }, Width * 0.5f + proud, across, stone, 100 + k));
				var top0 = new Vector2(e0.x, -Slab);
				var top1 = new Vector2(e1.x, -Slab);
				if (e0.y < -Slab - 0.02f || e1.y < -Slab - 0.02f)
				{
					d.Add(Across(new[] { e0, e1, top1, top0 }, Width * 0.5f - 0.05f, across, stone, 200 + k));
				}
			}

			// Parapets along both sides, a coping along their tops on the dressed arch.
			float wallHeight = ashlar ? 0.95f : 0.7f, wall = ashlar ? 0.4f : 0.45f;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (Width * 0.5f - wall * 0.5f);
				d.Add(StructureShapes.Box(new Vector3(x, wallHeight * 0.5f, 0f), new Vector3(wall, wallHeight, 2f * end), stone, 300 + s));
				if (ashlar && d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(x, wallHeight + 0.06f, 0f), new Vector3(wall + 0.12f, 0.12f, 2f * end + 0.12f), StructureMaterial.Stone, 310 + s, StructureRole.Detail));
				}
				// A string course along the deck's edge, under the parapet.
				if (ashlar && d.Fine)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (Width * 0.5f + 0.04f), -Slab - 0.06f, 0f), new Vector3(0.1f, 0.14f, 2f * end), StructureMaterial.Stone, 320 + s, StructureRole.Detail));
				}
			}
		}

		/// <summary>A wall section in the z–y plane (<paramref name="outline"/>: z, y), <paramref name="halfWidth"/> either side of x = 0.</summary>
		private static StructureSolid Across(Vector2[] outline, float halfWidth, Quaternion across, StructureMaterial material, int key)
		{
			// The prism's own x runs along the bridge backwards once turned a quarter about y (its z becomes the bridge's x).
			var local = new Vector2[outline.Length];
			for (int i = 0; i < outline.Length; i++)
			{
				local[i] = new Vector2(-outline[i].x, outline[i].y);
			}
			return StructureShapes.Prism(local, -halfWidth, halfWidth, material, key).Transform(Matrix4x4.TRS(Vector3.zero, across, Vector3.one));
		}

		/// <summary>
		/// A rope bridge: planks laid across two foot ropes that hang from post to post in a shallow curve (at most a
		/// thirtieth of the span down in the middle, so the walk off each end is under 8°; a twenty-fourth on v1, under 10°), two hand ropes a metre above
		/// them tied down to the foot ropes, and a pair of anchor posts with a crossbar sunk into each rim past the lip.
		/// The coarsest level, the collider, is a continuous walk with the hand ropes either side.
		/// </summary>
		private static void RopeBridge(StructureDraft d, float length)
		{
			const float Walk = 1.4f, RopeAbove = 1.05f, Post = 0.6f;
			float half = length * 0.5f;
			float sag = length / (d.Variant == 0 ? 30f : 24f);
			float Deck(float z) => -sag * (1f - 4f * z * z / (length * length));

			// The walk.
			// The planks are the walk's floor (2026-10-16).
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
					// Planks across at level 0, boards of three at level 1.
					float pitch = d.Fine ? 0.4f : 1.2f, gap = d.Fine ? 0.06f : 0.04f;
					int planks = Mathf.RoundToInt(length / pitch);
					pitch = length / planks;
					for (int k = 0; k < planks; k++)
					{
						float z0 = -half + pitch * k + gap * 0.5f, z1 = -half + pitch * (k + 1) - gap * 0.5f;
						float wobble = d.Fine ? d.Range(k, 3, -0.05f, 0.05f) : 0f;
						d.Add(StructureShapes.Beam(new Vector3(wobble, Deck(z0) - 0.03f, z0), new Vector3(wobble, Deck(z1) - 0.03f, z1), Walk + d.Range(k, 4, -0.08f, 0.08f), 0.06f, StructureMaterial.Timber, 1 + k)).Grain = Vector3.right;
					}
				}

			}
			// The ropes: two under the planks' ends, two at hand height, each a chain of short straight runs.
			int runs = d.Fine ? 16 : d.Mid ? 10 : 8;
			float rope = d.Lod == 2 ? 0.12f : 0.06f;
			for (int s = -1; s <= 1; s += 2)
			{
				for (int k = 0; k < runs; k++)
				{
					float z0 = -half + length * k / runs, z1 = -half + length * (k + 1) / runs;
					d.Add(StructureShapes.Beam(new Vector3(s * Walk * 0.5f, Deck(z0) + RopeAbove, z0), new Vector3(s * Walk * 0.5f, Deck(z1) + RopeAbove, z1), rope, rope, StructureMaterial.Canvas, 400 + s * 50 + k));
					if (d.Lod < 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(s * (Walk * 0.5f - 0.1f), Deck(z0) - 0.09f, z0), new Vector3(s * (Walk * 0.5f - 0.1f), Deck(z1) - 0.09f, z1), rope, rope, StructureMaterial.Canvas, 500 + s * 50 + k));
					}
				}
				// Ties from the hand rope down to the foot rope.
				if (d.Mid)
				{
					float every = d.Variant == 0 ? 2f : 3f;
					int ties = Mathf.RoundToInt(length / every);
					for (int k = 1; k < ties; k++)
					{
						float z = -half + length * k / ties;
						d.Add(StructureShapes.Beam(new Vector3(s * Walk * 0.5f, Deck(z) - 0.09f, z), new Vector3(s * Walk * 0.5f, Deck(z) + RopeAbove, z), 0.04f, 0.04f, StructureMaterial.Canvas, 600 + s * 50 + k, StructureRole.Detail));
					}
				}
				// The anchor posts past each lip, sunk into the rim, and their crossbars.
				for (int e = -1; e <= 1; e += 2)
				{
					float z = e * (half + Post);
					d.Add(StructureShapes.Beam(new Vector3(s * (Walk * 0.5f + 0.1f), -1.2f, z), new Vector3(s * (Walk * 0.5f + 0.1f), RopeAbove + 0.35f, z), 0.22f, 0.22f, StructureMaterial.Timber, 700 + s * 10 + e));
				}
			}
			for (int e = -1; e <= 1; e += 2)
			{
				float z = e * (half + Post);
				d.Add(StructureShapes.Beam(new Vector3(-Walk * 0.5f - 0.25f, RopeAbove + 0.2f, z), new Vector3(Walk * 0.5f + 0.25f, RopeAbove + 0.2f, z), 0.16f, 0.16f, StructureMaterial.Timber, 720 + e));
				// The landing: a few boards from the lip back onto the rim.
				d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, e * (half + Post * 0.5f)), new Vector3(Walk, 0.08f, Post + 0.1f), StructureMaterial.Timber, 730 + e));
			}
		}
	}
}
#endif
