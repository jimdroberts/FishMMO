#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Carves the cave kinds (cave, grotto, sea cave, ice cave, lava tube) and mines into the ground: plans the system
	/// (<see cref="CaveSystem"/>), joins it to a nearby cave's, holes the terrain at its mouth, then builds its rock as
	/// colliders on the Ground layer with client-only visuals (<see cref="CaveMesher"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Carved, not placed</b> (2026-10-10: "carve our own"). Unity's terrain has no caves, only holes, so the
	/// cave is meshes of our own and the terrain is holed where it opens. Deep (2026-10-11: "caves should go very
	/// deep, open up into chambers, connect to each other"): a trunk passage falls tens of metres under its mouth through
	/// rooms to a great cavern, side passages wander off, and a cave planned near one already carved is joined to it deep
	/// down, so the two sites are one system with two mouths. A mine is a straight adit with drifts and stopes. A large
	/// cave's great cavern holds a dungeon instance's entrance (PointOfInterestFeatureDefaults).
	/// </para>
	/// <para>
	/// <b>One system, stored once.</b> The system is stored as the shape of the site whose cave it first was
	/// (<see cref="PointOfInterestShape.Values"/> from <see cref="CaveSystem.ToValues"/>); a site joined to it stores a
	/// link (<see cref="LinkValues"/>: format, −1, the host's id in two halves) and its own mouth as the shape's position and heading, so
	/// the way to it still comes to its mouth (PathNetworkPlanner). Only the host meshes the rock.
	/// </para>
	/// <para>
	/// <b>A scene object, not a prop.</b> Every cave is its own meshes: sub-assets of one asset per scene,
	/// <c>&lt;Scene&gt; Caves.asset</c> beside the terrain, rewritten in place (so a repaint keeps every reference) and
	/// pruned of caves the scene no longer has. The colliders are on the cave's root, which the server keeps and the
	/// NavMesh bake collects (no floor is over 22°); the renderers are under a <see cref="ClientOnlyObject"/>, which
	/// server builds strip, and drawn near the camera only (<see cref="CaveVisibility"/>).
	/// </para>
	/// <para>
	/// <b>Plan once, build every time.</b> <see cref="Plan"/> runs on a cut (it holes the terrain, which a repaint must
	/// not do again); <see cref="Place"/> builds the same rock from the stored system on every cut and repaint.
	/// </para>
	/// </remarks>
	public sealed class CaveShaper : IPointOfInterestShaper
	{
		public static readonly CaveShaper Instance = new CaveShaper();

		/// <summary>The shape name a cave's system (or its link to one) is stored under.</summary>
		public const string ShaperName = "Cave";

		/// <summary>The sea's surface, world y: generated scenes put it at 0 (fishmmo-world-scale).</summary>
		public const float SeaLevel = 0f;

		/// <summary>How far apart two caves' mouths may be and still be joined into one system, metres.</summary>
		public const float JoinReach = 420f;

		/// <summary>The longest passage that joins two systems, metres (flat).</summary>
		public const float LinkLength = 320f;

		public bool Handles(POIType kind) => TryForm(kind, out _);

		/// <summary>The cave form a kind is carved as.</summary>
		public static bool TryForm(POIType kind, out CaveForm form)
		{
			switch (kind)
			{
				case POIType.Cave: form = CaveForm.Cave; return true;
				case POIType.Grotto: form = CaveForm.Grotto; return true;
				case POIType.SeaCave: form = CaveForm.SeaCave; return true;
				case POIType.IceCave: form = CaveForm.IceCave; return true;
				case POIType.LavaTube: form = CaveForm.LavaTube; return true;
				case POIType.Mine: form = CaveForm.Mine; return true;
				default: form = CaveForm.Cave; return false;
			}
		}

		/// <summary>Which forms may share a system: limestone caves with grottos and sea caves, ice with ice, lava tubes with lava tubes; never mines.</summary>
		private static int Family(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.Cave:
				case CaveForm.Grotto:
				case CaveForm.SeaCave:
					return 0;
				case CaveForm.IceCave:
					return 1;
				case CaveForm.LavaTube:
					return 2;
				default:
					return -1;
			}
		}

		/// <summary>A cave's own seed, from its site's.</summary>
		public static int SeedOf(PointOfInterestRecord site) => (int)(PointOfInterestPlanner.Mix(unchecked((uint)site.SiteSeed ^ 0xCA7E5EEDu)) & 0x7FFFFFFFu);

		// ── Plan ──────────────────────────────────────────────────

		public void Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			TryForm(site.Kind, out CaveForm form);
			TerrainTilePlan tiles = context.Tiles;
			CaveSystem.Profile profile = CaveSystem.ProfileFor(form, site.SizeClass);
			CaveGround ground = SampleGround(context.GroundAt, tiles, site.Position.x, site.Position.z, profile.TrunkMax + 60f);
			float water = form == CaveForm.Grotto ? WaterBefore(context, site) : float.NaN;
			CaveSystem system = CaveSystem.Plan(form, site.SizeClass, site.Position, site.Yaw, SeedOf(site), site.Id, ground, SeaLevel, out string problem, water);
			if (system == null)
			{
				context.Reject(site, problem);
				return;
			}
			if (!HoleGrid(context.Terrains, tiles, out float step, out float gridX, out float gridZ))
			{
				context.Reject(site, "the scene has no terrain to open it in");
				return;
			}

			// Joined to a system already carved near it, where a passage fits between them deep down.
			if (TryJoin(context, site, system, step, gridX, gridZ))
			{
				return;
			}
			List<Vector2Int> cells = system.HoleCells(0, context.GroundAt, step, gridX, gridZ);
			CutHoles(context.Terrains, tiles, cells, step, gridX, gridZ);
			AddMouthKeepOut(context, site, system, 0);
			context.AddShape(ShapeOf(site, system));
		}

		/// <summary>
		/// Joins a newly planned system into the nearest compatible one already planned (its host), if a link passage fits:
		/// the host's stored system grows by this one, this site stores a link to it, and its mouth is holed.
		/// </summary>
		private static bool TryJoin(PointOfInterestPlanContext context, PointOfInterestRecord site, CaveSystem system, float step, float gridX, float gridZ)
		{
			int family = Family(system.Form);
			if (family < 0)
			{
				return false;
			}
			var candidates = new List<(float distance, PointOfInterestShape shape)>();
			foreach (PointOfInterestShape shape in context.Plan.Shapes)
			{
				if (shape == null || shape.Shaper != ShaperName || IsLink(shape.Values, out _) || context.Rejected.Contains(shape.SiteId))
				{
					continue;
				}
				float d = Vector2.Distance(new Vector2(shape.Position.x, shape.Position.z), new Vector2(system.Origin.x, system.Origin.z));
				if (d <= JoinReach)
				{
					candidates.Add((d, shape));
				}
			}
			candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
			foreach ((float _, PointOfInterestShape hostShape) in candidates)
			{
				CaveSystem host = CaveSystem.FromValues(hostShape.Values, hostShape.Position, hostShape.Seed);
				if (host == null || Family(host.Form) != family)
				{
					continue;
				}
				Bounds a = host.Extent, b = system.Extent;
				float minX = Mathf.Min(host.Origin.x + a.min.x, system.Origin.x + b.min.x) - 20f, maxX = Mathf.Max(host.Origin.x + a.max.x, system.Origin.x + b.max.x) + 20f;
				float minZ = Mathf.Min(host.Origin.z + a.min.z, system.Origin.z + b.min.z) - 20f, maxZ = Mathf.Max(host.Origin.z + a.max.z, system.Origin.z + b.max.z) + 20f;
				CaveGround both = SampleGround(context.GroundAt, context.Tiles, minX, minZ, maxX, maxZ);
				if (!host.TryJoin(system, both, LinkLength, unchecked(SeedOf(site) * 31 + hostShape.SiteId)))
				{
					continue;
				}
				int mouth = host.Mouths.Count - system.Mouths.Count;
				List<Vector2Int> cells = host.HoleCells(mouth, context.GroundAt, step, gridX, gridZ);
				CutHoles(context.Terrains, context.Tiles, cells, step, gridX, gridZ);
				hostShape.Values = host.ToValues();
				hostShape.Size = SizeOf(host);
				AddMouthKeepOut(context, site, host, mouth);
				CaveMouth m = host.Mouths[mouth];
				context.AddShape(new PointOfInterestShape
				{
					SiteId = site.Id,
					Shaper = ShaperName,
					Position = host.Origin + m.Floor,
					Yaw = m.Yaw,
					Size = new Vector3(2f * m.Radius, 0f, 0f),
					Seed = hostShape.Seed,
					Values = LinkValues(hostShape.SiteId),
				});
				context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}): joined deep down to the cave of site {hostShape.SiteId}.");
				return true;
			}
			return false;
		}

		/// <summary>
		/// The surface of the water before a grotto's face (out along its heading, up to 30 m): a lake's or a river's, or
		/// the sea's where the ground runs under it; NaN where there is none.
		/// </summary>
		private static float WaterBefore(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			float yr = site.Yaw * Mathf.Deg2Rad;
			var outward = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
			for (float d = 0f; d <= 30f; d += 1f)
			{
				float x = site.Position.x + outward.x * d, z = site.Position.z + outward.y * d;
				float inland = context.Water != null ? context.Water.SurfaceAt(x, z) : float.NegativeInfinity;
				if (!float.IsNegativeInfinity(inland) && inland > SeaLevel)
				{
					return inland;
				}
				if (context.GroundAt(x, z) <= SeaLevel)
				{
					return SeaLevel;
				}
			}
			return float.NaN;
		}

		/// <summary>Lake ids grottos' pools take (moats take MoatShaper.LakeIdBase's): this with the site id's low 24 bits.</summary>
		public const int PoolIdBase = 1 << 29;

		/// <summary>
		/// An inland grotto's pool (at the sea the sea's own surface fills it): a lake of the scene's hydrology at the water
		/// it opens on, over the cells where the cave's floor lies under that level and the hill stands over it (outside, the
		/// lake or river it opens on is the water). Written after the scene's water is saved, on every cut, repaint and
		/// regeneration, so it is drawn and swum like any other lake.
		/// </summary>
		private static void WritePool(PointOfInterestPlaceContext context, PointOfInterestRecord site, CaveSystem system, CaveGround ground)
		{
			if (system.Form != CaveForm.Grotto || float.IsNaN(system.WaterLevel) || system.WaterLevel <= SeaLevel + 0.05f
				|| string.IsNullOrEmpty(context.TerrainFolder) || context.Request == null)
			{
				return;
			}
			var hydrology = AssetDatabase.LoadAssetAtPath<SceneHydrology>(SceneGenerator.HydrologyPath(context.TerrainFolder, context.Request.SceneName));
			if (hydrology == null)
			{
				return;
			}
			int id = PoolIdBase | (site.Id & 0x00FFFFFF);
			hydrology.Lakes.RemoveAll(l => l.Id == id);
			float level = system.WaterLevel, cell = SceneWater.LakeMaskCell;
			Bounds extent = system.Extent;
			var origin = new Vector2(system.Origin.x + extent.min.x - cell, system.Origin.z + extent.min.z - cell);
			int width = Mathf.CeilToInt((extent.size.x + 2f * cell) / cell), height = Mathf.CeilToInt((extent.size.z + 2f * cell) / cell);
			var bits = new byte[(width * height + 7) / 8];
			int covered = 0;
			Vector2 seed = default;
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float east = origin.x + (x + 0.5f) * cell, north = origin.y + (z + 0.5f) * cell;
					var local = new Vector3(east - system.Origin.x, level - 0.05f - system.Origin.y, north - system.Origin.z);
					if (system.Void(local) >= 0f || ground.At(east, north) < level + 0.5f)
					{
						continue;
					}
					int bit = z * width + x;
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
					seed = new Vector2(east, north);
					covered++;
				}
			}
			if (covered > 0)
			{
				hydrology.Lakes.Add(new SceneHydrology.Lake
				{
					Id = id,
					PlanetLake = -1,
					Level = level,
					SpillLevel = level,
					Seeds = new[] { seed },
					Bounds = new Rect(origin.x, origin.y, width * cell, height * cell),
					Mask = bits,
					MaskOrigin = origin,
					MaskCell = cell,
					MaskWidth = width,
					MaskHeight = height,
				});
			}
			EditorUtility.SetDirty(hydrology);
			AssetDatabase.SaveAssetIfDirty(hydrology);
		}

		/// <summary>Nothing grows, falls or is stood in a mouth or on the lip round it.</summary>
		private static void AddMouthKeepOut(PointOfInterestPlanContext context, PointOfInterestRecord site, CaveSystem system, int mouth)
		{
			CaveMouth m = system.Mouths[mouth];
			float yr = m.Yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
			Vector3 front = system.Origin + m.Floor + outward * m.Radius;
			context.AddKeepOut(site, front.x, front.z, Mathf.Max(site.Radius, 1.5f * m.Radius + m.Collar));
		}

		/// <summary>The stored form of a planned system.</summary>
		public static PointOfInterestShape ShapeOf(PointOfInterestRecord site, CaveSystem system)
			=> new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = system.Origin,
				Yaw = system.Mouths[0].Yaw,
				Size = SizeOf(system),
				Seed = system.Seed,
				Values = system.ToValues(),
			};

		/// <summary>A shape's extents: the bore's width, the depth under the first mouth, the passages' length.</summary>
		private static Vector3 SizeOf(CaveSystem system) => new Vector3(2f * system.Radius, system.Depth, system.PassageLength);

		/// <summary>A joined site's stored link to its host's system.</summary>
		public static float[] LinkValues(int hostSiteId) => new float[] { CaveSystem.Format, -1f, CaveSystem.IdHigh(hostSiteId), CaveSystem.IdLow(hostSiteId) };

		/// <summary>Whether stored values are a link (and to which host) rather than a system.</summary>
		public static bool IsLink(float[] values, out int hostSiteId)
		{
			hostSiteId = 0;
			if (values == null || values.Length != 4 || Mathf.RoundToInt(values[0]) != CaveSystem.Format || Mathf.RoundToInt(values[1]) != -1)
			{
				return false;
			}
			hostSiteId = CaveSystem.IdOf(values[2], values[3]);
			return true;
		}

		/// <summary>
		/// The ground within <paramref name="reach"/> of (x, z), on the heightmap's own samples (the tiles' grid), so the
		/// mesher reads back exactly the terrain's interpolation.
		/// </summary>
		public static CaveGround SampleGround(System.Func<float, float, float> groundAt, TerrainTilePlan tiles, float x, float z, float reach)
			=> SampleGround(groundAt, tiles, x - reach, z - reach, x + reach, z + reach);

		public static CaveGround SampleGround(System.Func<float, float, float> groundAt, TerrainTilePlan tiles, float minX, float minZ, float maxX, float maxZ)
		{
			float spacing = tiles.Resolution > 1 ? tiles.MetresPerSample : 1f;
			return CaveGround.Sample(groundAt, minX, minZ, maxX, maxZ, spacing, -0.5f * tiles.WidthMetres, -0.5f * tiles.DepthMetres);
		}

		/// <summary>The terrain's hole grid: one cell per heightmap quad, lines from the scene's south-west corner.</summary>
		public static bool HoleGrid(Terrain[,] terrains, TerrainTilePlan tiles, out float step, out float gridX, out float gridZ)
		{
			step = 1f;
			gridX = -0.5f * tiles.WidthMetres;
			gridZ = -0.5f * tiles.DepthMetres;
			foreach (Terrain terrain in terrains)
			{
				if (terrain != null && terrain.terrainData != null)
				{
					step = tiles.TileMetres / Mathf.Max(1, terrain.terrainData.holesResolution);
					return true;
				}
			}
			return false;
		}

		/// <summary>Holes the terrain's quads in <paramref name="cells"/> (from <see cref="CaveSystem.HoleCells"/>).</summary>
		public static void CutHoles(Terrain[,] terrains, TerrainTilePlan tiles, List<Vector2Int> cells, float step, float gridX, float gridZ)
		{
			if (cells.Count == 0)
			{
				return;
			}
			var set = new HashSet<long>();
			int i0 = int.MaxValue, i1 = int.MinValue, k0 = int.MaxValue, k1 = int.MinValue;
			foreach (Vector2Int c in cells)
			{
				set.Add(((long)c.x << 32) ^ (uint)c.y);
				i0 = Mathf.Min(i0, c.x);
				i1 = Mathf.Max(i1, c.x);
				k0 = Mathf.Min(k0, c.y);
				k1 = Mathf.Max(k1, c.y);
			}
			var area = new Rect(gridX + i0 * step, gridZ + k0 * step, (i1 - i0 + 1) * step, (k1 - k0 + 1) * step);
			PointOfInterestTerrain.CutHoles(terrains, tiles, area, (east, north) =>
			{
				int i = Mathf.FloorToInt((east - gridX) / step), k = Mathf.FloorToInt((north - gridZ) / step);
				return set.Contains(((long)i << 32) ^ (uint)k);
			});
		}

		/// <summary>
		/// Fills the terrain's holes round every cave mouth a stored plan opened (its system's mouths, a joined site's own,
		/// or an older format's single mouth): what Regenerate POIs does before planning again, so a cave the new plan
		/// moves or drops leaves no hole in the hillside with no rock under it. Only round the mouths: canyon walls hole
		/// the terrain too. Returns the texels filled.
		/// </summary>
		public static int RefillMouths(Terrain[,] terrains, TerrainTilePlan tiles, ScenePointsOfInterest previous)
		{
			if (previous?.Shapes == null)
			{
				return 0;
			}
			var discs = new List<Vector3>();
			foreach (PointOfInterestShape shape in previous.Shapes)
			{
				if (shape == null || shape.Shaper != ShaperName)
				{
					continue;
				}
				CaveSystem system = IsLink(shape.Values, out _) ? null : CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed);
				if (system != null)
				{
					foreach (CaveMouth mouth in system.Mouths)
					{
						Vector3 at = system.Origin + mouth.Floor;
						discs.Add(new Vector3(at.x, at.z, mouth.Zone + 2f * mouth.Radius + 6f));
					}
				}
				else
				{
					// A link (its host's mouths include it) or the single-tunnel format before systems: round its anchor.
					float reach = shape.Values != null && shape.Values.Length > 11 && Mathf.RoundToInt(shape.Values[0]) == 1
						? shape.Values[11] + 2f * shape.Values[3] + 6f : 30f;
					discs.Add(new Vector3(shape.Position.x, shape.Position.z, reach));
				}
			}
			int filled = 0;
			foreach (Vector3 d in discs)
			{
				var area = new Rect(d.x - d.z, d.y - d.z, 2f * d.z, 2f * d.z);
				filled += PointOfInterestTerrain.FillHoles(terrains, tiles, area, (east, north) => (east - d.x) * (east - d.x) + (north - d.y) * (north - d.y) <= d.z * d.z);
			}
			return filled;
		}

		// ── Place ─────────────────────────────────────────────────

		public void Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			PointOfInterestShape shape = ShapeFor(context.Points, site.Id);
			if (shape != null && IsLink(shape.Values, out _))
			{
				// Its host builds the rock; the mouth is all that is its own.
				return;
			}
			CaveSystem system = shape != null ? CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed) : null;
			string label = $"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0})";
			if (system == null)
			{
				context.Notes?.Add($"{label}: no cave is stored for it (or one in an older format); re-cut the scene.");
				return;
			}
			Bounds extent = system.Extent;
			CaveGround ground = SampleGround(context.GroundAt, context.Tiles, system.Origin.x + extent.min.x - 4f, system.Origin.z + extent.min.z - 4f,
				system.Origin.x + extent.max.x + 4f, system.Origin.z + extent.max.z + 4f);
			// The collar follows the terrain the plan actually cut at each mouth, read back from the tiles' holes.
			if (HoleGrid(context.Terrains, context.Tiles, out float holeStep, out float gridX, out float gridZ))
			{
				for (int m = 0; m < system.Mouths.Count; m++)
				{
					CaveMouth mouth = system.Mouths[m];
					Vector3 at = system.Origin + mouth.Floor;
					float reach = mouth.Zone + 2f * mouth.Radius + 10f;
					List<Vector2> holes = PointOfInterestTerrain.HolesIn(context.Terrains, context.Tiles, new Rect(at.x - reach, at.z - reach, 2f * reach, 2f * reach));
					system.UseCutHoles(m, holes, context.GroundAt, holeStep, gridX, gridZ);
				}
			}
			List<CaveMesher.Piece> pieces;
			try
			{
				pieces = CaveMesher.Build(system, ground, out string report);
				context.Notes?.Add($"{label}: {system.Form}, {system.PassageLength:0} m of passages, {system.Depth:0} m deep, {system.Chambers.Count} chambers, {system.Mouths.Count} mouth(s); {report}.");
			}
			catch (System.InvalidOperationException ex)
			{
				context.Notes?.Add($"{label}: its rock would not mesh ({ex.Message}); the terrain stays holed.");
				return;
			}
			string assetPath = string.IsNullOrEmpty(context.TerrainFolder) || context.Request == null ? null
				: AssetPath(context.TerrainFolder, context.Request.SceneName);
			Mesh[] meshes = WriteMeshes(assetPath, site.Id, pieces, LiveCaves(context.Points));

			string rock = RockOf(system.Form, context.Request, site, system.Origin);
			Material material = PointOfInterestRock.MaterialOf(rock);
			if (material == null)
			{
				context.Notes?.Add($"Caves: no material for {rock} ('{CliffRocks.MaterialName(rock)}'); run Generate Biome Art, then repaint.");
			}
			Build(context.Root, system, pieces, meshes, material);
			WritePool(context, site, system, ground);
		}

		/// <summary>The rock a cave is cut in: basalt for a lava tube, ice for an ice cave, else the ground's own.</summary>
		public static string RockOf(CaveForm form, SceneGenerationRequest request, PointOfInterestRecord site, Vector3 at)
			=> form == CaveForm.LavaTube ? "Basalt" : form == CaveForm.IceCave ? CliffRocks.Ice : PointOfInterestRock.At(request, site, at);

		/// <summary>The scene's cave mesh asset: <c>&lt;Scene&gt; Caves.asset</c> in its terrain folder.</summary>
		public static string AssetPath(string terrainFolder, string sceneName) => $"{terrainFolder}/{WorldEditorAssets.Sanitize(sceneName)} Caves.asset";

		/// <summary>The cave shape stored for a site (its system, or its link to one), or null.</summary>
		public static PointOfInterestShape ShapeFor(ScenePointsOfInterest points, int siteId)
		{
			if (points == null || points.Shapes == null)
			{
				return null;
			}
			foreach (PointOfInterestShape shape in points.Shapes)
			{
				if (shape != null && shape.SiteId == siteId && shape.Shaper == ShaperName)
				{
					return shape;
				}
			}
			return null;
		}

		/// <summary>The system a site's cave is part of (its own, or its host's when it was joined to one), or null.</summary>
		public static CaveSystem SystemOf(ScenePointsOfInterest points, int siteId)
		{
			PointOfInterestShape shape = ShapeFor(points, siteId);
			if (shape != null && IsLink(shape.Values, out int host))
			{
				shape = ShapeFor(points, host);
				if (shape == null || IsLink(shape.Values, out _))
				{
					return null;
				}
			}
			return shape != null ? CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed) : null;
		}

		/// <summary>
		/// Where a site's own chamber's floor is (world: its great cavern, else its deepest), how far from it the floor
		/// reaches and which way the chamber runs: the spot a camp, a den, a shrine or a mine's ore stands on. False for a
		/// site that is not a carved cave.
		/// </summary>
		public static bool TryChamber(ScenePointsOfInterest points, int siteId, out Vector3 floor, out float radius, out float yaw)
			=> TryChamber(points, siteId, out floor, out radius, out yaw, out _);

		/// <summary>As <see cref="TryChamber(ScenePointsOfInterest, int, out Vector3, out float, out float)"/>, with how its floor rises along <paramref name="yaw"/> (rise over run).</summary>
		public static bool TryChamber(ScenePointsOfInterest points, int siteId, out Vector3 floor, out float radius, out float yaw, out float slope)
		{
			floor = Vector3.zero;
			radius = 0f;
			yaw = 0f;
			slope = 0f;
			CaveSystem system = SystemOf(points, siteId);
			if (system == null || !system.TryChamberOf(siteId, out CaveChamber chamber))
			{
				return false;
			}
			floor = system.Origin + new Vector3(chamber.Centre.x, chamber.Floor, chamber.Centre.z);
			radius = Mathf.Max(1f, Mathf.Min(chamber.Radii.x, chamber.Radii.z) * 0.8f - system.Amp);
			yaw = chamber.Yaw;
			slope = chamber.Slope;
			if (system.Form == CaveForm.Grotto)
			{
				// A grotto's front is its pool: what stands in it stands on the dry ledge at its back.
				float back = 0.55f * chamber.Radii.x, yr = chamber.Yaw * Mathf.Deg2Rad;
				floor += new Vector3(Mathf.Sin(yr) * back, chamber.Slope * back, Mathf.Cos(yr) * back);
				radius = Mathf.Max(1f, Mathf.Min(0.35f * chamber.Radii.x, 0.6f * chamber.Radii.z) - system.Amp);
			}
			return true;
		}

		/// <summary>The sites that own a system (their meshes stay; any other cave's are pruned).</summary>
		private static HashSet<int> LiveCaves(ScenePointsOfInterest points)
		{
			var live = new HashSet<int>();
			if (points?.Shapes != null)
			{
				foreach (PointOfInterestShape shape in points.Shapes)
				{
					if (shape != null && shape.Shaper == ShaperName && !IsLink(shape.Values, out _))
					{
						live.Add(shape.SiteId);
					}
				}
			}
			return live;
		}

		/// <summary>
		/// A cave's pieces as meshes: sub-assets of <paramref name="assetPath"/> named <c>Cave &lt;id&gt; P&lt;n&gt;</c>, an
		/// existing one overwritten in place (its references survive a repaint); this cave's meshes it no longer has, and the
		/// meshes of caves not in <paramref name="live"/>, removed. In memory only when the path is null (tests, renders).
		/// </summary>
		public static Mesh[] WriteMeshes(string assetPath, int siteId, IReadOnlyList<CaveMesher.Piece> pieces, ICollection<int> live)
		{
			var meshes = new Mesh[pieces.Count];
			var names = new HashSet<string>();
			for (int i = 0; i < pieces.Count; i++)
			{
				meshes[i] = pieces[i].Mesh.ToMesh($"Cave {siteId} P{i}");
				names.Add(meshes[i].name);
			}
			if (assetPath == null)
			{
				return meshes;
			}
			// The main object is a placeholder that never goes, so any cave's meshes can be added and removed under it.
			var container = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (container == null)
			{
				WorldEditorAssets.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));
				container = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(assetPath) };
				AssetDatabase.CreateAsset(container, assetPath);
			}
			var existing = new Dictionary<string, Mesh>();
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
			{
				if (asset is Mesh mesh && asset != container)
				{
					bool ours = CaveIdOf(mesh.name, out int id);
					if (ours && (id == siteId ? !names.Contains(mesh.name) : live != null && !live.Contains(id)))
					{
						AssetDatabase.RemoveObjectFromAsset(mesh);
						Object.DestroyImmediate(mesh, true);
						continue;
					}
					existing[mesh.name] = mesh;
				}
			}
			for (int i = 0; i < meshes.Length; i++)
			{
				if (existing.TryGetValue(meshes[i].name, out Mesh old))
				{
					EditorUtility.CopySerialized(meshes[i], old);
					Object.DestroyImmediate(meshes[i]);
					meshes[i] = old;
				}
				else
				{
					AssetDatabase.AddObjectToAsset(meshes[i], assetPath);
				}
			}
			EditorUtility.SetDirty(container);
			AssetDatabase.SaveAssetIfDirty(container);
			return meshes;
		}

		private static bool CaveIdOf(string name, out int id)
		{
			id = 0;
			const string prefix = "Cave ";
			if (name == null || !name.StartsWith(prefix, System.StringComparison.Ordinal))
			{
				return false;
			}
			int end = name.IndexOf(' ', prefix.Length);
			return end > prefix.Length && int.TryParse(name.Substring(prefix.Length, end - prefix.Length), out id);
		}

		/// <summary>
		/// The cave's objects under a site: a collider per piece on the Ground layer under the cave's root (the server keeps
		/// them), the renderers under a client-only "Visual" child drawn by <see cref="CaveVisibility"/>, and an empty marker
		/// on each chamber's floor.
		/// </summary>
		public static GameObject Build(Transform parent, CaveSystem system, IReadOnlyList<CaveMesher.Piece> pieces, Mesh[] meshes, Material material)
		{
			int layer = CliffPlacer.ColliderLayer;
			var cave = new GameObject("Cave Rock") { layer = layer };
			cave.transform.SetParent(parent, false);
			cave.transform.SetPositionAndRotation(system.Origin, Quaternion.identity);
			GameObjectUtility.SetStaticEditorFlags(cave, CliffPlacer.Flags);

			var visual = new GameObject(CliffPlacer.VisualName) { layer = layer };
			visual.transform.SetParent(cave.transform, false);
			GameObjectUtility.SetStaticEditorFlags(visual, CliffPlacer.Flags);
			visual.AddComponent<ClientOnlyObject>();
			var renderers = new Renderer[meshes.Length];
			var mouths = new bool[meshes.Length];
			for (int i = 0; i < meshes.Length; i++)
			{
				var solid = new GameObject("Piece " + i) { layer = layer };
				solid.transform.SetParent(cave.transform, false);
				GameObjectUtility.SetStaticEditorFlags(solid, CliffPlacer.Flags);
				var collider = solid.AddComponent<MeshCollider>();
				collider.convex = false;
				collider.sharedMesh = meshes[i];

				var child = new GameObject("Piece " + i) { layer = layer };
				child.transform.SetParent(visual.transform, false);
				GameObjectUtility.SetStaticEditorFlags(child, CliffPlacer.Flags);
				child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
				var renderer = child.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				// Only the mouths stand in the sun; deep in, the terrain over them is what shades them.
				renderer.shadowCastingMode = pieces[i].Mouth ? ShadowCastingMode.On : ShadowCastingMode.Off;
				renderer.receiveShadows = true;
				renderers[i] = renderer;
				mouths[i] = pieces[i].Mouth;
			}
			visual.AddComponent<CaveVisibility>().Configure(renderers, mouths, 72f);

			foreach (CaveChamber chamber in system.Chambers)
			{
				var marker = new GameObject($"Chamber ({chamber.Kind})");
				marker.transform.SetParent(parent, false);
				marker.transform.SetPositionAndRotation(system.Origin + new Vector3(chamber.Centre.x, chamber.Floor, chamber.Centre.z), Quaternion.Euler(0f, chamber.Yaw, 0f));
			}
			return cave;
		}
	}

	/// <summary>Registers the terrain shapers (caves and mines, overhangs and arches, moats) with the point-of-interest stage.</summary>
	[InitializeOnLoad]
	public static class PointOfInterestTerrainShapers
	{
		static PointOfInterestTerrainShapers() => Register();

		/// <summary>Registers them all (again harmlessly): for code that runs before the editor's load hook, such as a test.</summary>
		public static void Register()
		{
			PointOfInterestShapers.Register(CaveShaper.Instance);
			PointOfInterestShapers.Register(OverhangShaper.Instance);
			PointOfInterestShapers.Register(MoatShaper.Instance);
		}
	}
}
#endif
