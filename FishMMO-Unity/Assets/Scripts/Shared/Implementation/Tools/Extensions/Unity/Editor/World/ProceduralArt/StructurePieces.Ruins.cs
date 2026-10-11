#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Ruins (made from the intact pieces by <see cref="StructureRuins"/>), columns, and rubble.</summary>
	public static partial class StructurePieces
	{
		private static void AddRuins(List<StructurePiece> list)
		{
			// "underwater": the stone ruins a sunken site is laid from (algae finish); timber would not have lasted.
			Ruin(list, "RuinedWall", "WallSegment", 2, 3, 0.55f, StructureSize.Medium, "A broken stretch of curtain wall in its rubble.", "ruin", "wall", "underwater");
			Ruin(list, "RuinedStoneHouse", "StoneHouse", 2, 2, 0.6f, StructureSize.Building, "A roofless stone house, its walls broken.", "ruin", "house", "building", "underwater");
			Ruin(list, "RuinedTimberHouse", "TimberHouseSmall", 2, 2, 0.5f, StructureSize.Building, "A fallen-in cottage; pairs well with the charred finish.", "ruin", "house", "building");
			Ruin(list, "CollapsedTower", "WallTower", 1, 2, 0.65f, StructureSize.Building, "A round tower fallen to a ragged stump.", "ruin", "tower", "wall");
			Ruin(list, "RuinedKeep", "Keep", 1, 2, 0.5f, StructureSize.Building, "A keep standing to half its height.", "ruin", "keep", "tower");
			Piece(list, "Column", StructureStyle.Stone, StructureSize.Small, "A standing column; v1 taller and thicker.", ColumnPiece, "column", "sacred", "monument");
			Ruin(list, "BrokenColumn", "Column", 2, 2, 0.55f, StructureSize.Medium, "A column snapped off, its top beside it.", "ruin", "column", "underwater");
			Piece(list, "FallenColumn", StructureStyle.Stone, StructureSize.Medium, "A column fallen in drums beside its stump.", FallenColumn, "ruin", "column", "rubble", "underwater");
			Ruin(list, "RuinedStatue", "Statue", 2, 2, 0.5f, StructureSize.Medium, "A statue broken at the body, its top on the ground.", "ruin", "statue");
			Piece(list, "RubblePile", StructureStyle.Stone, StructureSize.Medium, "A heap of fallen stone; v0 dressed blocks, v1 fieldstone.", RubblePile, "ruin", "rubble", "underwater");
		}

		/// <summary>A ruin piece: variant v is a ruin of the intact piece's variant v % <paramref name="baseVariants"/>, each broken its own way.</summary>
		private static void Ruin(List<StructurePiece> list, string id, string of, int baseVariants, int variants, float decay, StructureSize size, string description, params string[] tags)
		{
			StructurePiece intact = Find(list, of);
			var p = new StructurePiece
			{
				Id = id, Style = intact.Style, Size = size, Description = description, Tags = tags, Variants = variants,
				RuinOf = of, Decay = decay, Anchor = intact.Anchor, ModuleLength = intact.ModuleLength,
			};
			p.Builder = d =>
			{
				List<StructureSolid> whole = BuildSolids(intact, d.Variant % Mathf.Min(baseVariants, intact.Variants), d.Lod, d.GenerationSeed);
				d.AddRange(StructureRuins.Ruin(whole, decay, d));
			};
			list.Add(p);
		}

		private static StructurePiece Find(List<StructurePiece> list, string id)
		{
			foreach (StructurePiece p in list)
			{
				if (p.Id == id) return p;
			}
			throw new System.ArgumentException($"no structure piece '{id}' before its ruin");
		}

		private static void ColumnPiece(StructureDraft d)
		{
			bool tall = d.Variant == 1;
			Column(d, Vector3.zero, tall ? 6.5f : 5f, tall ? 0.42f : 0.35f, StructureMaterial.Monolith, 1);
		}

		private static void FallenColumn(StructureDraft d)
		{
			// The stump, broken at a slant.
			var whole = new StructureDraft(d.Piece, d.Variant, d.Lod, d.GenerationSeed);
			Column(whole, Vector3.zero, 5f, 0.35f, StructureMaterial.Monolith, 1);
			StructureSolid plinth = whole.Solids[0], shaft = whole.Solids[1], abacus = whole.Solids[2];
			d.Add(plinth);
			float stump = d.Range(1, 1, 0.7f, 1.5f);
			var n = Quaternion.Euler(d.Range(1, 2, -14f, 14f), 0f, d.Range(1, 3, -14f, 14f)) * Vector3.up;
			StructureSolid foot = shaft.Clone();
			foot.Clip(n, Vector3.Dot(n, new Vector3(0f, stump, 0f)));
			d.Add(foot);
			// The rest in two or three drums, rolled a little apart along a line.
			int drums = d.Variant == 0 ? 2 : 3;
			float from = stump, to = 4.55f;
			float yaw = d.Rand(2, 1) * 360f;
			Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
			float along = 0.9f;
			for (int i = 0; i < drums; i++)
			{
				float a = Mathf.Lerp(from, to, i / (float)drums), b = Mathf.Lerp(from, to, (i + 1f) / drums);
				StructureSolid drum = shaft.Clone();
				drum.Key = 10 + i;
				if (i > 0) drum.Clip(Vector3.down, -a);
				else drum.Clip(-n, -Vector3.Dot(n, new Vector3(0f, stump, 0f)));
				if (i < drums - 1) drum.Clip(Vector3.up, b);
				float len = b - a;
				// Lay it on its side, axis along the fall line, a gap after the last.
				Quaternion lay = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, d.Range(10 + i, 1, -10f, 10f), 0f);
				Vector3 centre = new Vector3(0f, (a + b) * 0.5f, 0f);
				drum.Transform(Matrix4x4.Rotate(lay) * Matrix4x4.Translate(-centre));
				Bounds lb = drum.Bounds;
				Vector3 place = dir * (along + len * 0.5f) + Quaternion.Euler(0f, 90f, 0f) * dir * d.Range(10 + i, 2, -0.4f, 0.4f);
				drum.Transform(Matrix4x4.Translate(new Vector3(place.x - lb.center.x, -lb.min.y - 0.04f, place.z - lb.center.z)));
				d.Add(drum);
				along += len + d.Range(10 + i, 3, 0.1f, 0.6f);
			}
			// The capital, upside down at the end of the line.
			abacus.Transform(Matrix4x4.Rotate(Quaternion.Euler(180f, d.Rand(20, 1) * 90f, d.Range(20, 2, -8f, 8f))));
			Bounds flipped = abacus.Bounds;
			Vector3 end = dir * (along + 0.6f);
			abacus.Transform(Matrix4x4.Translate(new Vector3(end.x - flipped.center.x, -flipped.min.y - 0.05f, end.z - flipped.center.z)));
			d.Add(abacus);
			// The kit's convention: the origin at the centre of the footprint, so a placer's footprint covers the whole fall
			// line rather than the stump alone.
			bool any = false;
			var bounds = new Bounds();
			foreach (StructureSolid s in d.Solids)
			{
				if (s.Faces.Count == 0) continue;
				if (any) bounds.Encapsulate(s.Bounds); else bounds = s.Bounds;
				any = true;
			}
			TransformSince(d, 0, Matrix4x4.Translate(new Vector3(-bounds.center.x, 0f, -bounds.center.z)));
		}

		private static void RubblePile(StructureDraft d)
		{
			bool dressed = d.Variant == 0;
			int stones = d.Lod == 0 ? 18 : d.Lod == 1 ? 12 : 8;
			for (int i = 0; i < stones; i++)
			{
				float t = (i + 0.5f) / stones;
				float a = i * 2.39996f + d.Range(i, 1, -0.3f, 0.3f);
				float r = 1.6f * Mathf.Sqrt(1f - t);
				float y = 1.1f * t - 0.15f;
				Vector3 size = dressed
					? new Vector3(d.Range(i, 2, 0.5f, 0.8f), d.Range(i, 3, 0.28f, 0.4f), d.Range(i, 4, 0.35f, 0.5f))
					: new Vector3(d.Range(i, 2, 0.35f, 0.7f), d.Range(i, 3, 0.25f, 0.45f), d.Range(i, 4, 0.3f, 0.6f));
				StructureSolid s = dressed
					? StructureShapes.ChamferBox(new Vector3(0f, size.y * 0.5f, 0f), size, d.Bevel(0.02f), StructureMaterial.Stone, 10 + i, StructureRole.Rubble)
					: RoughStone(d, Vector3.zero, size, 0f, 10 + i, 3, StructureMaterial.Fieldstone, StructureRole.Rubble);
				if (dressed && d.Rand(i, 5) < 0.5f)
				{
					// A broken block: one end knocked off.
					var n = new Vector3(1f, d.Range(i, 6, -0.4f, 0.4f), d.Range(i, 7, -0.4f, 0.4f)).normalized;
					s.Clip(n, Vector3.Dot(n, new Vector3(size.x * 0.25f, size.y * 0.5f, 0f)));
				}
				Quaternion turn = Quaternion.Euler(d.Range(i, 8, -25f, 25f), d.Rand(i, 9) * 360f, d.Range(i, 10, -25f, 25f));
				d.Add(s.Transform(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), turn, Vector3.one)));
			}
		}
	}
}
#endif
