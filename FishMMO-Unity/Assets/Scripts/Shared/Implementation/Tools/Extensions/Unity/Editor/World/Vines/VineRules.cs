#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One vine piece to lay: which, where, and how it is turned and sized.</summary>
	/// <remarks>
	/// Turned by <see cref="Yaw"/> about +y (the piece's +z, the way it faces, along that heading) and leant back by
	/// <see cref="Tilt"/> (its top away from +z), so a mat follows a face that leans back and a curtain one that slopes
	/// under its edge: the caller makes the rotation (<see cref="VinePlacement.RotationOf"/>; kept out of here so the rules
	/// run without the engine).
	/// </remarks>
	public struct VineSpot
	{
		public string Species;
		public VineForm Form;
		public string Size;
		public int Variant;
		public Vector3 Position;
		public float Yaw;
		public float Tilt;
		public Vector3 Scale;
		/// <summary>Laid on the ground: its height is the ground's and it is turned to the ground's slope where it is laid.</summary>
		public bool OnGround;

		/// <summary>The prefab's name under the generated prefabs folder.</summary>
		public string Prefab
		{
			get
			{
				ProceduralArtCatalogue.TryVinePiece(Form, Size, out VinePiece piece);
				return ProceduralArtCatalogue.VinePrefab(ProceduralArtCatalogue.VineName(Species, in piece, Variant));
			}
		}
	}

	/// <summary>
	/// Where vines grow, as pure rules (no engine, no assets): over a cave's mouth and the rock round it, on the faces of a
	/// ruin's walls, off a crag's edge, on the wet walls beside a waterfall, up a tree. <see cref="VinePlacement"/> lays what
	/// these choose.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Realism first</b> (2026-10-12: "Realism is the goal"). Vines grow where a vine can: up from the ground, over
	/// edges and down from them, on faces that stand up (a mat on a slope is ground cover, a spill), never across a doorway
	/// or a window, never floating in front of a face that leans back. Lengths, widths and which piece are each drawn from
	/// what is there (a curtain as long as the drop under it, a mat as tall as the clear face), then varied, so no two
	/// mouths or walls carry the same.
	/// </para>
	/// <para>
	/// <b>Deterministic.</b> Every draw comes from the seeded generator handed in, or from a hash of where the vine grows, so
	/// a re-cut grows the same vines.
	/// </para>
	/// </remarks>
	public static class VineRules
	{
		/// <summary>Clear headroom kept under a curtain over a cave's way in when it does not close the opening, metres.</summary>
		public const float Headroom = 2.15f;

		/// <summary>The share of a damp biome's natural caves whose whole opening is curtained (2026-10-12: "occasionally curtain cave entrances"), at a cave density of 1.</summary>
		public const float CurtainedShare = 0.28f;

		/// <summary>A 0..1 hash of a place and a salt: the same answer for the same place on every run.</summary>
		public static float Hash(Vector3 at, int salt)
		{
			uint h = ProceduralNoise.Hash(Mathf.RoundToInt(at.x * 8f), Mathf.RoundToInt(at.z * 8f), Mathf.RoundToInt(at.y * 8f), salt);
			return ProceduralNoise.ToUnit(h);
		}

		/// <summary>The heading (degrees about +y) that turns a piece's +z along a horizontal direction.</summary>
		public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

		private static Vector3 Flat(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
		}

		// ── A cave's mouth ──────────────────────────────────────

		/// <summary>What <see cref="CaveMouth"/> grew, for the notes.</summary>
		public struct CaveGrowth
		{
			public string Species;
			public bool Curtained, Parted;
			public int Lintel, Sides, Over, Around, Spill;
		}

		/// <summary>
		/// The vines at one cave's mouth (world): curtains hung along the lintel from <paramref name="left"/> to
		/// <paramref name="right"/> (the top corners of the opening, where its roof meets the face), longer at its sides and
		/// down both jambs to the floor, growth over the lintel and on the rock round the opening, and spill on the ground at
		/// the jambs' feet; now and then a curtain across the whole opening, parted or thinner where the way in is trodden.
		/// </summary>
		/// <param name="outward">Out of the cave, horizontal.</param>
		/// <param name="openingHeight">The opening's height under the middle of the lintel, metres.</param>
		/// <param name="solidAt">
		/// Whether a world point is inside rock (the cave's outcrop and shell, or under the ground); null when only the ground is
		/// known. What the growth over and round the opening is fitted to: without it only the lintel, the jambs and the ground
		/// are grown.
		/// </param>
		/// <param name="groundAt">The ground's height at (east, north).</param>
		public static List<VineSpot> CaveMouth(Vector3 left, Vector3 right, Vector3 outward, float openingHeight, VineBiomes.Habitat habitat,
			DeterministicRNG rng, Func<Vector3, bool> solidAt, Func<float, float, float> groundAt, out CaveGrowth growth)
		{
			var spots = new List<VineSpot>();
			growth = default;
			if (habitat == null || habitat.Caves <= 0f || rng == null)
			{
				return spots;
			}
			Vector3 o = Flat(outward);
			Vector3 span = right - left;
			span.y = 0f;
			float width = span.magnitude;
			if (width < 0.5f)
			{
				// A point given for a lintel: across the mouth at right angles to its way out.
				span = new Vector3(o.z, 0f, -o.x);
				width = 0.5f;
			}
			Vector3 a = span.normalized;
			Vector3 mid = (left + right) * 0.5f;
			float floorY = mid.y - Mathf.Max(0.5f, openingHeight);
			string species = habitat.Pick(rng.NextFloat());
			float lush = Mathf.Clamp01(habitat.Caves * rng.Range(0.7f, 1.15f));
			float yaw = YawOf(o);
			growth.Species = species;

			// The whole opening hung: a damp biome's cave now and then ("occasionally curtain cave entrances").
			growth.Curtained = rng.NextFloat() < CurtainedShare * habitat.Caves;
			growth.Parted = growth.Curtained && rng.NextFloat() < 0.55f;

			// ── Along the lintel ──
			int pieces = Mathf.Max(1, Mathf.CeilToInt(width / 2.1f));
			for (int i = 0; i < pieces; i++)
			{
				float t = (i + 0.5f) / pieces;
				Vector3 top = Vector3.Lerp(left, right, t) + o * 0.06f;
				float s = (t - 0.5f) * width;
				float edge = Mathf.Abs(s) / Mathf.Max(0.5f, width * 0.5f);
				float drop = top.y - floorY;
				float hang;
				if (growth.Curtained)
				{
					// Nearly to the floor, ragged; where the way in is, parted to a short fringe or thinned to hang higher.
					hang = drop - rng.Range(0.05f, 0.5f);
					if (edge < 0.36f)
					{
						hang = growth.Parted ? Mathf.Min(hang, rng.Range(0.6f, 1.3f)) : drop - rng.Range(1.3f, 1.9f);
					}
				}
				else
				{
					// Clear headroom under the middle; lower toward the sides, where nobody walks.
					hang = drop - Headroom + Mathf.Max(0f, edge - 0.4f) * drop * 0.9f;
				}
				float pieceWidth = width / pieces * rng.Range(1.1f, 1.3f);
				spots.Add(Curtain(species, top, yaw, hang, pieceWidth, rng));
				growth.Lintel++;
			}

			// ── Down both jambs, outside the opening ──
			for (int side = -1; side <= 1; side += 2)
			{
				if (rng.NextFloat() > 0.45f + 0.55f * lush)
				{
					continue;
				}
				Vector3 corner = side < 0 ? left : right;
				Vector3 top = corner + a * (side * rng.Range(0.2f, 0.5f)) + o * 0.08f;
				float hang = top.y - floorY + rng.Range(0f, 0.25f);
				spots.Add(Curtain(species, top, yaw, hang, rng.Range(1.4f, 2.2f), rng));
				growth.Sides++;
			}

			// ── Over the lintel: on the face above it, leant as that face leans ──
			if (solidAt != null && TryFace(solidAt, mid + Vector3.up * 0.4f, o, a, 3.2f, out Vector3 foot, out float faceTilt, out float faceHeight))
			{
				float matWidth = Mathf.Clamp(width / 2.2f, 0.7f, 1.6f);
				spots.Add(new VineSpot
				{
					Species = species, Form = VineForm.Mat, Size = width > 3.4f ? "Wall" : "Mat", Variant = rng.Range(0, 2),
					Position = foot + o * 0.03f + Vector3.down * 0.45f, Yaw = yaw, Tilt = faceTilt,
					Scale = new Vector3(width > 3.4f ? matWidth * 2.2f / 3.6f : matWidth, Mathf.Clamp(faceHeight / 2.4f, 0.5f, 1.2f) * rng.Range(0.85f, 1.1f), 1f),
				});
				growth.Over++;
			}
			else if (groundAt != null)
			{
				// The hill over the mouth: growth spilling down over the lintel from the ground above it.
				Vector3 above = mid - o * rng.Range(0.9f, 1.6f);
				float g = groundAt(above.x, above.z);
				if (g > mid.y + 0.6f && g < mid.y + 6f)
				{
					spots.Add(new VineSpot
					{
						Species = species, Form = VineForm.Spill, Size = width > 3f ? "Large" : "Small", Variant = rng.Range(0, 2),
						Position = new Vector3(above.x, g, above.z), Yaw = yaw, Scale = Vector3.one * rng.Range(0.85f, 1.15f), OnGround = true,
					});
					growth.Over++;
				}
			}

			// ── Round the opening: up the rock either side and over the outcrop's faces ──
			if (solidAt != null)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					for (float u = width * 0.5f + rng.Range(0.4f, 1f); u < width * 0.5f + 5.5f; u += rng.Range(1.3f, 2.2f))
					{
						if (rng.NextFloat() > 0.25f + 0.6f * lush)
						{
							continue;
						}
						Vector3 at = mid + a * (side * u);
						// From just over the ground in front of the face (the hillside may rise beside the opening).
						at.y = floorY + 0.3f;
						if (groundAt != null)
						{
							Vector3 front = at + o * 1.5f;
							at.y = Mathf.Max(at.y, groundAt(front.x, front.z) + 0.2f);
						}
						if (!TryFace(solidAt, at, o, a, 4f, out Vector3 faceFoot, out float tilt, out float height) || height < 0.9f)
						{
							continue;
						}
						bool big = height > 2.2f && rng.NextFloat() < 0.6f;
						spots.Add(new VineSpot
						{
							Species = species, Form = VineForm.Mat, Size = big ? "Mat" : "Patch", Variant = rng.Range(0, 2),
							Position = faceFoot + o * 0.03f + Vector3.down * 0.15f, Yaw = yaw + rng.Range(-8f, 8f), Tilt = tilt,
							Scale = new Vector3(rng.Range(0.8f, 1.15f), Mathf.Clamp(height / (big ? 2.4f : 1.2f), 0.6f, 1.15f), 1f),
						});
						growth.Around++;
					}
				}
			}

			// ── On the ground: spilling from the jambs' feet, fanned away from the way in ──
			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 corner = side < 0 ? left : right;
				Vector3 at = corner + a * (side * rng.Range(0.1f, 0.5f)) + o * rng.Range(0.2f, 0.6f);
				spots.Add(new VineSpot
				{
					Species = species, Form = VineForm.Spill, Size = rng.NextFloat() < 0.5f * lush + 0.2f ? "Large" : "Small", Variant = rng.Range(0, 2),
					Position = at, Yaw = yaw + side * rng.Range(25f, 60f), Scale = Vector3.one * rng.Range(0.8f, 1.15f), OnGround = true,
				});
				growth.Spill++;
				int more = Mathf.RoundToInt(lush * rng.Range(0.5f, 2.2f));
				for (int k = 0; k < more; k++)
				{
					Vector3 p = mid + a * (side * (width * 0.5f + rng.Range(0.6f, 3f))) + o * rng.Range(0.3f, 3.5f);
					spots.Add(new VineSpot
					{
						Species = species, Form = VineForm.Spill, Size = rng.NextFloat() < 0.4f ? "Large" : "Small", Variant = rng.Range(0, 2),
						Position = p, Yaw = yaw + side * rng.Range(10f, 80f), Scale = Vector3.one * rng.Range(0.75f, 1.1f), OnGround = true,
					});
					growth.Spill++;
				}
			}
			return spots;
		}

		/// <summary>
		/// A curtain hung from <paramref name="top"/> that falls <paramref name="hang"/> metres, about <paramref name="width"/>
		/// across: the piece whose length suits the drop, stretched to it.
		/// </summary>
		public static VineSpot Curtain(string species, Vector3 top, float yaw, float hang, float width, DeterministicRNG rng)
		{
			string size;
			float length, pieceWidth;
			if (hang > 3.3f)
			{
				size = "Fall"; length = 5f; pieceWidth = 1.8f;
			}
			else if (hang > 1.7f)
			{
				size = "Drape"; length = 2.8f; pieceWidth = 3f;
			}
			else
			{
				size = "Fringe"; length = 1.4f; pieceWidth = 2.2f;
			}
			return new VineSpot
			{
				Species = species, Form = VineForm.Curtain, Size = size, Variant = rng.Range(0, 2),
				Position = top, Yaw = yaw + rng.Range(-6f, 6f),
				Scale = new Vector3(Mathf.Clamp(width / pieceWidth, 0.6f, 1.6f), Mathf.Clamp(Mathf.Max(0.2f, hang) / length, 0.3f, 1.35f), 1f),
			};
		}

		/// <summary>
		/// The face of rock in front of a point: marched in from out in front (along −<paramref name="outward"/>) at the point's
		/// height and up from it every 0.6 m, until the face stops standing up. Its foot (the lowest hit), how far it leans back
		/// (degrees, 0 upright, held under 60), and how tall it stands, metres. False when no face is within reach, or the first
		/// is not a face but ground.
		/// </summary>
		public static bool TryFace(Func<Vector3, bool> solidAt, Vector3 at, Vector3 outward, Vector3 across, float maxHeight,
			out Vector3 foot, out float tilt, out float height)
		{
			return TryFace(solidAt, at, outward, maxHeight, 7f, out foot, out tilt, out height, out _);
		}

		/// <summary>
		/// <see cref="TryFace(Func{Vector3, bool}, Vector3, Vector3, Vector3, float, out Vector3, out float, out float)"/>
		/// marched in from <paramref name="reach"/> metres out in front of the point (so a narrow gorge's far wall is not
		/// taken for the open air in front of this one), with the highest point found on the face: its
		/// <paramref name="rim"/>, where it stops standing up, or where the search stopped at <paramref name="maxHeight"/>.
		/// </summary>
		public static bool TryFace(Func<Vector3, bool> solidAt, Vector3 at, Vector3 outward, float maxHeight, float reach,
			out Vector3 foot, out float tilt, out float height, out Vector3 rim)
		{
			foot = at;
			rim = at;
			tilt = 0f;
			height = 0f;
			Vector3 o = Flat(outward);
			const float step = 0.12f;
			float Depth(float y)
			{
				Vector3 start = new Vector3(at.x, at.y + y, at.z) + o * reach;
				if (solidAt(start))
				{
					return float.NaN;
				}
				for (float d = step; d <= reach * 2f; d += step)
				{
					if (solidAt(start - o * d))
					{
						return reach - d + step * 0.5f;
					}
				}
				return float.NaN;
			}
			float d0 = Depth(0f);
			if (float.IsNaN(d0))
			{
				return false;
			}
			float last = d0, first = d0, h = 0f;
			for (float y = 0.6f; y <= maxHeight + 1e-3f; y += 0.6f)
			{
				float d = Depth(y);
				// Out of rock, or the face stepped back further than a wall leans (the top of a block, the hill above it).
				if (float.IsNaN(d) || last - d > 0.85f || d - last > 0.5f)
				{
					break;
				}
				last = d;
				h = y;
			}
			if (h < 0.55f)
			{
				return false;
			}
			foot = at + o * d0;
			rim = at + o * last + Vector3.up * h;
			height = h + 0.3f;
			tilt = Mathf.Clamp(Mathf.Atan2(first - last, h) * Mathf.Rad2Deg, 0f, 60f);
			return true;
		}

		// ── Walls ───────────────────────────────────────────────

		/// <summary>A stretch of a wall's face a mat can climb, in the piece's own frame.</summary>
		public struct WallFace
		{
			/// <summary>The middle of its foot, on the face, at ground (y = 0) or where the face begins.</summary>
			public Vector3 Foot;
			/// <summary>Out of the face, horizontal.</summary>
			public Vector3 Normal;
			public float Width, Height;
			/// <summary>The solid's material: how readily ivy takes to it.</summary>
			public StructureMaterial Material;
			/// <summary>Faces into the building's footprint (the inside of an outer wall): seen only from within a roofless ruin.</summary>
			public bool Inside;
		}

		/// <summary>How readily ivy climbs a material: masonry and earth best, timber and plaster a little, nothing else.</summary>
		public static float Takes(StructureMaterial material)
		{
			switch (material)
			{
				case StructureMaterial.Stone:
				case StructureMaterial.Fieldstone:
				case StructureMaterial.Monolith:
					return 1f;
				case StructureMaterial.Earth:
					return 0.7f;
				case StructureMaterial.Plaster:
					return 0.5f;
				case StructureMaterial.Timber:
					return 0.3f;
				default:
					return 0f;
			}
		}

		/// <summary>
		/// The faces of a piece a mat may climb (piece frame): upright faces of masonry (and, less, of plaster and timber)
		/// that reach the ground, outside the building (not into one of its rooms, nor against another of its parts), each cut
		/// to the widest clear stretch: below its windows, and never across a doorway.
		/// </summary>
		public static List<WallFace> WallFaces(List<StructureSolid> solids, IReadOnlyList<StructureDoor> doors, IReadOnlyList<StructureRoom> rooms)
		{
			var faces = new List<WallFace>();
			if (solids == null || solids.Count == 0)
			{
				return faces;
			}
			// The footprint's middle, for which side of a wall is its inside (only a building has one: a wide footprint).
			Bounds all = solids[0].Bounds;
			foreach (StructureSolid s in solids)
			{
				all.Encapsulate(s.Bounds);
			}
			bool building = all.size.x > 3f && all.size.z > 3f;
			// What a mat must keep below or away from: windows (glass) and doorways, as boxes in the piece's frame.
			var openings = new List<Bounds>();
			foreach (StructureSolid s in solids)
			{
				if (s.Material == StructureMaterial.Glass)
				{
					Bounds b = s.Bounds;
					b.Expand(new Vector3(0.5f, 0.5f, 0.5f));
					openings.Add(b);
				}
			}
			if (doors != null)
			{
				foreach (StructureDoor d in doors)
				{
					Vector3 far = d.Hinge + d.Across.normalized * d.Width + Vector3.up * d.Height;
					var b = new Bounds((d.Hinge + far) * 0.5f, Vector3.zero);
					b.Encapsulate(d.Hinge);
					b.Encapsulate(far);
					// Both faces of the wall it stands in, and a margin round the frame.
					b.Expand(new Vector3(0.7f, 0.5f, 0.7f) + Abs(d.Inward.normalized) * 1.4f);
					openings.Add(b);
				}
			}
			foreach (StructureSolid solid in solids)
			{
				float takes = Takes(solid.Material);
				if (takes <= 0f || solid.Role == StructureRole.Rubble)
				{
					continue;
				}
				foreach (StructureFace face in solid.Faces)
				{
					if (Mathf.Abs(face.Normal.y) > 0.2f || face.Points.Count < 3)
					{
						continue;
					}
					Vector3 n = Flat(face.Normal);
					Vector3 u = Vector3.Cross(Vector3.up, n);
					Vector3 origin = face.Points[0];
					float uMin = float.MaxValue, uMax = float.MinValue;
					foreach (Vector3 p in face.Points)
					{
						float pu = Vector3.Dot(p - origin, u);
						uMin = Mathf.Min(uMin, pu);
						uMax = Mathf.Max(uMax, pu);
					}
					if (uMax - uMin < 0.8f)
					{
						continue;
					}
					// Column by column: where the face is, from the ground up, and how high it stays clear.
					const float step = 0.25f;
					int columns = Mathf.FloorToInt((uMax - uMin) / step);
					var top = new float[columns];
					for (int c = 0; c < columns; c++)
					{
						float cu = uMin + (c + 0.5f) * step;
						top[c] = ColumnTop(face, origin, u, n, cu, openings);
					}
					// The widest run of columns at least a metre and a half tall.
					int bestStart = -1, bestLength = 0;
					for (int c = 0; c < columns;)
					{
						if (top[c] < 1.2f)
						{
							c++;
							continue;
						}
						int start = c;
						while (c < columns && top[c] >= 1.2f)
						{
							c++;
						}
						if (c - start > bestLength)
						{
							bestLength = c - start;
							bestStart = start;
						}
					}
					if (bestLength * step < 0.8f)
					{
						continue;
					}
					float height = float.MaxValue;
					for (int c = bestStart; c < bestStart + bestLength; c++)
					{
						height = Mathf.Min(height, top[c]);
					}
					float mu = uMin + (bestStart + bestLength * 0.5f) * step;
					Vector3 foot = origin + u * mu;
					foot.y = 0f;
					// Its plane through the face, not the first point's depth along the normal.
					foot += n * Vector3.Dot(origin - foot, n);
					// Outside: not into a room of the building, nor against another of its parts.
					Vector3 probe = foot + n * 0.45f + Vector3.up * Mathf.Min(1f, height * 0.5f);
					if (InRoom(probe, rooms) || InsideAny(probe, solids, solid))
					{
						continue;
					}
					bool inside = building && Vector3.Dot(n, new Vector3(foot.x - all.center.x, 0f, foot.z - all.center.z)) < 0f;
					faces.Add(new WallFace { Foot = foot, Normal = n, Width = bestLength * step, Height = height, Material = solid.Material, Inside = inside });
				}
			}
			return faces;
		}

		private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

		/// <summary>How high a face is clear above the ground at a column (u along the face), metres; 0 where it does not reach the ground.</summary>
		private static float ColumnTop(StructureFace face, Vector3 origin, Vector3 u, Vector3 n, float cu, List<Bounds> openings)
		{
			float vMin = float.MaxValue, vMax = float.MinValue;
			int count = face.Points.Count;
			for (int i = 0; i < count; i++)
			{
				Vector3 p = face.Points[i], q = face.Points[(i + 1) % count];
				float pu = Vector3.Dot(p - origin, u), qu = Vector3.Dot(q - origin, u);
				if ((pu - cu) * (qu - cu) > 0f || Mathf.Abs(pu - qu) < 1e-5f && Mathf.Abs(pu - cu) > 1e-4f)
				{
					continue;
				}
				float t = Mathf.Abs(qu - pu) > 1e-5f ? Mathf.Clamp01((cu - pu) / (qu - pu)) : 0f;
				float v = Mathf.Lerp(p.y, q.y, t);
				vMin = Mathf.Min(vMin, v);
				vMax = Mathf.Max(vMax, v);
			}
			// Ivy climbs from the ground: a face that starts above it (an upper storey, a parapet) is out of its reach.
			if (vMin == float.MaxValue || vMin > 0.35f)
			{
				return 0f;
			}
			float top = vMax;
			Vector3 at = origin + u * cu;
			foreach (Bounds b in openings)
			{
				// An opening in this face over this column (its box reaches through the wall's face): the mat stops below it.
				if (Mathf.Abs(at.x - b.center.x) <= b.extents.x && Mathf.Abs(at.z - b.center.z) <= b.extents.z)
				{
					top = Mathf.Min(top, b.min.y);
				}
			}
			return Mathf.Max(0f, top);
		}

		private static bool InRoom(Vector3 p, IReadOnlyList<StructureRoom> rooms)
		{
			if (rooms == null)
			{
				return false;
			}
			foreach (StructureRoom r in rooms)
			{
				if (p.y < r.Centre.y - 0.2f || p.y > r.Centre.y + r.Height)
				{
					continue;
				}
				float dx = p.x - r.Centre.x, dz = p.z - r.Centre.z;
				if (r.Round ? dx * dx + dz * dz < r.Size.x * r.Size.x * 0.25f : Mathf.Abs(dx) < r.Size.x * 0.5f && Mathf.Abs(dz) < r.Size.y * 0.5f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Inside a solid other than <paramref name="own"/>: behind every one of its faces' planes (exact for the boxes and prisms walls are).</summary>
		private static bool InsideAny(Vector3 p, List<StructureSolid> solids, StructureSolid own)
		{
			foreach (StructureSolid s in solids)
			{
				if (s == own || s.Faces.Count < 4 || s.Material == StructureMaterial.Glass)
				{
					continue;
				}
				bool inside = true;
				foreach (StructureFace f in s.Faces)
				{
					if (f.Points.Count == 0 || Vector3.Dot(p - f.Points[0], f.Normal) > 0.02f)
					{
						inside = false;
						break;
					}
				}
				if (inside)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// A mat fitted to a face (piece frame): the size its clear stretch takes, at most as wide and as tall as the stretch,
		/// often less (ivy rarely clothes a whole wall), set somewhere along it. False when it takes none.
		/// </summary>
		public static bool FitMat(in WallFace face, string species, DeterministicRNG rng, out VineSpot spot)
		{
			spot = default;
			// The largest size the stretch takes without squashing it flat (a mat crushed to half its height reads as a hedge
			// at the wall's foot): the full wall mat now and then where there is room, else the mat, else the patch.
			string size = null;
			float w = 0f, h = 0f;
			if (face.Width >= 3.6f * 0.8f && face.Height >= 3.4f * 0.75f && rng.NextFloat() < 0.6f)
			{
				size = "Wall"; w = 3.6f; h = 3.4f;
			}
			else if (face.Width >= 2.2f * 0.75f && face.Height >= 2.4f * 0.7f)
			{
				size = "Mat"; w = 2.2f; h = 2.4f;
			}
			else if (face.Width >= 0.75f && face.Height >= 0.9f)
			{
				size = "Patch"; w = 1f; h = 1.2f;
			}
			if (size == null)
			{
				return false;
			}
			float sx = Mathf.Min(face.Width / w, rng.Range(0.8f, 1.15f));
			float sy = Mathf.Min(face.Height / h, rng.Range(0.75f, 1.15f));
			if (sx < 0.7f || sy < 0.7f)
			{
				return false;
			}
			Vector3 along = Vector3.Cross(Vector3.up, face.Normal);
			float slack = Mathf.Max(0f, face.Width - w * sx) * 0.5f;
			spot = new VineSpot
			{
				Species = species, Form = VineForm.Mat, Size = size, Variant = rng.Range(0, 2),
				Position = face.Foot + along * rng.Range(-slack, slack) + face.Normal * 0.02f + Vector3.down * 0.1f,
				Yaw = YawOf(face.Normal), Scale = new Vector3(sx, sy, 1f),
			};
			return true;
		}

		// ── Crags ───────────────────────────────────────────────

		/// <summary>
		/// Curtains off a crag's edges (world): its top surface rasterised from its mesh on a 0.5 m grid, every cell whose drop
		/// downhill (<paramref name="downhill"/>, to the rock or the ground a metre on) is a near-vertical 2.2 m or more is an
		/// edge; the best edge in each 2 m along the crag hangs a curtain as long as the drop, at most <paramref name="max"/>.
		/// </summary>
		public static List<VineSpot> CragCurtains(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles, Vector3 downhill,
			Func<float, float, float> groundAt, string species, DeterministicRNG rng, int max)
		{
			var spots = new List<VineSpot>();
			if (positions == null || positions.Count == 0 || max <= 0)
			{
				return spots;
			}
			Vector3 d = Flat(downhill);
			var across = new Vector3(d.z, 0f, -d.x);
			const float cell = 0.5f;
			Vector3 min = positions[0], maxP = positions[0];
			foreach (Vector3 p in positions)
			{
				min = Vector3.Min(min, p);
				maxP = Vector3.Max(maxP, p);
			}
			int w = Mathf.CeilToInt((maxP.x - min.x) / cell) + 1, h = Mathf.CeilToInt((maxP.z - min.z) / cell) + 1;
			if (w * h > 400000)
			{
				return spots;
			}
			var top = new float[w * h];
			for (int i = 0; i < top.Length; i++)
			{
				top[i] = float.NegativeInfinity;
			}
			for (int t = 0; t + 2 < triangles.Count; t += 3)
			{
				Vector3 a = positions[triangles[t]], b = positions[triangles[t + 1]], c = positions[triangles[t + 2]];
				int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - min.x) / cell));
				int x1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - min.x) / cell));
				int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - min.z) / cell));
				int z1 = Mathf.Min(h - 1, Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - min.z) / cell));
				float area = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
				if (Mathf.Abs(area) < 1e-8f)
				{
					continue;
				}
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						float px = min.x + x * cell, pz = min.z + z * cell;
						float w0 = ((b.x - px) * (c.z - pz) - (b.z - pz) * (c.x - px)) / area;
						float w1 = ((c.x - px) * (a.z - pz) - (c.z - pz) * (a.x - px)) / area;
						float w2 = 1f - w0 - w1;
						if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f)
						{
							continue;
						}
						float y = w0 * a.y + w1 * b.y + w2 * c.y;
						int k = z * w + x;
						if (y > top[k])
						{
							top[k] = y;
						}
					}
				}
			}
			float SurfaceAt(Vector3 p)
			{
				int x = Mathf.RoundToInt((p.x - min.x) / cell), z = Mathf.RoundToInt((p.z - min.z) / cell);
				float rock = x >= 0 && z >= 0 && x < w && z < h ? top[z * w + x] : float.NegativeInfinity;
				return Mathf.Max(rock, groundAt != null ? groundAt(p.x, p.z) : float.NegativeInfinity);
			}
			// The best edge in each 2 m strip along the crag.
			var best = new Dictionary<int, (Vector3 edge, float drop)>();
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float y = top[z * w + x];
					if (float.IsNegativeInfinity(y))
					{
						continue;
					}
					var p = new Vector3(min.x + x * cell, y, min.z + z * cell);
					// Standing on the rock, not under the ground.
					if (groundAt != null && groundAt(p.x, p.z) > y - 0.3f)
					{
						continue;
					}
					float below = SurfaceAt(p + d * 1f);
					float drop = y - below;
					// Near-vertical: halfway down the drop and a quarter metre further out, nothing stands above the curtain.
					if (drop < 2.2f || SurfaceAt(p + d * 0.4f) > y - 0.6f && SurfaceAt(p + d * 0.6f) > y - drop * 0.5f)
					{
						continue;
					}
					int strip = Mathf.FloorToInt(Vector3.Dot(p, across) / 2f);
					if (!best.TryGetValue(strip, out var known) || drop > known.drop)
					{
						// The edge itself: on out while the top stays level.
						Vector3 edge = p;
						for (int s = 1; s <= 8; s++)
						{
							Vector3 q = p + d * (s * 0.1f);
							if (SurfaceAt(q) < y - 0.3f)
							{
								break;
							}
							edge = q;
						}
						best[strip] = (edge, drop);
					}
				}
			}
			var strips = new List<int>(best.Keys);
			strips.Sort();
			// Seeded choice of which edges: a crag carries one to a few curtains, not one a strip.
			for (int i = strips.Count - 1; i > 0; i--)
			{
				int j = rng.Range(0, i + 1);
				(strips[i], strips[j]) = (strips[j], strips[i]);
			}
			float yaw = YawOf(d);
			for (int i = 0; i < strips.Count && spots.Count < max; i++)
			{
				(Vector3 edge, float drop) = best[strips[i]];
				spots.Add(Curtain(species, edge + d * 0.05f, yaw, Mathf.Min(drop - rng.Range(0.2f, 1.2f), 6.5f), rng.Range(1.6f, 3.2f), rng));
			}
			return spots;
		}

		// ── Waterfalls ──────────────────────────────────────────

		/// <summary>A waterfall as the vines round it see it (world).</summary>
		public struct FallFrame
		{
			/// <summary>The middle of the brink, on the water's surface.</summary>
			public Vector3 Lip;
			/// <summary>Downstream as the water leaves the lip (only its horizontal part is used).</summary>
			public Vector3 Downstream;
			/// <summary>The curtain's width as it leaves the lip: the lip's, metres.</summary>
			public float Width;
			/// <summary>The plunge pool's surface, altitude.</summary>
			public float Pool;
			/// <summary>How far downstream of the lip the curtain may reach the pool, metres: its throw included.</summary>
			public float Landing;
			/// <summary>The middle of the plunge pool (horizontal; its height is not used): where its walls are looked for from.</summary>
			public Vector3 Basin;
			/// <summary>The pool's radius, metres: how far round the basin its walls are looked for.</summary>
			public float PoolRadius;

			/// <summary>From the lip's surface to the pool's, metres.</summary>
			public float Drop => Lip.y - Pool;
		}

		/// <summary>What <see cref="FallWalls"/> grew, for the notes.</summary>
		public struct FallGrowth
		{
			public string Species;
			public int Mats, Curtains;
		}

		/// <summary>How far up the river above its lip the curtain starts, metres (the runtime sheet's brink run).</summary>
		public const float BrinkRunMetres = 1.6f;

		/// <summary>
		/// How far a fall's wall may lean back and still be grown on as a wall, degrees from upright: steeper than any carved
		/// bank (cut back no steeper than 38 degrees, so leaning 52), which is ground cover's, not a wall's.
		/// </summary>
		public const float FallWallTilt = 50f;

		/// <summary>
		/// How thickly a habitat's vines take to the walls round a waterfall, 0..1: the crag's share
		/// (<see cref="VineBiomes.Habitat.Cliffs"/>) doubled, as the spray keeps the rock wet through every dry spell, so the
		/// wet woods and the jungle clothe them and cold or dry country, where frost and drought still bite, carries a little.
		/// None where the biome grows no vine at all.
		/// </summary>
		public static float FallShare(VineBiomes.Habitat habitat)
		{
			return habitat != null && habitat.Species != null && habitat.Species.Length > 0 ? Mathf.Clamp01(2f * habitat.Cliffs) : 0f;
		}

		/// <summary>
		/// How far out from its lip a fall's water could land, metres, drag ignored: thrown at the brink's speed (critical
		/// flow over a free edge, the brink 0.715 of its critical depth) and falling the drop.
		/// </summary>
		public static float FallThrow(float unitDischarge, float drop)
		{
			const float g = 9.81f;
			float q = Mathf.Max(0.01f, unitDischarge);
			float critical = Mathf.Pow(q * q / g, 1f / 3f);
			float brinkSpeed = q / (0.715f * critical);
			return brinkSpeed * Mathf.Sqrt(2f * Mathf.Max(0f, drop) / g);
		}

		/// <summary>
		/// Half the band across the fall a vine keeps out of, metres: half the curtain, the spread its edges take on the way
		/// down and the strips that part round its rock, and a margin where the spray strips anything that takes hold.
		/// </summary>
		public static float CurtainHalfWidth(in FallFrame fall)
		{
			return 0.5f * Mathf.Max(0.5f, fall.Width) + Mathf.Clamp(0.75f + 0.04f * fall.Drop, 0.75f, 3f);
		}

		/// <summary>Where the curtain strikes the pool, about its landing, metres (the runtime's impact radius).</summary>
		public static float ImpactRadius(in FallFrame fall)
		{
			return Mathf.Clamp(0.5f * fall.Width + 0.15f * fall.Drop, 2f, Mathf.Max(2f, fall.PoolRadius));
		}

		/// <summary>
		/// Whether a vine <paramref name="halfWidth"/> metres either side of <paramref name="at"/> stays out of the falling
		/// water: out of the band <see cref="CurtainHalfWidth"/> either side of the fall's line, from its brink run above the
		/// lip down to where it strikes the pool.
		/// </summary>
		public static bool ClearOfCurtain(in FallFrame fall, Vector3 at, float halfWidth)
		{
			Vector3 d = Flat(fall.Downstream);
			float rx = at.x - fall.Lip.x, rz = at.z - fall.Lip.z;
			float along = rx * d.x + rz * d.z;
			float across = rz * d.x - rx * d.z;
			if (along + halfWidth < -BrinkRunMetres || along - halfWidth > fall.Landing + ImpactRadius(in fall))
			{
				return true;
			}
			return Mathf.Abs(across) - halfWidth >= CurtainHalfWidth(in fall);
		}

		/// <summary>The distance along <paramref name="direction"/> to the first rock from a point in the open, metres; NaN from inside rock or with none within reach.</summary>
		private static float March(Func<Vector3, bool> solidAt, Vector3 from, Vector3 direction, float reach)
		{
			const float step = 0.15f;
			if (solidAt(from))
			{
				return float.NaN;
			}
			for (float t = step; t <= reach; t += step)
			{
				if (solidAt(from + direction * t))
				{
					return t - step * 0.5f;
				}
			}
			return float.NaN;
		}

		/// <summary>
		/// Rock behind a piece's stretch of face and open air in front of it: at both its edges <paramref name="half"/> metres
		/// along the face from <paramref name="at"/>, <paramref name="up"/> metres up it (down, when negative), the face
		/// followed as it leans back (<paramref name="tan"/>, the tangent of its lean).
		/// </summary>
		private static bool Backed(Func<Vector3, bool> solidAt, Vector3 at, Vector3 normal, float half, float tan, float up)
		{
			var along = new Vector3(-normal.z, 0f, normal.x);
			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 p = at + along * (side * half) + Vector3.up * up - normal * (up * tan);
				if (!solidAt(p - normal * 0.35f) || solidAt(p + normal * 0.6f))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The vines on the walls beside and round a waterfall (world): mats on the damp faces flanking the curtain and round
		/// the plunge pool, and curtains hung from the walls' rims down them, each one found on a face that stands there
		/// (<see cref="TryFace(Func{Vector3, bool}, Vector3, Vector3, float, float, out Vector3, out float, out float, out Vector3)"/>),
		/// never in the falling water (<see cref="ClearOfCurtain"/>), nor down into the pool or a river.
		/// </summary>
		/// <remarks>
		/// The walls are looked for from the middle of the pool outward, all round it but its way out, at heights from just
		/// over the pool up past the lip: every face found, in a seeded order, is grown on (a share by how lush the habitat
		/// is) until the fall has its fill, more for a taller fall and a wider pool. How thickly is
		/// <see cref="FallShare"/>; the species is one of the habitat's for the whole fall.
		/// </remarks>
		/// <param name="solidAt">True inside rock or under the ground (world).</param>
		/// <param name="waterAt">The water's surface at (east, north), negative infinity where there is none; null for the pool's only.</param>
		public static List<VineSpot> FallWalls(in FallFrame fall, VineBiomes.Habitat habitat, DeterministicRNG rng, Func<Vector3, bool> solidAt,
			Func<float, float, float> waterAt, out FallGrowth growth)
		{
			var spots = new List<VineSpot>();
			growth = default;
			float share = FallShare(habitat);
			if (share <= 0f || rng == null || solidAt == null || fall.Drop < 1f)
			{
				return spots;
			}
			// Whether this fall is overgrown at all: every one in the wet woods, the odd one where vines are scarce.
			if (rng.NextFloat() >= Mathf.Min(1f, 1.4f * share))
			{
				return spots;
			}
			string species = habitat.Pick(rng.NextFloat());
			float lush = Mathf.Clamp01(share * rng.Range(0.8f, 1.15f));
			growth.Species = species;

			Vector3 d = Flat(fall.Downstream);
			var left = new Vector3(-d.z, 0f, d.x);
			float radius = Mathf.Max(3f, fall.PoolRadius);
			float reach = radius + 6f;
			// Up past the lip: a fall in a gorge or a cliff's slot has its walls standing on above the brink.
			float top = fall.Lip.y + Mathf.Clamp(0.5f * fall.Drop, 3f, 10f);
			int max = Mathf.RoundToInt(Mathf.Lerp(3f, 24f, lush) * Mathf.Clamp(Mathf.Sqrt(fall.Drop / 8f * radius / 8f), 0.5f, 2f));

			// Every look at the walls: a heading from the basin (all round but the pool's way out) at a height on the wall.
			int rays = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / 2.2f), 12, 40);
			float lowest = fall.Pool + 0.55f;
			int bands = Mathf.Clamp(Mathf.CeilToInt((top - 1.5f - lowest) / 3.2f), 1, 8);
			float bandStep = Mathf.Max(3.2f, (top - 1.5f - lowest) / bands);
			var looks = new List<(Vector3 direction, float height)>();
			for (int i = 0; i < rays; i++)
			{
				float angle = (i + rng.Range(-0.3f, 0.3f)) / rays * 2f * Mathf.PI;
				// Not down the way the river leaves the pool: no wall stands there.
				if (Mathf.Abs(Mathf.DeltaAngle(0f, angle * Mathf.Rad2Deg)) < 35f)
				{
					continue;
				}
				Vector3 direction = d * Mathf.Cos(angle) + left * Mathf.Sin(angle);
				for (int b = 0; b < bands; b++)
				{
					looks.Add((direction, lowest + b * bandStep));
				}
			}
			for (int i = looks.Count - 1; i > 0; i--)
			{
				int j = rng.Range(0, i + 1);
				(looks[i], looks[j]) = (looks[j], looks[i]);
			}

			var taken = new List<(Vector3 centre, float halfWidth, float halfHeight)>();
			bool Crowded(Vector3 centre, float halfWidth, float halfHeight)
			{
				foreach ((Vector3 c, float w, float h) in taken)
				{
					float dx = centre.x - c.x, dz = centre.z - c.z;
					if (Mathf.Sqrt(dx * dx + dz * dz) < 0.8f * (halfWidth + w) && Mathf.Abs(centre.y - c.y) < 0.8f * (halfHeight + h))
					{
						return true;
					}
				}
				return false;
			}

			Vector3 basin = new Vector3(fall.Basin.x, 0f, fall.Basin.z);
			foreach ((Vector3 direction, float band) in looks)
			{
				if (growth.Mats + growth.Curtains >= max)
				{
					break;
				}
				float y = band + rng.Range(0f, 0.6f);
				if (rng.NextFloat() > 0.25f + 0.65f * lush)
				{
					continue;
				}
				Vector3 from = basin + Vector3.up * y;
				float hit = March(solidAt, from, direction, reach);
				if (float.IsNaN(hit))
				{
					continue;
				}
				Vector3 at = from + direction * hit;
				if (!ClearOfCurtain(in fall, at, 0.5f))
				{
					continue;
				}
				// The face's own facing, from the rock a little either side: the heading only finds it.
				var sideways = new Vector3(-direction.z, 0f, direction.x);
				Vector3 normal = -direction;
				float h1 = March(solidAt, from + sideways * 0.75f, direction, reach), h2 = March(solidAt, from - sideways * 0.75f, direction, reach);
				if (!float.IsNaN(h1) && !float.IsNaN(h2))
				{
					Vector3 run = (sideways * 0.75f + direction * h1) - (-sideways * 0.75f + direction * h2);
					run.y = 0f;
					if (run.sqrMagnitude > 1e-6f)
					{
						normal = Vector3.Cross(run, Vector3.up).normalized;
						if (Vector3.Dot(normal, direction) > 0f)
						{
							normal = -normal;
						}
					}
				}
				// Met at a glance: the wall runs along the look, not across it.
				if (Vector3.Dot(normal, -direction) < 0.42f)
				{
					continue;
				}
				float maxHeight = Mathf.Clamp(top - y, 1.2f, 16f);
				if (!TryFace(solidAt, at, normal, maxHeight, Mathf.Clamp(0.9f * hit, 0.8f, 3f), out Vector3 foot, out float tilt, out float height, out Vector3 rim)
					|| tilt > FallWallTilt || height < 1f)
				{
					continue;
				}
				float tan = Mathf.Tan(tilt * Mathf.Deg2Rad);
				// Nothing reaches down into the water at the wall's foot (the pool, or the river above the lip).
				Vector3 front = foot + normal * 0.6f;
				float floor = Mathf.Max(fall.Pool, waterAt != null ? waterAt(front.x, front.z) : float.NegativeInfinity) + 0.3f;
				float yaw = YawOf(normal);

				// ── A curtain from the wall's rim, down its face ──
				bool rimmed = height < maxHeight - 0.35f;
				if (rimmed && tilt <= 35f && rim.y - floor >= 2.2f && rng.NextFloat() < 0.3f + 0.35f * lush)
				{
					float hang = Mathf.Max(1.2f, Mathf.Min(rim.y - floor, 6.5f) * rng.Range(0.6f, 1f));
					VineSpot curtain = Curtain(species, rim + normal * 0.05f, yaw, hang, rng.Range(1.6f, 3f), rng);
					curtain.Tilt = tilt;
					ProceduralArtCatalogue.TryVinePiece(curtain.Form, curtain.Size, out VinePiece piece);
					float half = 0.5f * piece.Width * curtain.Scale.x;
					float drop = Mathf.Min(piece.Length * curtain.Scale.y, rim.y - floor);
					Vector3 bottom = rim + Vector3.down * drop + normal * (drop * tan);
					Vector3 centre = (rim + bottom) * 0.5f;
					if (ClearOfCurtain(in fall, rim, half) && ClearOfCurtain(in fall, bottom, half) && !Crowded(centre, half, 0.5f * drop)
						&& Backed(solidAt, rim, normal, half * 0.8f, tan, -Mathf.Min(0.4f, 0.5f * drop))
						&& Backed(solidAt, rim, normal, half * 0.8f, tan, -0.5f * drop))
					{
						spots.Add(curtain);
						taken.Add((centre, half, 0.5f * drop));
						growth.Curtains++;
						continue;
					}
				}

				// ── A mat on the face ──
				if (foot.y - 0.1f < floor)
				{
					continue;
				}
				for (int attempt = 0; attempt < 2; attempt++)
				{
					string size;
					float w, h;
					if (attempt == 0 && height >= 2.6f && rng.NextFloat() < 0.35f + 0.3f * lush)
					{
						size = "Wall"; w = 3.6f; h = 3.4f;
					}
					else if (attempt == 0 && height >= 1.7f)
					{
						size = "Mat"; w = 2.2f; h = 2.4f;
					}
					else
					{
						size = "Patch"; w = 1f; h = 1.2f;
					}
					float sx = rng.Range(0.8f, 1.15f);
					float sy = Mathf.Min(height / h, rng.Range(0.75f, 1.15f));
					if (sy < 0.6f)
					{
						break;
					}
					float half = 0.5f * w * sx, tall = h * sy;
					Vector3 position = foot + normal * 0.04f + Vector3.down * 0.1f;
					Vector3 upper = position + Vector3.up * tall - normal * (tall * tan);
					Vector3 centre = (position + upper) * 0.5f;
					if (!ClearOfCurtain(in fall, position, half) || !ClearOfCurtain(in fall, upper, half) || Crowded(centre, half, 0.5f * tall))
					{
						break;
					}
					// Rock behind both its edges, low and high: never hung out past the end of the face.
					if (!Backed(solidAt, foot, normal, half * 0.85f, tan, 0.4f) || !Backed(solidAt, foot, normal, half * 0.85f, tan, Mathf.Min(0.6f * tall, height - 0.4f)))
					{
						continue;
					}
					spots.Add(new VineSpot
					{
						Species = species, Form = VineForm.Mat, Size = size, Variant = rng.Range(0, 2),
						Position = position, Yaw = yaw + rng.Range(-5f, 5f), Tilt = tilt, Scale = new Vector3(sx, sy, 1f),
					});
					taken.Add((centre, half, 0.5f * tall));
					growth.Mats++;
					break;
				}
			}
			return spots;
		}

		// ── Trees ───────────────────────────────────────────────

		/// <summary>
		/// Whether the tree rooted at <paramref name="root"/> carries a climber, and which: by the habitat's share for trees and a
		/// hash of where it stands, so the same trees carry ivy on every run. Taller variants on more of them where it is damp.
		/// </summary>
		public static bool Climbs(VineBiomes.Habitat habitat, string[] vines, Vector3 root, out string species, out int variant)
		{
			species = null;
			variant = 0;
			if (habitat == null || habitat.Trees <= 0f || vines == null || vines.Length == 0)
			{
				return false;
			}
			if (Hash(root, 0x1B3) >= habitat.Trees)
			{
				return false;
			}
			// The habitat's species that grow on this tree, by weight.
			float total = 0f;
			foreach ((string s, float weight) in habitat.Species)
			{
				if (Array.IndexOf(vines, s) >= 0)
				{
					total += weight;
				}
			}
			if (total <= 0f)
			{
				return false;
			}
			float at = Hash(root, 0x2C5) * total;
			foreach ((string s, float weight) in habitat.Species)
			{
				if (Array.IndexOf(vines, s) < 0)
				{
					continue;
				}
				species = s;
				if (at < weight)
				{
					break;
				}
				at -= weight;
			}
			variant = Hash(root, 0x3D7) < 0.35f + 0.4f * habitat.Trees ? 1 : 0;
			return true;
		}
	}
}
#endif
