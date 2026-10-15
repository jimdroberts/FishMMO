#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cave and mine kit (2026-10-13: "enormous caverns … rock bridges over chasms, ladders and ropes between levels …
	/// torches, lanterns, hanging lanterns, encampments"): what a climber climbs (timber and rope ladders, knotted ropes and
	/// their anchors), what a chasm is crossed on (a rope walk and a mine's timber trestle, both laid end to end), the lamps
	/// of a working mine (hung on chains, on wall brackets, on stands), a shaft's collar and windlass, a miner's tools,
	/// sacks and bucket, and what an underground camp adds to the camp pieces (a cooking fire with its pot, a straw bed).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Modules.</b> The ladders are 3 m (<see cref="LadderModule"/>) and 1 m (<see cref="LadderShortModule"/>) tall,
	/// their rungs a third of a metre apart starting a sixth of a metre up, so any mix of the two stacked end to end keeps
	/// one even pitch. Origin at the foot, rungs across x, the back (the wall side) at z = 0 and the whole ladder in −z,
	/// where the climber is. A climbing rope is 3 m (<see cref="ClimbRopeModule"/>), hung from its top
	/// (<see cref="StructureAnchor.Roof"/>), its knots half a metre apart so modules hung one under the next keep the pitch.
	/// The rope walk and the trestle are 4 m spans along z with the deck's top at y = 0, as BridgeSpan is.
	/// </para>
	/// <para>
	/// <b>Lamps.</b> Every lantern's pane is <see cref="StructureMaterial.Lamplight"/> (lit by its own emission day and
	/// night, since no dusk reaches a mine) and gives off <see cref="SceneEmitterKind.Lamp"/> at its middle, which the cut
	/// writes into the scene for the site lights (SiteLightPool lights a buried lamp day and night).
	/// </para>
	/// <para>
	/// <b>Colliders.</b> What a player walks on or into collides (spans, trestles, the collar, stands, posts, sacks); what
	/// is climbed or brushed past does not (ladders, ropes and their anchors, hung and wall lamps, tools, buckets, beds,
	/// the cooking fire): a ladder's rails standing proud of the wall would only snag the climber's capsule, and the
	/// climbing runtime gives a ladder or a rope its own volume.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>A ladder module's height (Ladder, RopeLadder), metres.</summary>
		public const float LadderModule = 3f;
		/// <summary>The short ladder module's height (LadderShort, RopeLadderShort), metres: for the odd metre a stack of 3 m modules leaves.</summary>
		public const float LadderShortModule = 1f;
		/// <summary>Rung to rung, metres: the first rung is half this above a module's foot, so stacked modules keep the pitch.</summary>
		public const float LadderRungPitch = 1f / 3f;
		/// <summary>A ladder's rails (or a rope ladder's ropes, 0.22) stand this far either side of x = 0, metres.</summary>
		public const float LadderHalfWidth = 0.25f;
		/// <summary>How far a ladder stands out from the wall (its back at z = 0, its front at −this), metres.</summary>
		public const float LadderDepth = 0.1f;
		/// <summary>A climbing rope module's length, hung from its top, metres.</summary>
		public const float ClimbRopeModule = 3f;
		/// <summary>Knot to knot on a climbing rope, metres (the first a quarter of a metre under the top).</summary>
		public const float ClimbRopeKnotPitch = 0.5f;
		/// <summary>A rope walk's and a trestle's span along z, metres.</summary>
		public const float CaveSpanModule = 4f;

		/// <summary>The clear height under a rope walk end's crossbar, metres: over any character's head.</summary>
		public const float RopeBridgeHeadroom = 2.3f;
		/// <summary>How far each trestle bent reaches below the deck at scale 1 (v0 two tiers, v1 one): scale y to the floor.</summary>
		public static readonly float[] TrestleBentDepths = { 5.8f, 2.9f };
		/// <summary>A hanging lantern's bottom below its fixing point (v0 on a long chain, v1 a short one), metres.</summary>
		public static readonly float[] HangingLanternDrops = { 1.38f, 1.02f };
		/// <summary>How far a wall lantern's arm holds it out from the wall, metres.</summary>
		public const float WallLanternReach = 0.4f;
		/// <summary>The clear opening inside a shaft collar, metres square.</summary>
		public const float ShaftOpening = 2.4f;

		private static void AddCave(List<StructurePiece> list)
		{
			Piece(list, "Ladder", StructureStyle.Timber, StructureSize.Small,
				"A 3 m ladder module, foot at the origin, back to the wall at z = 0: v0 sawn stiles with pegged rungs, v1 pole rails with lashed rungs.",
				d => TimberLadder(d, LadderModule), "climb", "mine").Collides = false;
			Piece(list, "LadderShort", StructureStyle.Timber, StructureSize.Small,
				"The 1 m ladder module, as Ladder: v0 sawn stiles, v1 pole rails.",
				d => TimberLadder(d, LadderShortModule), "climb", "mine").Collides = false;
			Piece(list, "RopeLadder", StructureStyle.Timber, StructureSize.Small,
				"A 3 m rope ladder module, foot at the origin, back to the wall at z = 0: hemp sides and flat wooden treads; v1 knotted above and below each tread.",
				d => RopeLadder(d, LadderModule), "climb", "rope").Collides = false;
			Piece(list, "RopeLadderShort", StructureStyle.Timber, StructureSize.Small,
				"The 1 m rope ladder module, as RopeLadder.",
				d => RopeLadder(d, LadderShortModule), "climb", "rope").Collides = false;
			StructurePiece rope = Piece(list, "ClimbRope", StructureStyle.Hide, StructureSize.Small,
				"A 3 m climbing rope hung from its top at the origin: v0 knotted every half metre, v1 with foot loops.",
				ClimbRope, "climb", "rope");
			rope.Anchor = StructureAnchor.Roof;
			rope.Collides = false;
			Piece(list, "RopeAnchor", StructureStyle.Iron, StructureSize.Small,
				"What a climbing rope is tied to at a lip, its rope hanging from the origin: v0 a ring piton driven into the rock (+z), v1 a ring bolt on a riveted plate.",
				RopeAnchor, "climb", "rope").Collides = false;
			Piece(list, "RopeBeam", StructureStyle.Timber, StructureSize.Small,
				"A beam laid across a shaft's mouth (3.4 m along x, resting on both rims at y = 0), a rope lashed round its middle hanging from the origin: v0 squared, v1 a log on chocks.",
				RopeBeam, "climb", "rope", "mine");
			StructurePiece walk = Piece(list, "RopeBridgeSpan", StructureStyle.Timber, StructureSize.Medium,
				"Four metres of plank-and-rope walk along z, laid end to end: planks on two foot ropes, hand ropes at a metre, ties between; v1 worn, a plank gone.",
				RopeBridgeSpan, "rope", "cave");
			walk.Anchor = StructureAnchor.Deck;
			walk.ModuleLength = CaveSpanModule;
			StructurePiece walkEnd = Piece(list, "RopeBridgeEnd", StructureStyle.Timber, StructureSize.Medium,
				"A rope walk's end on its ledge: two anchor posts at z = 0 with a crossbar, the ropes made fast to them, a landing in −z; the span leaves toward +z. v0 posts in a cairn, v1 guyed back to stakes.",
				RopeBridgeEnd, "rope", "cave");
			walkEnd.Anchor = StructureAnchor.Deck;
			StructurePiece trestle = Piece(list, "TrestleSpan", StructureStyle.Timber, StructureSize.Medium,
				"Four metres of a mine's timber trestle deck along z, laid end to end: planks on stringers, railed; v1 railed on one side with a cart track along it.",
				TrestleSpan, "mine");
			trestle.Anchor = StructureAnchor.Deck;
			trestle.ModuleLength = CaveSpanModule;
			StructurePiece bent = Piece(list, "TrestleBent", StructureStyle.Timber, StructureSize.Medium,
				"A trestle's bent across x at z = 0, under a joint of two spans: a cap under the deck, battered legs, cross braces and a sill; v0 5.8 m in two tiers, v1 2.9 m. Scale y to the floor.",
				TrestleBent, "mine");
			bent.Anchor = StructureAnchor.Deck;
			StructurePiece hanging = Piece(list, "HangingLantern", StructureStyle.Iron, StructureSize.Small,
				"An iron lantern on a chain from a roof or a beam, its fixing at the origin: v0 a square lantern 1.38 m down, v1 a round horn lantern 1.02 m down.",
				HangingLantern, "lantern", "light", "mine", "cave");
			hanging.Anchor = StructureAnchor.Roof;
			hanging.Collides = false;
			Piece(list, "WallLantern", StructureStyle.Iron, StructureSize.Small,
				"A lantern on an iron bracket, its back plate at z = 0 against a wall, held 0.4 m out along −z: v0 hung from the arm, v1 standing on a bracket shelf.",
				WallLantern, "lantern", "light", "mine", "cave").Collides = false;
			Piece(list, "LanternStand", StructureStyle.Timber, StructureSize.Small,
				"A lantern hung from a stand: v0 a timber post with an arm, the lantern at 1.6 m; v1 an iron crook driven into the ground.",
				LanternStand, "lantern", "light", "camp", "cave");
			Piece(list, "ShaftCollar", StructureStyle.Timber, StructureSize.Medium,
				"Cribbed timbers round a shaft's 2.4 m square mouth, its middle at the origin: v0 with a windlass, its rope and kibble down the shaft; v1 railed on three sides.",
				ShaftCollar, "mine");
			Piece(list, "MiningTools", StructureStyle.Iron, StructureSize.Small,
				"A miner's tools against a wall at z = 0: v0 two picks and a shovel leaning, v1 a rack of picks and a sledge.",
				MiningTools, "mine", "clutter").Collides = false;
			Piece(list, "OreSacks", StructureStyle.Hide, StructureSize.Small,
				"Hessian sacks of ore: v0 three slumped, one spilled; v1 four on a pallet.",
				OreSacks, "mine", "clutter");
			Piece(list, "WaterBucket", StructureStyle.Timber, StructureSize.Small,
				"A hooped wooden bucket with a dipper; v1 two buckets under a yoke.",
				WaterBucket, "mine", "camp", "clutter").Collides = false;
			Piece(list, "MineSign", StructureStyle.Timber, StructureSize.Small,
				"A mine's board: v0 on a post, a pick and hammer crossed on it under carved runes; v1 a warning, a red cross and a skull on a stake.",
				MineSign, "mine", "clutter");
			Piece(list, "CookingFire", StructureStyle.Stone, StructureSize.Small,
				"A camp's cooking fire: v0 a cauldron on an iron crane over a ring of stones, v1 a pot on a three-stone hearth.",
				CookingFire, "camp", "cave").Collides = false;
			Piece(list, "StrawBed", StructureStyle.Hide, StructureSize.Small,
				"A bed of straw on the ground with a blanket and a rolled pack: v1 a fur thrown back and a satchel.",
				StrawBed, "camp", "cave").Collides = false;
		}

		// ── Ladders and ropes ─────────────────────────────────────────

		/// <summary>The height of the rung <paramref name="k"/> of a ladder module over its foot.</summary>
		public static float LadderRungHeight(int k) => LadderRungPitch * (k + 0.5f);

		/// <summary>
		/// A timber ladder: v0 sawn stiles (deeper than wide, the deep side off the wall) with round rungs through them,
		/// wedged at Fine; v1 rails of pole with rungs of thinner pole lashed across them.
		/// </summary>
		private static void TimberLadder(StructureDraft d, float length)
		{
			bool poles = d.Variant == 1;
			const float z = -LadderDepth * 0.5f;
			int rungs = Mathf.RoundToInt(length / LadderRungPitch);
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * LadderHalfWidth;
				if (!poles)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, 0f, z), new Vector3(x, length, z), 0.06f, LadderDepth, StructureMaterial.Timber, 1 + s)).Grain = Vector3.up;
				}
				else
				{
					// Pole rails with a little bow off true in them, as cut poles have.
					var mid = new Vector3(x + s * 0.012f * length / 3f, length * 0.5f, z);
					Log(d, new Vector3(x, 0f, z), mid, LadderDepth * 0.5f, 1 + s, 0f, StructureMaterial.Timber, 7);
					Log(d, mid, new Vector3(x, length, z), LadderDepth * 0.5f, 4 + s, 0f, StructureMaterial.Timber, 7);
				}
			}
			for (int k = 0; k < rungs; k++)
			{
				float y = LadderRungHeight(k);
				if (!poles)
				{
					d.Add(StructureShapes.Rod(new Vector3(-LadderHalfWidth - 0.035f, y, z), new Vector3(LadderHalfWidth + 0.035f, y, z), 0.019f, d.Seg(6, 4), StructureMaterial.Timber, 10 + k));
					if (d.Fine)
					{
						// A wedge driven into each rung's end where it comes through the stile.
						for (int s = -1; s <= 1; s += 2)
						{
							d.Add(StructureShapes.Box(new Vector3(s * (LadderHalfWidth + 0.034f), y, z), new Vector3(0.012f, 0.044f, 0.03f), StructureMaterial.Timber, 40 + 2 * k + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
				}
				else
				{
					float lean = d.Range(k, 30, -0.015f, 0.015f);
					Log(d, new Vector3(-LadderHalfWidth - 0.06f, y + lean, z), new Vector3(LadderHalfWidth + 0.05f, y - lean, z), 0.021f, 10 + k, 0f, StructureMaterial.Timber, 6);
					if (d.Fine)
					{
						// Lashings crossing each joint.
						for (int s = -1; s <= 1; s += 2)
						{
							d.Add(StructureShapes.Block(new Vector3(s * LadderHalfWidth, y, z), new Vector3(0.11f, 0.028f, LadderDepth + 0.012f), Quaternion.Euler(0f, 0f, s * 45f),
								StructureMaterial.Canvas, 40 + 2 * k + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
				}
			}
		}

		/// <summary>A rope ladder: two hemp sides and flat wooden treads a tenth of a metre deep (a Jacob's ladder's), knotted under each tread.</summary>
		private static void RopeLadder(StructureDraft d, float length)
		{
			const float half = 0.22f, z = -LadderDepth * 0.55f;
			int rungs = Mathf.RoundToInt(length / LadderRungPitch);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * half, 0f, z), new Vector3(s * half, length, z), 0.028f, 0.028f, StructureMaterial.Canvas, 1 + s)).Grain = Vector3.up;
			}
			for (int k = 0; k < rungs; k++)
			{
				float y = LadderRungHeight(k);
				float twist = d.Fine ? d.Range(k, 31, -3f, 3f) : 0f;
				d.Add(StructureShapes.Block(new Vector3(0f, y, z), new Vector3(2f * half + 0.1f, 0.035f, 0.11f), Quaternion.Euler(0f, twist, 0f), StructureMaterial.Timber, 10 + k)).Grain = Vector3.right;
				if (!d.Mid)
				{
					continue;
				}
				for (int s = -1; s <= 1; s += 2)
				{
					// The knot under the tread that holds it; v1 one over it too, so it cannot ride up.
					d.Add(StructureShapes.Box(new Vector3(s * half, y - 0.04f, z), new Vector3(0.052f, 0.045f, 0.052f), StructureMaterial.Canvas, 40 + 4 * k + (s > 0 ? 1 : 0), StructureRole.Detail));
					if (d.Variant == 1 && d.Fine)
					{
						d.Add(StructureShapes.Box(new Vector3(s * half, y + 0.04f, z), new Vector3(0.048f, 0.04f, 0.048f), StructureMaterial.Canvas, 42 + 4 * k + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
		}

		/// <summary>
		/// A climbing rope hung from its top at the origin down to −3 m: v0 an overhand knot every half metre (the first a
		/// quarter metre down), v1 a foot loop tied off it every three quarters of a metre. The knots stay at every level:
		/// they are the hand holds, and the rope's girth at a distance.
		/// </summary>
		private static void ClimbRope(StructureDraft d)
		{
			d.Add(StructureShapes.Rod(new Vector3(0f, 0.02f, 0f), new Vector3(0f, -ClimbRopeModule, 0f), 0.019f, d.Seg(6, 4), StructureMaterial.Canvas, 1));
			int seg = d.Seg(7, 4), rings = d.Fine ? 4 : 3;
			if (d.Variant == 0)
			{
				int knots = Mathf.RoundToInt(ClimbRopeModule / ClimbRopeKnotPitch);
				for (int k = 0; k < knots; k++)
				{
					float y = -ClimbRopeKnotPitch * (k + 0.5f);
					d.Add(StructureShapes.Sphere(new Vector3(0f, y, 0f), new Vector3(0.06f, 0.05f, 0.06f), seg, rings, StructureMaterial.Canvas, 10 + k));
				}
				return;
			}
			for (int k = 0; k < 4; k++)
			{
				float y = -0.75f * (k + 0.5f);
				d.Add(StructureShapes.Sphere(new Vector3(0f, y, 0f), new Vector3(0.06f, 0.055f, 0.06f), seg, rings, StructureMaterial.Canvas, 10 + k));
				// The loop hangs off the knot, its plane across the rope's fall (y–z), the foot's way in from −z.
				Quaternion turn = Quaternion.Euler(0f, d.Range(k, 32, -15f, 15f), 0f) * Quaternion.Euler(0f, 0f, 90f);
				d.Add(Ring(new Vector3(0f, y - 0.11f, -0.04f), 0.09f, 0.022f, turn, d.Seg(10, 6), StructureMaterial.Canvas, 20 + k, StructureRole.Structure));
			}
		}

		/// <summary>An annulus of round-ish section (a ring, a hoop, a rope loop): radius to the middle of its section, turned by <paramref name="turn"/> (its axis +y before).</summary>
		private static StructureSolid Ring(Vector3 centre, float radius, float thick, Quaternion turn, int segments, StructureMaterial material, int key, StructureRole role)
		{
			float t = thick * 0.5f;
			var profile = new[] { new Vector2(radius - t, -t), new Vector2(radius + t, -t), new Vector2(radius + t, t), new Vector2(radius - t, t) };
			return StructureShapes.Lathe(profile, segments, material, key, true, 0f, true, false, role).Transform(Matrix4x4.TRS(centre, turn, Vector3.one));
		}

		/// <summary>A rope's anchor at a lip: the ring the rope is tied to hangs just under the origin; the iron goes into the rock at +z.</summary>
		private static void RopeAnchor(StructureDraft d)
		{
			int seg = d.Seg(10, 6);
			if (d.Variant == 0)
			{
				// A ring piton: a square spike driven into the rock, its forged eye, a ring through the eye.
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.06f, -0.03f), new Vector3(0f, 0.06f, 0.3f), 0.034f, 0.034f, StructureMaterial.Iron, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.045f, -0.035f), new Vector3(0.03f, 0.06f, 0.026f), StructureMaterial.Iron, 2));
				d.Add(Ring(new Vector3(0f, 0.02f, -0.05f), 0.05f, 0.016f, Quaternion.Euler(90f, 0f, 0f), seg, StructureMaterial.Iron, 3, StructureRole.Structure));
				return;
			}
			// A ring bolt on a plate: the plate riveted to the rock face, a staple through it, a heavy ring.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.1f, 0.006f), new Vector3(0.16f, 0.16f, 0.016f), StructureMaterial.Iron, 1));
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.1f, 0.22f), 0.03f, 0.03f, StructureMaterial.Iron, 2));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.1f, -0.02f), new Vector3(0.03f, 0.05f, 0.03f), StructureMaterial.Iron, 3));
			d.Add(Ring(new Vector3(0f, 0.05f, -0.04f), 0.065f, 0.02f, Quaternion.Euler(90f, 0f, 0f), seg, StructureMaterial.Iron, 4, StructureRole.Structure));
			if (d.Fine)
			{
				for (int i = 0; i < 4; i++)
				{
					float x = (i % 2 == 0 ? -1f : 1f) * 0.06f, y = 0.1f + (i < 2 ? -1f : 1f) * 0.06f;
					d.Add(StructureShapes.Box(new Vector3(x, y, -0.004f), new Vector3(0.022f, 0.022f, 0.012f), StructureMaterial.Iron, 10 + i, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// A beam across a shaft's mouth, resting on its rims either side (at y = 0), a rope lashed round its middle; the
		/// lashing's tail hangs half a metre past the origin, so a climbing rope hung from the origin carries on from it.
		/// </summary>
		private static void RopeBeam(StructureDraft d)
		{
			const float half = 1.7f;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.14f, 0f), new Vector3(2f * half, 0.24f, 0.24f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.14f, 0.28f, 0.28f), StructureMaterial.Canvas, 2, StructureRole.Detail));
				if (d.Fine)
				{
					// Iron dogs biting the beam into the rock at each end.
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * (half - 0.25f), 0.27f, 0f), new Vector3(0.04f, 0.03f, 0.34f), StructureMaterial.Iron, 3 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
			else
			{
				// A log on a chock at each end, lashed round its middle.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (half - 0.25f), 0.06f, 0f), new Vector3(0.36f, 0.12f, 0.4f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
				}
				Log(d, new Vector3(-half, 0.27f, 0f), new Vector3(half, 0.27f, 0f), 0.15f, 3, 0f, StructureMaterial.Timber, 10);
				d.Add(Ring(new Vector3(0f, 0.27f, 0f), 0.16f, 0.03f, Quaternion.Euler(0f, 0f, 90f), d.Seg(10, 6), StructureMaterial.Canvas, 4, StructureRole.Detail));
			}
			if (d.Mid)
			{
				d.Add(StructureShapes.Rod(new Vector3(0f, 0.04f, 0f), new Vector3(0f, -0.5f, 0f), 0.019f, d.Seg(6, 4), StructureMaterial.Canvas, 6, StructureRole.Detail));
			}
		}

		// ── Spans ─────────────────────────────────────────────────────

		/// <summary>
		/// Four metres of rope walk along z: planks across two foot ropes, hand ropes a metre up, ties from one to the other.
		/// The coarsest level, the collider, is a continuous walk with the hand ropes either side.
		/// </summary>
		private static void RopeBridgeSpan(StructureDraft d)
		{
			const float walk = 1.3f, length = CaveSpanModule, half = length * 0.5f, rope = 0.035f;
			bool worn = d.Variant == 1;
			float hand = worn ? 0.95f : 1.02f;
			if (d.Lod == 2)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.05f, 0f), new Vector3(walk, 0.1f, length), StructureMaterial.Timber, 1));
			}
			else
			{
				int planks = d.Fine ? 10 : 5;
				float pitch = length / planks, gap = d.Fine ? 0.05f : 0.03f;
				for (int k = 0; k < planks; k++)
				{
					if (worn && d.Fine && k == 6)
					{
						continue;
					}
					float zc = -half + pitch * (k + 0.5f);
					float wide = walk + (d.Fine ? d.Range(k, 3, -0.08f, 0.08f) : 0f);
					float yaw = d.Fine ? d.Range(k, 4, -2.5f, 2.5f) : 0f;
					float shift = d.Fine ? d.Range(k, 5, -0.04f, 0.04f) : 0f;
					d.Add(StructureShapes.Block(new Vector3(shift, -0.03f, zc), new Vector3(wide, 0.05f, pitch - gap), Quaternion.Euler(0f, yaw, 0f), StructureMaterial.Timber, 10 + k))
						.Grain = Vector3.right;
				}
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (walk * 0.5f + 0.02f), hand, 0f), new Vector3(rope, rope, length), StructureMaterial.Canvas, 30 + s));
				if (d.Lod < 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (walk * 0.5f - 0.08f), -0.075f, 0f), new Vector3(rope, rope, length), StructureMaterial.Canvas, 33 + s));
				}
				if (!d.Mid)
				{
					continue;
				}
				// Ties from the hand rope down to the foot rope, a metre apart (at Fine a V to two planks' ends).
				for (int t = 0; t < 4; t++)
				{
					float z = -half + 0.5f + t;
					var top = new Vector3(s * (walk * 0.5f + 0.02f), hand, z);
					if (d.Fine)
					{
						d.Add(StructureShapes.Beam(top, new Vector3(s * (walk * 0.5f - 0.05f), -0.06f, z - 0.2f), 0.018f, 0.018f, StructureMaterial.Canvas, 40 + 10 * (s > 0 ? 1 : 0) + 2 * t, StructureRole.Detail));
						d.Add(StructureShapes.Beam(top, new Vector3(s * (walk * 0.5f - 0.05f), -0.06f, z + 0.2f), 0.018f, 0.018f, StructureMaterial.Canvas, 41 + 10 * (s > 0 ? 1 : 0) + 2 * t, StructureRole.Detail));
					}
					else
					{
						d.Add(StructureShapes.Beam(top, new Vector3(s * (walk * 0.5f - 0.05f), -0.06f, z), 0.02f, 0.02f, StructureMaterial.Canvas, 40 + 10 * (s > 0 ? 1 : 0) + 2 * t, StructureRole.Detail));
					}
				}
			}
		}

		/// <summary>
		/// A rope walk's end: the anchor posts at z = 0 the span's ropes are made fast to (the span runs on toward +z), a
		/// crossbar over the way, a landing of boards back onto the ledge (−z); v0 the posts bedded in a cairn, v1 guyed back
		/// to stakes.
		/// </summary>
		/// <remarks>
		/// The crossbar stands clear over a walker's head (<see cref="RopeBridgeHeadroom"/>): at 1.28 m it barred the way to
		/// every character, whose capsule is 1.6 m tall, and the world sim walker stopped at the posts of all 47 of a cloud
		/// world's bridges (2026-10-15).
		/// </remarks>
		private static void RopeBridgeEnd(StructureDraft d)
		{
			const float walk = 1.3f, post = 0.85f, bar = RopeBridgeHeadroom + 0.1f, top = bar + 0.14f;
			float hand = d.Variant == 1 ? 0.95f : 1.02f;
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(s * post, -1.2f, 0f), new Vector3(s * post, top, 0f), 0.11f, 1 + s, 0f, StructureMaterial.Timber, 8);
				// The hand rope and the foot rope from the span's end round the post.
				d.Add(StructureShapes.Beam(new Vector3(s * (walk * 0.5f + 0.02f), hand, 0.02f), new Vector3(s * post, hand, 0f), 0.035f, 0.035f, StructureMaterial.Canvas, 4 + s));
				d.Add(StructureShapes.Beam(new Vector3(s * (walk * 0.5f - 0.08f), -0.075f, 0.02f), new Vector3(s * post, -0.06f, 0f), 0.035f, 0.035f, StructureMaterial.Canvas, 7 + s));
				if (d.Mid)
				{
					d.Add(Ring(new Vector3(s * post, hand, 0f), 0.125f, 0.035f, Quaternion.identity, d.Seg(10, 6), StructureMaterial.Canvas, 10 + s, StructureRole.Detail));
				}
				if (d.Variant == 0)
				{
					// A cairn of fieldstone about each post's foot.
					for (int i = 0; i < (d.Fine ? 4 : 2); i++)
					{
						float a = i * Mathf.PI * 0.5f + d.Range(i + 10 * (s > 0 ? 1 : 0), 6, -0.3f, 0.3f);
						var at = new Vector3(s * post + Mathf.Cos(a) * 0.28f, -0.12f, Mathf.Sin(a) * 0.28f);
						d.Add(RoughStone(d, at, new Vector3(0.32f, 0.3f, 0.28f), a * Mathf.Rad2Deg, 20 + i + 10 * (s > 0 ? 1 : 0), 2));
					}
				}
				else
				{
					// A guy from the post's head back to a stake driven in behind.
					Log(d, new Vector3(s * (post + 0.15f), -0.5f, -1.65f), new Vector3(s * (post + 0.12f), 0.3f, -1.5f), 0.06f, 40 + s, 0f, StructureMaterial.Timber, 6);
					d.Add(StructureShapes.Beam(new Vector3(s * post, bar, -0.05f), new Vector3(s * (post + 0.12f), 0.25f, -1.5f), 0.03f, 0.03f, StructureMaterial.Canvas, 43 + s));
				}
			}
			Log(d, new Vector3(-post - 0.2f, bar, 0f), new Vector3(post + 0.2f, bar, 0f), 0.08f, 50, 0f, StructureMaterial.Timber, 8);
			// The landing: boards across, from the posts back onto the ledge.
			int boards = d.Fine ? 4 : d.Mid ? 2 : 1;
			for (int b = 0; b < boards; b++)
			{
				float z0 = -1.2f + 1.2f * b / boards, z1 = -1.2f + 1.2f * (b + 1) / boards;
				d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, 0.5f * (z0 + z1)), new Vector3(walk + 0.1f, 0.08f, z1 - z0 - (boards > 1 ? 0.03f : 0f)), StructureMaterial.Timber, 60 + b))
					.Grain = Vector3.right;
			}
		}

		/// <summary>
		/// Four metres of a mine's trestle deck along z: planks across two stringers, a rail each side on posts at the −z end
		/// and the middle (the next span's −z posts stand at this one's +z end); v1 railed on +x only, with a cart track
		/// along the deck on MineTrack's gauge and heights. Its bent (<c>TrestleBent</c>) stands under each joint.
		/// </summary>
		private static void TrestleSpan(StructureDraft d)
		{
			const float width = 2f, length = CaveSpanModule, half = length * 0.5f;
			bool track = d.Variant == 1;
			if (d.Lod == 2)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, 0f), new Vector3(width, 0.08f, length), StructureMaterial.Timber, 1));
			}
			else
			{
				int planks = d.Fine ? 16 : 8;
				float pitch = length / planks;
				for (int k = 0; k < planks; k++)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, -half + pitch * (k + 0.5f)), new Vector3(width + (d.Fine ? d.Range(k, 3, -0.05f, 0.05f) : 0f), 0.08f, pitch - 0.02f),
						StructureMaterial.Timber, 10 + k)).Grain = Vector3.right;
				}
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.65f, -0.2f, 0f), new Vector3(0.18f, 0.24f, length), StructureMaterial.Timber, 30 + s)).Grain = Vector3.forward;
				if (track && s < 0)
				{
					continue;
				}
				float x = s * (width * 0.5f - 0.06f);
				foreach (float z in new[] { -half + 0.08f, 0f })
				{
					d.Add(StructureShapes.Beam(new Vector3(x, -0.32f, z), new Vector3(x, 1.08f, z), 0.12f, 0.12f, StructureMaterial.Timber, 35 + s + (z < 0f ? 0 : 3)));
				}
				d.Add(StructureShapes.Beam(new Vector3(x, 1.02f, -half), new Vector3(x, 1.02f, half), 0.08f, 0.1f, StructureMaterial.Timber, 42 + s));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, 0.52f, -half), new Vector3(x, 0.52f, half), 0.06f, 0.08f, StructureMaterial.Timber, 45 + s, StructureRole.Detail));
				}
				if (d.Fine)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, 0.05f, -half + 0.2f), new Vector3(x, 0.95f, -0.3f), 0.06f, 0.06f, StructureMaterial.Timber, 48 + s, StructureRole.Detail));
				}
			}
			if (track && d.Mid)
			{
				// Sleepers and two iron rails: the rails' tops at MineTrack's height over the deck.
				int sleepers = d.Fine ? 8 : 4;
				for (int i = 0; i < sleepers; i++)
				{
					float z = -half + length * (i + 0.5f) / sleepers;
					d.Add(StructureShapes.Box(new Vector3(-0.2f, 0.04f, z), new Vector3(1f, 0.08f, 0.16f), StructureMaterial.Timber, 60 + i, StructureRole.Detail)).Grain = Vector3.right;
				}
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(-0.2f + s * 0.3f, 0.11f, 0f), new Vector3(0.05f, 0.06f, length), StructureMaterial.Iron, 70 + s, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// A trestle bent across x at z = 0: a cap under the spans' stringers, two legs battered outward a tenth of a metre a
		/// metre down to a sill, cross braces on the faces (one each side, so they never cross through each other); v0 two
		/// tiers with a girt between them. Laid to the floor by scaling y (depth <see cref="TrestleBentDepths"/>).
		/// </summary>
		private static void TrestleBent(StructureDraft d)
		{
			float depth = TrestleBentDepths[d.Variant];
			const float top = 0.85f, capY = -0.43f, legTop = -0.55f;
			float foot = top + 0.1f * depth;
			d.Add(StructureShapes.Box(new Vector3(0f, capY, 0f), new Vector3(2.5f, 0.24f, 0.26f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * top, legTop + 0.02f, 0f), new Vector3(s * foot, -depth + 0.18f, 0f), 0.22f, 0.22f, StructureMaterial.Timber, 2 + s));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, -depth + 0.1f, 0f), new Vector3(2f * foot + 0.6f, 0.2f, 0.3f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
			int tiers = d.Variant == 0 ? 2 : 1;
			float span = (-depth + 0.2f - legTop) / tiers;
			float LegX(float y) => Mathf.Lerp(top, foot, Mathf.InverseLerp(legTop, -depth + 0.2f, y));
			for (int t = 0; t < tiers; t++)
			{
				float y0 = legTop + span * t - 0.1f, y1 = legTop + span * (t + 1) + 0.1f;
				d.Add(StructureShapes.Beam(new Vector3(-LegX(y0), y0, -0.15f), new Vector3(LegX(y1), y1, -0.15f), 0.12f, 0.08f, StructureMaterial.Timber, 10 + 2 * t));
				d.Add(StructureShapes.Beam(new Vector3(LegX(y0), y0, 0.15f), new Vector3(-LegX(y1), y1, 0.15f), 0.12f, 0.08f, StructureMaterial.Timber, 11 + 2 * t));
				if (t > 0)
				{
					float y = legTop + span * t;
					d.Add(StructureShapes.Beam(new Vector3(-LegX(y) - 0.08f, y, -0.15f), new Vector3(LegX(y) + 0.08f, y, -0.15f), 0.1f, 0.16f, StructureMaterial.Timber, 20 + t));
				}
			}
			if (d.Fine)
			{
				// Iron straps binding each leg's head to the cap.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * top, capY - 0.08f, -0.14f), new Vector3(0.08f, 0.4f, 0.02f), StructureMaterial.Iron, 30 + s, StructureRole.Detail));
				}
			}
		}

		// ── Lamps ─────────────────────────────────────────────────────

		/// <summary>
		/// A lantern standing on <paramref name="foot"/>: v-square an iron frame of four posts round a horn pane under a
		/// pyramid cap, or round, a horn cylinder between iron bands under a cone; a ring on top to hang it by. Gives off
		/// <see cref="SceneEmitterKind.Lamp"/> at the pane's middle. Returns the ring's top over the foot.
		/// </summary>
		private static float Lantern(StructureDraft d, Vector3 foot, bool round, int key)
		{
			Matrix4x4 at = Matrix4x4.Translate(foot);
			if (!round)
			{
				d.Add(StructureShapes.Box(foot + new Vector3(0f, 0.0125f, 0f), new Vector3(0.2f, 0.025f, 0.2f), StructureMaterial.Iron, key));
				d.Add(StructureShapes.Box(foot + new Vector3(0f, 0.125f, 0f), new Vector3(0.15f, 0.2f, 0.15f), StructureMaterial.Lamplight, key + 1));
				for (int i = 0; i < 4; i++)
				{
					float x = (i % 2 == 0 ? -1f : 1f) * 0.085f, z = (i < 2 ? -1f : 1f) * 0.085f;
					d.Add(StructureShapes.Beam(foot + new Vector3(x, 0.02f, z), foot + new Vector3(x, 0.245f, z), 0.018f, 0.018f, StructureMaterial.Iron, key + 2 + i));
				}
				d.Add(StructureShapes.Box(foot + new Vector3(0f, 0.235f, 0f), new Vector3(0.2f, 0.02f, 0.2f), StructureMaterial.Iron, key + 6));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.125f, 0.245f), new Vector2(0.03f, 0.33f) }, 4, StructureMaterial.Iron, key + 7, false, Mathf.PI * 0.25f).Transform(at));
			}
			else
			{
				int seg = d.Seg(10, 6);
				d.Add(StructureShapes.Cylinder(0.1f, 0f, 0.025f, seg, StructureMaterial.Iron, key).Transform(at));
				d.Add(StructureShapes.Cylinder(0.075f, 0.025f, 0.225f, seg, StructureMaterial.Lamplight, key + 1).Transform(at));
				if (d.Mid)
				{
					d.Add(StructureShapes.Cylinder(0.084f, 0.1f, 0.12f, seg, StructureMaterial.Iron, key + 2, false, StructureRole.Detail).Transform(at));
					for (int i = 0; i < 3; i++)
					{
						float a = i * Mathf.PI * 2f / 3f;
						var p = new Vector3(Mathf.Cos(a) * 0.079f, 0f, Mathf.Sin(a) * 0.079f);
						d.Add(StructureShapes.Beam(foot + p + Vector3.up * 0.02f, foot + p + Vector3.up * 0.23f, 0.014f, 0.014f, StructureMaterial.Iron, key + 3 + i, StructureRole.Detail));
					}
				}
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.1f, 0.225f), new Vector2(0f, 0.34f) }, seg, StructureMaterial.Iron, key + 7).Transform(at));
			}
			// The ring it hangs by.
			Vector3 ring = foot + new Vector3(0f, 0.365f, 0f);
			if (d.Fine)
			{
				d.Add(Ring(ring, 0.028f, 0.01f, Quaternion.Euler(90f, 0f, 0f), 8, StructureMaterial.Iron, key + 8, StructureRole.Detail));
			}
			else
			{
				d.Add(StructureShapes.Box(ring, new Vector3(0.06f, 0.06f, 0.012f), StructureMaterial.Iron, key + 8, StructureRole.Detail));
			}
			d.Emit(foot + new Vector3(0f, 0.125f, 0f), SceneEmitterKind.Lamp);
			return 0.4f;
		}

		/// <summary>A chain from <paramref name="top"/> straight down to <paramref name="bottom"/>: links at Fine, a rod below it.</summary>
		private static void Chain(StructureDraft d, Vector3 top, Vector3 bottom, int key)
		{
			float length = top.y - bottom.y;
			if (!d.Fine)
			{
				d.Add(StructureShapes.Beam(top, bottom, 0.016f, 0.016f, StructureMaterial.Iron, key));
				return;
			}
			int links = Mathf.Max(1, Mathf.RoundToInt(length / 0.065f));
			float pitch = length / links;
			for (int i = 0; i < links; i++)
			{
				var c = new Vector3(top.x, top.y - pitch * (i + 0.5f), top.z);
				d.Add(StructureShapes.Block(c, new Vector3(0.042f, pitch + 0.012f, 0.012f), Quaternion.Euler(0f, (i % 2) * 90f, 0f), StructureMaterial.Iron, key + i));
			}
		}

		/// <summary>A lantern on a chain from its fixing at the origin: v0 a square lantern on a long chain, v1 a round one on a short chain.</summary>
		private static void HangingLantern(StructureDraft d)
		{
			bool round = d.Variant == 1;
			float drop = HangingLanternDrops[d.Variant];
			// The fixing: a plate and a hook into the roof.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.012f, 0f), new Vector3(0.09f, 0.024f, 0.09f), StructureMaterial.Iron, 1));
			const float lanternHeight = 0.4f;
			Chain(d, new Vector3(0f, 0f, 0f), new Vector3(0f, -drop + lanternHeight - 0.02f, 0f), 10);
			Lantern(d, new Vector3(0f, -drop, 0f), round, 100);
		}

		/// <summary>A lantern on an iron bracket from a wall (back plate at z = 0), held out <see cref="WallLanternReach"/> along −z.</summary>
		private static void WallLantern(StructureDraft d)
		{
			d.Add(StructureShapes.Box(new Vector3(0f, 0f, 0.005f), new Vector3(0.1f, 0.24f, 0.03f), StructureMaterial.Iron, 1));
			if (d.Variant == 0)
			{
				// An arm with a scrolled brace under it, the lantern hung from a hook at its end.
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.07f, -0.01f), new Vector3(0f, 0.07f, -WallLanternReach - 0.04f), 0.024f, 0.024f, StructureMaterial.Iron, 2));
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.09f, -0.01f), new Vector3(0f, 0.06f, -WallLanternReach * 0.7f), 0.018f, 0.018f, StructureMaterial.Iron, 3));
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.07f, -WallLanternReach), new Vector3(0f, -0.01f, -WallLanternReach), 0.012f, 0.012f, StructureMaterial.Iron, 4));
				Lantern(d, new Vector3(0f, -0.4f, -WallLanternReach), false, 100);
				return;
			}
			// A shelf on a bracket, the lantern standing on it.
			d.Add(StructureShapes.Box(new Vector3(0f, -0.06f, -WallLanternReach * 0.5f - 0.02f), new Vector3(0.22f, 0.02f, WallLanternReach * 0.5f + 0.2f), StructureMaterial.Iron, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.11f, -0.01f), new Vector3(0f, -0.07f, -WallLanternReach + 0.04f), 0.02f, 0.02f, StructureMaterial.Iron, 3));
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, -0.01f), new Vector3(0f, -0.07f, -WallLanternReach * 0.75f), 0.02f, 0.02f, StructureMaterial.Iron, 4));
			Lantern(d, new Vector3(0f, -0.05f, -WallLanternReach + 0.1f), true, 100);
		}

		/// <summary>A lantern hung from a stand driven into the ground: v0 a timber post with an arm, v1 an iron crook.</summary>
		private static void LanternStand(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, 0f), new Vector3(0f, 2.2f, 0f), 0.12f, 0.12f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
				d.Add(StructureShapes.Beam(new Vector3(0f, 2.06f, 0.04f), new Vector3(0f, 2.06f, -0.56f), 0.07f, 0.07f, StructureMaterial.Timber, 2));
				d.Add(StructureShapes.Beam(new Vector3(0f, 1.72f, -0.04f), new Vector3(0f, 2.02f, -0.34f), 0.05f, 0.05f, StructureMaterial.Timber, 3, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(0f, 2.03f, -0.5f), new Vector3(0f, 1.97f, -0.5f), 0.012f, 0.012f, StructureMaterial.Iron, 4, StructureRole.Detail));
				Lantern(d, new Vector3(0f, 1.57f, -0.5f), false, 100);
				return;
			}
			// A crook: a rod, then a bend out and down to the hook.
			d.Add(StructureShapes.Rod(new Vector3(0f, -0.35f, 0f), new Vector3(0f, 1.5f, 0f), 0.018f, d.Seg(6, 4), StructureMaterial.Iron, 1));
			var bend = new[] { new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1.6f, -0.08f), new Vector3(0f, 1.6f, -0.2f), new Vector3(0f, 1.5f, -0.27f), new Vector3(0f, 1.44f, -0.27f) };
			for (int i = 0; i + 1 < bend.Length; i++)
			{
				d.Add(StructureShapes.Beam(bend[i], bend[i + 1], 0.024f, 0.024f, StructureMaterial.Iron, 2 + i));
			}
			Lantern(d, new Vector3(0f, 1.04f, -0.27f), true, 100);
		}

		// ── The shaft's collar ────────────────────────────────────────

		/// <summary>
		/// Cribbed timbers round a shaft's square mouth (<see cref="ShaftOpening"/> clear inside, centred on the origin): the
		/// lower course along x set into the ground, the upper along z on it, so they cross at the corners as cribbing does.
		/// v0 a windlass on two A-frames over it, its rope and an iron-bound kibble a metre down the shaft (detail only, so
		/// the collider keeps the shaft open); v1 a guard rail on three sides, open at −z.
		/// </summary>
		private static void ShaftCollar(StructureDraft d)
		{
			const float open = ShaftOpening, t = 0.3f;
			float edge = open * 0.5f + t * 0.5f, run = open + 2f * t + 0.2f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.15f, s * edge), new Vector3(run, t, t), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(s * edge, 0.15f, 0f), new Vector3(t, t, run), StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0))).Grain = Vector3.forward;
			}
			if (d.Fine)
			{
				// Treenails in the upper course.
				for (int i = 0; i < 4; i++)
				{
					float x = (i % 2 == 0 ? -1f : 1f) * edge, z = (i < 2 ? -1f : 1f) * edge;
					d.Add(StructureShapes.Cylinder(0.03f, 0.29f, 0.31f, 6, StructureMaterial.Timber, 5 + i, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(x, 0f, z))));
				}
			}
			if (d.Variant == 0)
			{
				const float frame = 1.78f, drumY = 1.05f;
				for (int s = -1; s <= 1; s += 2)
				{
					for (int e = -1; e <= 1; e += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(s * frame, -0.3f, e * 0.5f), new Vector3(s * frame, drumY + 0.12f, 0f), 0.12f, 0.12f, StructureMaterial.Timber, 10 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0)));
					}
				}
				d.Add(StructureShapes.Cylinder(0.14f, -frame + 0.1f, frame - 0.1f, d.Seg(12, 6), StructureMaterial.Timber, 15, true).Transform(Matrix4x4.TRS(new Vector3(0f, drumY, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				d.Add(StructureShapes.Beam(new Vector3(frame - 0.1f, drumY, 0f), new Vector3(frame + 0.12f, drumY, 0f), 0.05f, 0.05f, StructureMaterial.Iron, 16));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(frame + 0.12f, drumY, 0f), new Vector3(frame + 0.12f, drumY - 0.32f, 0f), 0.04f, 0.04f, StructureMaterial.Iron, 17, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(frame + 0.12f, drumY - 0.32f, 0f), new Vector3(frame + 0.32f, drumY - 0.32f, 0f), 0.04f, 0.04f, StructureMaterial.Timber, 18, StructureRole.Detail));
					// Rope wound on the drum, and down the shaft to the kibble.
					d.Add(StructureShapes.Cylinder(0.16f, -0.25f, 0.25f, d.Seg(12, 6), StructureMaterial.Canvas, 19, true, StructureRole.Detail).Transform(Matrix4x4.TRS(new Vector3(0.2f, drumY, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
					d.Add(StructureShapes.Rod(new Vector3(0.05f, drumY - 0.14f, 0f), new Vector3(0.05f, -0.8f, 0f), 0.02f, d.Seg(6, 4), StructureMaterial.Canvas, 20, StructureRole.Detail));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, -1.25f), new Vector2(0.25f, -0.88f), new Vector2(0.22f, -0.86f), new Vector2(0f, -0.86f) },
						d.Seg(10, 6), StructureMaterial.Timber, 21, true, 0f, false, true, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.05f, 0f, 0f))));
					d.Add(StructureShapes.Beam(new Vector3(-0.2f, -0.86f, 0f), new Vector3(0.05f, -0.8f, 0f), 0.025f, 0.025f, StructureMaterial.Iron, 22, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(0.3f, -0.86f, 0f), new Vector3(0.05f, -0.8f, 0f), 0.025f, 0.025f, StructureMaterial.Iron, 23, StructureRole.Detail));
				}
				return;
			}
			// A guard rail on three sides, open at −z where the ladder comes up.
			float corner = edge + 0.08f;
			var posts = new[] { new Vector2(-corner, -corner), new Vector2(-corner, corner), new Vector2(corner, corner), new Vector2(corner, -corner) };
			for (int i = 0; i < posts.Length; i++)
			{
				d.Add(StructureShapes.Beam(new Vector3(posts[i].x, -0.2f, posts[i].y), new Vector3(posts[i].x, 1.08f, posts[i].y), 0.12f, 0.12f, StructureMaterial.Timber, 30 + i)).Grain = Vector3.up;
			}
			for (int i = 0; i + 1 < posts.Length; i++)
			{
				var a = new Vector3(posts[i].x, 1.0f, posts[i].y);
				var b = new Vector3(posts[i + 1].x, 1.0f, posts[i + 1].y);
				d.Add(StructureShapes.Beam(a, b, 0.08f, 0.1f, StructureMaterial.Timber, 40 + i));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(a + Vector3.down * 0.5f, b + Vector3.down * 0.5f, 0.06f, 0.08f, StructureMaterial.Timber, 45 + i, StructureRole.Detail));
				}
			}
		}

		// ── A miner's things ──────────────────────────────────────────

		/// <summary>
		/// A pick: its haft from <paramref name="foot"/> to <paramref name="head"/>, the double-pointed iron head across the
		/// haft's end, each point curving a little down from it.
		/// </summary>
		private static void Pick(StructureDraft d, Vector3 foot, Vector3 head, Vector3 across, int key)
		{
			d.Add(StructureShapes.Beam(foot, head, 0.035f, 0.035f, StructureMaterial.Timber, key)).Grain = (head - foot).normalized;
			Vector3 along = (head - foot).normalized;
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 tip = head + across * (s * 0.24f) - along * 0.07f;
				d.Add(StructureShapes.Beam(head + along * 0.01f, tip, 0.03f, 0.03f, StructureMaterial.Iron, key + 1 + (s > 0 ? 1 : 0)));
			}
		}

		private static void MiningTools(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// Two picks and a shovel leaning on the wall (z = 0), feet out on the floor.
				Pick(d, new Vector3(-0.4f, 0f, -0.42f), new Vector3(-0.36f, 0.95f, -0.05f), Vector3.right, 1);
				Pick(d, new Vector3(0.05f, 0f, -0.4f), new Vector3(0.1f, 0.92f, -0.05f), new Vector3(0.94f, 0f, 0.34f).normalized, 5);
				var foot = new Vector3(0.45f, 0.02f, -0.42f);
				var top = new Vector3(0.42f, 1.1f, -0.03f);
				d.Add(StructureShapes.Beam(foot + (top - foot) * 0.25f, top, 0.035f, 0.035f, StructureMaterial.Timber, 10)).Grain = (top - foot).normalized;
				Quaternion lean = Quaternion.FromToRotation(Vector3.up, (top - foot).normalized);
				d.Add(StructureShapes.Block(foot + (top - foot) * 0.13f, new Vector3(0.24f, 0.3f, 0.02f), lean, StructureMaterial.Iron, 11));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(top, top + new Vector3(0f, 0.1f, 0f), 0.11f, 0.03f, StructureMaterial.Timber, 12, StructureRole.Detail));
				}
				return;
			}
			// A rack on the wall: a board on two pegs, three tools hung by their heads, a sledge leaning below.
			d.Add(StructureShapes.Box(new Vector3(0f, 1.25f, -0.02f), new Vector3(1.2f, 0.14f, 0.04f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int i = 0; i < 3; i++)
			{
				float x = -0.4f + 0.4f * i;
				d.Add(StructureShapes.Beam(new Vector3(x, 1.22f, -0.03f), new Vector3(x, 1.24f, -0.16f), 0.03f, 0.03f, StructureMaterial.Timber, 2 + i));
				Pick(d, new Vector3(x, 0.5f, -0.1f), new Vector3(x, 1.18f, -0.12f), Vector3.right, 10 + 4 * i);
			}
			// The sledge: a long haft and a square iron head on the floor against the wall.
			d.Add(StructureShapes.Beam(new Vector3(0.62f, 0.06f, -0.3f), new Vector3(0.6f, 0.82f, -0.05f), 0.035f, 0.035f, StructureMaterial.Timber, 30));
			d.Add(StructureShapes.Block(new Vector3(0.62f, 0.07f, -0.33f), new Vector3(0.09f, 0.09f, 0.22f), Quaternion.Euler(-18f, 0f, 0f), StructureMaterial.Iron, 31));
		}

		/// <summary>A hessian sack standing on <paramref name="at"/>, tied at the neck, slumped by <paramref name="slump"/> (1 full).</summary>
		private static StructureSolid Sack(StructureDraft d, Vector3 at, float height, float slump, Quaternion turn, int key)
		{
			float h = height, r = 0.24f + 0.06f * (1f - slump);
			var profile = new[]
			{
				new Vector2(r * 0.7f, 0f), new Vector2(r, h * 0.15f), new Vector2(r * 1.05f, h * 0.45f), new Vector2(r * 0.8f, h * 0.75f),
				new Vector2(0.06f, h * 0.9f), new Vector2(0.075f, h * 0.96f), new Vector2(0f, h),
			};
			StructureSolid s = StructureShapes.Lathe(profile, d.Seg(9, 6), StructureMaterial.Canvas, key, true, d.Rand(key, 7) * 6f);
			return d.Add(s.Transform(Matrix4x4.TRS(at, turn, new Vector3(1f, 1f, Mathf.Lerp(0.85f, 1f, slump)))));
		}

		private static void OreSacks(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				Sack(d, new Vector3(-0.3f, 0f, 0.05f), 0.58f, 0.8f, Quaternion.Euler(0f, 20f, 3f), 1);
				Sack(d, new Vector3(0.22f, 0f, -0.04f), 0.52f, 0.7f, Quaternion.Euler(0f, -35f, -4f), 2);
				// The third lying on its side, its mouth open and the ore spilled from it.
				Sack(d, new Vector3(0.05f, 0.24f, 0.42f), 0.6f, 0.6f, Quaternion.Euler(0f, 0f, 82f), 3);
				if (d.Mid)
				{
					for (int i = 0; i < (d.Fine ? 6 : 3); i++)
					{
						var p = new Vector3(-0.32f - d.Range(i, 1, 0f, 0.3f), 0.05f, 0.3f + d.Range(i, 2, -0.18f, 0.25f));
						d.Add(StructureShapes.Sphere(p, new Vector3(0.07f, 0.055f, 0.065f) * d.Range(i, 3, 0.7f, 1.2f), 5, 3, StructureMaterial.Fieldstone, 10 + i, StructureRole.Detail));
					}
				}
				return;
			}
			// Four on a pallet of boards on two bearers.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.4f, 0.05f, 0f), new Vector3(0.1f, 0.1f, 1.1f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.forward;
			}
			for (int b = 0; b < (d.Mid ? 4 : 1); b++)
			{
				float z = d.Mid ? -0.42f + 0.28f * b : 0f;
				d.Add(StructureShapes.Box(new Vector3(0f, 0.12f, z), new Vector3(1.1f, 0.04f, d.Mid ? 0.24f : 1.1f), StructureMaterial.Timber, 3 + b)).Grain = Vector3.right;
			}
			for (int i = 0; i < 4; i++)
			{
				var at = new Vector3((i % 2 == 0 ? -0.25f : 0.25f), 0.14f, (i < 2 ? -0.24f : 0.26f));
				Sack(d, at, 0.6f, 0.75f, Quaternion.Euler(d.Range(i, 4, -5f, 5f), d.Range(i, 5, 0f, 360f), d.Range(i, 6, -5f, 5f)), 10 + i);
			}
		}

		/// <summary>A bucket of staves standing at <paramref name="at"/>: open, its floor a little above the ground, two hoops, a rope bail.</summary>
		private static void Bucket(StructureDraft d, Vector3 at, int key)
		{
			Matrix4x4 place = Matrix4x4.Translate(at);
			int seg = d.Seg(11, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, 0f), new Vector2(0.17f, 0.32f), new Vector2(0.155f, 0.32f), new Vector2(0.125f, 0.04f) },
				seg, StructureMaterial.Timber, key, true, 0f, false, true).Transform(place));
			if (d.Fine)
			{
				foreach (float y in new[] { 0.06f, 0.26f })
				{
					float r = Mathf.Lerp(0.14f, 0.17f, y / 0.32f) + 0.006f;
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r - 0.012f, y), new Vector2(r, y), new Vector2(r, y + 0.03f), new Vector2(r - 0.012f, y + 0.03f) },
						d.Seg(10, 6), StructureMaterial.Iron, key + (y < 0.1f ? 1 : 2), true, 0f, true, false, StructureRole.Detail).Transform(place));
				}
			}
			if (d.Mid)
			{
				var bail = new[] { new Vector3(-0.17f, 0.3f, 0f), new Vector3(-0.12f, 0.46f, 0f), new Vector3(0.12f, 0.46f, 0f), new Vector3(0.17f, 0.3f, 0f) };
				for (int i = 0; i + 1 < bail.Length; i++)
				{
					d.Add(StructureShapes.Beam(at + bail[i], at + bail[i + 1], 0.018f, 0.018f, StructureMaterial.Canvas, key + 3 + i, StructureRole.Detail));
				}
			}
		}

		private static void WaterBucket(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				Bucket(d, Vector3.zero, 1);
				if (d.Mid)
				{
					// A dipper: a little bowl on a long handle, the handle over the rim.
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0.12f), new Vector2(0.06f, 0.14f), new Vector2(0.07f, 0.2f) }, 8, StructureMaterial.Timber, 10, true)
						.Transform(Matrix4x4.Translate(new Vector3(0.03f, 0f, 0.02f))));
					d.Add(StructureShapes.Beam(new Vector3(0.08f, 0.2f, 0.02f), new Vector3(0.3f, 0.48f, 0.05f), 0.025f, 0.018f, StructureMaterial.Timber, 11, StructureRole.Detail));
				}
				return;
			}
			// Two buckets under a carrying yoke laid across them.
			Bucket(d, new Vector3(-0.42f, 0f, 0f), 1);
			Bucket(d, new Vector3(0.42f, 0f, 0f), 20);
			var yoke = new[] { new Vector3(-0.7f, 0.5f, 0.02f), new Vector3(-0.2f, 0.56f, -0.04f), new Vector3(0.2f, 0.56f, -0.04f), new Vector3(0.7f, 0.5f, 0.02f) };
			for (int i = 0; i + 1 < yoke.Length; i++)
			{
				d.Add(StructureShapes.Beam(yoke[i], yoke[i + 1], 0.08f, 0.05f, StructureMaterial.Timber, 40 + i));
			}
		}

		private static void MineSign(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, 0f), new Vector3(0f, 1.9f, 0f), 0.12f, 0.12f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
				d.Add(StructureShapes.Box(new Vector3(0f, 1.55f, -0.08f), new Vector3(0.8f, 0.46f, 0.04f), StructureMaterial.Timber, 2)).Grain = Vector3.right;
				if (d.Mid)
				{
					// A pick and a hammer crossed, in iron nailed to the board.
					d.Add(StructureShapes.Block(new Vector3(0f, 1.52f, -0.105f), new Vector3(0.46f, 0.045f, 0.012f), Quaternion.Euler(0f, 0f, 38f), StructureMaterial.Iron, 3, StructureRole.Detail));
					d.Add(StructureShapes.Block(new Vector3(0f, 1.52f, -0.105f), new Vector3(0.46f, 0.045f, 0.012f), Quaternion.Euler(0f, 0f, -38f), StructureMaterial.Iron, 4, StructureRole.Detail));
				}
				if (d.Fine)
				{
					// A line of carved runes along the top, raised as carving reads at a distance.
					for (int i = 0; i < 6; i++)
					{
						float x = -0.3f + 0.12f * i;
						d.Add(StructureShapes.Box(new Vector3(x, 1.72f, -0.103f), new Vector3(0.04f, 0.06f + 0.03f * d.Rand(10 + i), 0.008f), StructureMaterial.Ash, 10 + i, StructureRole.Detail));
					}
				}
				return;
			}
			// A warning: a board on two stakes, a red cross daubed on it, a skull on a stake beside it.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.45f, -0.3f, 0f), new Vector3(s * 0.45f, 1.15f, 0f), 0.08f, 0.08f, StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, 0.92f, -0.06f), new Vector3(1.1f, 0.34f, 0.04f), StructureMaterial.Timber, 3)).Grain = Vector3.right;
			if (d.Mid)
			{
				d.Add(StructureShapes.Block(new Vector3(0f, 0.92f, -0.083f), new Vector3(0.36f, 0.06f, 0.008f), Quaternion.Euler(0f, 0f, 30f), StructureMaterial.Dyed, 4, StructureRole.Detail));
				d.Add(StructureShapes.Block(new Vector3(0f, 0.92f, -0.083f), new Vector3(0.36f, 0.06f, 0.008f), Quaternion.Euler(0f, 0f, -30f), StructureMaterial.Dyed, 5, StructureRole.Detail));
			}
			d.Add(StructureShapes.Beam(new Vector3(0.8f, -0.3f, -0.1f), new Vector3(0.8f, 1.3f, -0.1f), 0.05f, 0.05f, StructureMaterial.Timber, 6));
			d.Add(StructureShapes.Sphere(new Vector3(0.8f, 1.38f, -0.1f), new Vector3(0.09f, 0.1f, 0.11f), d.Seg(8, 5), d.Fine ? 5 : 3, StructureMaterial.Bone, 7));
			if (d.Fine)
			{
				d.Add(StructureShapes.Box(new Vector3(0.8f, 1.29f, -0.15f), new Vector3(0.1f, 0.05f, 0.08f), StructureMaterial.Bone, 8, StructureRole.Detail));
			}
		}

		// ── An underground camp ───────────────────────────────────────

		/// <summary>A pot: round-bellied iron on a lid, its foot at <paramref name="at"/>.</summary>
		private static void Pot(StructureDraft d, Vector3 at, float scale, int key)
		{
			var profile = new[] { new Vector2(0f, 0f), new Vector2(0.15f, 0.02f), new Vector2(0.22f, 0.13f), new Vector2(0.21f, 0.26f), new Vector2(0.18f, 0.29f), new Vector2(0.18f, 0.3f), new Vector2(0f, 0.32f) };
			for (int i = 0; i < profile.Length; i++)
			{
				profile[i] *= scale;
			}
			d.Add(StructureShapes.Lathe(profile, d.Seg(12, 6), StructureMaterial.Iron, key).Transform(Matrix4x4.Translate(at)));
		}

		private static void CookingFire(StructureDraft d)
		{
			int stones = d.Variant == 0 ? (d.Fine ? 8 : 5) : 3;
			float ring = d.Variant == 0 ? 0.52f : 0.2f;
			for (int i = 0; i < stones; i++)
			{
				float a = i * Mathf.PI * 2f / stones + (d.Variant == 1 ? 0.5f : 0f);
				Vector3 size = d.Variant == 0 ? new Vector3(0.22f, 0.16f, 0.2f) : new Vector3(0.2f, 0.2f, 0.18f);
				d.Add(RoughStone(d, new Vector3(Mathf.Cos(a) * ring, -0.02f, Mathf.Sin(a) * ring), size, a * Mathf.Rad2Deg, 10 + i, 2));
			}
			d.Add(StructureShapes.Cylinder(d.Variant == 0 ? 0.44f : 0.3f, -0.05f, 0.03f, d.Seg(10, 6), StructureMaterial.Ash, 30, false, StructureRole.Detail));
			if (d.Mid)
			{
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f + 0.4f;
					var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					Log(d, dir * 0.32f + Vector3.up * 0.04f, dir * 0.04f + Vector3.up * 0.1f, 0.04f, 40 + i, 0f, StructureMaterial.Timber, 5, StructureRole.Detail);
				}
			}
			d.Emit(new Vector3(0f, 0.12f, 0f), SceneEmitterKind.Fire);
			if (d.Variant == 1)
			{
				// The pot sits on the three stones.
				Pot(d, new Vector3(0f, 0.17f, 0f), 0.9f, 50);
				return;
			}
			// An iron crane: an upright beside the ring, an arm over the fire, a chain and the cauldron on it.
			d.Add(StructureShapes.Rod(new Vector3(0.78f, -0.3f, 0f), new Vector3(0.78f, 1.25f, 0f), 0.022f, d.Seg(6, 4), StructureMaterial.Iron, 50));
			d.Add(StructureShapes.Beam(new Vector3(0.8f, 1.15f, 0f), new Vector3(0f, 1.15f, 0f), 0.03f, 0.03f, StructureMaterial.Iron, 51));
			d.Add(StructureShapes.Beam(new Vector3(0.78f, 0.85f, 0f), new Vector3(0.4f, 1.13f, 0f), 0.022f, 0.022f, StructureMaterial.Iron, 52, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(0f, 1.15f, 0f), new Vector3(0f, 0.72f, 0f), 0.014f, 0.014f, StructureMaterial.Iron, 53));
			Pot(d, new Vector3(0f, 0.4f, 0f), 1.05f, 54);
		}

		private static void StrawBed(StructureDraft d)
		{
			// The straw heaped a little higher at the head.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.08f, 0f), new Vector3(1.05f, 0.16f, 2.1f), d.Bevel(0.05f), StructureMaterial.Thatch, 1)).Grain = Vector3.forward;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.16f, 0.72f), new Vector3(0.95f, 0.12f, 0.55f), d.Bevel(0.04f), StructureMaterial.Thatch, 2)).Grain = Vector3.forward;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.18f, -0.25f), new Vector3(0.82f, 0.04f, 1.35f), d.Bevel(0.015f), StructureMaterial.Dyed, 3));
				StructureSolid pack = StructureShapes.Cylinder(0.12f, -0.34f, 0.34f, d.Seg(10, 6), StructureMaterial.Canvas, 4);
				d.Add(pack.Transform(Matrix4x4.TRS(new Vector3(0f, 0.32f, 0.78f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
			}
			else
			{
				// A fur thrown back off the sleeper, and a satchel at the head.
				d.Add(StructureShapes.Block(new Vector3(0.05f, 0.19f, -0.45f), new Vector3(0.9f, 0.05f, 1f), Quaternion.Euler(0f, 6f, 0f), StructureMaterial.Canvas, 3));
				d.Add(StructureShapes.ChamferBox(new Vector3(0.05f, 0.22f, 0.35f), new Vector3(0.85f, 0.1f, 0.3f), d.Bevel(0.04f), StructureMaterial.Canvas, 4));
				d.Add(StructureShapes.Block(new Vector3(0.62f, 0.12f, 0.8f), new Vector3(0.3f, 0.24f, 0.12f), Quaternion.Euler(0f, -20f, 0f), StructureMaterial.Timber, 5, StructureRole.Structure, d.Bevel(0.02f)));
			}
			if (d.Fine)
			{
				// Loose straw kicked out from the edges.
				for (int i = 0; i < 5; i++)
				{
					float side = i % 2 == 0 ? -1f : 1f;
					var at = new Vector3(side * d.Range(i, 1, 0.5f, 0.62f), 0.02f, d.Range(i, 2, -0.9f, 0.9f));
					d.Add(StructureShapes.Block(at, new Vector3(0.18f, 0.03f, 0.3f), Quaternion.Euler(0f, d.Range(i, 3, -40f, 40f), side * 8f), StructureMaterial.Thatch, 10 + i, StructureRole.Detail));
				}
			}
		}
	}
}
#endif
