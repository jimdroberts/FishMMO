#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Writes a cave's molten lava (<see cref="CaveLava.Plan"/>) into its scene's hydrology as lakes and rivers flagged
	/// <see cref="SceneHydrology.Lake.Lava"/>: so the game has one lava liquid everywhere. <see cref="FishMMO.Shared.Weather.WaterQuery"/> reports it
	/// as <see cref="FishMMO.Shared.Weather.WaterBody.Lava"/> (fire damage, slow swimming for the fire-proof, breath), the
	/// inland water's renderer draws it with the lava's look, and the NavMesh bakes it as the "Lava" area.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Ids.</b> In the grotto pools' range (<see cref="CaveShaper.PoolIdBase"/> up to <see cref="MoatShaper.LakeIdBase"/>), so
	/// every rule for a cave's own water holds for its lava as well: no floating plants (FloatingPlants.IsGrottoPool), dropped by
	/// a plan made again (MoatShaper.ClearWater), left alone by the water's settling (WaterSettle). A grotto's pool takes only
	/// the site's low 24 bits over the base; the lava sets bit 28 besides, the body's index in bits 20..27 and the site's low 20
	/// bits under it (2026-10-21: four bits held only 16 bodies), for lakes and rivers alike (they are separate lists, so a lake
	/// and a river may share an id). An id written before (site's low 24 bits, body in 24..27) is still the site's lava.
	/// </para>
	/// <para>
	/// <b>Written by the cave, every time it is built</b>, after the scene's water is saved, as a grotto's pool is
	/// (CaveShaper.WritePool): a repaint reads the water back without any lava (SceneWater.FromAsset), and the cave writes it
	/// again. Scene metres are the system's frame moved to its origin: the system is never turned.
	/// </para>
	/// </remarks>
	public static class CaveLavaWater
	{
		/// <summary>The first id a cave's lava takes (lakes and rivers): the grotto pools' base with bit 28.</summary>
		public const int IdBase = CaveShaper.PoolIdBase | (1 << 28);
		/// <summary>How many bodies of lava a system may write: the plan's own cap (the body's index has eight bits, room for 256).</summary>
		public const int MaxBodies = CaveLava.MaxBodies;
		/// <summary>A lava lake's mask cell, metres: finer than the scene lakes' 4 m, so a pit's lava follows its walls.</summary>
		public const float LakeMaskCell = 1f;
		/// <summary>How deep a lava river runs over its bed, metres.</summary>
		public const float RiverDepth = 1.2f;
		/// <summary>
		/// The deepest a lava bed should lie under its molten surface, metres: the NavMesh marks the ground "Lava" down to
		/// this and a metre more (SceneNavMeshBaker.LavaModifiers), so a pit's floor carved deeper is not all marked.
		/// </summary>
		public const float MaxBedDepth = 3f;
		/// <summary>Surface slope (rise over run) from which a lava river runs as a rapid, and from which it falls.</summary>
		public const float RapidSlope = 0.03f;
		public const float FallSlope = 0.15f;
		/// <summary>The slowest and the fastest a lava river's surface runs, m/s (channelled basalt: 0.3 to a few m/s).</summary>
		public const float MinSpeed = 0.3f;
		public const float MaxSpeed = 2f;

		/// <summary>The site's bits in a lava id: its low 20 (a scene's lava tubes are few, and two share them one time in a million).</summary>
		private const int SiteMask = 0x000FFFFF;

		/// <summary>The id of a site's lava body (its index in the plan), for its lake or its river.</summary>
		public static int IdOf(int siteId, int body) => IdBase | ((body & 0xFF) << 20) | (siteId & SiteMask);

		/// <summary>True for any cave's lava id.</summary>
		public static bool IsCaveLava(int id) => (id & 0x70000000) == IdBase;

		/// <summary>True for a lava id of <paramref name="siteId"/>'s.</summary>
		public static bool OfSite(int id, int siteId) => IsCaveLava(id) && (id & SiteMask) == (siteId & SiteMask);

		/// <summary>Takes a site's lava out of the hydrology: every lake and river <see cref="IdOf"/> it. Returns how many.</summary>
		public static int Clear(SceneHydrology hydrology, int siteId) => hydrology != null ? Clear(hydrology.Lakes, hydrology.Rivers, siteId) : 0;

		/// <summary><see cref="Clear(SceneHydrology, int)"/> on a hydrology's two lists.</summary>
		public static int Clear(List<SceneHydrology.Lake> lakes, List<SceneHydrology.River> rivers, int siteId)
		{
			return (lakes != null ? lakes.RemoveAll(l => l != null && OfSite(l.Id, siteId)) : 0)
				+ (rivers != null ? rivers.RemoveAll(r => r != null && OfSite(r.Id, siteId)) : 0);
		}

		/// <summary>
		/// Replaces a site's lava in the hydrology with its system's plan: the old taken out (<see cref="Clear"/>), then one lake
		/// or river per body of the plan, at its index (bodies past <see cref="MaxBodies"/> are left out). Nothing is written
		/// for a cooled plan. Does not save the asset. Returns how many bodies were written.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A <see cref="CaveLavaKind.Lake"/> is a lava lake at its one point's height, over the disc of <c>Width</c> (its radius)
		/// round it, where the cave is open just under that height (<see cref="CaveSystem.Void"/>): carve its basin first.
		/// </para>
		/// <para>
		/// A <see cref="CaveLavaKind.River"/> or <see cref="CaveLavaKind.Fall"/> is a lava river down its path, each point's
		/// height its surface (never rising downstream), <c>Width</c> across. A fall's every point falls. A river or fall whose
		/// end lies in one of the plan's lakes runs into it (its end set at the lake's level), and one whose start lies in a
		/// lake runs out of it.
		/// </para>
		/// </remarks>
		public static int Write(SceneHydrology hydrology, int siteId, CaveSystem system, CaveLavaPlan plan)
			=> hydrology != null ? Write(hydrology.Lakes, hydrology.Rivers, siteId, system, plan) : 0;

		/// <summary><see cref="Write(SceneHydrology, int, CaveSystem, CaveLavaPlan)"/> into a hydrology's two lists.</summary>
		public static int Write(List<SceneHydrology.Lake> lakesOut, List<SceneHydrology.River> riversOut, int siteId, CaveSystem system, CaveLavaPlan plan)
		{
			if (lakesOut == null || riversOut == null || system == null)
			{
				return 0;
			}
			Clear(lakesOut, riversOut, siteId);
			if (plan == null || !plan.Active)
			{
				return 0;
			}
			Vector3 origin = system.Origin;
			bool Open(Vector3 scene) => system.Void(scene - origin) < 0f;
			int count = Mathf.Min(plan.Bodies.Count, MaxBodies);
			var lakes = new List<SceneHydrology.Lake>();
			int written = 0;
			for (int i = 0; i < count; i++)
			{
				CaveLavaBody body = plan.Bodies[i];
				if (body.Kind != CaveLavaKind.Lake || body.Path == null || body.Path.Length == 0)
				{
					continue;
				}
				SceneHydrology.Lake lake = Lake(IdOf(siteId, i), origin + body.Path[0], body.Width, Open);
				if (lake != null)
				{
					lakesOut.Add(lake);
					lakes.Add(lake);
					written++;
				}
			}
			for (int i = 0; i < count; i++)
			{
				CaveLavaBody body = plan.Bodies[i];
				if (body.Kind == CaveLavaKind.Lake || body.Path == null || body.Path.Length < 2)
				{
					continue;
				}
				var points = new Vector3[body.Path.Length];
				for (int k = 0; k < points.Length; k++)
				{
					points[k] = origin + body.Path[k];
				}
				SceneHydrology.River river = River(IdOf(siteId, i), points, body.Width, body.Kind == CaveLavaKind.Fall, lakes);
				if (river != null)
				{
					riversOut.Add(river);
					written++;
				}
			}
			return written;
		}

		/// <summary>
		/// <see cref="Write"/> into the scene's own hydrology asset, saved: what the cave's shaper calls on every build, after the
		/// scene's water is saved (a cooled plan clears what an earlier build wrote). A scene with no lakes or rivers has no asset
		/// yet: one is made for the lava, with the scene's Inland Water object to read and draw it
		/// (SceneGenerator.EnsureInlandWater). Returns how many bodies were written.
		/// </summary>
		public static int WriteToScene(PointOfInterestPlaceContext context, PointOfInterestRecord site, CaveSystem system, CaveLavaPlan plan)
		{
			if (context == null || site == null || system == null || string.IsNullOrEmpty(context.TerrainFolder) || context.Request == null)
			{
				return 0;
			}
			string path = SceneGenerator.HydrologyPath(context.TerrainFolder, context.Request.SceneName);
			var hydrology = AssetDatabase.LoadAssetAtPath<SceneHydrology>(path);
			bool molten = plan != null && plan.Active && plan.Bodies.Count > 0;
			if (hydrology == null)
			{
				if (!molten)
				{
					return 0;
				}
				hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
				hydrology.SeaLevel = context.Water != null ? context.Water.SeaLevel : float.NegativeInfinity;
				AssetDatabase.CreateAsset(hydrology, path);
			}
			int removed = Clear(hydrology, site.Id);
			int written = Write(hydrology, site.Id, system, plan);
			if (removed == 0 && written == 0)
			{
				return 0;
			}
			if (written > 0 && context.Scene.IsValid())
			{
				SceneGenerator.EnsureInlandWater(context.Scene, hydrology);
			}
			EditorUtility.SetDirty(hydrology);
			AssetDatabase.SaveAssetIfDirty(hydrology);
			return written;
		}

		/// <summary>
		/// A lava lake at <paramref name="centre"/>'s height (scene metres) over the disc of <paramref name="radius"/> round it, in
		/// <paramref name="cell"/> cells, a cell taken where <paramref name="open"/> says the space just under the surface (5 cm
		/// down to a metre) is open at its centre. Null when no cell is.
		/// </summary>
		public static SceneHydrology.Lake Lake(int id, Vector3 centre, float radius, Func<Vector3, bool> open, float cell = LakeMaskCell)
		{
			radius = Mathf.Max(0.5f, radius);
			cell = Mathf.Max(0.25f, cell);
			float level = centre.y;
			var origin = new Vector2(centre.x - radius - cell, centre.z - radius - cell);
			int width = Mathf.CeilToInt((2f * radius + 2f * cell) / cell), height = width;
			var bits = new byte[(width * height + 7) / 8];
			int covered = 0;
			Vector2 seed = default;
			float nearest = float.PositiveInfinity;
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float east = origin.x + (x + 0.5f) * cell, north = origin.y + (z + 0.5f) * cell;
					float dx = east - centre.x, dz = north - centre.z, d2 = dx * dx + dz * dz;
					if (d2 > radius * radius)
					{
						continue;
					}
					bool here = open == null;
					for (float drop = 0.05f; !here && drop <= 1.001f; drop += 0.475f)
					{
						here = open(new Vector3(east, level - drop, north));
					}
					if (!here)
					{
						continue;
					}
					int bit = z * width + x;
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
					covered++;
					// Its seed the cell nearest its centre: inside the lava however the walls cut the disc.
					if (d2 < nearest)
					{
						nearest = d2;
						seed = new Vector2(east, north);
					}
				}
			}
			if (covered == 0)
			{
				return null;
			}
			return new SceneHydrology.Lake
			{
				Id = id,
				PlanetLake = -1,
				Level = level,
				SpillLevel = level,
				Lava = true,
				Seeds = new[] { seed },
				Bounds = new Rect(origin.x, origin.y, width * cell, height * cell),
				Mask = bits,
				MaskOrigin = origin,
				MaskCell = cell,
				MaskWidth = width,
				MaskHeight = height,
			};
		}

		/// <summary>
		/// A lava river down <paramref name="points"/> (scene metres, downstream, y its surface), <paramref name="width"/> across:
		/// its surface held from rising downstream, its speed and reach from its slope (every point a fall when
		/// <paramref name="fall"/>), into or out of any of <paramref name="lakes"/> its ends lie in. Null with fewer than two points.
		/// </summary>
		public static SceneHydrology.River River(int id, IReadOnlyList<Vector3> points, float width, bool fall, IReadOnlyList<SceneHydrology.Lake> lakes = null)
		{
			int n = points != null ? points.Count : 0;
			if (n < 2)
			{
				return null;
			}
			width = Mathf.Max(0.5f, width);
			var river = new SceneHydrology.River
			{
				Id = id,
				PlanetRiver = -1,
				Perennial = true,
				Lava = true,
				Start = SceneHydrology.End.Source,
				Finish = SceneHydrology.End.Sink,
				Points = new Vector3[n],
				Bed = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Discharge = new float[n],
				Speed = new float[n],
				Reach = new byte[n],
			};
			for (int i = 0; i < n; i++)
			{
				Vector3 p = points[i];
				// A river's surface never rises downstream (SceneHydrology).
				if (i > 0)
				{
					p.y = Mathf.Min(p.y, river.Points[i - 1].y);
				}
				river.Points[i] = p;
			}
			// Into the lake its end lies in, at the lake's level; out of the one its start lies in.
			SceneHydrology.Lake into = LakeAt(lakes, river.Points[n - 1]), outOf = LakeAt(lakes, river.Points[0]);
			if (into != null)
			{
				river.Finish = SceneHydrology.End.Lake;
				river.EndLake = into.Id;
				river.Points[n - 1].y = Mathf.Min(into.Level, river.Points[n - 2].y);
			}
			if (outOf != null && outOf != into)
			{
				river.Start = SceneHydrology.End.Lake;
				river.StartLake = outOf.Id;
				river.Points[0].y = Mathf.Max(outOf.Level, river.Points[1].y);
			}
			for (int i = 0; i < n; i++)
			{
				float slope = Mathf.Max(i > 0 ? Slope(river.Points[i - 1], river.Points[i]) : 0f, i + 1 < n ? Slope(river.Points[i], river.Points[i + 1]) : 0f);
				river.Width[i] = width;
				river.Depth[i] = RiverDepth;
				river.Bed[i] = river.Points[i].y - RiverDepth;
				river.Speed[i] = Mathf.Clamp(MinSpeed + 6f * slope, MinSpeed, MaxSpeed);
				river.Discharge[i] = width * RiverDepth * river.Speed[i];
				river.Reach[i] = fall || slope >= FallSlope ? (byte)4 : slope >= RapidSlope ? (byte)3 : (byte)0;
			}
			return river;
		}

		/// <summary>Drop over horizontal run between two points; a drop with no run is a fall (a large slope).</summary>
		private static float Slope(Vector3 a, Vector3 b)
		{
			float run = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
			float drop = Mathf.Max(0f, a.y - b.y);
			return drop / Mathf.Max(run, 0.01f);
		}

		/// <summary>The lake whose lava covers a point, its level within two metres of the point's height; null for none.</summary>
		private static SceneHydrology.Lake LakeAt(IReadOnlyList<SceneHydrology.Lake> lakes, Vector3 p)
		{
			if (lakes == null)
			{
				return null;
			}
			foreach (SceneHydrology.Lake lake in lakes)
			{
				if (lake != null && Mathf.Abs(lake.Level - p.y) <= 2f && lake.Covers(p.x, p.z))
				{
					return lake;
				}
			}
			return null;
		}
	}
}
#endif
