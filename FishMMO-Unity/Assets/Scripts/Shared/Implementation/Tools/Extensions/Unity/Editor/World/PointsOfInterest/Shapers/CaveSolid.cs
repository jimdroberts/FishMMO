#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Which cave a site is carved as: its proportions, its walk and its stone.</summary>
	public enum CaveForm : byte
	{
		/// <summary>A walk-in cave, small to large; a large one ends where a dungeon's entrance goes.</summary>
		Cave = 0,
		/// <summary>A short passage opening into a broad chamber.</summary>
		Grotto = 1,
		/// <summary>A cave opened by the waves: its floor at the waterline, the sea running in.</summary>
		SeaCave = 2,
		/// <summary>A cave in ice: smoother walls, the ice's own material.</summary>
		IceCave = 3,
		/// <summary>A drained lava conduit: long, low, wide, nearly level, basalt.</summary>
		LavaTube = 4,
	}

	/// <summary>
	/// The ground over a cave: a heightfield patch sampled once on the main thread (terrain queries are main-thread
	/// only) and read bilinearly from the mesher's worker threads.
	/// </summary>
	public sealed class CaveGround
	{
		public readonly float OriginX, OriginZ, Spacing;
		public readonly int Width, Depth;
		public readonly float[] Heights;

		public CaveGround(float originX, float originZ, float spacing, int width, int depth, float[] heights)
		{
			OriginX = originX;
			OriginZ = originZ;
			Spacing = spacing;
			Width = width;
			Depth = depth;
			Heights = heights;
		}

		/// <summary>
		/// The ground over [minX, maxX] × [minZ, maxZ], sampled on a grid of <paramref name="spacing"/> whose lines pass
		/// through (<paramref name="alignX"/>, <paramref name="alignZ"/>): the terrain's own samples when those are its
		/// heightmap's, so reading it back bilinearly is the terrain's own interpolation.
		/// </summary>
		public static CaveGround Sample(Func<float, float, float> ground, float minX, float minZ, float maxX, float maxZ, float spacing,
			float alignX = 0f, float alignZ = 0f)
		{
			spacing = Mathf.Max(0.05f, spacing);
			int i0 = Mathf.FloorToInt((minX - alignX) / spacing), i1 = Mathf.CeilToInt((maxX - alignX) / spacing);
			int k0 = Mathf.FloorToInt((minZ - alignZ) / spacing), k1 = Mathf.CeilToInt((maxZ - alignZ) / spacing);
			int width = Mathf.Max(2, i1 - i0 + 1), depth = Mathf.Max(2, k1 - k0 + 1);
			float ox = alignX + i0 * spacing, oz = alignZ + k0 * spacing;
			var heights = new float[width * depth];
			for (int k = 0; k < depth; k++)
			{
				for (int i = 0; i < width; i++)
				{
					heights[k * width + i] = ground(ox + i * spacing, oz + k * spacing);
				}
			}
			return new CaveGround(ox, oz, spacing, width, depth, heights);
		}

		/// <summary>The ground at (x, z), world metres, bilinear; clamped to the patch's edge outside it.</summary>
		public float At(float x, float z)
		{
			float u = Mathf.Clamp((x - OriginX) / Spacing, 0f, Width - 1.0001f);
			float v = Mathf.Clamp((z - OriginZ) / Spacing, 0f, Depth - 1.0001f);
			int i = (int)u, k = (int)v;
			float fu = u - i, fv = v - k;
			int a = k * Width + i;
			float h0 = Heights[a] + (Heights[a + 1] - Heights[a]) * fu;
			float h1 = Heights[a + Width] + (Heights[a + Width + 1] - Heights[a + Width]) * fu;
			return h0 + (h1 - h0) * fv;
		}

		public bool Contains(float x, float z, float margin)
			=> x >= OriginX + margin && z >= OriginZ + margin && x <= OriginX + (Width - 1) * Spacing - margin && z <= OriginZ + (Depth - 1) * Spacing - margin;
	}

	/// <summary>One point of a cave's centre line, in the cave's frame (metres from its mouth's floor).</summary>
	public struct CaveNode
	{
		public Vector3 Centre;
		public float Radius;
		/// <summary>The floor's height under this point (the tube is cut flat there so it can be walked).</summary>
		public float Floor;
		/// <summary>Distance along the centre line from the mouth, metres.</summary>
		public float Along;
	}

	/// <summary>
	/// A carved cave as one signed field: the tunnel a seeded walk into the slope (a chain of capsules with flat floors),
	/// ending in an ellipsoid chamber, with rock noise on its walls; and the rock SHELL round it that is meshed — its inner
	/// face is the cave's wall seen from inside, its outer face is buried. Pure: no scene, no terrain, no editor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a shell and not the tunnel's own surface.</b> The terrain is holed where the tunnel meets it, a whole
	/// heightmap quad at a time (<see cref="HoleCells"/>), so the hole is a ragged outline larger than the opening. The
	/// shell is thick enough near the mouth to fill every holed quad (<see cref="Collar"/>, measured from the holes
	/// themselves), and stands <see cref="Rim"/> above the ground there: from outside the opening has a rock lip over the
	/// hole's edge, from inside every view out meets rock or sky, never the underside of the terrain. Deeper in, it is a
	/// thin skin kept <see cref="Skin"/> under the ground, never seen. A closed shell also validates like every other
	/// generated rock (<see cref="ProceduralSurfaceNets.Validate"/>), and its inner face's normals point into the void.
	/// </para>
	/// <para>
	/// <b>The walk.</b> From the mouth's floor, into the slope (the site's heading turned round), a step about a radius
	/// long at a time; each step's heading wanders a few degrees and is pulled back toward straight in, its pitch never
	/// over <see cref="ProfileFor"/>'s limit (15°, 5° for a lava tube) so the floor is walkable all the way, and it keeps at
	/// least <see cref="MinCover"/> of rock over its roof once past the mouth: where a step would not, it dives, then turns
	/// uphill and dives, and the walk ends where neither does. The chamber (1.5–2.5 radii) then has to fit under the same
	/// cover, else it shrinks, else the walk gives back a step.
	/// </para>
	/// <para>
	/// <b>Coordinates.</b> Everything is in the cave's frame: world axes, origin at <see cref="Origin"/> (the mouth's floor),
	/// so the meshes are small numbers and the rock material's metre UVs line up with the world's.
	/// </para>
	/// </remarks>
	public sealed class CaveSolid
	{
		/// <summary>Bumped when the stored values change meaning (<see cref="ToValues"/>).</summary>
		public const int Format = 1;
		/// <summary>The least rock over the roof past the mouth, metres.</summary>
		public const float MinCover = 3f;
		/// <summary>Floor depth under a node's centre, as a share of its (vertical) radius.</summary>
		public const float FloorDepth = 0.45f;
		/// <summary>
		/// The shell's apron is narrower up to this height over the mouth's floor (<see cref="CollarLow"/>): what lies on the
		/// ground in front of the mouth, metres.
		/// </summary>
		public const float ApronBand = 2.5f;
		/// <summary>Faces meeting at more than this keep a hard normal edge.</summary>
		public const float CreaseDegrees = 50f;
		/// <summary>The highest a sea cave's foot may stand over the sea, metres: its floor is laid awash under it.</summary>
		public const float SeaFootMetres = 4f;

		public CaveForm Form;
		public byte SizeClass;
		public int Seed;
		/// <summary>The direction the mouth faces, degrees about +y (0 = +z), out of the slope.</summary>
		public float Yaw;
		/// <summary>The cave's frame origin, world: the mouth's floor.</summary>
		public Vector3 Origin;
		/// <summary>The tunnel's nominal radius, metres.</summary>
		public float Radius;
		/// <summary>Vertical radius over horizontal: 1 a round bore, under 1 a low, wide lava tube.</summary>
		public float Flat = 1f;
		/// <summary>Wall noise as a share of the radius.</summary>
		public float NoiseShare = 0.3f;
		/// <summary>How far the shell stands over the ground round the mouth, metres.</summary>
		public float Rim = 0.35f;
		/// <summary>How far under the ground the buried shell's top is kept, metres.</summary>
		public float Skin = 1f;
		/// <summary>How far from the tunnel the shell rises to the rim: past every holed quad (from <see cref="HoleCells"/>).</summary>
		public float Collar = 1.5f;
		/// <summary>The collar near the mouth's floor (<see cref="ApronBand"/>): only as wide as the holes down there need.</summary>
		public float CollarLow = 1.5f;
		/// <summary>The shell's thickness at the mouth and deep in, metres.</summary>
		public float ShellMouth = 3f, ShellDeep = 2f;
		/// <summary>Distance along the line over which the roof may still be thin: the mouth.</summary>
		public float MouthZone = 12f;
		/// <summary>The meshing cell, metres.</summary>
		public float MeshCell = 0.5f;
		public readonly List<CaveNode> Nodes = new List<CaveNode>();
		/// <summary>The chamber: its centre, radii (along the line, up, across) and floor, all in the cave's frame.</summary>
		public Vector3 ChamberCentre, ChamberRadii;
		public float ChamberFloor;
		/// <summary>The chamber's heading, degrees about +y: its long axis.</summary>
		public float ChamberYaw;

		// Derived (Prepare): per-segment data for a fast field.
		private Vector3[] segA, segAB, segMin, segMax;
		private float[] segLen2, segLen;
		private Vector3 chamberX, chamberZ;
		private float blend, floorBlend, edgeBlend, amp;

		/// <summary>The length of the centre line, metres.</summary>
		public float Length => Nodes.Count > 0 ? Nodes[Nodes.Count - 1].Along : 0f;

		/// <summary>The chamber floor's centre, world: where a camp, a nest or a dungeon's entrance stands.</summary>
		public Vector3 ChamberFloorWorld => Origin + new Vector3(ChamberCentre.x, ChamberFloor, ChamberCentre.z);

		/// <summary>The chamber's walkable reach from its floor's centre, metres (the narrower radius less a wall's noise).</summary>
		public float ChamberFloorRadius => Mathf.Max(1f, Mathf.Min(ChamberRadii.x, ChamberRadii.z) * 0.8f - NoiseShare * Radius);

		// ── Proportions ───────────────────────────────────────────

		/// <summary>A form's proportions at a size: radius, length, pitch and turn limits, cross-section, chamber, noise.</summary>
		public struct Profile
		{
			public float RadiusMin, RadiusMax, LengthMin, LengthMax, Pitch, Turn, Flat, ChamberMin, ChamberMax, Noise;
		}

		/// <summary>
		/// Grottos and small caves 2–4 m in radius, large ones 4–8; grottos 12–25 m long, small caves 25–50, large 50–120;
		/// a lava tube long, low (its height 0.6 of its width) and nearly level; ice smoother than rock.
		/// </summary>
		public static Profile ProfileFor(CaveForm form, int size)
		{
			size = Mathf.Clamp(size, 0, 2);
			switch (form)
			{
				case CaveForm.Grotto:
					return new Profile { RadiusMin = 2f + 0.4f * size, RadiusMax = 2.8f + 0.6f * size, LengthMin = 12f, LengthMax = 25f, Pitch = 12f, Turn = 14f, Flat = 1f, ChamberMin = 1.9f, ChamberMax = 2.5f, Noise = 0.3f };
				case CaveForm.LavaTube:
					return size == 0 ? new Profile { RadiusMin = 3f, RadiusMax = 4.5f, LengthMin = 50f, LengthMax = 80f, Pitch = 5f, Turn = 6f, Flat = 0.62f, ChamberMin = 1.5f, ChamberMax = 2f, Noise = 0.18f }
						: size == 1 ? new Profile { RadiusMin = 4f, RadiusMax = 6f, LengthMin = 70f, LengthMax = 110f, Pitch = 5f, Turn = 6f, Flat = 0.62f, ChamberMin = 1.5f, ChamberMax = 2f, Noise = 0.18f }
						: new Profile { RadiusMin = 5f, RadiusMax = 8f, LengthMin = 90f, LengthMax = 120f, Pitch = 5f, Turn = 6f, Flat = 0.62f, ChamberMin = 1.5f, ChamberMax = 2f, Noise = 0.18f };
				default:
					float noise = form == CaveForm.IceCave ? 0.2f : 0.3f;
					float shorter = form == CaveForm.SeaCave ? 0.8f : 1f;
					return size == 0 ? new Profile { RadiusMin = 2f, RadiusMax = 3.5f, LengthMin = 25f * shorter, LengthMax = 50f * shorter, Pitch = 15f, Turn = 12f, Flat = 1f, ChamberMin = 1.5f, ChamberMax = 2.2f, Noise = noise }
						: size == 1 ? new Profile { RadiusMin = 3f, RadiusMax = 5f, LengthMin = 35f * shorter, LengthMax = 75f * shorter, Pitch = 15f, Turn = 12f, Flat = 1f, ChamberMin = 1.5f, ChamberMax = 2.3f, Noise = noise }
						: new Profile { RadiusMin = 4f, RadiusMax = 8f, LengthMin = 50f * shorter, LengthMax = 120f * shorter, Pitch = 15f, Turn = 10f, Flat = 1f, ChamberMin = 1.6f, ChamberMax = 2.5f, Noise = noise };
			}
		}

		/// <summary>The triangle budget of each level: LOD0 2,500 for a small cave (and every grotto), 6,000 for a large one.</summary>
		public static int[] LodTriangles(CaveForm form, int size)
		{
			int top = form == CaveForm.Grotto ? 2500 : size <= 0 ? 2500 : size == 1 ? 4000 : 6000;
			return new[] { top, Mathf.RoundToInt(top * 0.45f), Mathf.RoundToInt(top * 0.18f) };
		}

		// ── Planning ──────────────────────────────────────────────

		/// <summary>
		/// Walks a cave into the slope behind a mouth. Returns null, with <paramref name="problem"/> saying why, where the rock
		/// is too thin or too low for one. Deterministic in its inputs.
		/// </summary>
		/// <param name="mouth">The mouth's foot, world (its y is ignored: the ground there is read).</param>
		/// <param name="yaw">The direction the mouth faces, out of the slope, degrees about +y.</param>
		/// <param name="ground">The ground over everywhere the walk may go (at least the form's longest length round the mouth).</param>
		/// <param name="seaLevel">The sea's surface, world y: a sea cave's floor is laid just under it.</param>
		public static CaveSolid Plan(CaveForm form, int size, Vector3 mouth, float yaw, int seed, CaveGround ground, float seaLevel, out string problem)
		{
			problem = null;
			var rng = new DeterministicRNG(seed);
			Profile pf = ProfileFor(form, size);
			float r0 = rng.Range(pf.RadiusMin, pf.RadiusMax);
			float length = rng.Range(pf.LengthMin, pf.LengthMax);
			float amp = pf.Noise * r0;
			var solid = new CaveSolid
			{
				Form = form,
				SizeClass = (byte)Mathf.Clamp(size, 0, 2),
				Seed = seed,
				Yaw = Mathf.Repeat(yaw, 360f),
				Radius = r0,
				Flat = pf.Flat,
				NoiseShare = pf.Noise,
				MeshCell = Mathf.Clamp(r0 / 5f, 0.35f, 1f),
			};
			float yr = yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));

			// The site is the face's foot as the planner scores it, which may stand a few metres out on the apron; the mouth
			// opens where the slope behind it turns steep (40° over two metres), or at the site if it never does.
			float mx = mouth.x, mz = mouth.z;
			for (float d = 0f; d <= 20f; d += 0.5f)
			{
				float x = mouth.x - outward.x * d, z = mouth.z - outward.z * d;
				if (!ground.Contains(x, z, 4f))
				{
					break;
				}
				if (ground.At(x - outward.x * 2f, z - outward.z * 2f) - ground.At(x, z) >= 1.68f)
				{
					mx = x;
					mz = z;
					break;
				}
			}
			// The mouth's floor: level with the rock lip round it, just over the foot; a sea cave's just under the sea, the
			// waves running in, so its foot must be near the waterline.
			float foot = ground.At(mx, mz);
			float floor0 = foot + solid.Rim;
			if (form == CaveForm.SeaCave)
			{
				if (foot > seaLevel + SeaFootMetres)
				{
					problem = $"the face's foot stands {foot - seaLevel:0.#} m over the sea, too high for the waves to have opened it";
					return null;
				}
				floor0 = seaLevel - 0.4f;
			}
			solid.Origin = new Vector3(mx, floor0, mz);
			solid.MouthZone = Mathf.Max(14f, 3.5f * r0);
			float level = form == CaveForm.SeaCave ? 0.6f * r0 + 6f : 0.8f * r0 + 1f;

			float Ground(Vector3 local) => ground.At(local.x + mx, local.z + mz) - floor0;
			float Cover(Vector3 centre, float radius, Vector3 previous, float heading)
			{
				float top = centre.y + radius * pf.Flat + amp;
				float hr = heading * Mathf.Deg2Rad;
				var side = new Vector3(Mathf.Cos(hr), 0f, -Mathf.Sin(hr)) * (0.9f * radius);
				float cover = Ground(centre) - top;
				cover = Mathf.Min(cover, Ground(centre + side) - top);
				cover = Mathf.Min(cover, Ground(centre - side) - top);
				Vector3 mid = (centre + previous) * 0.5f;
				return Mathf.Min(cover, Ground(mid) - top);
			}
			bool Inside(Vector3 local, float reach) => ground.Contains(local.x + mx, local.z + mz, reach);

			float step = Mathf.Clamp(0.9f * r0, 2.5f, 5f);
			float inward = Mathf.Repeat(yaw + 180f, 360f);
			float heading = inward, pitch = 0f, radius = r0, floor = 0f, along = 0f;
			Vector3 centre = outward * (0.2f * r0) + Vector3.up * (FloorDepth * r0 * pf.Flat);
			solid.Nodes.Add(new CaveNode { Centre = centre, Radius = r0, Floor = 0f, Along = 0f });
			while (along < length)
			{
				// Every step draws the same numbers whichever candidate it keeps.
				float turn = rng.Range(-pf.Turn, pf.Turn);
				float tilt = rng.Range(-5f, 5f);
				float swell = rng.Range(0.93f, 1.07f);
				float nextRadius = Mathf.Clamp(radius * swell, 0.85f * r0, 1.15f * r0);
				float wander = heading + turn;
				wander += DeltaAngle(wander, inward) * 0.2f;
				wander = inward + Mathf.Clamp(DeltaAngle(inward, wander), -55f, 55f);
				float uphill = UphillHeading(ground, centre.x + mx, centre.z + mz, inward);
				bool entering = along + step <= level;
				// Level through the entrance; then a wandering pitch, diving a little more while the roof is still thin.
				float wanderPitch = entering ? 0f : Mathf.Clamp(pitch + tilt - (along < solid.MouthZone ? 2f : 0f), -pf.Pitch, pf.Pitch);
				var candidates = new (float heading, float pitch)[]
				{
					(wander, wanderPitch),
					(wander, entering ? 0f : -pf.Pitch),
					(heading + Mathf.Clamp(DeltaAngle(heading, uphill), -2f * pf.Turn, 2f * pf.Turn), entering ? 0f : -pf.Pitch),
				};
				bool taken = false;
				foreach ((float h, float p) in candidates)
				{
					float hr = h * Mathf.Deg2Rad;
					float run = step * Mathf.Cos(p * Mathf.Deg2Rad);
					float nextFloor = floor + step * Mathf.Sin(p * Mathf.Deg2Rad);
					var next = new Vector3(centre.x + Mathf.Sin(hr) * run, nextFloor + FloorDepth * nextRadius * pf.Flat, centre.z + Mathf.Cos(hr) * run);
					if (!Inside(next, nextRadius + 8f))
					{
						continue;
					}
					float nextAlong = along + step;
					float cover = Cover(next, nextRadius, centre, h);
					// Through the mouth the bore need only be going under the ground (its centre line below it); from the end of
					// the mouth on, the full cover over its roof.
					float need = nextAlong <= level ? float.NegativeInfinity
						: nextAlong >= solid.MouthZone ? MinCover
						: -(nextRadius * pf.Flat + amp);
					if (cover < need)
					{
						continue;
					}
					// A sea cave's floor stays awash through its mouth: never above the sea there, never deep under it.
					if (form == CaveForm.SeaCave && nextAlong <= solid.MouthZone && Mathf.Abs(nextFloor) > 0.6f)
					{
						continue;
					}
					centre = next;
					heading = h;
					pitch = p;
					radius = nextRadius;
					floor = nextFloor;
					along = nextAlong;
					solid.Nodes.Add(new CaveNode { Centre = centre, Radius = radius, Floor = floor, Along = along });
					taken = true;
					break;
				}
				if (!taken)
				{
					break;
				}
			}
			float shortest = form == CaveForm.Grotto ? 8f : Mathf.Max(solid.MouthZone + step, 0.5f * pf.LengthMin);
			if (along < shortest)
			{
				problem = along < solid.MouthZone
					? $"the face is too low or too thin to take a {r0:0.#} m bore under {MinCover} m of rock ({along:0} m in)"
					: $"only {along:0} m of the {pf.LengthMin:0} m a cave this size needs fits under {MinCover} m of rock";
				return null;
			}

			// The chamber, under the same cover: shrunk, then a step given back, until it fits.
			for (int attempt = 0; attempt < 8 && solid.Nodes.Count >= 3; attempt++)
			{
				CaveNode last = solid.Nodes[solid.Nodes.Count - 1];
				CaveNode before = solid.Nodes[solid.Nodes.Count - 2];
				Vector3 dir = last.Centre - before.Centre;
				dir.y = 0f;
				dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : -outward;
				float scale = rng.Range(pf.ChamberMin, pf.ChamberMax);
				float across = rng.Range(0.75f, 1f);
				for (int shrink = 0; shrink < 4; shrink++, scale = Mathf.Max(pf.ChamberMin * 0.95f, scale * 0.88f))
				{
					float rx = last.Radius * scale, rz = rx * across;
					float ry = Mathf.Max(3.2f, 1.15f * last.Radius * pf.Flat);
					Vector3 c = last.Centre + dir * (0.55f * rx);
					c.y = last.Floor + 0.4f * ry;
					float top = c.y + ry + amp;
					float cover = Ground(c) - top;
					var side = new Vector3(dir.z, 0f, -dir.x);
					for (int k = 0; k < 8; k++)
					{
						float a = k * Mathf.PI / 4f;
						Vector3 at = c + dir * (Mathf.Cos(a) * 0.9f * rx) + side * (Mathf.Sin(a) * 0.9f * rz);
						cover = Mathf.Min(cover, Ground(at) - top);
					}
					if (cover >= MinCover && Inside(c, Mathf.Max(rx, rz) + 8f))
					{
						solid.ChamberCentre = c;
						solid.ChamberRadii = new Vector3(rx, ry, rz);
						solid.ChamberFloor = last.Floor;
						solid.ChamberYaw = Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f);
						solid.Prepare();
						return solid;
					}
				}
				if (solid.Nodes.Count - 1 <= 2 || last.Along < shortest)
				{
					break;
				}
				solid.Nodes.RemoveAt(solid.Nodes.Count - 1);
			}
			problem = "no chamber fits under the rock at the tunnel's end";
			return null;
		}

		/// <summary>The heading up the ground's steepest slope at (x, z), world; <paramref name="fallback"/> on the flat.</summary>
		private static float UphillHeading(CaveGround ground, float x, float z, float fallback)
		{
			float gx = ground.At(x + 2f, z) - ground.At(x - 2f, z), gz = ground.At(x, z + 2f) - ground.At(x, z - 2f);
			return gx * gx + gz * gz < 1e-6f ? fallback : Mathf.Repeat(Mathf.Atan2(gx, gz) * Mathf.Rad2Deg, 360f);
		}

		/// <summary>The signed shortest turn from <paramref name="from"/> to <paramref name="to"/>, degrees.</summary>
		public static float DeltaAngle(float from, float to)
		{
			float d = Mathf.Repeat(to - from, 360f);
			return d > 180f ? d - 360f : d;
		}

		// ── The holes ─────────────────────────────────────────────

		/// <summary>
		/// How near (metres) the terrain may come to the tunnel before its quad is holed: the rim the floor stands over the
		/// foot, and half the fillet where the shell's lip meets the tunnel (which rounds the rock back by up to a quarter of
		/// it), so no terrain shows through a rounded lip or floor edge.
		/// </summary>
		public float HoleMargin => Rim + 1.25f * MeshCell;

		/// <summary>
		/// The terrain's hole quads the cave opens: every quad of the grid (<paramref name="step"/> metres, lines through
		/// <paramref name="gridX"/>, <paramref name="gridZ"/>) whose surface comes within <see cref="HoleMargin"/> of the
		/// tunnel, its surface read at its corners, edge middles and centre. Sets <see cref="Collar"/> and the shell's
		/// thickness from how far the furthest of them lies from the tunnel, so the shell fills every one. Each cell is
		/// (column, row) on the grid: the quad from gridX + column·step to the next line.
		/// </summary>
		public List<Vector2Int> HoleCells(Func<float, float, float> groundWorld, float step, float gridX, float gridZ)
		{
			var cells = new List<Vector2Int>();
			Bounds box = TunnelBounds(Radius * 0.5f + 2f * step);
			int i0 = Mathf.FloorToInt((box.min.x + Origin.x - gridX) / step), i1 = Mathf.CeilToInt((box.max.x + Origin.x - gridX) / step);
			int k0 = Mathf.FloorToInt((box.min.z + Origin.z - gridZ) / step), k1 = Mathf.CeilToInt((box.max.z + Origin.z - gridZ) / step);
			float needed = 0f, neededLow = 0f;
			var heights = new float[9];
			for (int k = k0; k < k1; k++)
			{
				for (int i = i0; i < i1; i++)
				{
					float x0 = gridX + i * step, z0 = gridZ + k * step;
					float lo = float.MaxValue, hi = float.MinValue;
					for (int s = 0; s < 9; s++)
					{
						float x = x0 + (s % 3) * 0.5f * step, z = z0 + (s / 3) * 0.5f * step;
						heights[s] = groundWorld(x, z);
						lo = Mathf.Min(lo, heights[s]);
						hi = Mathf.Max(hi, heights[s]);
					}
					// Far from the tunnel at its centre by more than the quad spans: no point of it can be near.
					float reach = Mathf.Sqrt(2f * step * step + (hi - lo) * (hi - lo));
					Vector3 mid = new Vector3(x0 + 0.5f * step, heights[4], z0 + 0.5f * step) - Origin;
					if (Smooth(mid) - amp > reach + HoleMargin)
					{
						continue;
					}
					float nearest = float.MaxValue, furthest = 0f, furthestLow = 0f;
					for (int s = 0; s < 9; s++)
					{
						var p = new Vector3(x0 + (s % 3) * 0.5f * step, heights[s], z0 + (s / 3) * 0.5f * step) - Origin;
						nearest = Mathf.Min(nearest, Void(p));
						if (p.y < ApronBand)
						{
							furthestLow = Mathf.Max(furthestLow, Smooth(p));
						}
						else
						{
							furthest = Mathf.Max(furthest, Smooth(p));
						}
					}
					if (nearest < HoleMargin)
					{
						cells.Add(new Vector2Int(i, k));
						needed = Mathf.Max(needed, furthest);
						neededLow = Mathf.Max(neededLow, furthestLow);
					}
				}
			}
			Cover(needed, neededLow);
			return cells;
		}

		/// <summary>
		/// Sets the collar and shell thickness for holes reaching <paramref name="needed"/> metres from the tunnel, and
		/// <paramref name="neededLow"/> on the ground in front of the mouth.
		/// </summary>
		public void Cover(float needed, float neededLow)
		{
			float h = MeshCell;
			Collar = Mathf.Max(needed + 0.4f, 3f * h);
			CollarLow = Mathf.Max(neededLow + 0.4f, 3f * h);
			ShellMouth = Mathf.Max(Collar, CollarLow) + NoiseShare * Radius + 2f * h;
			ShellDeep = Mathf.Max(3f * h, 1.2f) + NoiseShare * Radius;
		}

		// ── The field ─────────────────────────────────────────────

		/// <summary>Builds the per-segment tables the field reads; call after the nodes or chamber change.</summary>
		public void Prepare()
		{
			int n = Mathf.Max(0, Nodes.Count - 1);
			segA = new Vector3[n];
			segAB = new Vector3[n];
			segMin = new Vector3[n];
			segMax = new Vector3[n];
			segLen2 = new float[n];
			segLen = new float[n];
			for (int i = 0; i < n; i++)
			{
				CaveNode a = Nodes[i], b = Nodes[i + 1];
				segA[i] = a.Centre;
				segAB[i] = b.Centre - a.Centre;
				segLen2[i] = Mathf.Max(1e-6f, segAB[i].sqrMagnitude);
				segLen[i] = Mathf.Sqrt(segLen2[i]);
				float r = Mathf.Max(a.Radius, b.Radius);
				segMin[i] = Vector3.Min(a.Centre, b.Centre) - Vector3.one * r;
				segMax[i] = Vector3.Max(a.Centre, b.Centre) + Vector3.one * r;
			}
			float cy = ChamberYaw * Mathf.Deg2Rad;
			chamberZ = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
			chamberX = new Vector3(chamberZ.z, 0f, -chamberZ.x);
			amp = NoiseShare * Radius;
			// Blends span several meshing cells (sub-cell creases break the net) and soften the tube's joints and floor edge.
			blend = Mathf.Max(2.5f * MeshCell, 0.3f * Radius);
			floorBlend = Mathf.Max(2.5f * MeshCell, 0.25f * Radius);
			edgeBlend = 2.5f * MeshCell;
		}

		/// <summary>The bounds of the tunnel and chamber, grown by <paramref name="grow"/>, in the cave's frame.</summary>
		public Bounds TunnelBounds(float grow)
		{
			var b = new Bounds(Nodes[0].Centre, Vector3.zero);
			foreach (CaveNode node in Nodes)
			{
				b.Encapsulate(node.Centre + Vector3.one * (node.Radius + grow));
				b.Encapsulate(node.Centre - Vector3.one * (node.Radius + grow));
			}
			float c = Mathf.Max(ChamberRadii.x, Mathf.Max(ChamberRadii.y, ChamberRadii.z)) + grow;
			b.Encapsulate(ChamberCentre + Vector3.one * c);
			b.Encapsulate(ChamberCentre - Vector3.one * c);
			return b;
		}

		/// <summary>The box the shell is meshed in (the cave's frame): the tunnel grown by the shell, the noise and two cells.</summary>
		public Bounds Extent => TunnelBounds(Mathf.Max(ShellMouth, ShellDeep) + NoiseShare * Radius + 2f * MeshCell);

		/// <summary>The bare tube and chamber (no noise), negative inside the void; also where along the line the point is nearest.</summary>
		private float Tube(Vector3 p, out float along, out float radius, out float floor)
		{
			float d = float.MaxValue;
			float best = float.MaxValue;
			along = 0f;
			radius = Radius;
			floor = 0f;
			float invFlat = 1f / Mathf.Max(0.2f, Flat);
			for (int i = 0; i < segA.Length; i++)
			{
				// Outside this segment's box by more than the field so far (and its blend): it cannot matter.
				float ox = Mathf.Max(segMin[i].x - p.x, p.x - segMax[i].x);
				float oy = Mathf.Max(segMin[i].y - p.y, p.y - segMax[i].y);
				float oz = Mathf.Max(segMin[i].z - p.z, p.z - segMax[i].z);
				if (Mathf.Max(ox, Mathf.Max(oy, oz)) > d + blend)
				{
					continue;
				}
				Vector3 ap = p - segA[i];
				float t = Mathf.Clamp01(Vector3.Dot(ap, segAB[i]) / segLen2[i]);
				Vector3 q = ap - segAB[i] * t;
				q.y *= invFlat;
				CaveNode a = Nodes[i], b = Nodes[i + 1];
				float r = a.Radius + (b.Radius - a.Radius) * t;
				float fl = a.Floor + (b.Floor - a.Floor) * t;
				float cap = q.magnitude - r;
				float seg = SMax(cap, fl - p.y, floorBlend);
				if (seg < best)
				{
					best = seg;
					along = a.Along + segLen[i] * t;
					radius = r;
					floor = fl;
				}
				d = d == float.MaxValue ? seg : SMin(d, seg, blend);
			}
			// The chamber: an ellipsoid on its own flat floor.
			Vector3 c = p - ChamberCentre;
			var e = new Vector3(Vector3.Dot(c, chamberZ), c.y, Vector3.Dot(c, chamberX));
			var r1 = new Vector3(e.x / ChamberRadii.x, e.y / ChamberRadii.y, e.z / ChamberRadii.z);
			var r2 = new Vector3(r1.x / ChamberRadii.x, r1.y / ChamberRadii.y, r1.z / ChamberRadii.z);
			float k0 = r1.magnitude, k1 = Mathf.Max(1e-6f, r2.magnitude);
			float room = SMax(k0 * (k0 - 1f) / k1, ChamberFloor - p.y, floorBlend);
			if (room < best)
			{
				along = Length + Vector3.Dot(c, chamberZ) * 0.5f;
				radius = Mathf.Max(ChamberRadii.x, ChamberRadii.z);
				floor = ChamberFloor;
			}
			return d == float.MaxValue ? room : SMin(d, room, 1.5f * blend);
		}

		/// <summary>The void without its wall noise (the shell's outer face is an offset of it).</summary>
		public float Smooth(Vector3 p) => Tube(p, out _, out _, out _);

		/// <summary>The cave's void, negative inside: the tube with rock noise on its walls and roof and a near-flat floor.</summary>
		public float Void(Vector3 p) => Void(p, out _, out _);

		private float Void(Vector3 p, out float smooth, out float along)
		{
			smooth = Tube(p, out along, out float radius, out float floor);
			// Full noise from half a radius over the floor up; the floor itself only a few centimetres of bumps.
			float w = Mathf.Clamp01((p.y - floor) / Mathf.Max(0.5f, 0.5f * radius * Flat));
			w = w * w * (3f - 2f * w);
			float noise = ProceduralNoise.Fbm3(p / (0.9f * Radius), 2, 0.5f, Seed + 17);
			return smooth - amp * w * noise - 0.06f * (1f - w) * ProceduralNoise.Gradient3(p / 1.3f, Seed + 23);
		}

		/// <summary>The shell's thickness at a distance along the line: the mouth's out to the end of the mouth, easing to the deep one.</summary>
		private float ShellAt(float along)
		{
			float t = Mathf.Clamp01((along - MouthZone) / Mathf.Max(1f, 2f * Radius));
			t = t * t * (3f - 2f * t);
			return ShellMouth + (ShellDeep - ShellMouth) * t;
		}

		/// <summary>
		/// The rock shell, negative inside the rock: outside the void and inside its offset; under the ground by the skin,
		/// except within the collar of the tunnel, where it may stand the rim over the ground.
		/// </summary>
		/// <param name="ground">The ground, world.</param>
		public float Field(Vector3 p, CaveGround ground)
		{
			float v = Void(p, out float smooth, out float along);
			float shell = Mathf.Max(-v, smooth - ShellAt(along));
			float g = ground.At(p.x + Origin.x, p.z + Origin.z) - Origin.y;
			float buried = p.y - (g - Skin);
			// The apron on the ground before the mouth only as wide as its holes; the lip round the opening as wide as the face's.
			float t = Mathf.Clamp01((p.y - (ApronBand - 2f)) / 2f);
			float width = CollarLow + (Collar - CollarLow) * (t * t * (3f - 2f * t));
			float collar = Mathf.Max(smooth - width, p.y - (g + Rim));
			float clip = SMin(buried, collar, edgeBlend);
			return SMax(shell, clip, edgeBlend);
		}

		/// <summary>Polynomial smooth minimum: within <paramref name="k"/> of each other the two are blended into a fillet.</summary>
		public static float SMin(float a, float b, float k)
		{
			float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
			return Mathf.Min(a, b) - h * h * k * 0.25f;
		}

		public static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

		// ── Meshes ────────────────────────────────────────────────

		/// <summary>
		/// Every level of the shell, validated (<see cref="ProceduralSurfaceNets.BuildLevels"/>: throws rather than return a
		/// broken mesh), in the cave's frame (add <see cref="Origin"/> for world).
		/// </summary>
		public MeshBuilder[] BuildMeshes(CaveGround ground, out string report)
		{
			if (segA == null)
			{
				Prepare();
			}
			return ProceduralSurfaceNets.BuildLevels(p => Field(p, ground), Extent, MeshCell, LodTriangles(Form, SizeClass), CreaseDegrees, out report);
		}

		// ── Storage ───────────────────────────────────────────────

		private const int HeaderCount = 25, NodeStride = 6;

		/// <summary>Everything needed to build the same cave again, as plain floats (a <see cref="PointOfInterestShape"/>'s values).</summary>
		public float[] ToValues()
		{
			var values = new float[HeaderCount + Nodes.Count * NodeStride];
			values[0] = Format;
			values[1] = (float)Form;
			values[2] = SizeClass;
			values[3] = Radius;
			values[4] = Flat;
			values[5] = NoiseShare;
			values[6] = Rim;
			values[7] = Skin;
			values[8] = Collar;
			values[9] = ShellMouth;
			values[10] = ShellDeep;
			values[11] = MouthZone;
			values[12] = MeshCell;
			values[13] = ChamberCentre.x;
			values[14] = ChamberCentre.y;
			values[15] = ChamberCentre.z;
			values[16] = ChamberRadii.x;
			values[17] = ChamberRadii.y;
			values[18] = ChamberRadii.z;
			values[19] = ChamberFloor;
			values[20] = ChamberYaw;
			values[21] = Yaw;
			values[22] = Seed;
			values[23] = Nodes.Count;
			values[24] = CollarLow;
			for (int i = 0; i < Nodes.Count; i++)
			{
				int o = HeaderCount + i * NodeStride;
				CaveNode node = Nodes[i];
				values[o] = node.Centre.x;
				values[o + 1] = node.Centre.y;
				values[o + 2] = node.Centre.z;
				values[o + 3] = node.Radius;
				values[o + 4] = node.Floor;
				values[o + 5] = node.Along;
			}
			return values;
		}

		/// <summary>The cave stored by <see cref="ToValues"/>, its frame at <paramref name="origin"/>; null if they are not one.</summary>
		/// <remarks>The seed is kept as the shape's own int too: a float holds a seed exactly only below 2^24.</remarks>
		public static CaveSolid FromValues(float[] values, Vector3 origin, int seed)
		{
			if (values == null || values.Length < HeaderCount || Mathf.RoundToInt(values[0]) != Format)
			{
				return null;
			}
			int count = Mathf.RoundToInt(values[23]);
			if (count < 2 || values.Length != HeaderCount + count * NodeStride)
			{
				return null;
			}
			var solid = new CaveSolid
			{
				Form = (CaveForm)Mathf.RoundToInt(values[1]),
				SizeClass = (byte)Mathf.RoundToInt(values[2]),
				Radius = values[3],
				Flat = values[4],
				NoiseShare = values[5],
				Rim = values[6],
				Skin = values[7],
				Collar = values[8],
				CollarLow = values[24],
				ShellMouth = values[9],
				ShellDeep = values[10],
				MouthZone = values[11],
				MeshCell = values[12],
				ChamberCentre = new Vector3(values[13], values[14], values[15]),
				ChamberRadii = new Vector3(values[16], values[17], values[18]),
				ChamberFloor = values[19],
				ChamberYaw = values[20],
				Yaw = values[21],
				Seed = seed,
				Origin = origin,
			};
			for (int i = 0; i < count; i++)
			{
				int o = HeaderCount + i * NodeStride;
				solid.Nodes.Add(new CaveNode
				{
					Centre = new Vector3(values[o], values[o + 1], values[o + 2]),
					Radius = values[o + 3],
					Floor = values[o + 4],
					Along = values[o + 5],
				});
			}
			solid.Prepare();
			return solid;
		}

		/// <summary>A hash of a mesh's positions (rounded to 0.1 mm) and triangles, for determinism checks.</summary>
		public static ulong Hash(MeshBuilder mesh)
		{
			ulong h = 1469598103934665603UL;
			void Mix(long v)
			{
				unchecked
				{
					h ^= (ulong)v;
					h *= 1099511628211UL;
				}
			}
			foreach (Vector3 p in mesh.Positions)
			{
				Mix(Mathf.RoundToInt(p.x * 10000f));
				Mix(Mathf.RoundToInt(p.y * 10000f));
				Mix(Mathf.RoundToInt(p.z * 10000f));
			}
			foreach (List<int> sub in mesh.Submeshes)
			{
				foreach (int i in sub)
				{
					Mix(i);
				}
			}
			return h;
		}
	}
}
#endif
