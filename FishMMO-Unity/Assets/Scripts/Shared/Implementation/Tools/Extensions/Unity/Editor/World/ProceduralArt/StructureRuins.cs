#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Turns an intact piece into a ruin of it (Jim, 2026-10-10: ruins are made FROM the intact pieces by cut planes, tilt
	/// and missing chunks, by a decay parameter): roofs fall in, walls break along a ragged line, slices lean, tops lie
	/// where they fell, and rubble gathers round the foot.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The break line.</b> Two broad, tilted planes set the height the ruin stands to across its footprint (lower as
	/// <c>decay</c> rises). Every load-bearing part longer than a couple of metres is sliced across its length first, and
	/// each slice breaks at its own tilt and height about that line, so a wall's top comes out stepped and ragged, not
	/// sawn. Some slices also lose a corner (a missing chunk) or lean off true.
	/// </para>
	/// <para>
	/// <b>Same ruin at every level.</b> Every choice is a hash of the ruin variant's seed and the part's key
	/// (<see cref="StructureDraft.Rand"/>), and the intact piece gives its structural parts the same keys at every level,
	/// so the levels break identically; only detail and the number of rubble stones fall with the level.
	/// </para>
	/// </remarks>
	public static class StructureRuins
	{
		private struct Break
		{
			public Vector3 Normal;
			public float Distance;
			public float HeightAt(float x, float z) => (Distance - Normal.x * x - Normal.z * z) / Normal.y;
		}

		/// <summary>A ruin of <paramref name="intact"/>, its choices drawn from <paramref name="d"/> (the ruin's own draft).</summary>
		public static List<StructureSolid> Ruin(List<StructureSolid> intact, float decay, StructureDraft d)
		{
			decay = Mathf.Clamp01(decay);
			var result = new List<StructureSolid>();
			if (decay <= 0f)
			{
				foreach (StructureSolid s in intact) result.Add(s.Clone());
				return result;
			}
			Bounds all = BoundsOf(intact);
			float height = Mathf.Max(0.5f, all.max.y);
			float standing = Mathf.Max(0.6f, height * (1f - decay * d.Range(1, 1, 0.5f, 0.8f)));
			var breaks = new Break[2];
			for (int i = 0; i < breaks.Length; i++)
			{
				float tilt = d.Range(2, i, 5f, 12f + 25f * decay) * Mathf.Deg2Rad;
				float az = d.Rand(3, i) * 2f * Mathf.PI;
				var n = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(az));
				var at = new Vector3(all.center.x + d.Range(4, i, -0.3f, 0.3f) * all.size.x, standing + d.Range(5, i, -0.15f, 0.15f) * height * decay,
					all.center.z + d.Range(6, i, -0.3f, 0.3f) * all.size.z);
				breaks[i] = new Break { Normal = n, Distance = Vector3.Dot(n, at) };
			}
			float CutAt(Vector3 p) => Mathf.Min(breaks[0].HeightAt(p.x, p.z), breaks[1].HeightAt(p.x, p.z));

			var fallen = new List<StructureSolid>();
			var counts = new int[System.Enum.GetValues(typeof(StructureMaterial)).Length];
			foreach (StructureSolid s in intact)
			{
				if (s.Faces.Count == 0)
				{
					continue;
				}
				switch (s.Role)
				{
					case StructureRole.Rubble:
						result.Add(s.Clone());
						continue;
					case StructureRole.Roof:
						// The first thing to go; a sound ruin may keep a beam or a slab.
						if (decay >= 0.25f || d.Rand(s.Key, 11) < decay * 3f)
						{
							continue;
						}
						break;
					case StructureRole.Detail:
					{
						Vector3 c = s.Centroid;
						if (c.y < CutAt(c) - 0.15f)
						{
							result.Add(s.Clone());
						}
						continue;
					}
				}
				counts[(int)s.Material]++;
				BreakPart(s, decay, height, d, CutAt, result, fallen);
			}

			// Tops that came down: laid on their sides beside the ruin.
			int falls = Mathf.RoundToInt(1f + 3f * decay);
			Vector3 middle = all.center;
			for (int i = 0; i < fallen.Count && falls > 0; i++)
			{
				StructureSolid top = fallen[i];
				if (d.Rand(top.Key, 40) > 0.6f)
				{
					continue;
				}
				falls--;
				Bounds b = top.Bounds;
				if (b.size.y > 2.5f)
				{
					// Only the lower part of a tall top lies whole; the rest is rubble.
					top.Clip(Vector3.up, b.min.y + d.Range(top.Key, 41, 1.5f, 2.5f));
					b = top.Bounds;
				}
				float wide = Mathf.Max(b.size.x, b.size.z);
				if (wide > 2.5f)
				{
					// Nor does a long one: a stretch of wall top breaks up as it falls.
					Vector3 along = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
					float keep = d.Range(top.Key, 46, 1.4f, 2.4f);
					float from = Vector3.Dot(b.min, along) + d.Range(top.Key, 47, 0f, wide - keep);
					top.Clip(-along, -from);
					top.Clip(along, from + keep);
					b = top.Bounds;
				}
				if (top.Faces.Count == 0)
				{
					continue;
				}
				var away = new Vector3(b.center.x - middle.x, 0f, b.center.z - middle.z);
				if (away.sqrMagnitude < 0.01f)
				{
					float a = d.Rand(top.Key, 42) * 2f * Mathf.PI;
					away = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				}
				away = Quaternion.Euler(0f, d.Range(top.Key, 43, -30f, 30f), 0f) * away.normalized;
				Vector3 pivot = b.center;
				Quaternion fall = Quaternion.AngleAxis(d.Range(top.Key, 44, 70f, 95f), Vector3.Cross(Vector3.up, away));
				top.Transform(Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(fall) * Matrix4x4.Translate(-pivot));
				Bounds laid = top.Bounds;
				float reach = b.size.y * 0.5f + d.Range(top.Key, 45, 0.3f, 1.4f);
				Vector3 to = new Vector3(pivot.x, 0f, pivot.z) + away * reach;
				float sink = 0.08f * Mathf.Min(laid.size.x, Mathf.Min(laid.size.y, laid.size.z));
				top.Transform(Matrix4x4.Translate(new Vector3(to.x - laid.center.x, -laid.min.y - sink, to.z - laid.center.z)));
				top.Role = StructureRole.Rubble;
				result.Add(top);
			}

			// Rubble round the foot, in the ruin's most common material.
			int main = 0;
			for (int m = 1; m < counts.Length; m++)
			{
				if (counts[m] > counts[main]) main = m;
			}
			float area = all.size.x * all.size.z;
			int stones = Mathf.Clamp(Mathf.RoundToInt(decay * (4f + area / 3f)), 2, 36);
			int drawn = d.Lod == 0 ? stones : d.Lod == 1 ? Mathf.CeilToInt(stones * 0.6f) : Mathf.CeilToInt(stones * 0.35f);
			for (int i = 0; i < drawn; i++)
			{
				int key = 5000 + i;
				float size = d.Range(key, 1, 0.22f, 0.6f) * (1f + 0.5f * decay);
				var at = new Vector3(all.center.x + d.Range(key, 2, -1f, 1f) * (all.extents.x + 1.2f), 0f, all.center.z + d.Range(key, 3, -1f, 1f) * (all.extents.z + 1.2f));
				var dims = new Vector3(size * d.Range(key, 4, 0.8f, 1.6f), size * d.Range(key, 5, 0.5f, 0.9f), size * d.Range(key, 6, 0.7f, 1.2f));
				StructureSolid stone = StructurePieces.RoughStone(d, Vector3.zero, dims, 0f, key, 2, (StructureMaterial)main, StructureRole.Rubble);
				Quaternion turn = Quaternion.Euler(d.Range(key, 7, -20f, 20f), d.Rand(key, 8) * 360f, d.Range(key, 9, -20f, 20f));
				stone.Transform(Matrix4x4.TRS(at + Vector3.down * (dims.y * 0.3f), turn, Vector3.one));
				result.Add(stone);
			}
			return result;
		}

		/// <summary>Slices a load-bearing part across its length and breaks each slice about the break line.</summary>
		private static void BreakPart(StructureSolid s, float decay, float height, StructureDraft d, System.Func<Vector3, float> cutAt,
			List<StructureSolid> result, List<StructureSolid> fallen)
		{
			Bounds b = s.Bounds;
			if (b.max.y < cutAt(b.center) - 0.3f && b.max.y < cutAt(b.min) - 0.3f && b.max.y < cutAt(b.max) - 0.3f)
			{
				// Wholly below the break: stands as built.
				result.Add(s.Clone());
				return;
			}
			bool alongX = b.size.x >= b.size.z;
			float length = alongX ? b.size.x : b.size.z;
			Vector3 axis = alongX ? Vector3.right : Vector3.forward;
			float start = alongX ? b.min.x : b.min.z;
			int slices = decay > 0.15f ? Mathf.Clamp(Mathf.RoundToInt(length / 2.4f), 1, 4) : 1;
			var cuts = new float[slices + 1];
			cuts[0] = float.NegativeInfinity;
			cuts[slices] = float.PositiveInfinity;
			for (int j = 1; j < slices; j++)
			{
				cuts[j] = start + length * (j + d.Range(s.Key, 20 + j, -0.25f, 0.25f)) / slices;
			}
			for (int j = 0; j < slices; j++)
			{
				int key = 10000 + s.Key * 8 + j;
				StructureSolid piece = s.Clone();
				piece.Key = key;
				if (!float.IsNegativeInfinity(cuts[j]) && !piece.Clip(-axis, -cuts[j]))
				{
					continue;
				}
				if (!float.IsPositiveInfinity(cuts[j + 1]) && !piece.Clip(axis, cuts[j + 1]))
				{
					continue;
				}
				Bounds pb = piece.Bounds;
				float tilt = d.Range(key, 30, 4f, 10f + 30f * decay) * Mathf.Deg2Rad;
				float az = d.Rand(key, 31) * 2f * Mathf.PI;
				var n = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(az));
				float h = cutAt(pb.center) + d.Range(key, 32, -0.5f, 0.5f) * 0.35f * height * decay;
				if (pb.min.y <= 0.3f)
				{
					// A part that stands on the ground keeps a stump.
					h = Mathf.Max(h, Mathf.Max(0.25f, pb.min.y + 0.15f));
				}
				else if (h < pb.min.y + 0.2f)
				{
					// A part held up by what broke away comes down whole: never a sliver left floating where it was.
					AddFallen(piece, fallen);
					continue;
				}
				var at = new Vector3(pb.center.x, h, pb.center.z);
				float dist = Vector3.Dot(n, at);
				StructureSolid top = piece.Clone();
				bool hasTop = top.Clip(-n, -dist);
				if (!piece.Clip(n, dist))
				{
					continue;
				}
				// A missing chunk: a corner of the broken top knocked out.
				if (d.Rand(key, 33) < decay * 0.5f)
				{
					pb = piece.Bounds;
					float side = d.Rand(key, 34) < 0.5f ? -1f : 1f;
					Vector3 outward = axis * side;
					var n2 = (outward + Vector3.up).normalized;
					Vector3 corner = pb.center + outward * (alongX ? pb.extents.x : pb.extents.z);
					corner.y = pb.max.y;
					float bite = d.Range(key, 35, 0.3f, 0.9f) * Mathf.Min(1.2f, pb.size.y * 0.4f);
					StructureSolid bitten = piece.Clone();
					if (bitten.Clip(n2, Vector3.Dot(n2, corner) - bite))
					{
						piece = bitten;
					}
				}
				// Some slices lean off true about their foot.
				if (d.Rand(key, 36) < 0.25f * decay)
				{
					pb = piece.Bounds;
					var foot = new Vector3(pb.center.x, pb.min.y, pb.center.z);
					Quaternion lean = Quaternion.AngleAxis(d.Range(key, 37, -5f, 5f), axis);
					piece.Transform(Matrix4x4.Translate(foot) * Matrix4x4.Rotate(lean) * Matrix4x4.Translate(-foot));
				}
				result.Add(piece);
				if (hasTop)
				{
					AddFallen(top, fallen);
				}
			}
		}

		/// <summary>Keeps a broken-off top for laying down beside the ruin, when it is big enough to read as one.</summary>
		private static void AddFallen(StructureSolid top, List<StructureSolid> fallen)
		{
			Bounds tb = top.Bounds;
			if (Mathf.Max(tb.size.x, Mathf.Max(tb.size.y, tb.size.z)) > 0.35f && Mathf.Min(tb.size.x, Mathf.Min(tb.size.y, tb.size.z)) > 0.1f)
			{
				fallen.Add(top);
			}
		}

		private static Bounds BoundsOf(List<StructureSolid> solids)
		{
			bool any = false;
			var all = new Bounds();
			foreach (StructureSolid s in solids)
			{
				if (s.Faces.Count == 0) continue;
				Bounds b = s.Bounds;
				if (any) all.Encapsulate(b); else all = b;
				any = true;
			}
			return all;
		}
	}
}
#endif
