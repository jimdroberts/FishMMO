#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Mine pieces (2026-10-11: "Mines should be shafts that you can actually enter"): the timber sets that hold up an
	/// adit's roof, the portal at its mouth, the track down it and an ore cart. Built for a 4 m bore (CaveSystem's mine
	/// form, 1.9–2.3 m radius) and scaled to the bore where they stand (PointOfInterestCaveDressing); across is x, along
	/// the adit z, floor at y = 0.
	/// </summary>
	public static partial class StructurePieces
	{
		/// <summary>A set's posts stand this far either side of the adit's middle, and its cap this high, at scale 1.</summary>
		public const float MineSetHalfWidth = 1.15f, MineSetHeight = 2.3f;

		private static void AddMine(List<StructurePiece> list)
		{
			Piece(list, "MineSet", StructureStyle.Timber, StructureSize.Small,
				"A three-piece timber set holding up an adit's roof: two battered posts and a cap, 2.3 m clear; v1 with lagging boards over the cap.",
				MineSet, "mine");
			Piece(list, "MinePortal", StructureStyle.Timber, StructureSize.Medium,
				"The portal of an adit: v0 heavy timber posts, a doubled header and a hood of boards; v1 the same half boarded up, abandoned; v2 a delving's dressed-stone doorway, its lintel carved with runes, braziers either side.",
				MinePortal, "mine").Variants = 3;
			Piece(list, "Headframe", StructureStyle.Timber, StructureSize.Medium,
				"Over a shaft's square collar: v0 a timber headframe of four raking legs and a beam with a pulley wheel, a windlass at its foot; v1 a plain hand windlass on two posts.",
				Headframe, "mine");
			Piece(list, "OreBin", StructureStyle.Timber, StructureSize.Small,
				"Where the ore is tipped: v0 a timber bin on legs with a chute; v1 a heap of ore against a plank breast-wall.",
				OreBin, "mine", "storage");
			Piece(list, "MineTrack", StructureStyle.Timber, StructureSize.Small, "Three metres of narrow-gauge track: sleepers and two iron rails; v1 with a sleeper missing and a rail kinked.",
				MineTrack, "mine").Collides = false;
			Piece(list, "MineCart", StructureStyle.Timber, StructureSize.Small, "An ore tub on four wheels; v1 tipped on its side, its ore spilled.",
				MineCart, "mine", "storage");
		}

		private static void MineSet(StructureDraft d)
		{
			float w = MineSetHalfWidth, h = MineSetHeight;
			// Posts battered inward a little (they take the wall's push as well as the roof's), set in hitches in the floor.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * (w + 0.08f), -0.1f, 0f), new Vector3(s * w, h, 0f), 0.2f, 0.2f, StructureMaterial.Timber, 1 + s));
			}
			// The cap, notched over the posts and running into the wall either side.
			d.Add(StructureShapes.Beam(new Vector3(-(w + 0.35f), h + 0.11f, 0f), new Vector3(w + 0.35f, h + 0.11f, 0f), 0.24f, 0.22f, StructureMaterial.Timber, 4));
			if (d.Variant == 1 && d.Mid)
			{
				// Lagging: boards laid over the cap along the adit, half to the last set and half to the next.
				int boards = d.Fine ? 7 : 4;
				for (int b = 0; b < boards; b++)
				{
					float x = -w + 2f * w * (b + 0.5f) / boards;
					d.Add(StructureShapes.Box(new Vector3(x, h + 0.25f, 0f), new Vector3(2f * w / boards - 0.03f, 0.05f, 1.6f + 0.2f * d.Rand(10 + b)), StructureMaterial.Timber, 10 + b, StructureRole.Detail))
						.Grain = Vector3.forward;
				}
			}
			if (d.Fine)
			{
				// Wedges driven between cap and post.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * w, h - 0.02f, 0.13f), new Vector3(0.16f, 0.06f, 0.08f), StructureMaterial.Timber, 20 + s, StructureRole.Detail));
				}
			}
		}

		private static void MinePortal(StructureDraft d)
		{
			if (d.Variant == 2)
			{
				StonePortal(d);
				return;
			}
			bool boarded = d.Variant == 1;
			const float w = 1.6f, h = 2.8f;
			// The posts and the doubled header, standing proud of the rock face (+z into the hill, the portal's face at z = 0).
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * w, -0.2f, 0f), new Vector3(s * w, h, 0f), 0.3f, 0.3f, StructureMaterial.Timber, 1 + s));
				// A raking strut from the post's head to the ground in front.
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * (w + 0.05f), h - 0.6f, 0f), new Vector3(s * (w + 0.4f), -0.1f, -1.2f), 0.16f, 0.16f, StructureMaterial.Timber, 4 + s));
				}
			}
			d.Add(StructureShapes.Beam(new Vector3(-(w + 0.6f), h + 0.15f, 0f), new Vector3(w + 0.6f, h + 0.15f, 0f), 0.32f, 0.3f, StructureMaterial.Timber, 8));
			d.Add(StructureShapes.Beam(new Vector3(-(w + 0.45f), h + 0.45f, 0.05f), new Vector3(w + 0.45f, h + 0.45f, 0.05f), 0.28f, 0.26f, StructureMaterial.Timber, 9));
			// A hood of boards over the opening, sloping out to shed what falls off the face.
			var hood = new[] { new Vector2(-(w + 0.5f), 0f), new Vector2(w + 0.5f, 0f), new Vector2(w + 0.5f, 0.06f), new Vector2(-(w + 0.5f), 0.06f) };
			StructureSolid board = StructureShapes.Prism(hood, -0.9f, 0.2f, StructureMaterial.Timber, 10);
			d.Add(board.Transform(Matrix4x4.TRS(new Vector3(0f, h + 0.6f, 0f), Quaternion.Euler(-12f, 0f, 0f), Vector3.one))).Grain = Vector3.right;
			if (boarded)
			{
				// Planks nailed across the lower part of one side (the way in is still open).
				for (int b = 0; b < 3; b++)
				{
					float y = 0.5f + 0.55f * b;
					d.Add(StructureShapes.Block(new Vector3(-w * 0.55f, y, -0.18f), new Vector3(w * 1.05f, 0.22f, 0.05f), Quaternion.Euler(0f, 0f, (b - 1) * 7f),
						StructureMaterial.Timber, 12 + b, StructureRole.Detail)).Grain = Vector3.right;
				}
				if (d.Mid)
				{
					// A lantern hung from a bracket on the right post.
					d.Add(StructureShapes.Beam(new Vector3(w, 2.1f, -0.1f), new Vector3(w, 2.1f, -0.55f), 0.06f, 0.06f, StructureMaterial.Iron, 16, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(w, 1.88f, -0.5f), new Vector3(0.16f, 0.24f, 0.16f), StructureMaterial.Glass, 17, StructureRole.Detail));
					d.Emit(new Vector3(w, 1.88f, -0.5f), SceneEmitterKind.Lamp);
				}
			}
			if (d.Fine)
			{
				// A sign board over the header.
				d.Add(StructureShapes.Box(new Vector3(0f, h + 0.95f, -0.2f), new Vector3(1.4f, 0.36f, 0.05f), StructureMaterial.Timber, 20, StructureRole.Detail)).Grain = Vector3.right;
			}
		}

		/// <summary>
		/// A delving's doorway (dwarven work): dressed-stone jambs on plinths, a lintel with a band of carved runes, a stepped
		/// threshold, and braziers either side burning day and night. Faces −z, the hill behind it at +z.
		/// </summary>
		private static void StonePortal(StructureDraft d)
		{
			const float w = 1.7f, h = 3f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * (w + 0.3f), 0.2f, -0.05f), new Vector3(0.8f, 0.6f, 0.9f), d.Bevel(0.04f), StructureMaterial.Stone, 1 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.ChamferBox(new Vector3(s * (w + 0.3f), (0.5f + h) * 0.5f, 0f), new Vector3(0.6f, h - 0.5f, 0.7f), d.Bevel(0.03f), StructureMaterial.Stone, 3 + (s > 0 ? 1 : 0)));
				if (d.Mid)
				{
					// A fire bowl on a stone post before each jamb.
					Vector3 bowl = new Vector3(s * (w + 1.1f), 0f, -1.1f);
					d.Add(StructureShapes.Box(bowl + new Vector3(0f, 0.45f, 0f), new Vector3(0.3f, 1.1f, 0.3f), StructureMaterial.Stone, 30 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.08f, 0.95f), new Vector2(0.3f, 1.1f), new Vector2(0.34f, 1.2f), new Vector2(0.28f, 1.2f), new Vector2(0.08f, 1.05f) },
						d.Seg(10, 6), StructureMaterial.Iron, 32 + (s > 0 ? 1 : 0), true, 0f, true).Transform(Matrix4x4.Translate(new Vector3(bowl.x, 0f, bowl.z))));
					d.Emit(bowl + new Vector3(0f, 1.18f, 0f), SceneEmitterKind.Fire);
				}
			}
			// The lintel, and the rune band cut along its face (raised blocks, as carving reads at a distance).
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, h + 0.35f, 0f), new Vector3(2f * w + 1.4f, 0.7f, 0.8f), d.Bevel(0.04f), StructureMaterial.Stone, 10));
			if (d.Fine)
			{
				for (int i = 0; i < 9; i++)
				{
					float x = -w + (i + 0.5f) * (2f * w / 9f);
					float tall = 0.16f + 0.12f * d.Rand(40 + i);
					d.Add(StructureShapes.Box(new Vector3(x, h + 0.35f, -0.42f), new Vector3(0.09f, tall, 0.04f), StructureMaterial.Monolith, 40 + i, StructureRole.Detail));
				}
			}
			// The threshold: two broad steps down to the ground before it.
			d.Add(StructureShapes.Box(new Vector3(0f, -0.05f, -0.55f), new Vector3(2f * w, 0.2f, 0.5f), StructureMaterial.Stone, 20));
			d.Add(StructureShapes.Box(new Vector3(0f, -0.18f, -0.95f), new Vector3(2f * w + 0.4f, 0.2f, 0.4f), StructureMaterial.Stone, 21));
		}

		private static void Headframe(StructureDraft d)
		{
			const float collar = 1.6f;
			// The collar: a square curb of squared logs round the shaft's mouth, its middle open.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.12f, s * collar * 0.5f), new Vector3(collar + 0.3f, 0.3f, 0.3f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(s * collar * 0.5f, 0.12f, 0f), new Vector3(0.3f, 0.3f, collar - 0.3f), StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0))).Grain = Vector3.forward;
			}
			if (d.Variant == 1)
			{
				// A hand windlass: two posts, the drum between them, a crank, the rope down the shaft.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * 1.05f, -0.3f, 0f), new Vector3(s * 1.05f, 1.1f, 0f), 0.16f, 0.16f, StructureMaterial.Timber, 6 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Cylinder(0.14f, -0.95f, 0.95f, d.Seg(10, 6), StructureMaterial.Timber, 8, true).Transform(Matrix4x4.TRS(new Vector3(0f, 0.95f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(1.1f, 0.95f, 0f), new Vector3(1.3f, 0.95f, 0f), 0.04f, 0.04f, StructureMaterial.Iron, 9, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(1.3f, 0.95f, 0f), new Vector3(1.3f, 0.65f, 0f), 0.04f, 0.04f, StructureMaterial.Iron, 10, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(0f, 0.85f, 0f), new Vector3(0f, -0.4f, 0f), 0.02f, 0.02f, StructureMaterial.Canvas, 11, StructureRole.Detail));
				}
				return;
			}
			// Four raking legs to a head beam over the shaft, a wheel in it, braces, and the windlass at its foot.
			const float head = 5.2f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * 1.6f, -0.3f, sz * 1.4f), new Vector3(sx * 0.45f, head, sz * 0.35f), 0.2f, 0.2f, StructureMaterial.Timber, 12 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Beam(new Vector3(sx * 1.1f, 2.2f, -1f), new Vector3(sx * 1.1f, 2.2f, 1f), 0.14f, 0.14f, StructureMaterial.Timber, 17 + (sx > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Beam(new Vector3(-0.7f, head, 0f), new Vector3(0.7f, head, 0f), 0.26f, 0.26f, StructureMaterial.Timber, 20)).Grain = Vector3.right;
			d.Add(StructureShapes.Cylinder(0.5f, -0.06f, 0.06f, d.Seg(16, 8), StructureMaterial.Timber, 21, true).Transform(Matrix4x4.TRS(new Vector3(0f, head + 0.45f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			if (d.Mid)
			{
				// The windlass at the foot, a little off the collar, and the rope up over the wheel and down the shaft.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * 0.9f, -0.3f, 2.4f), new Vector3(s * 0.9f, 1.1f, 2.4f), 0.14f, 0.14f, StructureMaterial.Timber, 22 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Cylinder(0.13f, -0.85f, 0.85f, d.Seg(10, 6), StructureMaterial.Timber, 24, true).Transform(Matrix4x4.TRS(new Vector3(0f, 0.95f, 2.4f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.95f, 2.3f), new Vector3(0f, head + 0.9f, 0.05f), 0.025f, 0.025f, StructureMaterial.Canvas, 25, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(0f, head, -0.48f), new Vector3(0f, -0.3f, -0.48f), 0.025f, 0.025f, StructureMaterial.Canvas, 26, StructureRole.Detail));
			}
		}

		private static void OreBin(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				// A breast-wall of planks between two posts, the ore heaped against it.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * 1.4f, -0.3f, 0.5f), new Vector3(s * 1.4f, 1.3f, 0.5f), 0.14f, 0.14f, StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
				}
				for (int b = 0; b < 4; b++)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.15f + b * 0.3f, 0.55f), new Vector3(2.8f, 0.26f, 0.06f), StructureMaterial.Timber, 3 + b)).Grain = Vector3.right;
				}
				d.Add(StructureShapes.Sphere(new Vector3(0f, 0f, 0f), new Vector3(1.3f, 0.75f, 0.7f), d.Seg(12, 6), d.Seg(6, 3), StructureMaterial.Fieldstone, 8));
				return;
			}
			// A bin on four legs, sloping-floored, its chute out of the front over a cart's height.
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * 0.9f, -0.3f, sz * 0.8f), new Vector3(sx * 0.9f, 1.4f, sz * 0.8f), 0.16f, 0.16f, StructureMaterial.Timber, 1 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			var bin = new[] { new Vector2(-1f, 1.2f), new Vector2(1f, 1.2f), new Vector2(1.05f, 2.3f), new Vector2(-1.05f, 2.3f) };
			d.Add(StructureShapes.Prism(bin, -0.9f, 0.9f, StructureMaterial.Timber, 6)).Grain = Vector3.up;
			if (d.Mid)
			{
				d.Add(StructureShapes.Block(new Vector3(0f, 1.15f, -1.25f), new Vector3(0.6f, 0.06f, 0.9f), Quaternion.Euler(-28f, 0f, 0f), StructureMaterial.Timber, 7, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(new Vector3(0f, 2.3f, 0f), new Vector3(0.85f, 0.25f, 0.7f), d.Seg(10, 6), 3, StructureMaterial.Fieldstone, 8, StructureRole.Detail));
			}
		}

		private static void MineTrack(StructureDraft d)
		{
			bool worn = d.Variant == 1;
			const float gauge = 0.6f, length = 3f;
			int sleepers = 5;
			for (int i = 0; i < sleepers; i++)
			{
				if (worn && i == 2)
				{
					continue;
				}
				float z = -length * 0.5f + length * (i + 0.5f) / sleepers;
				d.Add(StructureShapes.Box(new Vector3(0.03f * (d.Rand(i) - 0.5f), 0.04f, z), new Vector3(1.0f, 0.08f, 0.16f), StructureMaterial.Timber, 1 + i)).Grain = Vector3.right;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				float kink = worn && s == 1 ? 0.04f : 0f;
				d.Add(StructureShapes.Beam(new Vector3(s * gauge * 0.5f, 0.11f, -length * 0.5f), new Vector3(s * gauge * 0.5f + kink, 0.11f + kink, length * 0.5f), 0.05f, 0.06f, StructureMaterial.Iron, 10 + s));
			}
		}

		private static void MineCart(StructureDraft d)
		{
			bool tipped = d.Variant == 1;
			var parts = new List<StructureSolid>();
			// The tub: a box wider at the top on a timber frame, four iron wheels on the 0.6 m gauge.
			var tub = new[] { new Vector2(-0.42f, 0.32f), new Vector2(0.42f, 0.32f), new Vector2(0.52f, 1.02f), new Vector2(-0.52f, 1.02f) };
			parts.Add(StructureShapes.Prism(tub, -0.65f, 0.65f, StructureMaterial.Timber, 1));
			parts.Add(StructureShapes.Box(new Vector3(0f, 0.28f, 0f), new Vector3(0.7f, 0.08f, 1.1f), StructureMaterial.Iron, 2));
			int seg = d.Seg(12, 6);
			for (int s = -1; s <= 1; s += 2)
			{
				for (int e = -1; e <= 1; e += 2)
				{
					StructureSolid wheel = StructureShapes.Cylinder(0.17f, -0.03f, 0.03f, seg, StructureMaterial.Iron, 4 + s + 2 * e);
					parts.Add(wheel.Transform(Matrix4x4.TRS(new Vector3(s * 0.3f, 0.17f, e * 0.38f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				}
			}
			if (d.Mid && !tipped)
			{
				// Ore heaped in the tub.
				parts.Add(StructureShapes.Sphere(new Vector3(0f, 0.98f, 0f), new Vector3(0.44f, 0.14f, 0.55f), d.Seg(10, 6), 4, StructureMaterial.Fieldstone, 12, StructureRole.Detail));
			}
			Matrix4x4 lay = tipped ? Matrix4x4.TRS(new Vector3(0.52f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one) : Matrix4x4.identity;
			foreach (StructureSolid part in parts)
			{
				d.Add(tipped ? part.Transform(lay) : part);
			}
			if (tipped && d.Mid)
			{
				// What it held, spilled before its mouth.
				for (int i = 0; i < 4; i++)
				{
					float a = d.Rand(30 + i) * 0.9f - 0.45f;
					d.Add(StructureShapes.Sphere(new Vector3(-0.4f - 0.3f * d.Rand(40 + i), 0.08f, a), new Vector3(0.18f, 0.1f, 0.16f), 6, 3, StructureMaterial.Fieldstone, 20 + i, StructureRole.Detail));
				}
			}
		}
	}
}
#endif
