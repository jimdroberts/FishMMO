#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Writes a cave's water (<see cref="CaveStreams.Plan"/>) into its scene's hydrology as lakes and rivers (2026-10-20, caves
	/// livened): so it is the game's one water everywhere. <see cref="FishMMO.Shared.Weather.WaterQuery"/> reports it (waded,
	/// swum, breath), the inland water's renderer draws it (its falls with their sheets, spray and roar), and the world's water
	/// sounds and life find it as they find any river.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Ids.</b> In the grotto pools' range (<see cref="CaveShaper.PoolIdBase"/> up to <see cref="MoatShaper.LakeIdBase"/>), so
	/// every rule for a cave's own water holds for its streams and pools: no floating plants (FloatingPlants.IsGrottoPool),
	/// dropped by a plan made again (MoatShaper.ClearWater), left alone by the water's settling (WaterSettle); never read back
	/// into the water the ground is cut by (SceneWater.FromAsset). A grotto's pool takes only the site's low 24 bits over the
	/// base and the cave's lava sets bit 28 (<see cref="CaveLavaWater"/>); the water leaves bit 28 clear and keeps the body's
	/// index plus one in bits 24..27 (never zero: zero there is the grotto's pool), for lakes and rivers alike (separate lists).
	/// </para>
	/// <para>
	/// <b>Written by the cave, every time it is built</b>, after the scene's water is saved, as a grotto's pool and the lava
	/// are (CaveShaper.WritePool, WriteLava). Scene metres are the system's frame moved to its origin: the system is never turned.
	/// A frozen plan (an ice cave's) writes nothing: its ice is the dressing's.
	/// </para>
	/// </remarks>
	public static class CaveStreamWater
	{
		/// <summary>The grotto pools' base: a cave's water takes it with its body's index plus one in bits 24..27.</summary>
		public const int IdBase = CaveShaper.PoolIdBase;
		/// <summary>How many bodies of water a system may write: four bits, the zero kept for the grotto's own pool.</summary>
		public const int MaxBodies = 15;
		/// <summary>A pool's mask cell, metres: finer than the scene lakes' 4 m, so a cave's pool follows its walls.</summary>
		public const float LakeMaskCell = 1f;
		/// <summary>Surface slope (rise over run) from which a cave stream runs as a rapid.</summary>
		public const float RapidSlope = 0.04f;
		/// <summary>The slowest and the fastest a cave stream's surface runs, m/s.</summary>
		public const float MinSpeed = 0.25f;
		public const float MaxSpeed = 2.5f;
		/// <summary>The widest a spring's water leaves its crack, metres (the fall's curtain is its lip's width).</summary>
		public const float SpringWidth = 0.9f;

		private const int SiteMask = 0x00FFFFFF;

		/// <summary>The id of a site's body of water (its index in the plan), for its lake or its river.</summary>
		public static int IdOf(int siteId, int body) => IdBase | (((body + 1) & 0xF) << 24) | (siteId & SiteMask);

		/// <summary>True for any cave's water id (not its grotto pool's, not its lava's).</summary>
		public static bool IsCaveStream(int id) => (id & 0x70000000) == IdBase && (id & 0x0F000000) != 0;

		/// <summary>True for a water id of <paramref name="siteId"/>'s.</summary>
		public static bool OfSite(int id, int siteId) => IsCaveStream(id) && (id & SiteMask) == (siteId & SiteMask);

		/// <summary>Takes a site's water out of the hydrology: every lake and river <see cref="IdOf"/> it. Returns how many.</summary>
		public static int Clear(SceneHydrology hydrology, int siteId) => hydrology != null ? Clear(hydrology.Lakes, hydrology.Rivers, siteId) : 0;

		/// <summary><see cref="Clear(SceneHydrology, int)"/> on a hydrology's two lists.</summary>
		public static int Clear(List<SceneHydrology.Lake> lakes, List<SceneHydrology.River> rivers, int siteId)
		{
			return (lakes != null ? lakes.RemoveAll(l => l != null && OfSite(l.Id, siteId)) : 0)
				+ (rivers != null ? rivers.RemoveAll(r => r != null && OfSite(r.Id, siteId)) : 0);
		}

		/// <summary>
		/// Replaces a site's water in the hydrology with its system's plan: the old taken out (<see cref="Clear"/>), then one lake
		/// per pool and one river per stream, at its index (bodies past <see cref="MaxBodies"/> are left out). Nothing is written
		/// for a dry or a frozen plan. Does not save the asset. Returns how many bodies were written.
		/// </summary>
		/// <remarks>
		/// A <see cref="CaveStreamKind.Pool"/> is a lake at its point's height over the disc of its <c>Width</c> round it, where the
		/// cave is open just under that height (<see cref="CaveSystem.Void"/>, its bed already cut). A
		/// <see cref="CaveStreamKind.Stream"/> is a river down its path, its points reached falling marked as a fall (the inland
		/// water draws a curtain there), into the pool it runs into.
		/// </remarks>
		public static int Write(List<SceneHydrology.Lake> lakesOut, List<SceneHydrology.River> riversOut, int siteId, CaveSystem system, CaveStreamPlan plan)
		{
			if (lakesOut == null || riversOut == null || system == null)
			{
				return 0;
			}
			Clear(lakesOut, riversOut, siteId);
			if (plan == null || !plan.Active || plan.Frozen)
			{
				return 0;
			}
			Vector3 origin = system.Origin;
			bool Open(Vector3 scene) => system.Void(scene - origin) < 0f;
			int count = Mathf.Min(plan.Bodies.Count, MaxBodies);
			var pools = new SceneHydrology.Lake[count];
			int written = 0;
			for (int i = 0; i < count; i++)
			{
				CaveStreamBody body = plan.Bodies[i];
				if (body.Kind != CaveStreamKind.Pool || body.Path == null || body.Path.Length == 0)
				{
					continue;
				}
				// The lava's lake maker: its mask follows the cave's walls; then made water.
				SceneHydrology.Lake lake = CaveLavaWater.Lake(IdOf(siteId, i), origin + body.Path[0], body.Width, Open, LakeMaskCell);
				if (lake == null)
				{
					continue;
				}
				lake.Lava = false;
				lake.Terminal = true;
				pools[i] = lake;
				lakesOut.Add(lake);
				written++;
			}
			var rivers = new List<SceneHydrology.River>();
			for (int i = 0; i < count; i++)
			{
				CaveStreamBody body = plan.Bodies[i];
				if (body.Kind != CaveStreamKind.Stream || body.Path == null || body.Path.Length < 2)
				{
					continue;
				}
				var points = new Vector3[body.Path.Length];
				for (int k = 0; k < points.Length; k++)
				{
					points[k] = origin + body.Path[k];
				}
				SceneHydrology.Lake into = body.Into >= 0 && body.Into < count ? pools[body.Into] : null;
				// One ending at an awash cave's own water runs into it (a grotto's spring: its pool, CaveShaper.WritePool).
				if (into == null && body.Into < 0 && !float.IsNaN(system.WaterLevel) && Mathf.Abs(points[points.Length - 1].y - system.WaterLevel) < 0.05f)
				{
					into = lakesOut.Find(l => l != null && l.Id == (CaveShaper.PoolIdBase | (siteId & SiteMask)));
				}
				SceneHydrology.River river = River(IdOf(siteId, i), points, body.Falls, body.Width, body.Depth, into);
				if (river != null)
				{
					riversOut.Add(river);
					rivers.Add(river);
					written++;
				}
			}
			// A shower's fall into the stream under it, a run on under the rock into the next (the lava's rule: CaveLavaWater.Join).
			CaveLavaWater.Join(rivers);
			return written;
		}

		/// <summary><see cref="Write(List{SceneHydrology.Lake}, List{SceneHydrology.River}, int, CaveSystem, CaveStreamPlan)"/> into a hydrology.</summary>
		public static int Write(SceneHydrology hydrology, int siteId, CaveSystem system, CaveStreamPlan plan)
			=> hydrology != null ? Write(hydrology.Lakes, hydrology.Rivers, siteId, system, plan) : 0;

		/// <summary>
		/// <see cref="Write(SceneHydrology, int, CaveSystem, CaveStreamPlan)"/> into the scene's own hydrology asset, saved: what the
		/// cave's shaper calls on every build, after the scene's water is saved (a dry plan clears what an earlier build wrote).
		/// A scene with no lakes or rivers has no asset yet: one is made for the cave's water, with the scene's Inland Water object
		/// to read and draw it (SceneGenerator.EnsureInlandWater). Returns how many bodies were written.
		/// </summary>
		public static int WriteToScene(PointOfInterestPlaceContext context, PointOfInterestRecord site, CaveSystem system, CaveStreamPlan plan)
		{
			if (context == null || site == null || system == null || string.IsNullOrEmpty(context.TerrainFolder) || context.Request == null)
			{
				return 0;
			}
			string path = SceneGenerator.HydrologyPath(context.TerrainFolder, context.Request.SceneName);
			var hydrology = AssetDatabase.LoadAssetAtPath<SceneHydrology>(path);
			bool wet = plan != null && plan.Active && !plan.Frozen && plan.Bodies.Count > 0;
			if (hydrology == null)
			{
				if (!wet)
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
		/// A cave stream down <paramref name="points"/> (scene metres, downstream, y its surface), <paramref name="width"/> across and
		/// <paramref name="depth"/> deep: its surface held from rising downstream; a fall over every run of points reached falling
		/// (<paramref name="falls"/>), a spring's at its head no wider than its crack; rapids where it runs steep; into
		/// <paramref name="into"/> when set (its end at the pool's level). Null with fewer than two points.
		/// </summary>
		public static SceneHydrology.River River(int id, IReadOnlyList<Vector3> points, IReadOnlyList<bool> falls, float width, float depth, SceneHydrology.Lake into = null)
		{
			int n = points != null ? points.Count : 0;
			if (n < 2)
			{
				return null;
			}
			width = Mathf.Max(0.4f, width);
			depth = Mathf.Max(0.1f, depth);
			var river = new SceneHydrology.River
			{
				Id = id,
				PlanetRiver = -1,
				Perennial = true,
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
				if (i > 0)
				{
					p.y = Mathf.Min(p.y, river.Points[i - 1].y);
				}
				river.Points[i] = p;
			}
			if (into != null && into.Covers(river.Points[n - 1].x, river.Points[n - 1].z))
			{
				river.Finish = SceneHydrology.End.Lake;
				river.EndLake = into.Id;
				river.Points[n - 1].y = Mathf.Min(into.Level, river.Points[n - 2].y);
			}
			bool Falling(int k) => falls != null && k >= 0 && k < falls.Count && falls[k];
			// The first point not reached falling: before it, a spring's fall from its crack (its water as narrow as the crack).
			int head = 0;
			while (head < n && (Falling(head) || Falling(head + 1)))
			{
				head++;
			}
			for (int i = 0; i < n; i++)
			{
				float slope = Mathf.Max(i > 0 ? Slope(river.Points[i - 1], river.Points[i]) : 0f, i + 1 < n ? Slope(river.Points[i], river.Points[i + 1]) : 0f);
				// A fall's inner points: reached falling, and falling on to the next (its lip the point before, its foot the point after).
				bool fall = Falling(i) && Falling(i + 1);
				float w = i < head && falls != null && Falling(1) ? Mathf.Min(width, SpringWidth) : width;
				river.Width[i] = w;
				river.Depth[i] = depth;
				river.Bed[i] = river.Points[i].y - depth;
				river.Speed[i] = fall ? MaxSpeed : RunSpeed(slope);
				river.Discharge[i] = w * depth * RunSpeed(slope);
				// Its lip and its foot run smooth into and out of the curtain: the rapid is the stream's own steep runs.
				bool edge = Falling(i) || Falling(i + 1);
				river.Reach[i] = fall ? (byte)4 : slope >= RapidSlope && !edge ? (byte)3 : (byte)0;
			}
			return river;
		}

		/// <summary>How fast a cave stream runs where its surface falls at a slope (rise over run), m/s: the steeper the faster, to a fall's.</summary>
		public static float RunSpeed(float slope) => Mathf.Clamp(MinSpeed + 4f * Mathf.Min(slope, 0.5f), MinSpeed, MaxSpeed);

		/// <summary>
		/// The water per metre of lip going over a cave fall of a body <paramref name="depth"/> deep, m²/s, as <see cref="River"/>
		/// writes it (its lip's discharge over its width: the depth at its steepest run's speed) and the inland water reads it
		/// (FishMMO.Water.FallHydraulics.UnitDischarge): what throws its curtain out from the lip (FallHydraulics.Throw).
		/// </summary>
		public static float LipUnitDischarge(float depth) => Mathf.Max(0.1f, depth) * RunSpeed(float.MaxValue);

		/// <summary>How far under a cave pool's surface its NavMesh marking reaches, metres (a winze's water deeper still is climbed, not walked).</summary>
		public const float DeepMarkMetres = 6f;

		/// <summary>
		/// Marks the bed of every cave pool too deep to wade (<see cref="IsCaveStream"/> lakes) as the NavMesh's deep water
		/// (SceneNavMeshBaker.DeepWaterArea): boxes over its mask's rows, run-merged, from <see cref="DeepMarkMetres"/> under its
		/// surface up to the wading depth under it (SceneNavMeshBaker.WadeMetres), so only the bed deeper than a wade is inside and
		/// its shelving edge stays plain walkable. Returns how many boxes.
		/// </summary>
		/// <remarks>
		/// Not the terrain grid SceneNavMeshBaker.WaterModifiers reads: a cave's water lies tens of metres under the hillside, and
		/// the grid knows only the ground over it. NPCs that cannot swim have the area masked out (AIController.Swimming).
		/// </remarks>
		public static int DeepModifiers(IReadOnlyList<SceneHydrology.Lake> lakes, List<NavMeshBuildSource> into)
		{
			if (lakes == null || into == null)
			{
				return 0;
			}
			int area = NavMesh.GetAreaFromName(SceneNavMeshBaker.DeepWaterArea);
			if (area < 0)
			{
				area = NavMesh.GetAreaFromName("Not Walkable");
			}
			float top = SceneNavMeshBaker.WadeMetres, height = DeepMarkMetres - top;
			int boxes = 0;
			foreach (SceneHydrology.Lake lake in lakes)
			{
				if (lake == null || lake.Lava || !IsCaveStream(lake.Id) || lake.Mask == null || lake.MaskCell <= 0f)
				{
					continue;
				}
				float cell = lake.MaskCell;
				for (int z = 0; z < lake.MaskHeight; z++)
				{
					int x = 0;
					while (x < lake.MaskWidth)
					{
						float cz = lake.MaskOrigin.y + (z + 0.5f) * cell;
						if (!lake.Covers(lake.MaskOrigin.x + (x + 0.5f) * cell, cz))
						{
							x++;
							continue;
						}
						int x1 = x;
						while (x1 + 1 < lake.MaskWidth && lake.Covers(lake.MaskOrigin.x + (x1 + 1.5f) * cell, cz))
						{
							x1++;
						}
						into.Add(new NavMeshBuildSource
						{
							shape = NavMeshBuildSourceShape.ModifierBox,
							transform = Matrix4x4.Translate(new Vector3(lake.MaskOrigin.x + (x + x1 + 1) * 0.5f * cell, lake.Level - top - 0.5f * height, cz)),
							size = new Vector3((x1 - x + 1) * cell, height, cell),
							area = area,
						});
						boxes++;
						x = x1 + 1;
					}
				}
			}
			return boxes;
		}

		/// <summary>Drop over horizontal run between two points; a drop with no run is a fall (a large slope).</summary>
		private static float Slope(Vector3 a, Vector3 b)
		{
			float run = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
			float drop = Mathf.Max(0f, a.y - b.y);
			return drop / Mathf.Max(run, 0.01f);
		}
	}
}
#endif
