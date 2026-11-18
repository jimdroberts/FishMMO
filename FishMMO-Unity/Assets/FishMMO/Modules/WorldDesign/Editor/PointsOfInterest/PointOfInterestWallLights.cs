#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Street lights hung on the walls of the buildings along a street (2026-10-18, I6g): where a biome's way lights name a
	/// wall light for a settlement's streets (a torch in a sconce, a lantern on a bracket, a lantern from a scrolled iron arm),
	/// each is fixed to a street-facing wall of a building laid there, at the light's spacing, between 2.4 and 3 m up, facing
	/// the street and clear of the doors and windows; where no wall is within reach of a light's place, a post of the same kind
	/// of light stands at the street's edge instead (<see cref="Biomes.BiomeWayLights.PostFor"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two steps.</b> <see cref="SpansOf(List{StructureSolid}, IReadOnlyList{StructureDoor}, IReadOnlyList{StructureRoom})"/>
	/// finds, once a building piece, the stretches of its outer walls a light may be fixed to (piece frame); <see cref="StreetWalls"/>
	/// carries them to where the site laid its buildings, and <see cref="Plan"/> walks each street as the standing lights always
	/// have (from 6 m in, at the spacing, the sides in turn) and fixes each light to the best wall on its side, else the other
	/// side's, else stands a post.
	/// </para>
	/// <para>
	/// <b>Nothing else moves.</b> The pass runs after the buildings and their clutter are laid, draws only on a stream of its
	/// own (<see cref="StreetSeeds"/>: each street's variant of the light), and only where a biome names a wall light for the
	/// kind (no biome does before its own round): the standing lights' code is untouched, so every other site lays exactly what
	/// it laid before.
	/// </para>
	/// </remarks>
	public static class PointOfInterestWallLights
	{
		/// <summary>The heights a light is fixed at on a wall, metres over the building's foot (piece frame), in preference.</summary>
		public static readonly float[] Fixings = { 2.7f, 2.6f, 2.8f, 2.5f, 2.9f, 2.4f, 3f };

		/// <summary>How much wall a light's fixing needs below and above it, metres: its back plate and its flame or lantern's top.</summary>
		public const float Below = 0.35f, Above = 0.4f;

		/// <summary>How far a fixing keeps from a window's pane, and from a door's frame (and above its head), metres.</summary>
		public const float WindowMargin = 0.35f, DoorMargin = 0.6f, DoorHeadroom = 0.6f;

		/// <summary>The shortest stretch of wall a light is fixed to, metres; the step the walls are searched in.</summary>
		public const float MinStretch = 0.5f, Step = 0.25f;

		/// <summary>How far out from the wall's face a fixing stands (the back plate clear of the plaster and timbers), metres.</summary>
		public const float Proud = 0.03f;

		/// <summary>
		/// The room a hung light takes in front of its wall (2026-10-18, I6h), metres: from <see cref="ClearFrom"/> out of the face
		/// (past the framing, sills and surrounds, which stand at most 0.14 m proud) to <see cref="ClearOut"/> (a bracket's 0.6 m arm
		/// and its lantern), <see cref="ClearBeside"/> either side, from <see cref="Below"/> under the fixing to <see cref="Above"/>
		/// over it. Nothing else of the building may stand there: a tavern's sign on its bracket, a chimney stack, a buttress, a
		/// low eave, a flat roof's beam ends and spouts.
		/// </summary>
		public const float ClearFrom = 0.15f, ClearOut = 0.75f, ClearBeside = 0.25f;

		/// <summary>How far behind a street's edge a wall may stand and still carry its light, metres (times the site's stature).</summary>
		public const float Reach = 5f;

		/// <summary>How far along the street a light may move from its place to reach a wall, as a share of the spacing.</summary>
		public const float Slide = 0.35f;

		/// <summary>How square to the street a wall must face (the cosine between its face and the way across the street).</summary>
		public const float Facing = 0.75f;

		/// <summary>A stretch of wall a light may be fixed to: its two ends at the fixing height on the face, and the way the face looks.</summary>
		public struct WallSpan
		{
			public Vector3 A, B;
			/// <summary>Out of the face, horizontal, unit.</summary>
			public Vector3 Normal;
		}

		/// <summary>One street light: where it stands (world x, z), its fixing's height (world y, on a wall), its heading, whether on a wall.</summary>
		public struct WallLight
		{
			public Vector2 At;
			public float Height;
			public float Yaw;
			public bool OnWall;
			/// <summary>Which street (index into the plan's streets) and which of its places.</summary>
			public int Street, Place;
		}

		/// <summary>
		/// The heading a wall light is laid at so it stands out of a wall facing <paramref name="outward"/> (x, z): the kit's wall
		/// lights stand out along their −z (StructurePieces.Lights.cs: the back plate at z = 0 against the wall).
		/// </summary>
		public static float YawFacing(Vector2 outward) => Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg;

		/// <summary>Whether a material is a wall a light may be fixed to (not glass, a lamp, cloth, iron, thatch or ash).</summary>
		public static bool Mountable(StructureMaterial material)
		{
			switch (material)
			{
				case StructureMaterial.Glass:
				case StructureMaterial.Lamplight:
				case StructureMaterial.Runelight:
				case StructureMaterial.Faelight:
				case StructureMaterial.Canvas:
				case StructureMaterial.Dyed:
				case StructureMaterial.Iron:
				case StructureMaterial.Thatch:
				case StructureMaterial.Reed:
				case StructureMaterial.Ash:
					return false;
				default:
					return true;
			}
		}

		// ── The walls of a piece ──────────────────────────────────────

		private static readonly Dictionary<(string, int, int), List<WallSpan>> spans = new Dictionary<(string, int, int), List<WallSpan>>();

		/// <summary>The stretches of a building piece's outer walls a light may be fixed to (piece frame), cached; a roof variant's own.</summary>
		public static IReadOnlyList<WallSpan> SpansOf(StructurePiece piece, int variant, StructureRoofPitch? roof)
		{
			var key = (piece.Id, variant, roof == null ? -1 : (int)roof.Value);
			if (!spans.TryGetValue(key, out List<WallSpan> list))
			{
				List<StructureSolid> solids = StructurePieces.BuildSolids(piece, variant, 0, StructurePieces.DefaultSeed, roof);
				list = SpansOf(solids, StructurePieces.DoorsOf(piece, variant), StructurePieces.RoomsOf(piece, variant));
				spans[key] = list;
			}
			return list;
		}

		/// <summary>
		/// The stretches of a building's outer walls a light may be fixed to (the piece's frame, its foot at y = 0): upright faces
		/// of its load-bearing walls, looking out of the building (not into a room, nor against another of its parts), that run
		/// from <see cref="Below"/> under the fixing to <see cref="Above"/> over it at one of the <see cref="Fixings"/> (the first
		/// height any stretch of the face takes), cut round its windows and doors, each at least <see cref="MinStretch"/> long.
		/// </summary>
		public static List<WallSpan> SpansOf(List<StructureSolid> solids, IReadOnlyList<StructureDoor> doors, IReadOnlyList<StructureRoom> rooms)
		{
			var found = new List<WallSpan>();
			if (solids == null || solids.Count == 0)
			{
				return found;
			}
			Bounds all = solids[0].Bounds;
			foreach (StructureSolid s in solids)
			{
				all.Encapsulate(s.Bounds);
			}
			// What a fixing keeps clear of, as boxes in the piece's frame: the windows' panes and the doorways (both faces of the
			// wall a door stands in, a margin either side, and up over its head).
			var openings = new List<Bounds>();
			foreach (StructureSolid s in solids)
			{
				if (s.Material == StructureMaterial.Glass)
				{
					Bounds b = s.Bounds;
					b.Expand(new Vector3(2f * WindowMargin, 2f * WindowMargin, 2f * WindowMargin));
					openings.Add(b);
				}
			}
			if (doors != null)
			{
				foreach (StructureDoor d in doors)
				{
					Vector3 far = d.Hinge + d.Across.normalized * d.Width + Vector3.up * d.Height;
					var b = new Bounds(d.Hinge, Vector3.zero);
					b.Encapsulate(far);
					Vector3 through = d.Inward.normalized;
					b.Expand(new Vector3(2f * DoorMargin, 0f, 2f * DoorMargin) + new Vector3(Mathf.Abs(through.x), 0f, Mathf.Abs(through.z)) * 2.8f);
					b.SetMinMax(new Vector3(b.min.x, Mathf.Min(b.min.y, 0f), b.min.z), new Vector3(b.max.x, b.max.y + DoorHeadroom, b.max.z));
					openings.Add(b);
				}
			}
			foreach (StructureSolid solid in solids)
			{
				if (solid.Role != StructureRole.Structure || !Mountable(solid.Material))
				{
					continue;
				}
				foreach (StructureFace face in solid.Faces)
				{
					if (face.Points.Count < 3 || Mathf.Abs(face.Normal.y) > 0.2f)
					{
						continue;
					}
					Vector3 n = new Vector3(face.Normal.x, 0f, face.Normal.z).normalized;
					Vector3 u = Vector3.Cross(Vector3.up, n);
					Vector3 origin = face.Points[0];
					float uMin = float.MaxValue, uMax = float.MinValue;
					foreach (Vector3 p in face.Points)
					{
						float pu = Vector3.Dot(p - origin, u);
						uMin = Mathf.Min(uMin, pu);
						uMax = Mathf.Max(uMax, pu);
					}
					if (uMax - uMin < MinStretch)
					{
						continue;
					}
					// A face looking into the building (the inside of an outer wall) carries no street light.
					Vector3 mid = origin + u * (0.5f * (uMin + uMax));
					if (Vector3.Dot(n, new Vector3(mid.x - all.center.x, 0f, mid.z - all.center.z)) <= 0f)
					{
						continue;
					}
					int columns = Mathf.FloorToInt((uMax - uMin) / Step);
					var clear = new bool[columns];
					foreach (float h in Fixings)
					{
						bool any = false;
						for (int c = 0; c < columns; c++)
						{
							float cu = uMin + (c + 0.5f) * Step;
							clear[c] = Clear(face, solids, solid, openings, rooms, origin, u, n, cu, h);
							any |= clear[c];
						}
						if (!any)
						{
							continue;
						}
						int before = found.Count;
						for (int c = 0; c < columns;)
						{
							if (!clear[c])
							{
								c++;
								continue;
							}
							int start = c;
							while (c < columns && clear[c])
							{
								c++;
							}
							if ((c - start) * Step + 1e-4f < MinStretch)
							{
								continue;
							}
							found.Add(new WallSpan
							{
								A = OnFace(origin, u, n, uMin + (start + 0.5f) * Step, h),
								B = OnFace(origin, u, n, uMin + (c - 0.5f) * Step, h),
								Normal = n,
							});
						}
						if (found.Count > before)
						{
							break;
						}
					}
				}
			}
			return found;
		}

		/// <summary>The point on a face's plane at a column (u along it) and a height, stood <see cref="Proud"/> out from it.</summary>
		private static Vector3 OnFace(Vector3 origin, Vector3 u, Vector3 n, float cu, float h)
		{
			Vector3 at = origin + u * cu;
			at.y = h;
			at += n * (Vector3.Dot(origin - at, n) + Proud);
			return at;
		}

		/// <summary>Whether a light may be fixed at a column of a face at height <paramref name="h"/>.</summary>
		private static bool Clear(StructureFace face, List<StructureSolid> solids, StructureSolid own, List<Bounds> openings,
			IReadOnlyList<StructureRoom> rooms, Vector3 origin, Vector3 u, Vector3 n, float cu, float h)
		{
			// The face runs from under the fixing to over it at this column.
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
			if (vMin > h - Below || vMax < h + Above)
			{
				return false;
			}
			Vector3 at = OnFace(origin, u, n, cu, h);
			foreach (Bounds b in openings)
			{
				if (Mathf.Abs(at.x - b.center.x) <= b.extents.x && Mathf.Abs(at.z - b.center.z) <= b.extents.z
					&& h + Above >= b.min.y && h - Below <= b.max.y)
				{
					return false;
				}
			}
			// Out in the open in front of it: not in a room of the building, nor inside another of its parts (a porch, a buttress),
			// and nothing else hung on or standing off the wall where the light hangs (2026-10-18, I6h: a sign, a hood, the eaves).
			Vector3 probe = at + n * 0.45f;
			return !InRoom(probe, rooms) && !InsideAny(probe, solids, own) && !Crowded(at, n, u, h, solids, own);
		}

		/// <summary>
		/// Whether any part of the building but the wall itself stands in the room a light hung at <paramref name="at"/> takes
		/// (<see cref="ClearFrom"/>…<see cref="ClearOut"/> out, <see cref="ClearBeside"/> along, <see cref="Below"/> under to
		/// <see cref="Above"/> over the fixing): a small fitting (a sign's bracket and board, a beam's end, a spout: a Detail part
		/// thinner than <see cref="ThinPart"/>) by its bounds, anything larger (a chimney, a buttress, a roof's eave) by points
		/// through that room inside it.
		/// </summary>
		private static bool Crowded(Vector3 at, Vector3 n, Vector3 u, float h, List<StructureSolid> solids, StructureSolid own)
		{
			Vector3 face = at - n * Proud;
			var room = new Bounds(face + n * ClearFrom + Vector3.up * (h - Below - at.y), Vector3.zero);
			foreach (float o in new[] { ClearFrom, ClearOut })
			{
				foreach (float a in new[] { -ClearBeside, ClearBeside })
				{
					foreach (float y in new[] { h - Below, h + Above })
					{
						Vector3 p = face + n * o + u * a;
						p.y = y;
						room.Encapsulate(p);
					}
				}
			}
			foreach (StructureSolid s in solids)
			{
				if (s == own || s.Faces.Count < 4 || s.Material == StructureMaterial.Glass || !s.Bounds.Intersects(room))
				{
					continue;
				}
				Vector3 size = s.Bounds.size;
				if (s.Role == StructureRole.Detail && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < ThinPart)
				{
					return true;
				}
				for (int i = 0; i <= 4; i++)
				{
					float o = Mathf.Lerp(ClearFrom, ClearOut, i / 4f);
					for (int j = 0; j <= 4; j++)
					{
						float a = Mathf.Lerp(-ClearBeside, ClearBeside, j / 4f);
						for (int k = 0; k <= 4; k++)
						{
							Vector3 p = face + n * o + u * a;
							p.y = Mathf.Lerp(h - Below, h + Above, k / 4f);
							if (Inside(p, s))
							{
								return true;
							}
						}
					}
				}
			}
			return false;
		}

		/// <summary>A part thinner than this in some direction is avoided by its bounds (points through a light's room could miss it), metres.</summary>
		public const float ThinPart = 0.15f;

		/// <summary>Inside a convex solid: behind every one of its faces' planes (2 cm of slack).</summary>
		private static bool Inside(Vector3 p, StructureSolid s)
		{
			foreach (StructureFace f in s.Faces)
			{
				if (f.Points.Count == 0 || Vector3.Dot(p - f.Points[0], f.Normal) > 0.02f)
				{
					return false;
				}
			}
			return true;
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

		/// <summary>Inside a solid other than <paramref name="own"/>: behind every one of its faces' planes.</summary>
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

		// ── The walls of a site ───────────────────────────────────────

		/// <summary>
		/// The wall stretches of every lived-in building a site has laid (a piece with rooms), where it stands (world), in the
		/// order the site laid them.
		/// </summary>
		public static List<WallSpan> StreetWalls(IReadOnlyList<PointOfInterestSiteContext.PlacedProp> laid)
		{
			var walls = new List<WallSpan>();
			if (laid == null)
			{
				return walls;
			}
			foreach (PointOfInterestSiteContext.PlacedProp prop in laid)
			{
				if (prop.Prefab == null || !StructurePieces.TryParsePrefab(prop.Prefab.name, out StructurePiece piece, out int variant, out _, out StructureRoofPitch? roof)
					|| StructurePieces.RoomsOf(piece, variant).Count == 0)
				{
					continue;
				}
				foreach (WallSpan span in SpansOf(piece, variant, roof))
				{
					Vector3 n = prop.Rotation * span.Normal;
					n.y = 0f;
					if (n.sqrMagnitude < 1e-6f)
					{
						continue;
					}
					walls.Add(new WallSpan
					{
						A = prop.Position + prop.Rotation * Vector3.Scale(span.A, prop.Scale),
						B = prop.Position + prop.Rotation * Vector3.Scale(span.B, prop.Scale),
						Normal = n.normalized,
					});
				}
			}
			return walls;
		}

		// ── The plan ──────────────────────────────────────────────────

		/// <summary>The salt of the pass's own random stream (the site's stream is never drawn on).</summary>
		public const int StreamSalt = 0x57A11;

		/// <summary>
		/// Each street's seed for the variant of its lights and of their posts (StructureKit.PickVariant): one street's lights
		/// alike, the next street's perhaps the other variant. From a stream of the pass's own, keyed by the site, so the same
		/// site lays the same lights on every cut and repaint, and nothing else the site draws moves.
		/// </summary>
		public static int[] StreetSeeds(int siteId, int streets)
		{
			var seeds = new int[Mathf.Max(0, streets)];
			var stream = new DeterministicRNG(unchecked((int)PointOfInterestPlanner.Hash(siteId, StreamSalt, 0x4C, 0x17)));
			for (int i = 0; i < seeds.Length; i++)
			{
				seeds[i] = stream.Next();
			}
			return seeds;
		}

		/// <summary>
		/// A site's street lights: each street (its ends and half its width, world x, z) of 10 m or more walked as the standing
		/// lights are (from 6 m in, every <paramref name="spacing"/> metres, the sides in turn), each light fixed to the nearest
		/// fitting wall on its side (facing across the street within <paramref name="reach"/> of its edge, its stretch within
		/// <see cref="Slide"/> of the spacing along), else on the other side's, else a post at the street's edge where the
		/// standing light would stand. A wall place within half the spacing of a light already hung is taken.
		/// </summary>
		public static List<WallLight> Plan(IReadOnlyList<(Vector2 a, Vector2 b, float half)> streets, IReadOnlyList<WallSpan> walls, float spacing, float reach)
		{
			var lights = new List<WallLight>();
			if (streets == null || spacing <= 0f)
			{
				return lights;
			}
			var hung = new List<Vector2>();
			for (int s = 0; s < streets.Count; s++)
			{
				(Vector2 a, Vector2 b, float half) = streets[s];
				Vector2 ab = b - a;
				float length = ab.magnitude;
				if (length < 10f)
				{
					continue;
				}
				Vector2 along = ab / length, across = new Vector2(-along.y, along.x);
				int side = 1, place = 0;
				for (float t = 6f; t < length - 6f; t += spacing, side = -side, place++)
				{
					if (TryWall(walls, hung, a, along, across, half, length, t, side, spacing, reach, out WallLight light)
						|| TryWall(walls, hung, a, along, across, half, length, t, -side, spacing, reach, out light))
					{
						light.Street = s;
						light.Place = place;
						hung.Add(light.At);
						lights.Add(light);
						continue;
					}
					// No wall in reach: a post where the standing light stands (PropsFeature.AddClutter's street lights).
					Vector2 p = a + along * t + across * side * (half + 0.7f);
					lights.Add(new WallLight
					{
						At = p,
						Height = float.NaN,
						Yaw = Mathf.Atan2(-across.x * side, -across.y * side) * Mathf.Rad2Deg,
						OnWall = false,
						Street = s,
						Place = place,
					});
				}
			}
			return lights;
		}

		private static bool TryWall(IReadOnlyList<WallSpan> walls, List<Vector2> hung, Vector2 a, Vector2 along, Vector2 across, float half, float length,
			float t, int side, float spacing, float reach, out WallLight light)
		{
			light = default;
			if (walls == null)
			{
				return false;
			}
			Vector2 toStreet = -across * side;
			float best = float.MaxValue;
			for (int i = 0; i < walls.Count; i++)
			{
				WallSpan w = walls[i];
				var n = new Vector2(w.Normal.x, w.Normal.z);
				if (Vector2.Dot(n, toStreet) < Facing)
				{
					continue;
				}
				Vector2 pa = new Vector2(w.A.x, w.A.z) - a, pb = new Vector2(w.B.x, w.B.z) - a;
				float da = Vector2.Dot(pa, across) * side, db = Vector2.Dot(pb, across) * side;
				// On this side, behind the street's edge, and near enough to it.
				if (Mathf.Min(da, db) < half - 0.3f || Mathf.Max(da, db) > half + reach)
				{
					continue;
				}
				float ta = Vector2.Dot(pa, along), tb = Vector2.Dot(pb, along);
				float tc = Mathf.Clamp(t, Mathf.Max(0f, Mathf.Min(ta, tb)), Mathf.Min(length, Mathf.Max(ta, tb)));
				if (tc < Mathf.Min(ta, tb) - 1e-4f || tc > Mathf.Max(ta, tb) + 1e-4f || Mathf.Abs(tc - t) > Slide * spacing)
				{
					continue;
				}
				float k = Mathf.Abs(tb - ta) > 1e-4f ? Mathf.Clamp01((tc - ta) / (tb - ta)) : 0.5f;
				Vector3 point = Vector3.Lerp(w.A, w.B, k);
				var at = new Vector2(point.x, point.z);
				bool crowded = false;
				foreach (Vector2 other in hung)
				{
					if ((other - at).sqrMagnitude < 0.25f * spacing * spacing)
					{
						crowded = true;
						break;
					}
				}
				if (crowded)
				{
					continue;
				}
				float d = Vector2.Dot(at - a, across) * side - half;
				float score = Mathf.Abs(tc - t) + 0.25f * Mathf.Max(0f, d);
				if (score < best)
				{
					best = score;
					light = new WallLight { At = at, Height = point.y, Yaw = YawFacing(n), OnWall = true };
				}
			}
			return best < float.MaxValue;
		}
	}
}
#endif
