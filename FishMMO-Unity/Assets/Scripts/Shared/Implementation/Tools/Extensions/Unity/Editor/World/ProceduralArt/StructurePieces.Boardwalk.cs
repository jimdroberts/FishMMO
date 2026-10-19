#if UNITY_EDITOR
using System.Collections.Generic;
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
				"Four metres of a raised plank walk along z, its deck at y = 0: split planks across two stringers, a pole handrail on stout posts; v0 railed both sides, v1 one side (+x). Laid end to end along a way, scaled across to its width.",
				BoardwalkSpan, "boardwalk");
			span.Anchor = StructureAnchor.Deck;
			span.ModuleLength = BoardwalkModule;
			StructurePiece bent = Piece(list, "BoardwalkBent", StructureStyle.Timber, StructureSize.Medium,
				"A boardwalk's bent across x at z = 0, under a joint of two spans: a cap under the stringers on two driven round piles 1.2 m down; v1 braced. Scale y to the ground.",
				BoardwalkBent, "boardwalk");
			bent.Anchor = StructureAnchor.Deck;
		}

		/// <summary>
		/// A boardwalk module: planks across two stringers (the floor), a handrail of round poles lashed to square posts at the
		/// module's start (the next module's posts close the rail), on both sides (v0) or the +x side (v1).
		/// </summary>
		private static void BoardwalkSpan(StructureDraft d)
		{
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
	}
}
#endif
