#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How the rock of a waterfall's face is broken up: its ledges, the boulders on its lip and the overhang of a hard cap.</summary>
	public sealed class FallLedgeSettings
	{
		/// <summary>
		/// The least drop, metres, that counts as a fall: the runtime's own threshold
		/// (InlandWaterRenderer.MinFallMetres), so every fall the curtain is drawn for is given its rock and no other.
		/// </summary>
		public float MinDropMetres = 1.5f;
		/// <summary>How far above the foot's surface the water counts as having reached its pool, metres (the runtime's PoolLevelMetres).</summary>
		public float PoolLevelMetres = 0.5f;
		/// <summary>
		/// Where on the face a ledge's top may stand, as a share of the face's height measured down from its top: never in
		/// the brink's own metre, where the water has not yet left the rock, nor at the very foot, where the pool laps.
		/// </summary>
		public float MinFaceShare = 0.15f;
		public float MaxFaceShare = 0.85f;
		/// <summary>How far across a ledge runs, the least and the most, in the fall's widths.</summary>
		public float MinSpanWidths = 0.3f;
		public float MaxSpanWidths = 1.5f;
		/// <summary>
		/// The widest one rock may be, in the fall's widths. A ledge wider than this is laid as several rocks with gaps
		/// between, so it splits the fall rather than damming it.
		/// </summary>
		public float MaxRockWidths = 0.8f;
		/// <summary>How far a ledge stands out of the face, metres: what the falling water strikes.</summary>
		public float MinProtrusion = 0.4f;
		public float MaxProtrusion = 1.5f;
		/// <summary>The share of a ledge's depth that stands out of the face, the least and the most; the rest is inside the rock.</summary>
		public float MinOutShare = 0.3f;
		public float MaxOutShare = 0.45f;
		/// <summary>How far past the water's own edges, either side, rock may be laid, metres.</summary>
		public float SideSlackMetres = 2f;
		/// <summary>The least clearance between a ledge's underside and the pool's water, metres.</summary>
		public float PoolClearMetres = 0.2f;
		/// <summary>The thinnest a ledge is worth placing, metres: thinner reads as a crack, not a step.</summary>
		public float MinThickness = 0.25f;
		/// <summary>A face at least this steep, degrees, makes a plunge fall: free-falling water, a face worn back under it.</summary>
		public float PlungeDegrees = 70f;
		/// <summary>How far a hard cap's overhang projects over the face, metres, the least and the most.</summary>
		public float MinOverhang = 1f;
		public float MaxOverhang = 3f;
		/// <summary>The most boulders lodged on one lip.</summary>
		public int MaxLipBoulders = 2;
		/// <summary>The most rocks one scene takes.</summary>
		public int MaxRocks = 8000;

		/// <summary>
		/// Ground of a fall's basin at least this steep, degrees, is dressed in rock (<see cref="FallRockKind.Wall"/>): the
		/// headwall and the gorge's walls the carve stands up (WaterCarver's FallBasin), which a heightfield can only draw as
		/// terrain texture stretched down a near-upright face.
		/// </summary>
		public float WallDressDegrees = 48f;
		/// <summary>One wall rock is tried per cell of this size over the basin, metres.</summary>
		public float WallCellMetres = 2.5f;
		/// <summary>A wall rock's face, the least and the most across and up the wall, metres; scaled down on a low wall.</summary>
		public float MinWallRock = 2.2f;
		public float MaxWallRock = 5f;
		/// <summary>The share of a wall rock's thickness standing out of the face, the least and the most; the rest is in the rock.</summary>
		public float MinWallOutShare = 0.25f;
		public float MaxWallOutShare = 0.45f;
		/// <summary>The most wall rocks one fall takes.</summary>
		public int MaxWallRocksPerFall = 400;
	}

	/// <summary>What one rock of a fall is.</summary>
	public enum FallRockKind : byte
	{
		/// <summary>A step of harder rock standing out of the face, which the falling water strikes and is split by.</summary>
		Ledge = 0,
		/// <summary>A boulder lodged on the lip, partly sunk in its bed: a gap in the brink.</summary>
		LipBoulder = 1,
		/// <summary>The hard cap of a plunge fall, projecting over the face worn back beneath it.</summary>
		Overhang = 2,
		/// <summary>A slab of the wall's own rock, sunk in the steep ground of the fall's headwall and gorge: what the face is made of.</summary>
		Wall = 3,
	}

	/// <summary>One rock of a fall: where, how big each way, which way it lies, its art, and the fall it belongs to.</summary>
	public struct FallLedge
	{
		public FallRockKind Kind;
		/// <summary>
		/// For a ledge or an overhang, the middle of its planned box; for a lip boulder, its foot (its lowest point), as
		/// <see cref="RiverBoulder.Position"/>.
		/// </summary>
		public Vector3 Position;
		/// <summary>
		/// The planned box in the rock's own frame: x across the river, y up, z downstream (out of the face); for a wall rock,
		/// x along the wall, y out of it and z up it, as its slab would lie on the ground. A lip boulder is scaled evenly from
		/// x, its height its art's own.
		/// </summary>
		public Vector3 Size;
		public Quaternion Rotation;
		public string Prefab;
		/// <summary>The river (<see cref="RiverPath.Id"/>) and its fall's lip and foot points.</summary>
		public int River;
		public int Lip;
		public int Foot;
		/// <summary>
		/// The middle's offset across the river from the line through the lip, metres, positive to the left looking downstream;
		/// for a wall rock, the offset of the spot on the ground it was sunk at.
		/// </summary>
		public float Across;
		/// <summary>
		/// How far downstream of the lip the rock's front edge stands, metres (negative: upstream of it); for a wall rock, how
		/// far downstream the spot on the ground it was sunk at.
		/// </summary>
		public float FrontAlong;
	}

	/// <summary>
	/// The rock a waterfall falls over: ledges standing out of its face, boulders lodged on its lip, and the overhang of
	/// a hard cap, so the water parts and breaks on its way down instead of sliding down a smooth wall; and the rock of its
	/// basin's walls, so the headwall and gorge the carve stands up are rock rather than stretched terrain.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why props.</b> A heightfield cannot overhang and cannot put a step in a face narrower than its sample: every
	/// fall's face was one clean slope, and the curtain fell down it as a single sheet. The rock is baked props with
	/// collision (<see cref="ScenePropBaker"/>), which the runtime curtain parts round, strikes and rides over exactly as
	/// it does any other rock in the scene's collision set; so placing the rock is all the generator has to do.
	/// </para>
	/// <para>
	/// <b>From the geology.</b> How a face breaks follows what it is made of. Bedded rock (sandstone, limestone, shale,
	/// conglomerate, chalk, tuff) weathers bed by bed, the harder beds left standing as steps: one to three ledges, a
	/// stepped, segmented fall. Lava flows and foliated rock break along their flows and their cleavage: one or two.
	/// Massive rock (granite, marble, quartzite) wears smooth: none or one. Where the lip is a lava flow over softer
	/// rock, or over the rubbly top of the flow below, the soft rock is scoured back under it by the spray, and the
	/// cap is left as an overhang: a recess behind the curtain (the classic plunge fall).
	/// </para>
	/// <para>
	/// <b>Split, never dammed.</b> No one rock is wider than <see cref="FallLedgeSettings.MaxRockWidths"/> of the fall:
	/// a wider ledge is laid as several rocks with gaps between, so the water finds its way through in streams. Nothing
	/// is laid further than <see cref="FallLedgeSettings.SideSlackMetres"/> past the water's edges, and nothing in the
	/// plunge pool: every ledge's underside clears the pool's water.
	/// </para>
	/// <para>
	/// <b>Mostly inside the rock.</b> A ledge is a slab sunk into the face with only a share of it
	/// (<see cref="FallLedgeSettings.MaxOutShare"/> at most) standing out, so what shows is a step of the face, not a
	/// block stuck to it.
	/// </para>
	/// <para>
	/// <b>The basin's walls.</b> The carve stands a fall's headwall and its gorge's walls at 50–75°, up to upright beside the
	/// curtain (WaterCarver's FallBasin): steeper than a heightfield can texture without stretching. Every steep, dry spot of
	/// the basin outside the curtain's band is dressed with a slab of the wall's own rock sunk in it, face out, the size of
	/// the wall about it (<see cref="FallRockKind.Wall"/>).
	/// </para>
	/// <para>
	/// The lip boulders are recorded in the scene's water like river boulders (<see cref="SceneWater.Boulders"/>), so the
	/// solved flow runs round them and the curtain's lip profile has its gaps where they stand.
	/// </para>
	/// </remarks>
	public static class FallLedges
	{
		/// <summary>The source the falls' rock props and colliders are baked under (<see cref="ScenePropBaker"/>).</summary>
		public const string PropSource = "FallLedges";

		/// <summary>The step, metres, the face is searched along at.</summary>
		private const float SearchStep = 0.25f;

		/// <summary>One fall, as the runtime finds it: a run of falling points, from the point before to the point after.</summary>
		public struct FallSpan
		{
			public int Lip;
			public int Foot;
			/// <summary>The first point at the pool's level: where the falling water lands.</summary>
			public int Plunge;
			public float Drop;
		}

		/// <summary>
		/// Every fall of a river: each run of <see cref="RiverReach.Fall"/> points (gaps of one point bridged) from the
		/// point before it to the point after, where its surface drops at least <paramref name="minDrop"/>. The same
		/// definition as the runtime's (InlandWaterRenderer.FindFalls), so the rock is placed on the falls that are drawn.
		/// </summary>
		public static List<FallSpan> FindFalls(RiverPath river, float minDrop, float poolLevelMetres = 0.5f)
		{
			var found = new List<FallSpan>();
			int n = river.Count;
			if (!river.Perennial || n < 3 || river.Reach == null || river.Reach.Length != n)
			{
				return found;
			}
			int i = 0;
			while (i < n)
			{
				if (river.Reach[i] != RiverReach.Fall)
				{
					i++;
					continue;
				}
				int last = i;
				while (last + 1 < n && (river.Reach[last + 1] == RiverReach.Fall || (last + 2 < n && river.Reach[last + 2] == RiverReach.Fall)))
				{
					last++;
				}
				int lip = Math.Max(0, i - 1), foot = Math.Min(n - 1, last + 1);
				float drop = river.Surface[lip] - river.Surface[foot];
				if (drop >= minDrop && foot > lip)
				{
					int plunge = foot;
					for (int k = lip + 1; k <= foot; k++)
					{
						if (river.Surface[k] <= river.Surface[foot] + poolLevelMetres)
						{
							plunge = k;
							break;
						}
					}
					found.Add(new FallSpan { Lip = lip, Foot = foot, Plunge = plunge, Drop = drop });
				}
				i = last + 1;
			}
			return found;
		}

		/// <summary>How a rock breaks on a fall's face.</summary>
		public enum FaceHabit : byte
		{
			/// <summary>Wears smooth: granite, marble, quartzite, ice, or rock not known.</summary>
			Massive = 0,
			/// <summary>Breaks along lava flows or cleavage: basalt, andesite, slate, schist, gneiss.</summary>
			Jointed = 1,
			/// <summary>Weathers bed by bed: sandstone, shale, limestone, chalk, conglomerate, tuff.</summary>
			Bedded = 2,
		}

		/// <summary>How a geology rock name breaks on a face (<see cref="PlanetGeology.Lithologies"/>).</summary>
		public static FaceHabit Habit(string rockType)
		{
			if (TryLithology(rockType, out Lithology lithology))
			{
				switch (lithology.Style)
				{
					case StrataStyle.Bedded:
						return lithology.Family == GeologyFamily.Ice ? FaceHabit.Massive : FaceHabit.Bedded;
					case StrataStyle.Flows:
					case StrataStyle.Foliated:
						return FaceHabit.Jointed;
				}
			}
			return FaceHabit.Massive;
		}

		/// <summary>
		/// True for a lip of lava-flow rock (basalt, andesite) over rock that wears faster: a softer rock below, or the
		/// same flows, whose rubbly flow tops between the dense sheets are what the spray scours back.
		/// </summary>
		public static bool HardCap(string lipRock, string faceRock)
		{
			if (!TryLithology(lipRock, out Lithology cap) || cap.Style != StrataStyle.Flows)
			{
				return false;
			}
			if (faceRock == null || faceRock == lipRock)
			{
				return true;
			}
			return TryLithology(faceRock, out Lithology below) && below.Hardness < cap.Hardness - 0.05f;
		}

		private static bool TryLithology(string rockType, out Lithology lithology)
		{
			if (rockType != null)
			{
				foreach (Lithology candidate in PlanetGeology.Lithologies)
				{
					if (candidate.Name == rockType)
					{
						lithology = candidate;
						return true;
					}
				}
			}
			lithology = default;
			return false;
		}

		/// <summary>Where the falls' rock goes. Deterministic in the seed.</summary>
		/// <param name="ground">The finished ground at (east, north), scene metres.</param>
		/// <param name="rockTypeAt">The rock at (east, altitude, north), a geology name or null (read as massive rock).</param>
		public static List<FallLedge> Plan(SceneWater water, Func<float, float, float> ground, Func<float, float, float, string> rockTypeAt,
			uint seed, FallLedgeSettings settings = null)
		{
			settings ??= new FallLedgeSettings();
			var result = new List<FallLedge>();
			// Each river's basins as the carve cut them, for the walls' rock.
			float spacing = water.Grid != null ? water.Grid.Spacing : 2f;
			var basinsOf = new Dictionary<RiverPath, FallBasin[]>();
			foreach (RiverPath river in water.Rivers)
			{
				FallBasin[] basins = FallBasin.Along(river, water.Settings, spacing);
				if (basins != null)
				{
					basinsOf[river] = basins;
				}
			}
			foreach (RiverPath river in water.Rivers)
			{
				foreach (FallSpan fall in FindFalls(river, settings.MinDropMetres, settings.PoolLevelMetres))
				{
					if (result.Count >= settings.MaxRocks)
					{
						return result;
					}
					var random = new System.Random(unchecked((int)(seed ^ (uint)(river.Id * 0x9E3779B1u) ^ (uint)(fall.Lip * 0x85EBCA6Bu) ^ 0xFA11ED6Eu)));
					PlanFall(river, fall, ground, rockTypeAt, random, settings, result);
					// The walls on a stream of their own, so dressing them leaves the ledges and boulders where they were.
					FallBasin basin = basinsOf.TryGetValue(river, out FallBasin[] basins) && fall.Lip < basins.Length ? basins[fall.Lip] : null;
					if (basin != null)
					{
						var walls = new System.Random(unchecked((int)(seed ^ (uint)(river.Id * 0x9E3779B1u) ^ (uint)(fall.Lip * 0x85EBCA6Bu) ^ 0x3A11D0C5u)));
						PlanWalls(water, river, fall, basin, ground, rockTypeAt, walls, settings, result);
					}
				}
			}
			if (result.Count > settings.MaxRocks)
			{
				result.RemoveRange(settings.MaxRocks, result.Count - settings.MaxRocks);
			}
			return result;
		}

		private static void PlanFall(RiverPath river, FallSpan fall, Func<float, float, float> ground, Func<float, float, float, string> rockTypeAt,
			System.Random random, FallLedgeSettings settings, List<FallLedge> result)
		{
			int lip = fall.Lip, foot = fall.Foot;
			float width = Mathf.Max(1f, river.Width[lip]);
			float half = 0.5f * width;
			float side = half + settings.SideSlackMetres;
			float lipSurface = river.Surface[lip];
			float pool = river.Surface[foot];
			float run = river.S[foot] - river.S[lip];

			/* Downstream as the water leaves the lip: the lip to the point after it. The run of a knickpoint's step is a
			 * metre or two, so the line hardly turns over it. */
			int next = Math.Min(foot, lip + 1);
			float tx = river.X[next] - river.X[lip], tz = river.Z[next] - river.Z[lip];
			float tl = Mathf.Sqrt(tx * tx + tz * tz);
			if (tl < 1e-4f)
			{
				return;
			}
			tx /= tl;
			tz /= tl;
			// Left, looking downstream.
			float nx = -tz, nz = tx;
			Vector3 Point(float along, float across) => new Vector3(river.X[lip] + tx * along + nx * across, 0f, river.Z[lip] + tz * along + nz * across);
			float GroundAt(float along, float across)
			{
				Vector3 p = Point(along, across);
				return ground(p.x, p.z);
			}

			/* The face is the ground's drop, not the water's: from the lip's bed (the water runs over it, a depth above)
			 * down to the pool's surface (below that it is under the pool). The ledges stand on that. */
			float bedAtLip = Mathf.Min(GroundAt(0f, 0f), lipSurface - Mathf.Max(0f, river.Depth[lip]));
			float faceTop = Mathf.Min(bedAtLip, lipSurface);
			float faceHeight = faceTop - pool;
			if (faceHeight < 0.5f)
			{
				return;
			}
			float searchTo = run + 2f;
			// The first distance downstream of the lip, along a line across offset `across`, where the ground is down to `altitude`.
			float FaceAt(float altitude, float across)
			{
				for (float d = 0f; d <= searchTo; d += SearchStep)
				{
					if (GroundAt(d, across) <= altitude)
					{
						return d;
					}
				}
				return float.NaN;
			}

			Vector3 lipPoint = Point(0f, 0f);
			string lipRock = rockTypeAt?.Invoke(lipPoint.x, faceTop - 0.25f, lipPoint.z);
			float midFace = FaceAt(faceTop - 0.5f * faceHeight, 0f);
			Vector3 midPoint = Point(float.IsNaN(midFace) ? 0f : midFace, 0f);
			string faceRock = rockTypeAt?.Invoke(midPoint.x, faceTop - 0.5f * faceHeight, midPoint.z);
			FaceHabit habit = Habit(faceRock);
			bool cap = HardCap(lipRock, faceRock);
			Quaternion downstream = Quaternion.LookRotation(new Vector3(tx, 0f, tz), Vector3.up);

			// ── Ledges ──
			/* How many the rock gives, and no more than the face has room for: one per two metres of it. */
			int most = habit == FaceHabit.Bedded ? 3 : habit == FaceHabit.Jointed ? 2 : 1;
			int least = habit == FaceHabit.Massive ? 0 : 1;
			int count = least + random.Next(most - least + 1);
			count = Math.Min(count, Math.Max(1, Mathf.FloorToInt(faceHeight / 2f)));
			string ledgeArt = $"Boulder_{RiverBoulders.Material(faceRock)}_Slab";
			for (int k = 0; k < count; k++)
			{
				/* One band of the face each, jittered within it, so the steps stand one above another down the fall and
				 * the water drops from one to the next rather than all striking at one height. */
				float band = (settings.MaxFaceShare - settings.MinFaceShare) / count;
				float share = settings.MinFaceShare + band * (k + (float)random.NextDouble());
				float top = faceTop - share * faceHeight;
				// Bedded rock steps in thinner beds than lava flows or cleaved rock break into.
				float thickness = Mathf.Lerp(0.4f, 1.2f, (float)random.NextDouble()) * (habit == FaceHabit.Bedded ? 1f : 1.3f);
				thickness = Mathf.Min(thickness, top - pool - settings.PoolClearMetres);
				float span = Mathf.Lerp(settings.MinSpanWidths, settings.MaxSpanWidths, (float)random.NextDouble()) * width;
				// Across the water's path, so it intercepts part of the curtain, within the span rock may be laid in.
				float centre = ((float)random.NextDouble() * 2f - 1f) * 0.5f * width;
				span = Mathf.Min(span, 2f * side);
				centre = Mathf.Clamp(centre, -side + 0.5f * span, side - 0.5f * span);
				float protrusion = Mathf.Lerp(settings.MinProtrusion, Mathf.Clamp(0.2f * faceHeight, settings.MinProtrusion, settings.MaxProtrusion),
					(float)random.NextDouble());
				float outShare = Mathf.Lerp(settings.MinOutShare, settings.MaxOutShare, (float)random.NextDouble());
				float depth = protrusion / outShare;
				// A dip of a few degrees, as beds and flows lie; and a little roll, so the steps are not ruled lines.
				Quaternion tilt = Quaternion.Euler((float)(random.NextDouble() - 0.5) * 8f, 0f, (float)(random.NextDouble() - 0.5) * 6f);
				if (thickness < settings.MinThickness)
				{
					continue;   // the pool is too near: this step would stand in it
				}

				/* Wider than one rock may be: laid as pieces with gaps, each gap about a tenth of the span and never under
				 * half a metre, so the water runs through between them. */
				float widest = settings.MaxRockWidths * width;
				int pieces = Mathf.Max(1, Mathf.CeilToInt(span / widest));
				float gap = pieces > 1 ? Mathf.Max(0.5f, 0.1f * span) : 0f;
				float piece = (span - gap * (pieces - 1)) / pieces;
				if (piece < 0.5f)
				{
					pieces = 1;
					gap = 0f;
					piece = Mathf.Min(span, widest);
				}
				piece = Mathf.Min(piece, widest);
				for (int p = 0; p < pieces; p++)
				{
					float across = centre - 0.5f * span + 0.5f * piece + p * (piece + gap);
					// Where the face is at this ledge's top, at its own place across: the ground drops there.
					float face = FaceAt(top, across);
					if (float.IsNaN(face))
					{
						continue;
					}
					float front = face + protrusion;
					if (front > run)
					{
						continue;   // past the foot: that is the pool's floor, not the face
					}
					float middle = front - 0.5f * depth;
					Vector3 at = Point(middle, across);
					at.y = top - 0.5f * thickness;
					result.Add(new FallLedge
					{
						Kind = FallRockKind.Ledge,
						Position = at,
						Size = new Vector3(piece, thickness, depth),
						Rotation = downstream * tilt,
						Prefab = ledgeArt,
						River = river.Id,
						Lip = lip,
						Foot = foot,
						Across = across,
						FrontAlong = front,
					});
				}
			}

			// ── The overhang ──
			/* A plunge fall: the face from a tenth of the way down to nine tenths stands steeper than PlungeDegrees. A
			 * heightfield blurs a sheer step over one sample, so a tall step still reads near upright and a short one
			 * does not, which is as it should be: a metre-high step is a cascade, not a plunge. */
			float upper = FaceAt(faceTop - 0.1f * faceHeight, 0f), lower = FaceAt(faceTop - 0.9f * faceHeight, 0f);
			bool plunge = !float.IsNaN(upper) && !float.IsNaN(lower)
				&& Mathf.Atan2(0.8f * faceHeight, Mathf.Max(1e-3f, lower - upper)) * Mathf.Rad2Deg >= settings.PlungeDegrees;
			if (plunge && cap)
			{
				/* The cap's slab, its top just under the lip's bed so the river runs on over it and leaves from its front
				 * edge, projecting over the face, its back sunk under the bed upstream. As wide as the channel and a little
				 * more (the cap runs on into the banks): it splits nothing, the water runs over it, so the one-rock limit
				 * on ledges does not bind it. */
				OverhangDimensions slab = OverhangPlacer.Draw(faceHeight, faceHeight - settings.PoolClearMetres - 0.05f, width, 2f * side,
					new OverhangSettings { MinReach = settings.MinOverhang, MaxReach = settings.MaxOverhang }, () => (float)random.NextDouble());
				float reach = slab.Reach, back = slab.Back, thickness = slab.Thickness, across = slab.Across;
				float front = upper + reach;
				// Not held to the foot as the ledges are: it stands at the lip's level, far over the pool, however short the run.
				if (thickness >= settings.MinThickness)
				{
					Vector3 at = Point(front - 0.5f * (reach + back), 0f);
					at.y = faceTop - 0.05f - 0.5f * thickness;
					result.Add(new FallLedge
					{
						Kind = FallRockKind.Overhang,
						Position = at,
						Size = new Vector3(across, thickness, reach + back),
						Rotation = downstream,
						Prefab = $"Boulder_{RiverBoulders.Material(lipRock)}_Slab",
						River = river.Id,
						Lip = lip,
						Foot = foot,
						Across = 0f,
						FrontAlong = front,
					});
				}
			}

			// ── Lip boulders ──
			/* None, one or two lodged on the brink, partly sunk, one each side of the line so they never touch: the lip
			 * profile gets its gaps and the curtain leaves it in separate sheets. Just upstream of the edge, so each sits
			 * on the lip's bed rather than hanging over the drop. */
			double roll = random.NextDouble();
			int boulders = Math.Min(settings.MaxLipBoulders, roll < 0.35 ? 0 : roll < 0.75 ? 1 : 2);
			float firstSide = random.NextDouble() < 0.5 ? -1f : 1f;
			float lastAcross = float.NaN, lastSize = 0f;
			for (int b = 0; b < boulders; b++)
			{
				float size = Mathf.Clamp(Mathf.Lerp(0.15f, 0.3f, (float)random.NextDouble()) * width, 0.6f, Mathf.Max(0.6f, Mathf.Min(2.4f, settings.MaxRockWidths * width)));
				float across = (b == 0 ? firstSide : -firstSide) * Mathf.Lerp(0.15f, 0.6f, (float)random.NextDouble()) * half;
				across = Mathf.Clamp(across, -side + 0.5f * size, side - 0.5f * size);
				if (!float.IsNaN(lastAcross) && Mathf.Abs(across - lastAcross) < 0.5f * (size + lastSize) + 0.3f)
				{
					continue;   // too close to the first to leave water between them
				}
				float along = -0.5f * size * Mathf.Lerp(0.6f, 1.1f, (float)random.NextDouble());
				float buried = Mathf.Lerp(0.3f, 0.5f, (float)random.NextDouble());
				Vector3 at = Point(along, across);
				at.y = ground(at.x, at.z) - buried * size;
				// Worn round on a lip; the odd slab freshly fallen from the cap.
				string shape = random.NextDouble() < 0.8 ? "Round" : "Slab";
				result.Add(new FallLedge
				{
					Kind = FallRockKind.LipBoulder,
					Position = at,
					Size = new Vector3(size, size, size),
					Rotation = Quaternion.Euler((float)(random.NextDouble() - 0.5) * 20f, (float)random.NextDouble() * 360f, (float)(random.NextDouble() - 0.5) * 20f),
					Prefab = $"Boulder_{RiverBoulders.Material(lipRock)}_{shape}",
					River = river.Id,
					Lip = lip,
					Foot = foot,
					Across = across,
					FrontAlong = along + 0.5f * size,
				});
				lastAcross = across;
				lastSize = size;
			}
		}

		/// <summary>
		/// Dresses the steep ground of a fall's basin in rock: slabs of the wall's own rock sunk in the headwall and the gorge's
		/// walls, faces out, wherever the finished ground stands steeper than <see cref="FallLedgeSettings.WallDressDegrees"/>,
		/// so a near-upright face is rock and not terrain texture stretched down it.
		/// </summary>
		/// <remarks>
		/// One rock is tried in each <see cref="FallLedgeSettings.WallCellMetres"/> cell of the basin (its headwall's circle and
		/// the gorge to where the carve's basin has faded), at a jittered place in it, and kept where the ground is steep,
		/// dry and not the curtain's: the band the water falls down is the ledges' and the overhang's, and the pool's floor is
		/// the pool's. Each is a slab the size of the wall about it, its face turned out along the ground's normal and rolled a
		/// little, with only a share of its thickness standing out, so the wall reads as jointed rock in blocks; none overlaps
		/// another by more than a third. Deterministic in <paramref name="random"/>.
		/// </remarks>
		private static void PlanWalls(SceneWater water, RiverPath river, FallSpan fall, FallBasin basin, Func<float, float, float> ground,
			Func<float, float, float, string> rockTypeAt, System.Random random, FallLedgeSettings settings, List<FallLedge> result)
		{
			int lip = fall.Lip, foot = fall.Foot;
			int next = Math.Min(foot, lip + 1);
			float tx = river.X[next] - river.X[lip], tz = river.Z[next] - river.Z[lip];
			float tl = Mathf.Sqrt(tx * tx + tz * tz);
			if (tl < 1e-4f)
			{
				return;
			}
			tx /= tl;
			tz /= tl;
			float nx = -tz, nz = tx;
			float pool = river.Surface[foot];
			float drop = basin.LipLevel - pool;
			if (drop < 1f)
			{
				return;
			}
			float plungeAlong = river.S[Mathf.Clamp(fall.Plunge, lip, foot)] - river.S[lip];
			// The basin's reach: its headwall's circle, on down the gorge until the carve's basin has faded back to banks, and as
			// far out as walls of the gentlest steepness the carve stands rise from the widest water to the lip's height.
			float alongTo = basin.FadeEnd;
			float widest = basin.HalfLip;
			for (int k = lip; k < river.Count && river.S[k] - river.S[lip] <= alongTo; k++)
			{
				widest = Mathf.Max(widest, 0.5f * river.Width[k]);
			}
			float acrossTo = Mathf.Min(80f, widest + basin.Radius + drop / Mathf.Tan(Mathf.Max(30f, water.Settings.FallWallDegrees - water.Settings.FallWallJitterDegrees) * Mathf.Deg2Rad) + 2f);
			float curtain = basin.HalfLip + settings.SideSlackMetres;
			float cell = Mathf.Max(1f, settings.WallCellMetres);
			float steep = Mathf.Tan(settings.WallDressDegrees * Mathf.Deg2Rad);
			const float probe = 0.75f;
			int placed = 0;
			int first = result.Count;
			for (float along = -cell; along <= alongTo && placed < settings.MaxWallRocksPerFall; along += cell)
			{
				for (float across = -acrossTo; across <= acrossTo && placed < settings.MaxWallRocksPerFall; across += cell)
				{
					// A jittered place in the cell, drawn whether or not it is kept, so each cell's draw is its own.
					float ja = along + (float)random.NextDouble() * cell, jc = across + (float)random.NextDouble() * cell;
					double sizeRoll = random.NextDouble(), shapeRoll = random.NextDouble(), outRoll = random.NextDouble(), spinRoll = random.NextDouble();
					// The curtain's band, from the lip down to where it lands, is the ledges' and the overhang's.
					if (Mathf.Abs(jc) < curtain && ja < plungeAlong + 1f)
					{
						continue;
					}
					float x = river.X[lip] + tx * ja + nx * jc, z = river.Z[lip] + tz * ja + nz * jc;
					float y = ground(x, z);
					if (float.IsNaN(y) || y < pool + 0.3f)
					{
						continue;   // under the pool, or at its edge
					}
					WaterKind kind = water.KindAt(x, z);
					if (kind == WaterKind.River || kind == WaterKind.Lake)
					{
						continue;
					}
					float gx = (ground(x + probe, z) - ground(x - probe, z)) / (2f * probe);
					float gz = (ground(x, z + probe) - ground(x, z - probe)) / (2f * probe);
					float grade = Mathf.Sqrt(gx * gx + gz * gz);
					if (float.IsNaN(grade) || grade < steep)
					{
						continue;
					}
					// Out of the face, and up it.
					var normal = new Vector3(-gx, 1f, -gz).normalized;
					// As big as the wall about it: never much more than the drop.
					float most = Mathf.Min(settings.MaxWallRock, 0.35f * drop + 1f);
					float face = Mathf.Lerp(Mathf.Min(settings.MinWallRock, most), most, (float)sizeRoll);
					float wide = face * Mathf.Lerp(0.85f, 1.35f, (float)shapeRoll);
					float high = face * Mathf.Lerp(0.7f, 1.05f, (float)(1.0 - shapeRoll));
					float thick = Mathf.Max(0.8f, 0.4f * face);
					float outShare = Mathf.Lerp(settings.MinWallOutShare, settings.MaxWallOutShare, (float)outRoll);
					/* Lying in the face as the slab art lies on the ground: its thin axis (y) out along the normal, its length (z) up
					 * the face; rolled a little about the normal so the joints are not ruled lines. */
					Vector3 upFace = (Vector3.up - normal * normal.y).normalized;
					Quaternion turn = Quaternion.AngleAxis(((float)spinRoll - 0.5f) * 50f, normal) * Quaternion.LookRotation(upFace, normal);
					Vector3 middle = new Vector3(x, y, z) + normal * (thick * (outShare - 0.5f));
					// Its foot clear of the pool's water: made smaller to fit, or left out where only a pebble would.
					float reach = 0.5f * (wide * Mathf.Abs((turn * Vector3.right).y) + thick * Mathf.Abs((turn * Vector3.up).y) + high * Mathf.Abs((turn * Vector3.forward).y));
					float clear = middle.y - pool - PoolClearOf(settings);
					if (reach > clear)
					{
						float fit = clear / Mathf.Max(1e-3f, reach);
						if (fit < 0.5f)
						{
							continue;   // too near the pool for a block of the wall
						}
						wide *= fit;
						high *= fit;
						thick *= fit;
						middle = new Vector3(x, y, z) + normal * (thick * (outShare - 0.5f));
					}
					// Overlapping the rocks already on this wall by a third at most.
					bool crowded = false;
					for (int r = first; r < result.Count; r++)
					{
						FallLedge other = result[r];
						float apart = 0.5f * (Mathf.Max(other.Size.x, other.Size.z) + Mathf.Max(wide, high));
						if ((other.Position - middle).sqrMagnitude < apart * apart * 0.45f)
						{
							crowded = true;
							break;
						}
					}
					if (crowded)
					{
						continue;
					}
					string rock = rockTypeAt?.Invoke(x, y, z);
					result.Add(new FallLedge
					{
						Kind = FallRockKind.Wall,
						Position = middle,
						Size = new Vector3(wide, thick, high),
						Rotation = turn,
						Prefab = $"Boulder_{RiverBoulders.Material(rock)}_{(shapeRoll < 0.8 ? "Slab" : "Round")}",
						River = river.Id,
						Lip = lip,
						Foot = foot,
						Across = jc,
						FrontAlong = ja,
					});
					placed++;
				}
			}
		}

		/// <summary>How far over the pool's water a wall rock's foot stands, metres.</summary>
		private static float PoolClearOf(FallLedgeSettings settings) => Mathf.Max(0.2f, settings.PoolClearMetres);

		/// <summary>
		/// Bakes the falls' rock into a scene as props with collision under <see cref="PropSource"/>, replacing what that
		/// source baked before, and records each lip boulder in the water (position and radius) for the flow to run
		/// round. Run after <see cref="RiverBoulders.Place"/>, which clears the water's boulders. With the ground and the biome
		/// field, the vines on each fall's walls (<see cref="VinePlacement.DrapeFalls"/>) are baked with the rock. Returns how
		/// many rocks were placed.
		/// </summary>
		/// <param name="ground">The finished ground at (east, north), scene metres; null for no vines.</param>
		/// <param name="field">The scene's biomes, for which vines grow and how thickly; null for no vines.</param>
		/// <param name="seed">The scene's seed, as <see cref="Plan"/> took it.</param>
		/// <param name="keptOut">Ground no vine is laid on (under a bridge, in a riverside site's footprint); null for none.</param>
		public static int Place(Scene scene, SceneWater water, List<FallLedge> rocks, List<string> notes,
			Func<float, float, float> ground = null, SceneBiomeField field = null, uint seed = 0u, Func<float, float, bool> keptOut = null)
		{
			var prototypes = new List<ScenePropSet.Prototype>();
			var prototypeOf = new Dictionary<GameObject, int>();
			var props = new List<ScenePropSet.Prop>(rocks.Count);
			var art = new Dictionary<string, (GameObject prefab, Bounds bounds)>();
			var recorded = new List<Vector4>();
			int placed = 0, missing = 0, ledges = 0, overhangs = 0, boulders = 0, walls = 0;
			foreach (FallLedge rock in rocks)
			{
				if (!art.TryGetValue(rock.Prefab, out var found))
				{
					var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RiverBoulders.PrefabFolder}/{rock.Prefab}.prefab");
					// Its own size, from what it draws: the prefab is scaled to the planned box from this, as river boulders are.
					Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
					if (prefab != null)
					{
						bool any = false;
						foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
						{
							if (filter.sharedMesh == null)
							{
								continue;
							}
							Bounds b = filter.sharedMesh.bounds;
							if (!any) { bounds = b; any = true; } else { bounds.Encapsulate(b); }
						}
					}
					found = (prefab, bounds);
					art[rock.Prefab] = found;
				}
				if (found.prefab == null)
				{
					missing++;
					continue;
				}
				if (!prototypeOf.TryGetValue(found.prefab, out int prototype))
				{
					prototype = prototypes.Count;
					prototypeOf[found.prefab] = prototype;
					prototypes.Add(new ScenePropSet.Prototype { Prefab = found.prefab, Layer = -1 });
				}
				Vector3 size = found.bounds.size;
				Vector3 scale;
				Vector3 at;
				if (rock.Kind == FallRockKind.LipBoulder)
				{
					// Evenly, as river boulders are, its foot sunk where planned.
					float s = rock.Size.x / Mathf.Max(0.1f, Mathf.Max(size.x, size.z));
					scale = Vector3.one * s;
					at = rock.Position - Vector3.up * found.bounds.min.y * s;
					Vector4 record = new Vector4(rock.Position.x, rock.Position.y + 0.5f * rock.Size.x, rock.Position.z, 0.5f * rock.Size.x);
					recorded.Add(record);
					boulders++;
				}
				else
				{
					/* Stretched to the planned box each way: a slab of the rock's own art made a ledge's width, thickness and
					 * depth. Scale is applied before the turn (T·R·S), so the box stays a box, no shear. Its middle where planned. */
					OverhangPlacer.Stretch(found.bounds, rock.Position, rock.Rotation, rock.Size, out at, out scale);
					if (rock.Kind == FallRockKind.Overhang) { overhangs++; } else if (rock.Kind == FallRockKind.Wall) { walls++; } else { ledges++; }
				}
				props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = at, Rotation = rock.Rotation, Scale = scale });
				placed++;
			}
			/* The water's boulders were cleared by RiverBoulders.Place this generation; these are added after. Any of these
			 * already there (this placed twice without the river boulders between) are taken out first, never doubled. */
			var mine = new HashSet<Vector4>(recorded);
			water.Boulders.RemoveAll(mine.Contains);
			water.Boulders.AddRange(recorded);
			/* Vines on the wet walls beside and round each fall, in the biomes that grow them: with the falls' own props, so a
			 * re-placing clears them with the rock; no collider, so no collision. */
			if (ground != null && field != null)
			{
				VinePlacement.DrapeFalls(water, rocks, ground, field, seed, keptOut, prototypes, props, notes);
			}
			int collidable = ScenePropBaker.Write(scene, PropSource, prototypes, props);
			notes?.Add($"Fall ledges: {ledges:N0} ledges, {overhangs:N0} overhangs, {boulders:N0} lip boulders and {walls:N0} wall rocks placed on waterfalls as baked props, {collidable:N0} collidable"
				+ $"{(missing > 0 ? $"; {missing:N0} skipped, their boulder art is missing (run Generate biome art)" : string.Empty)}.");
			return placed;
		}
	}
}
#endif
