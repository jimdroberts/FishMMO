#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cut's stages for a cloud world (<see cref="Celestial.CloudWorld"/>), in the order the cut runs them: the islands'
	/// holes in the tiles (<see cref="Hole"/>), what the sites must keep clear of (<see cref="Reserve"/>), the rock
	/// undersides (<see cref="Undersides"/>), the rope bridges (<see cref="Bridges"/>) and the record the server and
	/// clients read (<see cref="Record"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What a cloud world's cut leaves out:</b> the planet's ground (a giant has none), erosion and its rivers, lakes and
	/// sea, the canyon walls, and the backdrop ground to the horizon. Past the islands there is only sky and the deck.
	/// </para>
	/// </remarks>
	public static class CloudWorldCut
	{
		/// <summary>The prop source of the bridges, so a re-cut replaces them.</summary>
		public const string BridgeSource = "Cloud Bridges";

		/// <summary>The object the undersides hang under.</summary>
		public const string UndersideRootName = "Island Undersides";

		/// <summary>How far below the deck's top a character is lost, metres: far enough to be well inside the cloud.</summary>
		public const float DeathBelowDeck = 60f;

		/// <summary>How deep the deck is drawn, metres.</summary>
		public const float DeckDepth = 400f;

		/// <summary>How long a landing stage reaches out past its island's edge, metres.</summary>
		public const float DockReach = 12f;

		/// <summary>
		/// Re-cuts one atlas scene headless, as the World Atlas page's Re-cut does, with the procedural art brought up to date
		/// first (the airships and landing stages are pieces of it): <c>-executeMethod
		/// FishMMO.Shared.WorldDesign.CloudWorldCut.RecutFromCommandLine</c>, FISHMMO_RECUT_SCENE naming the scene.
		/// </summary>
		public static void RecutFromCommandLine()
		{
			string name = Environment.GetEnvironmentVariable("FISHMMO_RECUT_SCENE");
			var log = new List<string>();
			BiomeArtGenerator.EnsureCurrentForBuild(log);
			foreach (string line in log)
			{
				Debug.Log($"[Cloud world re-cut] art: {line}");
			}
			foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (entry != null && entry.SceneName == name)
				{
					SceneGenerationResult result = SceneGenerator.Recut(entry);
					Debug.Log($"[Cloud world re-cut] '{name}' {(result != null && result.Success ? "succeeded" : "FAILED")}: " +
						(result != null ? string.Join(" | ", result.Notes) + (result.Problem != null ? " | " + result.Problem : string.Empty) : "no result"));
					return;
				}
			}
			Debug.LogError($"[Cloud world re-cut] no atlas scene '{name}'.");
		}

		/// <summary>True when a scene of this request is a cloud world: on a gas giant, and not an underground layer.</summary>
		public static bool Applies(SceneGenerationRequest request)
			=> request != null && CloudWorld.Is(request.Body) && (request.Layer == null || !request.Layer.Underground);

		/// <summary>
		/// Kinds a cloud world never builds: the caves and mines, which dig under the ground (and would come out through an
		/// island's underside), and the overhangs and arches, which carve a cliff the island's edge is not.
		/// </summary>
		public static bool Forbids(POIType kind)
			=> CaveShaper.TryForm(kind, out _) || kind == POIType.Overhang || kind == POIType.NaturalArch;

		/// <summary>The scene's islands, laid out for its size and seed.</summary>
		public static CloudIslands.Plan Lay(SceneGenerationRequest request, TerrainTilePlan tiles)
			=> CloudIslands.Lay(tiles.WidthMetres, tiles.DepthMetres, SceneGenerator.SceneSeed(request));

		/// <summary>
		/// Cuts every cell outside the islands out of the tiles: their surface and their collider go, so only the islands
		/// stand. Before the paint, so the biomes, the scatter and the cliffs find the holes and keep to the islands.
		/// </summary>
		public static int Hole(Terrain[,] terrains, TerrainTilePlan plan, bool[] solid, int cellsX, int cellsZ)
		{
			int perTile = plan.Resolution - 1, holed = 0;
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					TerrainData data = terrains[tx, tz] != null ? terrains[tx, tz].terrainData : null;
					if (data == null)
					{
						continue;
					}
					int holes = data.holesResolution;
					// The hole map is the heightmap's cells, [z, x], true where the surface is.
					var surface = new bool[holes, holes];
					for (int z = 0; z < holes && z < perTile; z++)
					{
						for (int x = 0; x < holes && x < perTile; x++)
						{
							int cx = tx * perTile + x, cz = tz * perTile + z;
							bool island = cx < cellsX && cz < cellsZ && solid[cz * cellsX + cx];
							surface[z, x] = island;
							if (!island)
							{
								holed++;
							}
						}
					}
					data.SetHoles(0, 0, surface);
					EditorUtility.SetDirty(data);
				}
			}
			return holed;
		}

		/// <summary>
		/// Keeps the sites, their ways and the scatter off the bridges' ends and the landing stages' feet, which are laid
		/// after them.
		/// </summary>
		public static void Reserve(CloudIslands.Plan islands, List<PointOfInterestKeepOut> reserved)
		{
			foreach (CloudIslands.Bridge bridge in islands.Bridges)
			{
				reserved.Add(new PointOfInterestKeepOut { Id = -1, X = bridge.FromAt.x, Z = bridge.FromAt.y, Radius = 6f });
				reserved.Add(new PointOfInterestKeepOut { Id = -1, X = bridge.ToAt.x, Z = bridge.ToAt.y, Radius = 6f });
			}
			foreach (CloudIslands.Dock dock in islands.Docks)
			{
				Vector2 foot = dock.Edge - dock.Outward * 4f;
				reserved.Add(new PointOfInterestKeepOut { Id = -1, X = foot.x, Z = foot.y, Radius = 9f });
			}
		}

		// ── Undersides ────────────────────────────────────────────

		/// <summary>
		/// The rock each island stands on, hung down into the void: a ledge tucked under the turf's edge, a lip just outside
		/// it (which hides the stepped edge a hole leaves along the cells), and below that a rough, tapering mass, deep under
		/// a small island and proportionally shallower under a big one, ending in a point. Collides (the server keeps it);
		/// drawn in the cliff rock of the planet's stone.
		/// </summary>
		public static void Undersides(Scene scene, SceneGenerationRequest request, CloudIslands.Plan islands, TerrainTilePlan tiles, string assetPath,
			List<string> notes, List<string> wrote)
		{
			foreach (GameObject existing in scene.GetRootGameObjects())
			{
				if (existing.name == UndersideRootName)
				{
					UnityEngine.Object.DestroyImmediate(existing);
				}
			}
			Material material = RockMaterial(request, out string rock);
			int layer = CliffPlacer.ColliderLayer;
			var root = new GameObject(UndersideRootName) { layer = layer };
			SceneManager.MoveGameObjectToScene(root, scene);
			GameObjectUtility.SetStaticEditorFlags(root, CliffPlacer.Flags);
			// The lip reaches past the cells' stepped edge: at least a cell and a half.
			float lip = Mathf.Max(1.2f, tiles.MetresPerSample * 1.5f), tuck = Mathf.Max(2.5f, tiles.MetresPerSample * 2.5f);
			int triangles = 0;
			for (int i = 0; i < islands.Islands.Count; i++)
			{
				CloudIslands.Island island = islands.Islands[i];
				Mesh mesh = Underside(island, lip, tuck);
				mesh.name = $"{scene.name} Underside {i}";
				triangles += mesh.triangles.Length / 3;
				if (i == 0)
				{
					AssetDatabase.CreateAsset(mesh, assetPath);
				}
				else
				{
					AssetDatabase.AddObjectToAsset(mesh, assetPath);
				}

				var host = new GameObject($"Underside {i}") { layer = layer };
				host.transform.SetParent(root.transform, false);
				host.transform.localPosition = new Vector3(island.Centre.x, 0f, island.Centre.y);
				GameObjectUtility.SetStaticEditorFlags(host, CliffPlacer.Flags);
				host.AddComponent<MeshCollider>().sharedMesh = mesh;

				var visual = new GameObject(CliffPlacer.VisualName) { layer = layer };
				visual.transform.SetParent(host.transform, false);
				GameObjectUtility.SetStaticEditorFlags(visual, CliffPlacer.Flags);
				visual.AddComponent<ClientOnlyObject>();
				visual.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = visual.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = ShadowCastingMode.On;
			}
			if (islands.Islands.Count > 0)
			{
				wrote?.Add(assetPath);
			}
			notes?.Add($"Cloud world: {islands.Islands.Count} island underside(s) in {rock} rock, {triangles:N0} triangles" +
				(material == null ? "; no cliff material for it yet (run Generate Biome Art, then re-cut)" : string.Empty) + ".");
		}

		/// <summary>One island's underside, in the island's frame (its centre at x = z = 0, heights as y).</summary>
		public static Mesh Underside(CloudIslands.Island island, float lip, float tuck)
		{
			float circumference = Mathf.PI * 2f * island.Radius;
			int columns = Mathf.Clamp(Mathf.RoundToInt(circumference / 2f), 24, 360);
			int bands = Mathf.Clamp(Mathf.RoundToInt(island.Keel / 5f), 8, 48);
			// Ring 0 the ledge under the turf, ring 1 the lip, rings 2 … bands + 1 the mass, then the point.
			int rings = bands + 2;
			var vertices = new List<Vector3>(rings * (columns + 1) + 1);
			var uvs = new List<Vector2>(vertices.Capacity);
			var rim = new float[columns + 1];
			float meanRim = 0f;
			for (int j = 0; j <= columns; j++)
			{
				float angle = j % columns / (float)columns * Mathf.PI * 2f;
				float r = island.OutlineAt(angle);
				rim[j] = island.GroundAt(island.Centre.x + Mathf.Cos(angle) * r, island.Centre.y + Mathf.Sin(angle) * r);
				if (j < columns)
				{
					meanRim += rim[j];
				}
			}
			meanRim /= columns;
			float bottom = meanRim - island.Keel;
			uint seed = island.Seed ^ 0xB0770Bu;
			// Where the point hangs: not quite under the middle.
			var tipOffset = new Vector2(CloudIslands.ValueNoise(1.7f, 3.1f, seed) - 0.5f, CloudIslands.ValueNoise(5.3f, 0.9f, seed) - 0.5f) * island.Radius * 0.35f;

			for (int ring = 0; ring < rings; ring++)
			{
				for (int j = 0; j <= columns; j++)
				{
					float angle = j % columns / (float)columns * Mathf.PI * 2f;
					var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
					float r = island.OutlineAt(angle);
					Vector3 p;
					if (ring == 0)
					{
						p = new Vector3(dir.x * Mathf.Max(0.5f, r - tuck), rim[j] - 1.2f, dir.y * Mathf.Max(0.5f, r - tuck));
					}
					else if (ring == 1)
					{
						p = new Vector3(dir.x * (r + lip), rim[j] - 0.35f, dir.y * (r + lip));
					}
					else
					{
						float u = (ring - 1) / (float)(bands + 1);
						// Bellied just under the lip, then tapering like an upturned peak.
						float profile = Mathf.Pow(1f - u, 0.85f) + 0.06f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u / 0.18f));
						float y = Mathf.Lerp(rim[j] - 0.35f, bottom, Mathf.Pow(u, 0.9f));
						var centre = Vector2.Lerp(Vector2.zero, tipOffset, u);
						float reach = (r + lip) * profile;
						// Rough rock: ragged in and out, harder the further down, banded like strata.
						float rough = Mathf.SmoothStep(0f, 1f, u / 0.12f);
						float wx = dir.x * reach, wz = dir.y * reach;
						float n = CloudIslands.ValueNoise(wx / 9f, y / 7f, wz / 9f, seed) - 0.5f;
						float fine = CloudIslands.ValueNoise(wx / 3f, y / 2.5f, wz / 3f, seed ^ 0x5u) - 0.5f;
						float strata = Mathf.Sin(y * 0.45f + CloudIslands.ValueNoise(angle * 3f, 0f, seed) * 6f) * 0.5f;
						float push = rough * (n * 0.22f * reach + fine * Mathf.Min(1.2f, reach * 0.08f) + strata * Mathf.Min(0.8f, reach * 0.04f));
						reach = Mathf.Max(0.3f, reach + push);
						p = new Vector3(centre.x + dir.x * reach, y, centre.y + dir.y * reach);
					}
					vertices.Add(p);
					uvs.Add(new Vector2(j / (float)columns * circumference / 6f, p.y / 6f));
				}
			}
			int tip = vertices.Count;
			vertices.Add(new Vector3(tipOffset.x, bottom - island.Keel * 0.04f, tipOffset.y));
			uvs.Add(new Vector2(0f, (bottom - island.Keel * 0.04f) / 6f));

			// Faces out: going round (+x toward +z) and down, (top j, top j+1, bottom j) faces away from the axis.
			var indices = new List<int>(rings * columns * 6 + columns * 3);
			int stride = columns + 1;
			for (int ring = 0; ring < rings - 1; ring++)
			{
				for (int j = 0; j < columns; j++)
				{
					int a = ring * stride + j, b = a + 1, c = a + stride, d = c + 1;
					indices.Add(a); indices.Add(b); indices.Add(c);
					indices.Add(b); indices.Add(d); indices.Add(c);
				}
			}
			int last = (rings - 1) * stride;
			for (int j = 0; j < columns; j++)
			{
				indices.Add(last + j); indices.Add(last + j + 1); indices.Add(tip);
			}

			var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>The cliff rock of the stone under the scene, as the canyon walls take it; null when the art is not generated.</summary>
		private static Material RockMaterial(SceneGenerationRequest request, out string rock)
		{
			rock = "Sandstone";
			try
			{
				PlanetGeology geology = PlanetGeology.For(request, SolarSystemProfile.Resolve(request.Body));
				Vector3 direction = AtlasGeometry.SceneToUnit(request.Footprint, 0.0, 0.0, request.ResolvedRadiusKm).ToVector3();
				string type = geology.ColumnAt(direction).Lithology.RockTypeName;
				if (!string.IsNullOrEmpty(type))
				{
					rock = type;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[Scene generator] The islands' rock could not be read from the planet's geology, so they are sandstone: {ex.Message}");
			}
			string name = CliffRocks.MaterialName(rock);
			string path = name != null ? ProceduralArtCatalogue.MaterialPath(name) : null;
			return path != null ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
		}

		// ── Bridges ───────────────────────────────────────────────

		/// <summary>
		/// The rope bridges between the islands: posts at each end on the ground, the walk hung between on a sag, as the
		/// caves' rope walks are, laid as baked props (with their collision) so a player crosses on them.
		/// </summary>
		public static int Bridges(Scene scene, CloudIslands.Plan islands, Func<float, float, float> ground, uint seed, List<string> notes)
		{
			var prototypes = new List<ScenePropSet.Prototype>();
			var props = new List<ScenePropSet.Prop>();
			var prototypeOf = new Dictionary<GameObject, int>();
			int missing = 0;
			void Lay(string piece, int variant, Vector3 at, Quaternion rotation, Vector3 scale)
			{
				GameObject prefab = StructureKit.Prefab(piece, variant);
				if (prefab == null)
				{
					missing++;
					return;
				}
				if (!prototypeOf.TryGetValue(prefab, out int prototype))
				{
					prototype = prototypes.Count;
					prototypeOf[prefab] = prototype;
					prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1 });
				}
				props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = at, Rotation = rotation, Scale = scale });
			}

			var random = new DeterministicRNG(unchecked((int)(seed * 0x2C1B3C6Du ^ 0xB81D6Eu)));
			int laid = 0;
			float metres = 0f;
			foreach (CloudIslands.Bridge bridge in islands.Bridges)
			{
				var a = new Vector3(bridge.FromAt.x, ground(bridge.FromAt.x, bridge.FromAt.y), bridge.FromAt.y);
				var b = new Vector3(bridge.ToAt.x, ground(bridge.ToAt.x, bridge.ToAt.y), bridge.ToAt.y);
				if (float.IsNaN(a.y) || float.IsNaN(b.y) || float.IsInfinity(a.y) || float.IsInfinity(b.y))
				{
					continue;
				}
				Vector3 span = b - a;
				var flat = new Vector3(span.x, 0f, span.z);
				if (flat.magnitude < 2f)
				{
					continue;
				}
				flat.Normalize();
				float length = span.magnitude;
				int n = Mathf.Max(1, Mathf.CeilToInt(length / CaveArt.SpanModule));
				// Hung slack over the drop: a long walk sags a good deal more than a cave's short one.
				float sag = Mathf.Min(1.6f, 0.025f * length);
				Vector3 At(int i)
				{
					float f = i / (float)n;
					return Vector3.Lerp(a, b, f) - Vector3.up * (sag * 4f * f * (1f - f));
				}
				int variant = random.Range(0, 2);
				Lay(CaveArt.RopeBridgeEnd, variant, a, Quaternion.LookRotation(flat, Vector3.up), Vector3.one);
				Lay(CaveArt.RopeBridgeEnd, variant, b, Quaternion.LookRotation(-flat, Vector3.up), Vector3.one);
				for (int i = 0; i < n; i++)
				{
					Vector3 p0 = At(i), p1 = At(i + 1);
					Vector3 d = p1 - p0;
					int worn = random.NextFloat() < 0.15f ? 1 : 0;
					Lay(CaveArt.RopeBridgeSpan, worn, 0.5f * (p0 + p1), Quaternion.LookRotation(d, Vector3.up), new Vector3(1f, 1f, d.magnitude / CaveArt.SpanModule));
				}
				laid++;
				metres += length;
			}
			if (props.Count > 0)
			{
				int collidable = ScenePropBaker.Write(scene, BridgeSource, prototypes, props);
				notes?.Add($"Cloud world: {laid} rope bridge(s), {metres:0} m of walk, {collidable:N0} collidable piece(s).");
			}
			else
			{
				ScenePropBaker.Clear(scene, BridgeSource);
			}
			if (missing > 0)
			{
				notes?.Add($"Cloud world: {missing} bridge piece(s) skipped, their art is missing (run Generate biome art, then re-cut).");
			}
			return laid;
		}

		// ── Sky traffic ───────────────────────────────────────────

		/// <summary>The landing stages and the airships plying between them (<see cref="PointOfInterestSkyTraffic"/>), under the record's object.</summary>
		public static void SkyTraffic(Scene scene, SceneCloudWorld world, SceneGenerationRequest request, TerrainTilePlan tiles, Func<float, float, float> ground,
			List<string> notes)
		{
			if (world == null)
			{
				return;
			}
			try
			{
				PointOfInterestSkyTraffic.Build(scene, world.transform, world, request, tiles.WidthMetres, tiles.DepthMetres, ground, notes);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				notes?.Add($"Sky traffic threw: {ex.Message}");
			}
		}

		// ── The record ────────────────────────────────────────────

		/// <summary>
		/// The scene's <see cref="SceneCloudWorld"/>: the deck, the depth that kills, the islands as they were cut and the
		/// landing stages' ends, on an object the server and every client keep. Replaces one a re-cut left.
		/// </summary>
		public static SceneCloudWorld Record(Scene scene, CloudIslands.Plan islands, Func<float, float, float> ground, List<string> notes)
		{
			foreach (GameObject existing in scene.GetRootGameObjects())
			{
				if (existing.name == SceneCloudWorld.ObjectName)
				{
					UnityEngine.Object.DestroyImmediate(existing);
				}
			}
			var host = new GameObject(SceneCloudWorld.ObjectName);
			SceneManager.MoveGameObjectToScene(host, scene);
			SceneCloudWorld world = host.AddComponent<SceneCloudWorld>();
			world.DeckTop = CloudIslands.DeckTop;
			world.DeckDepth = DeckDepth;
			world.DeathAltitude = CloudIslands.DeckTop - DeathBelowDeck;

			SceneCloudWorld.Island[] records = IslandRecords(islands, out float lowest, out float highest);
			world.Islands = records;
			world.IslandFloor = records.Length > 0 ? lowest : islands.Floor;
			world.IslandCeiling = records.Length > 0 ? highest : islands.Ceiling;
			SceneCloudWorld.Dock[] docks = DockRecords(islands, ground);
			world.Docks = docks;
			EditorUtility.SetDirty(world);
			notes?.Add($"Cloud world: {records.Length} island(s) between {world.IslandFloor:0} and {world.IslandCeiling:0} m over the deck, " +
				$"{islands.Bridges.Count} bridge(s) planned, {world.Docks.Length} landing stage(s); lost below {world.DeathAltitude:0} m.");
			return world;
		}

		/// <summary>The islands as the record keeps them, and the lowest and highest of their ground.</summary>
		public static SceneCloudWorld.Island[] IslandRecords(CloudIslands.Plan islands, out float lowest, out float highest)
		{
			var records = new SceneCloudWorld.Island[islands.Islands.Count];
			lowest = float.PositiveInfinity;
			highest = float.NegativeInfinity;
			for (int i = 0; i < records.Length; i++)
			{
				CloudIslands.Island island = islands.Islands[i];
				float centre = island.GroundAt(island.Centre.x, island.Centre.y);
				records[i] = new SceneCloudWorld.Island
				{
					Kind = island.Kind,
					Centre = new Vector3(island.Centre.x, centre, island.Centre.y),
					Radius = island.Reach,
					// The point hangs a little below the mean rim by the keel's depth (Underside).
					Keel = island.Height - island.Keel * 1.05f,
				};
				lowest = Mathf.Min(lowest, island.Height);
				highest = Mathf.Max(highest, centre + island.HillHeight);
			}
			return records;
		}

		/// <summary>The landing stages' ends as the record keeps them, each level with the ground it leaves from.</summary>
		public static SceneCloudWorld.Dock[] DockRecords(CloudIslands.Plan islands, Func<float, float, float> ground)
		{
			var docks = new List<SceneCloudWorld.Dock>();
			foreach (CloudIslands.Dock dock in islands.Docks)
			{
				// The stage's deck level with the ground it leaves from, a step in from the edge.
				Vector2 foot = dock.Edge - dock.Outward * 2f;
				float deck = ground(foot.x, foot.y);
				if (float.IsNaN(deck) || float.IsInfinity(deck))
				{
					continue;
				}
				Vector2 end = dock.Edge + dock.Outward * DockReach;
				docks.Add(new SceneCloudWorld.Dock
				{
					Island = dock.Island,
					Position = new Vector3(end.x, deck + 0.1f, end.y),
					Outward = new Vector3(dock.Outward.x, 0f, dock.Outward.y),
				});
			}
			return docks.ToArray();
		}
	}
}
#endif
