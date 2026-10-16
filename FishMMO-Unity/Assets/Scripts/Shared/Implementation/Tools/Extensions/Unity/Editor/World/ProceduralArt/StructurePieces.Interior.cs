#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What furnishes the rooms the buildings now have (2026-10-11: every building with a door gets an interior):
	/// beds, tables and stools, chests, shelves, hearths, a bar, pews and a lectern, a sarcophagus, stable stalls.
	/// PointOfInterestInteriors lays them out by room kind. Each faces −z into the room, its back (+z) to the wall it
	/// stands against, on the floor at y = 0; a hearth gives off a fire, a lectern's candle a lamp.
	/// </summary>
	public static partial class StructurePieces
	{
		private static void AddInterior(List<StructurePiece> list)
		{
			Piece(list, "Bed", StructureStyle.Timber, StructureSize.Small, "A bed, 1×2.2 m (a 2 m sleeper's), head to the wall: v0 a straw pallet on a low frame, v1 a framed bed with a headboard and a blanket.",
				Bed, "furniture", "bed");
			Piece(list, "Table", StructureStyle.Timber, StructureSize.Small, "A table: v0 a plank trestle table 1.6×0.8 m, v1 a round table 1.1 m across.",
				Table, "furniture", "table");
			Piece(list, "Stool", StructureStyle.Timber, StructureSize.Small, "A seat: v0 a three-legged stool, v1 a chair with a slat back.",
				Stool, "furniture", "chair").Collides = false;
			Piece(list, "Chest", StructureStyle.Timber, StructureSize.Small, "A chest, 0.9 m: v0 iron-bound, v1 a plain lidded box.",
				Chest, "furniture", "chest");
			Piece(list, "Shelf", StructureStyle.Timber, StructureSize.Small, "Wall shelves, 1.2 m wide and 1.8 high, crocks and jars on them; v1 a dresser with a cupboard below.",
				Shelf, "furniture", "shelf");
			Piece(list, "Hearth", StructureStyle.Stone, StructureSize.Small, "A fireplace against the wall: a stone hearth, cheeks and a hood, a fire in it; v1 a raised hearth with a pot on a crane.",
				Hearth, "furniture", "hearth");
			Piece(list, "BarCounter", StructureStyle.Timber, StructureSize.Small, "A tavern's bar, 3 m, its top on a panelled front; v1 with a rack of mugs on the back.",
				BarCounter, "furniture", "bar");
			Piece(list, "Pew", StructureStyle.Timber, StructureSize.Small, "A church pew, 2.4 m, its back to the −z; v1 a plain backless form.",
				Pew, "furniture", "pew");
			Piece(list, "Lectern", StructureStyle.Timber, StructureSize.Small, "A lectern with a book and a candle; v1 a stone pulpit step.",
				Lectern, "furniture", "lectern");
			Piece(list, "Sarcophagus", StructureStyle.Stone, StructureSize.Small, "A stone sarcophagus, 2.2 m: v0 a plain lid, v1 an effigy on the lid.",
				Sarcophagus, "furniture", "sarcophagus");
			// A dressed block a metre each way, its base at y = 0: scaled to each step of a person's flight up to a big race's
			// door (PointOfInterestDoors), where the building's own steps are too high for a person to climb.
			Piece(list, "StepStone", StructureStyle.Stone, StructureSize.Small, "A stone block, 1 m each way, base at y = 0: a step, scaled; v0 dressed, v1 a rough-hewn fieldstone.",
				Under(StepRoles, d => d.Add(d.Variant == 1
					? StructureShapes.ChamferBox(new Vector3(0f, 0.5f, 0f), Vector3.one, 0.08f, StructureMaterial.Fieldstone, 1)
					: StructureShapes.Box(new Vector3(0f, 0.5f, 0f), Vector3.one, StructureMaterial.Stone, 1))), "step");
			Piece(list, "StallPartition", StructureStyle.Timber, StructureSize.Small, "A stable stall's side, 2.4 m deep, a post at its open end; v1 with a manger at the wall.",
				StallPartition, "furniture", "stall");
		}

		private static void Bed(StructureDraft d)
		{
			bool framed = d.Variant == 1;
			// Head at +z (the wall), the bed running out into the room along −z.
			float h = framed ? 0.45f : 0.25f;
			d.Add(StructureShapes.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(1f, h, 2.2f), StructureMaterial.Timber, 1)).Grain = Vector3.forward;
			d.Add(StructureShapes.Box(new Vector3(0f, h + 0.08f, -0.05f), new Vector3(0.92f, 0.16f, 2.05f), framed ? StructureMaterial.Canvas : StructureMaterial.Thatch, 2));
			if (framed)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.55f, 1.07f), new Vector3(1.04f, 1.1f, 0.07f), StructureMaterial.Timber, 3)).Grain = Vector3.up;
				d.Add(StructureShapes.Box(new Vector3(0f, h + 0.2f, -0.35f), new Vector3(0.96f, 0.08f, 1.2f), StructureMaterial.Dyed, 4, StructureRole.Detail));
			}
			if (d.Mid)
			{
				// The pillow at the head.
				d.Add(StructureShapes.Box(new Vector3(0f, h + 0.21f, 0.82f), new Vector3(0.6f, 0.12f, 0.3f), StructureMaterial.Canvas, 5, StructureRole.Detail));
			}
		}

		private static void Table(StructureDraft d)
		{
			const float top = 0.76f;
			if (d.Variant == 1)
			{
				int seg = d.Seg(14, 8);
				d.Add(StructureShapes.Cylinder(0.55f, top - 0.05f, top, seg, StructureMaterial.Timber, 1));
				d.Add(StructureShapes.Cylinder(0.08f, 0f, top - 0.05f, d.Seg(8, 5), StructureMaterial.Timber, 2, true));
				d.Add(StructureShapes.Cylinder(0.3f, 0f, 0.06f, seg, StructureMaterial.Timber, 3));
			}
			else
			{
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.03f, 0f), new Vector3(1.6f, 0.06f, 0.8f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int s = -1; s <= 1; s += 2)
				{
					// Trestle legs: a splayed pair at each end and a foot.
					d.Add(StructureShapes.Box(new Vector3(s * 0.62f, (top - 0.06f) * 0.5f, 0f), new Vector3(0.08f, top - 0.06f, 0.12f), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Box(new Vector3(s * 0.62f, 0.04f, 0f), new Vector3(0.1f, 0.08f, 0.7f), StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0)));
				}
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.24f, 0.07f, 0.07f), StructureMaterial.Timber, 6, StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				// A bowl and a jug on it.
				d.Add(StructureShapes.Cylinder(0.11f, top, top + 0.06f, 8, StructureMaterial.Earth, 10, false, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.06f, top, top + 0.2f, 7, StructureMaterial.Earth, 11, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.3f, 0f, 0.1f))));
			}
		}

		private static void Stool(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.44f, 0.05f, 0.42f), StructureMaterial.Timber, 1));
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						float top = sz > 0 ? 0.95f : 0.43f;
						d.Add(StructureShapes.Box(new Vector3(sx * 0.18f, top * 0.5f, sz * 0.17f), new Vector3(0.05f, top, 0.05f), StructureMaterial.Timber, 2 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.78f, 0.17f), new Vector3(0.36f, 0.22f, 0.03f), StructureMaterial.Timber, 6, StructureRole.Detail));
				return;
			}
			int seg = d.Seg(10, 6);
			d.Add(StructureShapes.Cylinder(0.18f, 0.4f, 0.45f, seg, StructureMaterial.Timber, 1));
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				var foot = new Vector3(Mathf.Cos(a) * 0.2f, 0f, Mathf.Sin(a) * 0.2f);
				var head = new Vector3(Mathf.Cos(a) * 0.1f, 0.41f, Mathf.Sin(a) * 0.1f);
				d.Add(StructureShapes.Beam(foot, head, 0.04f, 0.04f, StructureMaterial.Timber, 2 + i));
			}
		}

		private static void Chest(StructureDraft d)
		{
			d.Add(StructureShapes.Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.5f, 0.5f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.53f, 0f), new Vector3(0.94f, 0.06f, 0.54f), StructureMaterial.Timber, 2)).Grain = Vector3.right;
			if (d.Variant == 0 && d.Mid)
			{
				foreach (float x in new[] { -0.3f, 0.3f })
				{
					d.Add(StructureShapes.Box(new Vector3(x, 0.28f, 0f), new Vector3(0.05f, 0.58f, 0.56f), StructureMaterial.Iron, 3 + (x > 0f ? 1 : 0), StructureRole.Detail));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.42f, -0.27f), new Vector3(0.08f, 0.1f, 0.02f), StructureMaterial.Iron, 5, StructureRole.Detail));
			}
		}

		private static void Shelf(StructureDraft d)
		{
			const float width = 1.2f, depth = 0.35f, height = 1.8f;
			bool dresser = d.Variant == 1;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.03f), height * 0.5f, 0f), new Vector3(0.05f, height, depth), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
			}
			d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, depth * 0.5f - 0.015f), new Vector3(width, height, 0.03f), StructureMaterial.Timber, 3)).Grain = Vector3.up;
			if (dresser)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.45f, -0.06f), new Vector3(width - 0.06f, 0.9f, depth + 0.12f), StructureMaterial.Timber, 4)).Grain = Vector3.right;
			}
			int boards = dresser ? 3 : 4;
			for (int i = 0; i < boards; i++)
			{
				float y = dresser ? 1.2f + i * 0.3f : 0.15f + i * 0.5f;
				d.Add(StructureShapes.Box(new Vector3(0f, y, 0f), new Vector3(width - 0.06f, 0.03f, depth), StructureMaterial.Timber, 10 + i)).Grain = Vector3.right;
				if (!d.Mid)
				{
					continue;
				}
				// Crocks and jars along the board.
				int pots = d.Fine ? 3 : 2;
				for (int p = 0; p < pots; p++)
				{
					float x = -width * 0.35f + p * (width * 0.7f / Mathf.Max(1, pots - 1)) + d.Range(20 + i * 4 + p, 1, -0.06f, 0.06f);
					float r = d.Range(20 + i * 4 + p, 2, 0.06f, 0.1f), tall = d.Range(20 + i * 4 + p, 3, 0.12f, 0.24f);
					d.Add(StructureShapes.Cylinder(r, y + 0.015f, y + 0.015f + tall, 7, StructureMaterial.Earth, 20 + i * 4 + p, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(x, 0f, 0f))));
				}
			}
		}

		private static void Hearth(StructureDraft d)
		{
			bool raised = d.Variant == 1;
			float bed = raised ? 0.4f : 0.15f;
			// The hearthstone and the back and cheeks round the fire, a hood above.
			d.Add(StructureShapes.Box(new Vector3(0f, bed * 0.5f, -0.05f), new Vector3(1.6f, bed, 0.9f), StructureMaterial.Fieldstone, 1));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.9f, 0.3f), new Vector3(1.6f, 1.8f, 0.2f), StructureMaterial.Fieldstone, 2));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.7f, 0.65f, 0f), new Vector3(0.2f, 1.3f, 0.5f), StructureMaterial.Fieldstone, 3 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, 1.5f, 0.02f), new Vector3(1.6f, 0.4f, 0.65f), StructureMaterial.Fieldstone, 5));
			d.Add(StructureShapes.Box(new Vector3(0f, bed + 0.02f, 0f), new Vector3(1f, 0.04f, 0.4f), StructureMaterial.Ash, 6, StructureRole.Detail));
			if (d.Mid)
			{
				// Two logs crossed on the fire.
				Log(d, new Vector3(-0.35f, bed + 0.08f, 0.08f), new Vector3(0.3f, bed + 0.08f, -0.12f), 0.07f, 7, 0f, StructureMaterial.Timber, 6);
				Log(d, new Vector3(-0.3f, bed + 0.12f, -0.12f), new Vector3(0.35f, bed + 0.12f, 0.1f), 0.06f, 8, 0f, StructureMaterial.Timber, 6);
			}
			if (raised && d.Mid)
			{
				// A pot on a crane.
				d.Add(StructureShapes.Beam(new Vector3(-0.55f, bed, 0.15f), new Vector3(-0.55f, 1.2f, 0.15f), 0.04f, 0.04f, StructureMaterial.Iron, 9, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-0.55f, 1.15f, 0.15f), new Vector3(0f, 1.15f, 0.0f), 0.03f, 0.03f, StructureMaterial.Iron, 10, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.16f, bed + 0.35f, bed + 0.6f, 8, StructureMaterial.Iron, 11, false, StructureRole.Detail));
			}
			d.Emit(new Vector3(0f, bed + 0.1f, 0f), SceneEmitterKind.Hearth);
		}

		private static void BarCounter(StructureDraft d)
		{
			const float length = 3f, height = 1.1f, depth = 0.7f;
			d.Add(StructureShapes.Box(new Vector3(0f, (height - 0.06f) * 0.5f, 0.05f), new Vector3(length - 0.1f, height - 0.06f, depth - 0.15f), StructureMaterial.Timber, 1)).Grain = Vector3.up;
			d.Add(StructureShapes.Box(new Vector3(0f, height - 0.03f, 0f), new Vector3(length, 0.06f, depth), StructureMaterial.Timber, 2)).Grain = Vector3.right;
			if (d.Mid)
			{
				// Panels on the front (−z) and a foot rail.
				for (int i = 0; i < 4; i++)
				{
					float x = -length * 0.5f + 0.4f + i * ((length - 0.8f) / 3f);
					d.Add(StructureShapes.Box(new Vector3(x, height * 0.5f, -depth * 0.5f + 0.06f), new Vector3(0.06f, height - 0.2f, 0.03f), StructureMaterial.Timber, 10 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Beam(new Vector3(-length * 0.5f + 0.1f, 0.18f, -depth * 0.5f - 0.04f), new Vector3(length * 0.5f - 0.1f, 0.18f, -depth * 0.5f - 0.04f), 0.04f, 0.04f,
					StructureMaterial.Iron, 14, StructureRole.Detail));
			}
			if (d.Variant == 1 && d.Fine)
			{
				// Mugs along the top.
				for (int i = 0; i < 5; i++)
				{
					d.Add(StructureShapes.Cylinder(0.045f, height, height + 0.11f, 7, StructureMaterial.Earth, 20 + i, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(-1f + i * 0.45f + d.Range(20 + i, 1, -0.05f, 0.05f), 0f, d.Range(20 + i, 2, -0.15f, 0.15f)))));
				}
			}
		}

		private static void Pew(StructureDraft d)
		{
			const float length = 2.4f;
			bool form = d.Variant == 1;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.44f, 0f), new Vector3(length, 0.05f, 0.4f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				float h = form ? 0.42f : 0.95f;
				d.Add(StructureShapes.Box(new Vector3(s * (length * 0.5f - 0.04f), h * 0.5f, form ? 0f : 0.04f), new Vector3(0.06f, h, form ? 0.36f : 0.5f), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
			}
			if (!form)
			{
				// The back, behind the sitter (+z), and the book ledge on it for the pew behind.
				d.Add(StructureShapes.Box(new Vector3(0f, 0.72f, 0.22f), new Vector3(length - 0.1f, 0.5f, 0.04f), StructureMaterial.Timber, 4)).Grain = Vector3.right;
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.8f, 0.3f), new Vector3(length - 0.12f, 0.03f, 0.14f), StructureMaterial.Timber, 5, StructureRole.Detail));
				}
			}
		}

		private static void Lectern(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.2f, 0f), new Vector3(1.2f, 0.4f, 0.9f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.95f, 0.25f), new Vector3(0.9f, 1.1f, 0.2f), StructureMaterial.Stone, 2));
			}
			else
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f), StructureMaterial.Timber, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.55f, 0f), new Vector3(0.14f, 1f, 0.14f), StructureMaterial.Timber, 2)).Grain = Vector3.up;
				d.Add(StructureShapes.Block(new Vector3(0f, 1.1f, 0f), new Vector3(0.55f, 0.04f, 0.42f), Quaternion.Euler(-25f, 0f, 0f), StructureMaterial.Timber, 3));
				if (d.Mid)
				{
					d.Add(StructureShapes.Block(new Vector3(0f, 1.14f, -0.01f), new Vector3(0.4f, 0.05f, 0.3f), Quaternion.Euler(-25f, 0f, 0f), StructureMaterial.Dyed, 4, StructureRole.Detail));
				}
			}
			// A candle (the church's light is the room's, at its middle: PointOfInterestInteriors).
			float cy = d.Variant == 1 ? 1.55f : 1.18f;
			d.Add(StructureShapes.Cylinder(0.025f, cy, cy + 0.15f, 6, StructureMaterial.Canvas, 5, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.24f, 0f, 0.1f))));
		}

		private static void Sarcophagus(StructureDraft d)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.08f, 0f), new Vector3(1.05f, 0.16f, 2.35f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 0.7f, 2.2f), d.Bevel(0.02f), StructureMaterial.Monolith, 2));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.9f, 0f), new Vector3(1f, 0.12f, 2.3f), d.Bevel(0.02f), StructureMaterial.Monolith, 3));
			if (d.Variant == 1 && d.Mid)
			{
				// The effigy: a figure lying with its hands joined, its feet to −z.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.05f, 0.05f), new Vector3(0.45f, 0.18f, 1.6f), d.Bevel(0.03f), StructureMaterial.Monolith, 4, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(new Vector3(0f, 1.08f, 0.88f), new Vector3(0.13f, 0.12f, 0.14f), d.Seg(8, 5), 4, StructureMaterial.Monolith, 5, StructureRole.Detail));
			}
		}

		private static void StallPartition(StructureDraft d)
		{
			// A plank wall running out from the back wall (+z) along −z, a post at its open end.
			const float depth = 2.4f, height = 1.3f;
			d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(0.06f, height, depth), StructureMaterial.Timber, 1)).Grain = Vector3.forward;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.85f, -depth * 0.5f), new Vector3(0.14f, 1.7f, 0.14f), StructureMaterial.Timber, 2)).Grain = Vector3.up;
			if (d.Variant == 1)
			{
				// A manger against the wall beside it.
				d.Add(StructureShapes.Box(new Vector3(0.6f, 0.75f, depth * 0.5f - 0.25f), new Vector3(1f, 0.35f, 0.45f), StructureMaterial.Timber, 3));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0.6f, 0.94f, depth * 0.5f - 0.25f), new Vector3(0.9f, 0.04f, 0.35f), StructureMaterial.Thatch, 4, StructureRole.Detail));
				}
			}
		}
	}
}
#endif
