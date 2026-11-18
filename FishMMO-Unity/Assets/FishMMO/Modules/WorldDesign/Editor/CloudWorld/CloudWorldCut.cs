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
					if (result != null && result.Success && !string.IsNullOrEmpty(result.ScenePath))
					{
						// What the editor does on saving the scene and a batch run never does: the terrain arrays rebaked for the
						// layers the cut painted (TerrainArrayAutoBake), or the scene draws the old arrays' layers.
						UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(result.ScenePath,
							UnityEditor.SceneManagement.OpenSceneMode.Single);
						int baked = 0;
						foreach (TerrainArrayBinder binder in TerrainArrayBaker.BindersIn(scene))
						{
							TerrainArraySetup.SyncProvenance(binder);
							if (TerrainArrayBaker.IsStale(binder, out _) && TerrainArrayBaker.Bake(binder).Baked)
							{
								baked++;
							}
						}
						UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
						Debug.Log($"[Cloud world re-cut] '{name}': {baked} terrain array set(s) rebaked.");
					}
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
			=> CloudIslands.Lay(tiles.WidthMetres, tiles.DepthMetres, SceneGenerator.SceneSeed(request), tiles.MetresPerSample);

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
				// Wide enough for the end posts and landing, which stand inland of the rim (BridgeWay).
				reserved.Add(new PointOfInterestKeepOut { Id = -1, X = bridge.FromAt.x, Z = bridge.FromAt.y, Radius = 8f });
				reserved.Add(new PointOfInterestKeepOut { Id = -1, X = bridge.ToAt.x, Z = bridge.ToAt.y, Radius = 8f });
			}
			foreach (CloudIslands.Dock dock in islands.Docks)
			{
				Vector2 foot = dock.Edge - dock.Outward * 4f;
				reserved.Add(new PointOfInterestKeepOut { Id = -1, X = foot.x, Z = foot.y, Radius = 9f });
			}
		}

		// ── Undersides ────────────────────────────────────────────

		/// <summary>
		/// The rock each island stands on, hung down into the void: a rim of stone over the turf's rolled-down edge, its
		/// ledge running in under the grass, a cap under the whole turf, and below the rim a rough, tapering mass, deep under
		/// a small island and proportionally shallower under a big one, ending in a point. The mass and rim collide (the
		/// server keeps them); drawn in the cliff rock of the planet's stone. See <see cref="Rim"/> for how the turf meets it.
		/// </summary>
		public static RimProfile[] Undersides(Scene scene, SceneGenerationRequest request, CloudIslands.Plan islands, TerrainTilePlan tiles, string assetPath,
			Func<float, float, float> ground, Func<float, float, bool> hole, Func<float, float, float> turf, List<string> notes, List<string> wrote)
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
			// The rim over the turf's rolled edge (RimRing), kept for the bridges, which land over it.
			float cell = tiles.MetresPerSample;
			var rims = new RimProfile[islands.Islands.Count];
			int triangles = 0;
			for (int i = 0; i < islands.Islands.Count; i++)
			{
				CloudIslands.Island island = islands.Islands[i];
				rims[i] = Profile(island, ground, turf, hole, cell);
				Mesh mesh = Underside(island, rims[i]);
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

				// The rock's top under the whole turf: drawn only, as nothing ever stands under the turf (see Cap).
				Mesh cap = Cap(island, ground, hole, cell);
				cap.name = $"{scene.name} Underside Cap {i}";
				triangles += cap.triangles.Length / 3;
				AssetDatabase.AddObjectToAsset(cap, assetPath);
				var capHost = new GameObject("Cap") { layer = layer };
				capHost.transform.SetParent(visual.transform, false);
				GameObjectUtility.SetStaticEditorFlags(capHost, CliffPlacer.Flags);
				capHost.AddComponent<MeshFilter>().sharedMesh = cap;
				var capRenderer = capHost.AddComponent<MeshRenderer>();
				capRenderer.sharedMaterial = material;
				capRenderer.shadowCastingMode = ShadowCastingMode.Off;
			}
			if (islands.Islands.Count > 0)
			{
				wrote?.Add(assetPath);
			}
			notes?.Add($"Cloud world: {islands.Islands.Count} island underside(s) in {rock} rock, {triangles:N0} triangles" +
				(material == null ? "; no cliff material for it yet (run Generate Biome Art, then re-cut)" : string.Empty) + ".");
			return rims;
		}

		/// <summary>
		/// How far under the turf's own height at its edge (before it rolled down) the rock's rim stands, metres: over the
		/// rolled turf by most of the roll, under the grass where the grass meets it.
		/// </summary>
		public const float RimUnderTurf = 0.3f;

		/// <summary>
		/// How far past the last solid cell the rim reaches, in cells: past the half cell the terrain draws a hole's edge
		/// across (its filtered holes texture), and a little more for its compression.
		/// </summary>
		public const float RimPastEdge = 1.0f;

		/// <summary>How far in from the last solid cell the rim's ledge runs under the turf, in cells, and how far under it, metres.</summary>
		public const float LedgeIn = 2f, LedgeUnderTurf = 0.2f;

		/// <summary>Columns per heightmap cell round the edge, so the lip follows the turf's staircase between them.</summary>
		public const float ColumnsPerCell = 3f;

		/// <summary>
		/// One island's underside, in the island's frame (its centre at x = z = 0, heights as y). <paramref name="ground"/> is
		/// the finished terrain's height and <paramref name="hole"/> its holes (null for either: the island's planned ground
		/// and outline), <paramref name="cell"/> its sample spacing.
		/// </summary>
		public static Mesh Underside(CloudIslands.Island island, RimProfile rim)
		{
			float circumference = Mathf.PI * 2f * island.Reach;
			int columns = rim.Columns;
			int bands = Mathf.Clamp(Mathf.RoundToInt(island.Keel / 5f), 8, 48);
			// Ring 0 the ledge under the turf, ring 1 the rim, 2 its brow, 3 the calm face under it, 4 … the mass, then the point.
			int rings = bands + 4;
			var vertices = new List<Vector3>(rings * (columns + 1) + 1);
			var uvs = new List<Vector2>(vertices.Capacity);
			float[] edgeR = rim.RimR, edgeY = rim.RimY, ledgeR = rim.LedgeR, ledgeY = rim.LedgeY;
			float meanRim = 0f;
			for (int j = 0; j < columns; j++)
			{
				meanRim += edgeY[j];
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
					float r = edgeR[j], top = edgeY[j];
					Vector3 p;
					if (ring == 0)
					{
						// The ledge's inner edge, under the turf where it has all but finished rolling down.
						p = new Vector3(dir.x * ledgeR[j], ledgeY[j], dir.y * ledgeR[j]);
					}
					else if (ring == 1)
					{
						p = new Vector3(dir.x * r, top, dir.y * r);
					}
					else if (ring == 2)
					{
						// The brow: the rim rounding over, a hand's breadth out.
						p = new Vector3(dir.x * (r + BrowOut), top - BrowDrop, dir.y * (r + BrowOut));
					}
					else if (ring == 3)
					{
						// The face under it, still calm: the rim's line held a couple of metres down before the rock breaks up.
						p = new Vector3(dir.x * (r + FaceOut), top - FaceDrop, dir.y * (r + FaceOut));
					}
					else
					{
						float u = (ring - 3) / (float)(bands + 1);
						float faceY = top - FaceDrop, faceR = r + FaceOut;
						// Bellied just under the face, then tapering like an upturned peak.
						float profile = Mathf.Pow(1f - u, 0.85f) + 0.06f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u / 0.18f));
						float y = Mathf.Lerp(faceY, bottom, Mathf.Pow(u, 0.9f));
						var centre = Vector2.Lerp(Vector2.zero, tipOffset, u);
						float reach = faceR * profile;
						/* Rough rock: ragged in and out, banded like strata, coming in over the first dozen metres under the face.
						 * Its reach is metres, not a share of the island: a share put a city's rock ten metres in and out a
						 * few metres under its rim, and the wall met the rim in a row of great V's (2026-10-15 render). */
						float rough = Mathf.SmoothStep(0f, 1f, (faceY - y) / RoughIn);
						float wx = dir.x * reach, wz = dir.y * reach;
						float n = CloudIslands.ValueNoise(wx / 9f, y / 7f, wz / 9f, seed) - 0.5f;
						float fine = CloudIslands.ValueNoise(wx / 3f, y / 2.5f, wz / 3f, seed ^ 0x5u) - 0.5f;
						float strata = Mathf.Sin(y * 0.45f + CloudIslands.ValueNoise(wx / 20f, wz / 20f, seed) * 6f) * 0.5f;
						float push = rough * (n * Mathf.Min(0.22f * reach, 6f) + fine * Mathf.Min(0.6f, reach * 0.06f) + strata * Mathf.Min(0.6f, reach * 0.04f));
						reach = Mathf.Max(0.3f, reach + push);
						p = new Vector3(centre.x + dir.x * reach, y, centre.y + dir.y * reach);
					}
					vertices.Add(p);
					// Down the walls by height; across the ledge, which is nearly level, by its width, or the texture is one row
					// smeared across it.
					float v = ring == 0 ? (top + (r - ledgeR[j])) / 6f : p.y / 6f;
					uvs.Add(new Vector2(j / (float)columns * circumference / 6f, v));
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

		/// <summary>How far under the turf the cap lies, metres: clear of the terrain between its samples and of its level of detail.</summary>
		public const float CapDepth = 0.4f;

		/// <summary>The cap's sample spacing, metres, round and across.</summary>
		public const float CapSpacing = 2.5f;

		/// <summary>
		/// The rock's top under the whole of an island's turf, <see cref="CapDepth"/> under it, from just inside the ledge's
		/// inner edge to the middle, in the island's frame, facing up.
		/// </summary>
		/// <remarks>
		/// <b>Why it is there.</b> The underside alone is a shell, its walls and a ledge reaching a little way in under the turf,
		/// and hollow inside; the terrain is not drawn from below. So any sight line that slipped in between the rock's edge
		/// and the turf's staircase of cells (at any angle, from the island's own edge or another) ran on under the turf,
		/// through the hollow, and out the other side: the sky, in slivers all round every island (2026-10-14/15, three
		/// rounds of moving the edge, which only moved the slivers). Closed by this, such a line meets rock. Drawn only:
		/// nothing stands under the turf, so it has no collider, and it casts no shadow from under the ground.
		/// </remarks>
		public static Mesh Cap(CloudIslands.Island island, Func<float, float, float> ground, Func<float, float, bool> hole, float cell)
		{
			int columns = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * 2f * island.Reach / CapSpacing), 16, 400);
			var outer = new float[columns];
			float widest = 0f;
			for (int j = 0; j < columns; j++)
			{
				float angle = j / (float)columns * Mathf.PI * 2f;
				float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
				Func<float, bool> holeAt = hole == null ? null : (Func<float, bool>)(d => hole(island.Centre.x + cos * d, island.Centre.y + sin * d));
				// Out under the ledge, past its inner edge, so the two overlap.
				outer[j] = Mathf.Max(0.5f, LastSolid(island, angle, holeAt, cell) - (LedgeIn - 0.5f) * cell);
				widest = Mathf.Max(widest, outer[j]);
			}
			int rings = Mathf.Clamp(Mathf.CeilToInt(widest / CapSpacing), 1, 120);
			var vertices = new List<Vector3>(columns * rings + 1);
			var uvs = new List<Vector2>(vertices.Capacity);
			for (int k = 0; k < rings; k++)
			{
				float share = 1f - k / (float)rings;
				for (int j = 0; j < columns; j++)
				{
					float angle = j / (float)columns * Mathf.PI * 2f;
					float d = outer[j] * share;
					float x = Mathf.Cos(angle) * d, z = Mathf.Sin(angle) * d;
					vertices.Add(new Vector3(x, CapHeight(island, ground, x, z), z));
					uvs.Add(new Vector2(x / 6f, z / 6f));
				}
			}
			int centre = vertices.Count;
			vertices.Add(new Vector3(0f, CapHeight(island, ground, 0f, 0f), 0f));
			uvs.Add(Vector2.zero);

			// Facing up: (outer j, inner j, outer j+1) and (outer j+1, inner j, inner j+1), round toward +z.
			var indices = new List<int>(columns * rings * 6);
			for (int k = 0; k < rings - 1; k++)
			{
				for (int j = 0; j < columns; j++)
				{
					int n = (j + 1) % columns;
					int a = k * columns + j, b = (k + 1) * columns + j, c = k * columns + n, d = (k + 1) * columns + n;
					indices.Add(a); indices.Add(b); indices.Add(c);
					indices.Add(c); indices.Add(b); indices.Add(d);
				}
			}
			int last = (rings - 1) * columns;
			for (int j = 0; j < columns; j++)
			{
				indices.Add(last + j); indices.Add(centre); indices.Add(last + (j + 1) % columns);
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

		/// <summary>The cap's height at a point of the island's frame: the turf's, <see cref="CapDepth"/> under it.</summary>
		private static float CapHeight(CloudIslands.Island island, Func<float, float, float> ground, float x, float z)
		{
			float east = island.Centre.x + x, north = island.Centre.y + z;
			float h = ground != null ? ground(east, north) : island.GroundAt(east, north);
			if (float.IsNaN(h) || float.IsInfinity(h))
			{
				h = island.GroundAt(east, north);
			}
			return h - CapDepth;
		}

		/// <summary>
		/// The last solid point of the turf toward <paramref name="angle"/>, from the island's middle: marched in from past the
		/// outline through the terrain's holes (the staircase of cells they leave lies inside the smooth outline). The
		/// outline itself with no holes to read.
		/// </summary>
		private static float LastSolid(CloudIslands.Island island, float angle, Func<float, bool> holeAt, float cell)
		{
			float outline = island.OutlineAt(angle);
			if (holeAt == null)
			{
				return outline;
			}
			float step = Mathf.Max(0.02f, cell * 0.05f);
			float from = outline + 1.5f * cell, to = Mathf.Max(0.5f, outline - 4f * cell);
			for (float d = from; d >= to; d -= step)
			{
				if (!holeAt(d))
				{
					return d;
				}
			}
			return outline;
		}

		/// <summary>An island's rim all round (<see cref="RimRing"/>), column by column from +x toward +z, the last the first again.</summary>
		public sealed class RimProfile
		{
			public int Columns;
			public float[] RimR, RimY, LedgeR, LedgeY;

			/// <summary>The rim toward <paramref name="angle"/> (radians from +x toward +z), between the columns either side.</summary>
			public void Sample(float angle, out float rimR, out float rimY, out float ledgeR, out float ledgeY)
			{
				float t = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * Columns;
				int j = Mathf.Clamp((int)t, 0, Columns - 1);
				float f = t - j;
				rimR = Mathf.Lerp(RimR[j], RimR[j + 1], f);
				rimY = Mathf.Lerp(RimY[j], RimY[j + 1], f);
				ledgeR = Mathf.Lerp(LedgeR[j], LedgeR[j + 1], f);
				ledgeY = Mathf.Lerp(LedgeY[j], LedgeY[j + 1], f);
			}
		}

		/// <summary>An island's rim profile as the cut builds it: <see cref="ColumnsPerCell"/> columns a cell round its widest reach.</summary>
		public static RimProfile Profile(CloudIslands.Island island, Func<float, float, float> ground, Func<float, float, float> turf,
			Func<float, float, bool> hole, float cell)
		{
			float circumference = Mathf.PI * 2f * island.Reach;
			int columns = Mathf.Clamp(Mathf.RoundToInt(circumference * ColumnsPerCell / Mathf.Max(0.25f, cell)), 24, 3000);
			var profile = new RimProfile
			{
				Columns = columns,
				RimR = new float[columns + 1],
				RimY = new float[columns + 1],
				LedgeR = new float[columns + 1],
				LedgeY = new float[columns + 1],
			};
			RimRing(island, columns, ground, turf, hole, cell, profile.RimR, profile.RimY, profile.LedgeR, profile.LedgeY);
			return profile;
		}

		/// <summary>
		/// The rock's rim all round an island, <paramref name="columns"/> columns from +x toward +z (arrays of columns + 1, the
		/// last the first again), in the island's frame: its outer edge (where the wall drops away), past the turf's drawn
		/// edge, a little under the turf's own height there; and its ledge's inner edge, a couple of cells in under the turf.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Over the turf's edge, not meeting it.</b> The turf's drawn edge is not where its cells end: the terrain clips a
		/// hole where its filtered holes texture reads zero, up to half a cell past the last solid cell, cut diagonally at the
		/// staircase's corners and nudged by compression. So the turf rolls down at its edge (CloudIslands.RollUnder) and the
		/// rim lies over it: wherever within a cell the drawn edge falls, it falls under stone, and the grass meets the rock
		/// where it rises past the ledge, at the stone's level (2026-10-14/15).
		/// </para>
		/// <para>
		/// <b>Smooth, round the staircase's outside.</b> Column by column the turf's last solid point jumps a whole cell in and
		/// out along the staircase. A rim that followed it left the rolled turf of the next cell out standing past the stone
		/// in every notch (a red stripe of it, painted) and the wall dropping from every tooth in a spike (2026-10-15 render).
		/// So the rim's outer edge is the outermost last solid point within <see cref="RimWindowCells"/> cells either way,
		/// <see cref="RimPastEdge"/> cells past it, smoothed; the ledge's inner edge the innermost, <see cref="LedgeIn"/> in.
		/// The heights are the turf's own (<paramref name="turf"/>: as shaped, before it rolled down; null: <paramref name="ground"/>),
		/// read inside the outermost cell and smoothed the same way.
		/// </para>
		/// </remarks>
		public static void RimRing(CloudIslands.Island island, int columns, Func<float, float, float> ground, Func<float, float, float> turf,
			Func<float, float, bool> hole, float cell, float[] rimR, float[] rimY, float[] ledgeR, float[] ledgeY)
		{
			var last = new float[columns];
			var edgeTurf = new float[columns];
			Func<float, float, float> own = turf ?? ground;
			for (int j = 0; j < columns; j++)
			{
				float angle = j / (float)columns * Mathf.PI * 2f;
				float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
				Func<float, bool> holeAt = hole == null ? null : (Func<float, bool>)(d => hole(island.Centre.x + cos * d, island.Centre.y + sin * d));
				last[j] = LastSolid(island, angle, holeAt, cell);
				float edge = Mathf.Max(0.3f, last[j] - 0.25f * cell);
				edgeTurf[j] = Height(island, own, cos * edge, sin * edge);
			}
			float arc = Mathf.PI * 2f * Mathf.Max(1f, island.Reach) / columns;
			int window = Mathf.Max(1, Mathf.CeilToInt(RimWindowCells * cell / arc));
			var outer = new float[columns];
			var inner = new float[columns];
			for (int j = 0; j < columns; j++)
			{
				float most = float.NegativeInfinity, least = float.PositiveInfinity;
				for (int k = -window; k <= window; k++)
				{
					float l = last[((j + k) % columns + columns) % columns];
					most = Mathf.Max(most, l);
					least = Mathf.Min(least, l);
				}
				outer[j] = most;
				inner[j] = least;
			}
			for (int j = 0; j < columns; j++)
			{
				float sumOuter = 0f, sumInner = 0f, sumY = 0f;
				int n = 0;
				for (int k = -window; k <= window; k++)
				{
					int m = ((j + k) % columns + columns) % columns;
					sumOuter += outer[m];
					sumInner += inner[m];
					sumY += edgeTurf[m];
					n++;
				}
				float angle = j / (float)columns * Mathf.PI * 2f;
				float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
				// Smoothed, but never in past the outermost turf near it, nor out past the innermost.
				rimR[j] = Mathf.Max(sumOuter / n, outer[j]) + RimPastEdge * cell;
				rimY[j] = sumY / n - RimUnderTurf;
				ledgeR[j] = Mathf.Max(0.3f, Mathf.Min(sumInner / n, inner[j]) - LedgeIn * cell);
				ledgeY[j] = Height(island, ground, cos * ledgeR[j], sin * ledgeR[j]) - LedgeUnderTurf;
			}
			rimR[columns] = rimR[0];
			rimY[columns] = rimY[0];
			ledgeR[columns] = ledgeR[0];
			ledgeY[columns] = ledgeY[0];
		}

		/// <summary>The rim's brow and the calm face under it: how far out and down from the rim, metres.</summary>
		public const float BrowOut = 0.15f, BrowDrop = 0.45f, FaceOut = 0.1f, FaceDrop = 2f;

		/// <summary>How far under the face the rock comes in to its full roughness, metres.</summary>
		public const float RoughIn = 12f;

		/// <summary>The rim's window either way along the edge, in cells: wider than the staircase's steps.</summary>
		public const float RimWindowCells = 2f;

		/// <summary>A height at a point of the island's frame from <paramref name="ground"/>, or the island's planned ground.</summary>
		private static float Height(CloudIslands.Island island, Func<float, float, float> ground, float x, float z)
		{
			float east = island.Centre.x + x, north = island.Centre.y + z;
			float h = ground != null ? ground(east, north) : island.GroundAt(east, north);
			return float.IsNaN(h) || float.IsInfinity(h) ? island.GroundAt(east, north) : h;
		}

		/// <summary>The cliff rock of the stone under the scene, as the canyon walls take it; null when the art is not generated.</summary>
		private static Material RockMaterial(SceneGenerationRequest request, out string rock)
		{
			rock = RockType(request);
			string name = CliffRocks.MaterialName(rock);
			string path = name != null ? ProceduralArtCatalogue.MaterialPath(name) : null;
			return path != null ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
		}

		/// <summary>The rock type under the scene's middle, from the planet's geology; sandstone when it cannot be read.</summary>
		private static string RockType(SceneGenerationRequest request)
		{
			string rock = "Sandstone";
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
			return rock;
		}

		// ── Blending the turf into the rock ───────────────────────

		/// <summary>
		/// How far rolled down (metres) the turf is where it starts to give way to stone, and where it is all stone: only
		/// under the rock's rim, where the turf's drawn fringe may show. Painted further in, the ground-colour map (a couple
		/// of metres a texel) carried the stone's pale colour into the grass by the rim, which looked dead (2026-10-15 render).
		/// </summary>
		public const float BlendFrom = 0.35f, BlendTo = 0.8f;

		/// <summary>
		/// How far rolled down (metres) the turf may be and still carry grass, flowers and trees: everywhere the rim does not
		/// cover, so the grass grows right up to the stone and leans over it; under the rim it would stand up through it.
		/// </summary>
		public const float PlantsUpTo = 0.2f;

		/// <summary>
		/// The turf's rolled-down edge (CloudIslands.RollUnder), where the rock's rim lies over it, given over to the islands'
		/// stone: painted with the ground layer of the same stone the undersides are drawn in (added to a tile that lacks it),
		/// so the fringe the terrain draws past its last solid cell reads as rock, and cleared of grass, flowers and trees,
		/// which would stand up through the rim. After the paint and the sites, before the trees are baked into props.
		/// </summary>
		public static void Blend(Terrain[,] terrains, TerrainTilePlan plan, SceneHeightField field, float[] rolled, SceneGenerationRequest request,
			List<string> notes)
		{
			if (terrains == null || field == null || rolled == null || rolled.Length != field.Metres.Length)
			{
				return;
			}
			string rock = RockType(request);
			TerrainLayer stone = null;
			foreach (string family in new[] { rock, "CliffRock", "Rock" })
			{
				stone = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ProceduralArtCatalogue.GroundLayerPath(family));
				if (stone != null)
				{
					break;
				}
			}
			Func<float, float, float> RolledAt = CloudIslands.RollAt(field, rolled);

			int painted = 0, cleared = 0, felled = 0;
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;

				// ── The stone, painted ──
				if (stone != null && data.alphamapLayers > 0)
				{
					int res = data.alphamapResolution;
					float[,,] maps = data.GetAlphamaps(0, 0, res, res);
					int layers = maps.GetLength(2);
					TerrainLayer[] palette = data.terrainLayers;
					int index = Array.IndexOf(palette, stone);
					if (index < 0 && layers < SceneTerrainPalette.MaximumLayers)
					{
						// The stone joins the tile's palette; every texel keeps the weights it had.
						var grown = new TerrainLayer[palette.Length + 1];
						Array.Copy(palette, grown, palette.Length);
						grown[palette.Length] = stone;
						data.terrainLayers = grown;
						var wider = new float[res, res, layers + 1];
						for (int z = 0; z < res; z++)
						{
							for (int x = 0; x < res; x++)
							{
								for (int l = 0; l < layers; l++)
								{
									wider[z, x, l] = maps[z, x, l];
								}
							}
						}
						maps = wider;
						index = layers;
						layers++;
					}
					if (index >= 0)
					{
						for (int z = 0; z < res; z++)
						{
							float north = origin.z + (z + 0.5f) / res * size.z;
							for (int x = 0; x < res; x++)
							{
								float east = origin.x + (x + 0.5f) / res * size.x;
								float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BlendFrom, BlendTo, RolledAt(east, north)));
								if (w <= 0f)
								{
									continue;
								}
								for (int l = 0; l < layers; l++)
								{
									maps[z, x, l] = l == index ? Mathf.Lerp(maps[z, x, l], 1f, w) : maps[z, x, l] * (1f - w);
								}
								painted++;
							}
						}
						data.SetAlphamaps(0, 0, maps);
					}
				}

				// ── No grass, flowers or trees where the rim lies over the turf ──
				int detailRes = data.detailResolution;
				int[] detailLayers = data.GetSupportedLayers(0, 0, detailRes, detailRes);
				if (detailRes > 0 && detailLayers != null && detailLayers.Length > 0)
				{
					var under = new bool[detailRes, detailRes];
					bool any = false;
					for (int z = 0; z < detailRes; z++)
					{
						float north = origin.z + (z + 0.5f) / detailRes * size.z;
						for (int x = 0; x < detailRes; x++)
						{
							float east = origin.x + (x + 0.5f) / detailRes * size.x;
							if (RolledAt(east, north) > PlantsUpTo)
							{
								under[z, x] = true;
								any = true;
							}
						}
					}
					if (any)
					{
						foreach (int layer in detailLayers)
						{
							int[,] density = data.GetDetailLayer(0, 0, detailRes, detailRes, layer);
							for (int z = 0; z < detailRes; z++)
							{
								for (int x = 0; x < detailRes; x++)
								{
									if (under[z, x] && density[z, x] != 0)
									{
										density[z, x] = 0;
										cleared++;
									}
								}
							}
							data.SetDetailLayer(0, 0, layer, density);
						}
					}
				}
				TreeInstance[] trees = data.treeInstances;
				if (trees != null && trees.Length > 0)
				{
					var kept = new List<TreeInstance>(trees.Length);
					foreach (TreeInstance tree in trees)
					{
						float east = origin.x + tree.position.x * size.x, north = origin.z + tree.position.z * size.z;
						if (RolledAt(east, north) > PlantsUpTo)
						{
							felled++;
							continue;
						}
						kept.Add(tree);
					}
					if (kept.Count != trees.Length)
					{
						data.treeInstances = kept.ToArray();
					}
				}
				EditorUtility.SetDirty(data);
			}
			notes?.Add($"Cloud world: the turf's rolled edge given over to {(stone != null ? stone.name : "(no stone layer: run Generate biome art)")}: " +
				$"{painted:N0} texel(s) painted, {cleared:N0} detail cell(s) cleared, {felled:N0} tree(s) taken off the rim.");
		}

		// ── Bridges ───────────────────────────────────────────────

		/// <summary>How far inland of the rim's ledge a bridge's end posts stand, metres: their feet at the ledge's inner edge, the landing behind them on the grass.</summary>
		public const float LandingIn = 0.4f;

		/// <summary>How far past the rim's outer edge a bridge's walk runs before it starts to hang, metres: clear of the brow.</summary>
		public const float RunOut = 0.9f;

		/// <summary>How high a bridge's walk passes over the rim's stone, at least, metres: no plank in the rock.</summary>
		public const float DeckOverRim = 0.35f;

		/// <summary>
		/// A bridge's walk from island <paramref name="a"/> to island <paramref name="b"/>, as joints from one end post to the
		/// other: each end on the grass inland of its rim (<see cref="LandingIn"/>), flush with the ground so a walker steps
		/// straight on; a run out over the rim, level or rising gently to clear its stone (<see cref="DeckOverRim"/>); and the
		/// hanging span between the two run-outs, sagging as a long rope walk does. False when the islands are too close for one.
		/// </summary>
		/// <remarks>
		/// It used to hang from end posts two and a half metres in from the outline, sagging from the first plank: once the
		/// rim of stone lay over the turf's edge, the first planks ran into it and the walk came out under it (2026-10-15).
		/// </remarks>
		public static bool BridgeWay(CloudIslands.Island a, RimProfile rimA, CloudIslands.Island b, RimProfile rimB, Func<float, float, float> ground,
			out List<Vector3> joints, out int landA, out int landB, List<Vector3> postsA = null, List<Vector3> postsB = null)
		{
			var way = new List<Vector3>();
			joints = way;
			landA = landB = 0;
			Vector2 d = b.Centre - a.Centre;
			if (d.sqrMagnitude < 1f)
			{
				return false;
			}
			Vector2 dir = d.normalized;
			bool End(CloudIslands.Island island, RimProfile rim, Vector2 toward, List<Vector3> taken, out Vector3 post, out Vector3 runOut)
			{
				rim.Sample(Mathf.Atan2(toward.y, toward.x), out float rimR, out float rimY, out float ledgeR, out _);
				// Inland of the ledge, where the landing behind the posts lies best on the grass (LandingMisfit): a steep islet's
				// ground can fall away faster than a landing can be pitched to follow.
				var outward = new Vector3(toward.x, 0f, toward.y);
				post = default;
				float best = float.PositiveInfinity;
				for (int k = 0; k <= 8; k++)
				{
					float r = ledgeR - LandingIn - k * 0.5f;
					if (r < 0.5f)
					{
						break;
					}
					Vector2 at = island.Centre + toward * r;
					var candidate = new Vector3(at.x, ground(at.x, at.y), at.y);
					if (Crowded(candidate, taken))
					{
						continue;
					}
					// Further in only for a better fit: a little misfit is worth less than a longer walk out.
					float misfit = LandingMisfit(candidate, outward, ground) + k * 0.01f;
					if (misfit < best)
					{
						best = misfit;
						post = candidate;
					}
				}
				runOut = default;
				if (float.IsPositiveInfinity(best))
				{
					// Nowhere inland of the rim clear of the island's other bridges' posts: this bridge is left out.
					return false;
				}
				Vector2 off = island.Centre + toward * (rimR + RunOut);
				runOut = new Vector3(off.x, Mathf.Max(post.y, rimY + DeckOverRim), off.y);
				return true;
			}
			if (!End(a, rimA, dir, postsA, out Vector3 postA, out Vector3 outA) || !End(b, rimB, -dir, postsB, out Vector3 postB, out Vector3 outB))
			{
				return false;
			}
			var hang = new Vector3(outB.x - outA.x, 0f, outB.z - outA.z);
			if (hang.magnitude < 1f || Vector3.Dot(hang, new Vector3(dir.x, 0f, dir.y)) <= 0f)
			{
				return false;
			}
			void Straight(Vector3 from, Vector3 to)
			{
				int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / CaveArt.SpanModule));
				for (int i = 1; i <= n; i++)
				{
					Vector3 joint = Vector3.Lerp(from, to, i / (float)n);
					// Never under the grass: over a hump between the post and the rim, the walk rides up on it.
					if (i < n)
					{
						joint.y = Mathf.Max(joint.y, ground(joint.x, joint.z) + 0.03f);
					}
					way.Add(joint);
				}
			}
			joints.Add(postA);
			Straight(postA, outA);
			landA = joints.Count - 1;
			// Hung slack between the run-outs: a long walk sags a good deal more than a cave's short one.
			float length = Vector3.Distance(outA, outB);
			float sag = Mathf.Min(1.6f, 0.025f * length);
			int spans = Mathf.Max(1, Mathf.CeilToInt(length / CaveArt.SpanModule));
			for (int i = 1; i <= spans; i++)
			{
				float f = i / (float)spans;
				joints.Add(Vector3.Lerp(outA, outB, f) - Vector3.up * (sag * 4f * f * (1f - f)));
			}
			landB = joints.Count - 1;
			Straight(outB, postB);
			return true;
		}

		/// <summary>How far behind its posts a bridge end's landing reaches, metres (StructurePieces' RopeBridgeEnd).</summary>
		public const float LandingLength = 1.2f;

		/// <summary>
		/// How far a bridge end is pitched about its posts, degrees, so its landing lies on the grass behind them however the
		/// ground slopes there (2026-10-15: on a steep islet a level landing stood most of a metre off the grass, a ledge a
		/// walker could not step up); a little over the ground rather than in it, and no more than 25°.
		/// </summary>
		public static float LandingPitch(Vector3 post, Vector3 outward, Func<float, float, float> ground)
		{
			var back = new Vector3(-outward.x, 0f, -outward.z).normalized;
			Vector3 behind = post + back * LandingLength;
			float rise = ground(behind.x, behind.z) + 0.03f - post.y;
			return Mathf.Clamp(Mathf.Asin(Mathf.Clamp(rise / LandingLength, -1f, 1f)) * Mathf.Rad2Deg, -25f, 25f);
		}

		/// <summary>
		/// How far apart two bridges' end posts on one island stand, at least, metres: each end's posts (1.9 m across), its
		/// landing behind them and room for a walker between.
		/// </summary>
		public const float PostSpacing = 3.5f;

		private static bool Crowded(Vector3 post, List<Vector3> taken)
		{
			if (taken == null)
			{
				return false;
			}
			foreach (Vector3 other in taken)
			{
				float dx = other.x - post.x, dz = other.z - post.z;
				if (dx * dx + dz * dz < PostSpacing * PostSpacing)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>How far a pitched landing (<see cref="LandingPitch"/>) lies off the grass under it at worst, metres.</summary>
		public static float LandingMisfit(Vector3 post, Vector3 outward, Func<float, float, float> ground)
		{
			var back = new Vector3(-outward.x, 0f, -outward.z).normalized;
			float pitch = LandingPitch(post, outward, ground) * Mathf.Deg2Rad;
			float worst = 0f;
			for (float k = 0.3f; k <= LandingLength + 1e-3f; k += 0.3f)
			{
				Vector3 p = post + back * (k * Mathf.Cos(pitch));
				worst = Mathf.Max(worst, Mathf.Abs(ground(p.x, p.z) - (post.y + k * Mathf.Sin(pitch))));
			}
			return worst;
		}

		/// <summary>
		/// The rope bridges between the islands, laid on <see cref="BridgeWay"/>: posts and a landing at each end on the grass,
		/// the walk run out over the rim and hung between, as baked props (with their collision) so a player crosses on them.
		/// Returns each bridge's two landings, end post to end post, for the record.
		/// </summary>
		/// <remarks>
		/// <paramref name="structures"/>: the world body's own (2026-10-18, I6e; a cloud world stands in no biome): the palette the
		/// bridges are laid in, and the light beside each end (<see cref="CloudWorldWayPlace.BridgeEnd"/>). Null or empty, the kit's
		/// own materials and no lights, as before.
		/// </remarks>
		public static List<SceneCloudWorld.Crossing> Bridges(Scene scene, CloudIslands.Plan islands, RimProfile[] rims, Func<float, float, float> ground,
			uint seed, List<string> notes, CloudWorldStructures structures = null)
		{
			var prototypes = new List<ScenePropSet.Prototype>();
			var props = new List<ScenePropSet.Prop>();
			var prototypeOf = new Dictionary<GameObject, int>();
			var crossings = new List<SceneCloudWorld.Crossing>();
			int missing = 0;
			FishMMO.Shared.Biomes.BiomeStructurePalette palette = structures?.Laid;
			void LayPrefab(GameObject prefab, Vector3 at, Quaternion rotation, Vector3 scale)
			{
				if (prefab == null)
				{
					missing++;
					return;
				}
				if (!prototypeOf.TryGetValue(prefab, out int prototype))
				{
					prototype = prototypes.Count;
					prototypeOf[prefab] = prototype;
					prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1, Materials = StructurePaletteDressing.MaterialsFor(prefab, palette) });
				}
				props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = at, Rotation = rotation, Scale = scale });
			}
			void Lay(string piece, int variant, Vector3 at, Quaternion rotation, Vector3 scale) => LayPrefab(StructureKit.Prefab(piece, variant), at, rotation, scale);
			// The light beside each bridge end, where the body names one that stands on the ground.
			string lightQuery = structures != null && structures.TryLight(CloudWorldWayPlace.BridgeEnd, out CloudWorldWayLight endLight)
				&& FishMMO.Shared.Biomes.BiomeWayLights.MountOf(endLight.Light) == FishMMO.Shared.Biomes.BiomeWayLightMount.Ground ? endLight.PieceQuery : null;
			int lights = 0, unlit = 0;
			// What the lights of the bridge being laid give off (2026-10-25, I6i), kept on its crossing for the client's site lights.
			var endLights = new List<SceneEmitter>();
			void LightEnd(CloudIslands.Island island, Vector3 post, Vector3 outward, List<Vector3> taken)
			{
				if (lightQuery == null || island == null)
				{
					return;
				}
				if (BridgeEndLight(island, post, outward, ground, taken, out Vector3 at))
				{
					Quaternion facing = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.z), Vector3.up);
					GameObject prefab = PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest(lightQuery, "timber",
						unchecked((int)(seed ^ (uint)Mathf.RoundToInt(at.x * 10f) * 0x9E3779B1u ^ (uint)Mathf.RoundToInt(at.z * 10f)))));
					LayPrefab(prefab, at, facing, Vector3.one);
					// A crystal post's crystal in its cradle (2026-10-25, I6i-b), where its art exists.
					if (PostCrystal(prefab, at, facing, Vector3.one, seed, out GameObject crystal, out Vector3 crystalAt, out Vector3 crystalSize))
					{
						LayPrefab(crystal, crystalAt, facing, crystalSize);
					}
					lights += prefab != null ? 1 : 0;
					LightEmitters(prefab, at, facing, Vector3.one, seed, endLights);
				}
				else
				{
					unlit++;
				}
			}

			var random = new DeterministicRNG(unchecked((int)(seed * 0x2C1B3C6Du ^ 0xB81D6Eu)));
			int laid = 0, crowded = 0;
			float metres = 0f;
			// Every island's bridge posts so far, so no two bridges' ends crowd each other (PostSpacing).
			var posts = new Dictionary<int, List<Vector3>>();
			List<Vector3> PostsOn(int island)
			{
				if (!posts.TryGetValue(island, out List<Vector3> list))
				{
					list = new List<Vector3>();
					posts[island] = list;
				}
				return list;
			}
			foreach (CloudIslands.Bridge bridge in islands.Bridges)
			{
				if (rims == null || bridge.A >= rims.Length || bridge.B >= rims.Length || rims[bridge.A] == null || rims[bridge.B] == null)
				{
					continue;
				}
				if (!BridgeWay(islands.Islands[bridge.A], rims[bridge.A], islands.Islands[bridge.B], rims[bridge.B], ground,
					out List<Vector3> joints, out _, out _, PostsOn(bridge.A), PostsOn(bridge.B)))
				{
					crowded++;
					continue;
				}
				Vector3 first = joints[0], last = joints[joints.Count - 1];
				PostsOn(bridge.A).Add(first);
				PostsOn(bridge.B).Add(last);
				var flat = new Vector3(last.x - first.x, 0f, last.z - first.z).normalized;
				int variant = random.Range(0, 2);
				// The posts at each end, the landing behind them on the grass, the span leaving toward +z.
				Lay(CaveArt.RopeBridgeEnd, variant, first, Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(LandingPitch(first, flat, ground), 0f, 0f), Vector3.one);
				Lay(CaveArt.RopeBridgeEnd, variant, last, Quaternion.LookRotation(-flat, Vector3.up) * Quaternion.Euler(LandingPitch(last, -flat, ground), 0f, 0f), Vector3.one);
				endLights.Clear();
				LightEnd(islands.Islands[bridge.A], first, flat, PostsOn(bridge.A));
				LightEnd(islands.Islands[bridge.B], last, -flat, PostsOn(bridge.B));
				for (int i = 0; i < joints.Count - 1; i++)
				{
					Vector3 p0 = joints[i], p1 = joints[i + 1];
					Vector3 step = p1 - p0;
					if (step.sqrMagnitude < 1e-4f)
					{
						continue;
					}
					int worn = random.NextFloat() < 0.15f ? 1 : 0;
					Lay(CaveArt.RopeBridgeSpan, worn, 0.5f * (p0 + p1), Quaternion.LookRotation(step, Vector3.up), new Vector3(1f, 1f, step.magnitude / CaveArt.SpanModule));
					metres += step.magnitude;
				}
				crossings.Add(new SceneCloudWorld.Crossing { From = first, To = last, Lights = endLights.ToArray() });
				laid++;
			}
			if (props.Count > 0)
			{
				int collidable = ScenePropBaker.Write(scene, BridgeSource, prototypes, props);
				notes?.Add($"Cloud world: {laid} rope bridge(s), {metres:0} m of walk, {collidable:N0} collidable piece(s)" +
					(crowded > 0 ? $"; {crowded} left out, no footing for their posts clear of the island's other bridges" : string.Empty) +
					(lightQuery != null ? $"; {lights} bridge-end light(s){(unlit > 0 ? $", {unlit} end(s) with no grass for one" : string.Empty)}" : string.Empty) + ".");
			}
			else
			{
				ScenePropBaker.Clear(scene, BridgeSource);
			}
			if (missing > 0)
			{
				notes?.Add($"Cloud world: {missing} bridge piece(s) skipped, their art is missing (run Generate biome art, then re-cut).");
			}
			return crossings;
		}

		/// <summary>
		/// What a way light laid on a cloud world gives off (2026-10-25, I6i), in the scene's frame, into <paramref name="into"/>: its kit
		/// piece's emitters (StructurePieces.EmittersOf) where the prop stands, each seeded by its place as a site's are; a cold glow
		/// takes its piece's own hue (rune, faerie), a crystal post's its crystal's hue (<see cref="PostCrystalKind"/>, laid in its cradle by
		/// <see cref="PostCrystal"/>, 2026-10-25, I6i-b), flickering only where that crystal does, else pulsing. Nothing for a prefab that is not a kit piece. What lets the world's lamps light the islands at night
		/// (SceneCloudWorld.Lights → SiteLightPool), which they could not while they were props alone.
		/// </summary>
		public static void LightEmitters(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, uint salt, List<SceneEmitter> into)
		{
			if (prefab == null || into == null || !StructurePieces.TryParsePrefab(prefab.name, out StructurePiece piece, out int variant, out _, out StructureRoofPitch? roof))
			{
				return;
			}
			foreach ((Vector3 at, SceneEmitterKind kind) in StructurePieces.EmittersOf(piece, variant, roof))
			{
				Vector3 world = position + rotation * Vector3.Scale(at, scale);
				uint seed = PointOfInterestPlanner.Hash(unchecked((int)salt), Mathf.RoundToInt(world.x * 10f), Mathf.RoundToInt(world.z * 10f), (int)kind);
				if (kind == SceneEmitterKind.Glow)
				{
					// As a site's (PointOfInterestSiteContext): a crystal post's light its crystal's hue and flicker.
					SceneGlowHue? own = StructurePieces.GlowHueOf(piece.Id);
					bool cradled = !own.HasValue && StructurePieces.TryCrystalSocket(piece, variant, out _, out _);
					CaveMineral postMineral = cradled ? ProceduralArtCatalogue.CaveMineralOf(PostCrystalKind) : default;
					seed = own.HasValue ? SceneGlow.WithHue(seed, own.Value) : SceneGlow.WithCrystalHue(seed, cradled ? postMineral.LightHue : -1);
					seed = SceneGlow.WithFlicker(seed, cradled && postMineral.Flickers);
				}
				into.Add(new SceneEmitter { Position = world, Kind = kind, Seed = seed });
			}
		}

		/// <summary>
		/// The crystal a cloud world's crystal posts carry (2026-10-25, I6i-b): a cloud world stands in no biome, so the one a post with
		/// no biome takes (BiomeTemplateArt.PostCrystal: the default cave crystals' heaviest glowing kind).
		/// </summary>
		public static FishMMO.Shared.Biomes.BiomeCaveCrystalKind PostCrystalKind => BiomeTemplateArt.PostCrystal(null);

		/// <summary>
		/// A way light's crystal (2026-10-25, I6i-b): where <paramref name="prefab"/> is a crystal post (a kit piece with a crystal
		/// socket) and the art of <see cref="PostCrystalKind"/> exists, the crystal to lay in its cradle, where and how large, its variant
		/// hashed from <paramref name="salt"/> and the post's place as a site's are. False otherwise (nothing to lay).
		/// </summary>
		public static bool PostCrystal(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, uint salt, out GameObject crystal, out Vector3 at,
			out Vector3 size)
		{
			crystal = null;
			at = position;
			size = scale;
			if (prefab == null || !StructurePieces.TryParsePrefab(prefab.name, out StructurePiece piece, out int variant, out _, out _)
				|| !StructurePieces.TryCrystalSocket(piece, variant, out Vector3 socket, out float crystalScale))
			{
				return false;
			}
			crystal = CaveArt.MineralPrefab(PostCrystalKind, unchecked((int)PointOfInterestPlanner.Hash(unchecked((int)salt),
				Mathf.RoundToInt(position.x * 10f), Mathf.RoundToInt(position.z * 10f), 0x5C)) & 0xFF);
			if (crystal == null)
			{
				return false;
			}
			at = position + rotation * Vector3.Scale(socket, scale);
			size = scale * crystalScale;
			return true;
		}

		/// <summary>How far to the side of a bridge's line its end's light stands, metres: clear of the posts (0.85 m out) and their cairn or guy.</summary>
		public const float EndLightAside = 1.75f;

		/// <summary>How far behind its posts, inland along the landing, a bridge end's light stands, metres.</summary>
		public const float EndLightBehind = 0.7f;

		/// <summary>
		/// Where a bridge end's light stands (2026-10-18, I6e): beside the landing behind the posts, to the right of the way out
		/// (else the left), on the island's own ground at least a metre and a half in from its outline and clear of every bridge
		/// post already on the island (it faces the way out). False when neither side has the ground. Pure.
		/// </summary>
		public static bool BridgeEndLight(CloudIslands.Island island, Vector3 post, Vector3 outward, Func<float, float, float> ground, List<Vector3> taken,
			out Vector3 at)
		{
			var way = new Vector3(outward.x, 0f, outward.z);
			at = default;
			if (island == null || way.sqrMagnitude < 1e-8f)
			{
				return false;
			}
			way.Normalize();
			var right = new Vector3(way.z, 0f, -way.x);
			for (int side = 1; side >= -1; side -= 2)
			{
				Vector3 p = post - way * EndLightBehind + right * (side * EndLightAside);
				if (island.Beyond(p.x, p.z) > -1.5f || Crowds(p, post, taken))
				{
					continue;
				}
				at = new Vector3(p.x, ground != null ? ground(p.x, p.z) : post.y, p.z);
				return true;
			}
			return false;
		}

		// Within a metre and a half of another bridge's post on the island (its own end's posts are the ones it stands beside).
		private static bool Crowds(Vector3 p, Vector3 own, List<Vector3> taken)
		{
			if (taken == null)
			{
				return false;
			}
			foreach (Vector3 other in taken)
			{
				if ((other - own).sqrMagnitude < 1e-4f)
				{
					continue;
				}
				float dx = other.x - p.x, dz = other.z - p.z;
				if (dx * dx + dz * dz < 1.5f * 1.5f)
				{
					return true;
				}
			}
			return false;
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
				// The world body's own palette and lights (2026-10-18, I6e): a cloud world stands in no biome. Empty: the kit's.
				CloudWorldStructures structures = request.Body != null ? request.Body.CloudWorldStructures : null;
				PointOfInterestSkyTraffic.Build(scene, world.transform, world, request, tiles.WidthMetres, tiles.DepthMetres, ground, notes, structures?.Laid, structures);
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
		public static SceneCloudWorld Record(Scene scene, CloudIslands.Plan islands, Func<float, float, float> ground, List<string> notes,
			List<SceneCloudWorld.Crossing> crossings = null)
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
			world.Bridges = crossings != null ? crossings.ToArray() : Array.Empty<SceneCloudWorld.Crossing>();
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
