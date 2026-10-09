#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Tuning for <see cref="CliffPlacer.Place"/>; the defaults are the shipped behaviour.</summary>
	public sealed class CliffPlacerOptions
	{
		/// <summary>The planner's own settings.</summary>
		public CliffRockPlacementOptions Placement = new CliffRockPlacementOptions();

		/// <summary>
		/// Least weight the biome's cliff layer must have in the alphamap at a point, so cliff rocks only
		/// stand where the ground is painted as that biome's cliff.
		/// </summary>
		public float MinCliffPaint = 0.25f;

		/// <summary>Screen heights at which each level of detail gives way to the next (the last one culls).</summary>
		public float[] LodHeights = CliffRocks.LodHeights;

		/// <summary>Size of the grouping chunks under the root, metres: hierarchy tidiness only.</summary>
		public float ChunkMetres = 128f;

		/// <summary>The seed the shared meshes are carved from: the art generator's, so every scene shares them.</summary>
		public int MeshSeed = ProceduralArtCatalogue.DefaultSeed;

		/// <summary>
		/// The rock the ground is made of at a scene position (x, altitude, z) — a <see cref="RockTypes"/>
		/// name — or null where unknown. Where the biome there accepts it (<see cref="CliffRocks.Accepts"/>:
		/// its own rock or one of its <see cref="BiomeArtSpec.Entry.Rocks"/>) it wins over the biome's own,
		/// so a granite country's grassland stands in granite and a desert's canyon in its sandstone; a rock
		/// the biome does not accept (sandstone under a bog) gives way to the biome's own. Biomes whose
		/// cliffs are ice keep their ice. Null: every cliff is its biome's rock.
		/// </summary>
		public Func<float, float, float, string> RockTypeAt;

		/// <summary>
		/// True at a scene position (x, z) where no cliff rock may stand: in a river's channel or under a
		/// lake. Null: anywhere the cliff paint allows.
		/// </summary>
		public Func<float, float, bool> Excluded;

		/// <summary>Raise the talus cones into the terrain heightmap and paint them as the biome's cliff (scree).</summary>
		public bool EditTerrain = true;

		/// <summary>Remove trees and grass inside rocks and on the scree, and re-seat trees on the raised ground.</summary>
		public bool CleanScatter = true;

		/// <summary>
		/// The material rocks of a type are given, from the generated material's name and the generated
		/// material itself (null when not generated); null keeps the generated one.
		/// </summary>
		/// <remarks>
		/// The seam a LOCAL scene's material overrides come through (<see cref="LocalArtScope.CliffOptions"/>).
		/// It is left null for every scene outside Assets/LOCAL, so committed scenes only ever reference
		/// the generated materials.
		/// </remarks>
		public Func<string, Material, Material> MaterialFor;
	}

	/// <summary>What <see cref="CliffPlacer.Place"/> did.</summary>
	public sealed class CliffPlacerReport
	{
		/// <summary>Problems and things a person should know, one line each, for the generation result's notes.</summary>
		public readonly List<string> Notes = new List<string>();
		public CliffRockStats Stats = new CliffRockStats();
		/// <summary>Rocks placed in the scene (face and talus).</summary>
		public int Pieces;
		/// <summary>Rocks left out because their meshes or material have not been generated.</summary>
		public int MissingAssets;
		/// <summary>Rocks per rock type.</summary>
		public readonly Dictionary<string, int> PiecesByType = new Dictionary<string, int>();
		/// <summary>LOD0 triangles of every placed rock.</summary>
		public long Triangles;
		/// <summary>Trees taken out of rocks or off scree, trees re-seated on raised ground, detail texels cleared.</summary>
		public int TreesRemoved, TreesReseated, DetailTexelsCleared;
		/// <summary>Rocks big enough to collide (their colliders streamed near characters).</summary>
		public int Collidable;
		public TimeSpan Elapsed;

		public override string ToString()
		{
			var types = new List<string>();
			foreach (KeyValuePair<string, int> kv in PiecesByType)
			{
				types.Add($"{kv.Key} {kv.Value}");
			}
			float per100 = Stats.CliffLength > 0f ? Pieces / Stats.CliffLength * 100f : 0f;
			return $"[Cliffs] {Pieces:N0} rock(s) ({per100:0.0} per 100 m) as instanced data ({Collidable:N0} collidable, streamed), {Triangles:N0} triangles at LOD0 [{string.Join(", ", types)}]; {Stats}; " +
				$"{TreesRemoved} trees removed and {TreesReseated} re-seated, {DetailTexelsCleared:N0} detail texels cleared; " +
				$"{MissingAssets} left out for missing assets; {Elapsed.TotalSeconds:0.0} s.";
		}
	}

	/// <summary>
	/// Builds a generated scene's cliffs from jointed cliff sections (<see cref="CliffSections"/>) on the
	/// steep ground the biomes paint as cliff, each in the stone of its biome's cliff, with talus cones
	/// raised into the terrain below them and fallen debris on the cones.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where it runs.</b> Inside <c>SceneGenerator.PaintBiomes</c>, after the terrain scatter: it
	/// needs the alphamaps (a rock only stands where the biome's cliff layer is painted) and the biome
	/// field. PaintBiomes also backs the Repaint Biomes tool, so a designer who sculpts the ground and
	/// repaints gets the cliffs re-placed on the new shape.
	/// </para>
	/// <para>
	/// <b>Order with the scatter.</b> The scatter has already baked trees and details when the cliffs are
	/// planned, and the cones then raise the ground. Rather than move the scatter (the biome session's,
	/// and it needs the alphamaps the cliffs also read), the placer cleans up after itself: trees inside a
	/// rock or on scree are removed, trees on raised ground are re-seated on it, and the detail layers are
	/// cleared under rock and scree. The cone areas are painted with the biome's cliff layer (scree).
	/// </para>
	/// <para>
	/// <b>Idempotent.</b> Every root object of the scene named <see cref="RootName"/> and carrying
	/// <see cref="GeneratedCliffs"/> is destroyed before anything is placed, and exactly one is made.
	/// The cones' heightmap raise is recorded on an editor-only child of the root
	/// (<see cref="GeneratedCliffTalus"/>) and taken back out before the next placement, so re-running
	/// never piles cone on cone.
	/// </para>
	/// <para>
	/// <b>Collision on both sides.</b> Each rock object carries only a non-convex <see cref="MeshCollider"/>
	/// on its collider level (<see cref="CliffRocks.CollisionLod"/>) on the Ground layer, like the
	/// terrain — the server keeps it. Its "Visual" child carries the LOD group and renderers under
	/// <see cref="ClientOnlyObject"/>, which server builds strip.
	/// </para>
	/// <para>
	/// <b>Assets are referenced, never made.</b> The art generator writes every rock mesh
	/// (<see cref="CliffRocks.AllMeshes"/>, <see cref="CliffSections.AllMeshes"/>) with a path-derived GUID.
	/// A rock missing its assets is left out and reported.
	/// </para>
	/// <para>
	/// <b>Stone by biome.</b> A rock wears the material of its site's rock type
	/// (<see cref="CliffRocks.MaterialName"/>): the rock the biome's cliff layer stands for
	/// (<see cref="CliffRocks.RockTypeFor"/>: the family's rock, or the biome's own bedrock), or the
	/// planet geology's rock under it where the biome accepts that rock (<see cref="CliffPlacerOptions.RockTypeAt"/>,
	/// <see cref="CliffRocks.RockFor"/>). Sections are shared by
	/// every type, so one section mesh is sandstone in a desert canyon and granite on a mountain; each
	/// mesh-and-material pair is its own prefab.
	/// </para>
	/// </remarks>
	public static class CliffPlacer
	{
		/// <summary>The one root every placed rock lives under.</summary>
		public const string RootName = "Cliffs";

		/// <summary>The name of a rock's client-only child that holds its renderers.</summary>
		public const string VisualName = "Visual";

		/// <summary>
		/// The static flags every cliff object carries: culled by occluders, seen by reflection probes. NOT
		/// batching-static: the client draws the rocks GPU-instanced (CliffRockInstancing), which needs each
		/// rock's own shared meshes, and static batching would copy every 10–45k-triangle rock into combined
		/// meshes (hundreds of MB for a scene) and hand the instancer a combined mesh instead.
		/// </summary>
		public const StaticEditorFlags Flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

		/// <summary>Where the art generator writes, and the placer loads, a piece's mesh at a level of detail.</summary>
		public static string MeshPath(in CliffPiece piece, int lod) => ProceduralArtCatalogue.MeshPath(CliffRocks.MeshName(in piece, lod));

		/// <summary>The layer every collider is on: Ground, as the terrain, falling back to Default if a project has no such layer.</summary>
		public static int ColliderLayer
		{
			get
			{
				int ground = LayerMask.NameToLayer("Ground");
				return ground >= 0 ? ground : 0;
			}
		}

		/// <summary>
		/// Places the cliffs of a scene whose terrain is already painted and scattered.
		/// </summary>
		/// <param name="scene">The scene the root goes into.</param>
		/// <param name="tiles">The scene's terrain tiles.</param>
		/// <param name="palette">The painted layers, and which biome's cliff layer each one is.</param>
		/// <param name="field">Which biome lies where.</param>
		/// <param name="seed">The scene's seed (the same one the splat and the scatter use).</param>
		/// <param name="normalizedHeight">Planet-normalised height at (x, y, z), for cliff layers' height bands; null skips the bands.</param>
		/// <param name="options">Null uses the defaults.</param>
		public static CliffPlacerReport Place(Scene scene, IReadOnlyList<Terrain> tiles, SceneTerrainPalette palette, SceneBiomeField field,
			uint seed, Func<float, float, float, float> normalizedHeight, CliffPlacerOptions options = null)
		{
			options ??= new CliffPlacerOptions();
			var report = new CliffPlacerReport();
			var clock = System.Diagnostics.Stopwatch.StartNew();
			int removed = Clear(scene, out bool removedEdits);
			if (removed > 1)
			{
				report.Notes.Add($"Cliffs: {removed} '{RootName}' roots were found and replaced by one.");
			}
			if (tiles == null || palette == null || field == null)
			{
				report.Notes.Add("Cliffs: no terrain, palette or biome field was given; none were placed.");
				return report;
			}
			var ground = new TerrainGround(tiles);
			if (ground.Count == 0)
			{
				report.Notes.Add("Cliffs: no terrain tile has data; none were placed.");
				return report;
			}

			// Each biome's cliff: the first of its cliff layers whose family stands for a rock.
			int biomes = field.Biomes.Count;
			var rock = new string[biomes];
			var spec = new BiomeArtSpec.Entry[biomes];
			var minAngle = new float[biomes];
			var paintLayer = new int[biomes];
			var heightBand = new CliffTextureLayer[biomes];
			var unmapped = new SortedSet<string>(StringComparer.Ordinal);
			for (int b = 0; b < biomes; b++)
			{
				paintLayer[b] = -1;
				foreach (SceneTerrainPalette.Entry entry in palette.EntriesFor(b))
				{
					if (entry.Role != PaletteRole.Cliff || !(entry.Source is CliffTextureLayer cliff))
					{
						continue;
					}
					string family = FamilyOf(entry);
					spec[b] = entry.Biome != null ? BiomeArtSpec.For(entry.Biome.name) : null;
					string type = CliffRocks.RockTypeFor(family, spec[b]);
					if (type == null)
					{
						unmapped.Add(family ?? $"{entry.Biome?.ResolvedDisplayName} {entry.Slot} (unknown family)");
						continue;
					}
					rock[b] = type;
					minAngle[b] = Mathf.Max(20f, cliff.minCliffAngle);
					paintLayer[b] = entry.LayerIndex;
					heightBand[b] = cliff.useCliffHeightConstraint ? cliff : null;
					ground.NeedLayer(entry.LayerIndex);
					break;
				}
			}
			if (unmapped.Count > 0)
			{
				report.Notes.Add($"Cliffs: these cliff families stay plain terrain (no rock stands for them): {string.Join(", ", unmapped)}.");
			}
			if (Array.TrueForAll(rock, r => r == null))
			{
				return report;
			}

			var scratch = new float[Math.Max(1, biomes)];
			CliffRockSiteAt siteAt = (float x, float z, float y, out CliffRockSite site) =>
			{
				site = default;
				if (options.Excluded != null && options.Excluded(x, z))
				{
					return false;
				}
				int b = field.DominantIndexAt(x, z, scratch);
				if (b < 0 || rock[b] == null)
				{
					return false;
				}
				if (heightBand[b] != null && normalizedHeight != null)
				{
					float h = normalizedHeight(x, y, z);
					if (h < heightBand[b].cliffHeightRange.min || h > heightBand[b].cliffHeightRange.max)
					{
						return false;
					}
				}
				if (ground.Paint(x, z, paintLayer[b]) < options.MinCliffPaint)
				{
					return false;
				}
				string type = rock[b];
				if (options.RockTypeAt != null && type != CliffRocks.Ice)
				{
					// The geology's rock, where this biome accepts it; else the biome's own.
					string bedrock = options.RockTypeAt(x, y, z);
					if (bedrock != null && RockTypes.TryGet(bedrock, out _) && CliffRocks.Accepts(spec[b], bedrock))
					{
						type = bedrock;
					}
				}
				site = new CliffRockSite
				{
					Type = type,
					MinAngle = minAngle[b],
				};
				return true;
			};

			var assets = new RockAssets(options.MaterialFor);
			var seating = new Dictionary<string, MeshBuilder>(StringComparer.Ordinal);
			// The talus too keeps out of the water the rock sites keep out of.
			options.Placement ??= new CliffRockPlacementOptions();
			options.Placement.Excluded = options.Excluded;
			CliffRockPlan plan = CliffRockPlacement.Plan(ground, ground.Area, siteAt, seed, piece =>
			{
				// Keyed by mesh name: a section's collider is one mesh for every type.
				string key = CliffRocks.MeshName(in piece, CliffRocks.CollisionLod);
				if (!seating.TryGetValue(key, out MeshBuilder m))
				{
					/* A section's generated collider, read back: building one live builds its whole net (seconds each,
					 * two dozen of them). The asset is the same mesh (same seed); a rock is left out below anyway
					 * when its assets are missing, so the live build is only a fallback. */
					Mesh generated = CliffRocks.IsSection(in piece) ? assets.Mesh(in piece, CliffRocks.CollisionLod) : null;
					seating[key] = m = generated != null && generated.isReadable ? Seating(generated) : CliffRocks.Build(in piece, CliffRocks.CollisionLod, options.MeshSeed);
				}
				return m;
			}, options.Placement);
			report.Stats = plan.Stats;
			if (plan.Rocks.Count == 0)
			{
				report.Elapsed = clock.Elapsed;
				report.Notes.Add($"Cliffs: none placed. {plan.Stats}");
				return report;
			}

			List<GeneratedCliffTalus.TerrainEdit> edits = options.EditTerrain && plan.Cones.Count > 0
				? RaiseCones(ground, plan, field, paintLayer, scratch)
				: new List<GeneratedCliffTalus.TerrainEdit>();
			if (options.CleanScatter)
			{
				CleanScatter(ground, plan, report);
			}

			int layer = ColliderLayer;
			var root = new GameObject(RootName) { layer = layer };
			root.AddComponent<GeneratedCliffs>();
			SceneManager.MoveGameObjectToScene(root, scene);
			GameObjectUtility.SetStaticEditorFlags(root, Flags);
			if (edits.Count > 0)
			{
				// What the cones raised, so the next placement can lower it again (left out of builds).
				var record = new GameObject(GeneratedCliffTalus.ObjectName) { tag = "EditorOnly" };
				record.transform.SetParent(root.transform, false);
				record.AddComponent<GeneratedCliffTalus>().Edits = edits;
			}
			/* The rocks as baked props, not objects: one shared prefab per distinct rock (ScenePropSet "Cliffs",
			 * drawn instanced on the GPU by the client) and their collision streamed into physics near characters
			 * from the prefab's shared collision mesh (ScenePropCollisionSet). As objects, a scene's 20,000 rocks were 101,000 game objects, 60,000 renderers and 20,000
			 * LOD groups culled and levelled on the main thread every frame. */
			var prototypeOf = new Dictionary<string, int>();
			var prototypes = new List<ScenePropSet.Prototype>();
			var props = new List<ScenePropSet.Prop>(plan.Rocks.Count);
			int levels = options.LodHeights.Length;
			var meshes = new Mesh[levels];
			string firstMissing = null;
			foreach (PlacedCliffRock r in plan.Rocks)
			{
				CliffPiece piece = r.Piece;
				bool complete = true;
				for (int lod = 0; lod < levels; lod++)
				{
					meshes[lod] = assets.Mesh(in piece, lod);
					complete &= meshes[lod] != null;
				}
				Mesh collisionMesh = meshes[Mathf.Min(CliffRocks.CollisionLod, levels - 1)];
				Material material = assets.Material(piece.Type);
				if (!complete || material == null)
				{
					if (report.MissingAssets++ == 0)
					{
						firstMissing = material == null ? ProceduralArtCatalogue.MaterialPath(CliffRocks.MaterialName(piece.Type)) : MeshPath(in piece, 0);
					}
					continue;
				}
				string name = $"{(r.Talus ? "Talus" : "Crag")} {CliffRocks.BaseName(in piece)} {material.name}";
				if (!prototypeOf.TryGetValue(name, out int prototype))
				{
					GameObject prefab = RockPrefab(name, meshes, collisionMesh, material, layer, options.LodHeights, r.Talus);
					if (prefab == null)
					{
						report.MissingAssets++;
						continue;
					}
					prototype = prototypes.Count;
					prototypeOf[name] = prototype;
					prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = layer });
				}
				Quaternion rotation = RotationOf(in r);
				props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = r.Position, Rotation = rotation, Scale = r.Scale });
				report.Triangles += meshes[0].GetIndexCount(0) / 3;
				report.Pieces++;
				report.PiecesByType[piece.Type] = (report.PiecesByType.TryGetValue(piece.Type, out int n) ? n : 0) + 1;
			}
			report.Collidable = ScenePropBaker.Write(scene, PropSource, prototypes, props, layer);
			if (report.MissingAssets > 0)
			{
				report.Notes.Add($"Cliffs: {report.MissingAssets} rock(s) left out because their generated assets are missing (e.g. '{firstMissing}'); run Generate Biome Art, then repaint.");
			}
			EditorSceneManager.MarkSceneDirty(scene);
			report.Elapsed = clock.Elapsed;
			report.Notes.Add(report.ToString());
			return report;
		}

		/// <summary>A generated mesh as the planner reads it: positions, normals and triangles.</summary>
		private static MeshBuilder Seating(Mesh mesh)
		{
			var m = new MeshBuilder(1);
			Vector3[] positions = mesh.vertices, normals = mesh.normals;
			var white = new Color32(255, 255, 255, 0);
			for (int i = 0; i < positions.Length; i++)
			{
				m.AddVertex(positions[i], normals.Length == positions.Length ? normals[i] : Vector3.up, Vector2.zero, white);
			}
			m.Submeshes[0].AddRange(mesh.GetTriangles(0));
			return m;
		}

		/// <summary>The source the cliffs' props and colliders are baked under (<see cref="ScenePropBaker"/>).</summary>
		public const string PropSource = "Cliffs";

		/// <summary>
		/// The shared prefab for one distinct rock, made once and kept with the generated art: its
		/// <see cref="LODGroup"/> and a renderer child per level, and a non-convex <see cref="MeshCollider"/> on
		/// its collision mesh, which the bake reads (the prefab is never placed). Talus casts no shadow from its
		/// last level. Returns the existing prefab when there is one with the same parts.
		/// </summary>
		public static GameObject RockPrefab(string name, Mesh[] lodMeshes, Mesh collision, Material material, int layer, float[] lodHeights, bool talus)
		{
			string path = ProceduralArtCatalogue.PrefabPath($"Rocks/{name}");
			GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			// Reused only when it is this rock, collision included, and has the GUID its path gives it on every machine.
			if (existing != null && SameRock(existing, lodMeshes, material) && existing.TryGetComponent(out MeshCollider kept)
				&& kept.sharedMesh == collision && ProceduralArtPayload.HasExpectedGuid(path))
			{
				return existing;
			}
			string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
			if (!AssetDatabase.IsValidFolder(folder))
			{
				System.IO.Directory.CreateDirectory(folder);
				AssetDatabase.Refresh();
			}
			var go = new GameObject(name) { layer = layer };
			try
			{
				int levels = Mathf.Min(lodMeshes.Length, lodHeights.Length);
				var lods = new LOD[levels];
				for (int lod = 0; lod < levels; lod++)
				{
					var child = new GameObject("LOD" + lod) { layer = layer };
					child.transform.SetParent(go.transform, false);
					child.AddComponent<MeshFilter>().sharedMesh = lodMeshes[lod];
					var renderer = child.AddComponent<MeshRenderer>();
					renderer.sharedMaterial = material;
					renderer.shadowCastingMode = lod < levels - 1 || !talus ? ShadowCastingMode.On : ShadowCastingMode.Off;
					renderer.receiveShadows = true;
					renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
					lods[lod] = new LOD(lodHeights[lod], new Renderer[] { renderer });
				}
				LODGroup group = go.AddComponent<LODGroup>();
				group.SetLODs(lods);
				group.fadeMode = LODFadeMode.CrossFade;
				group.animateCrossFading = true;
				group.RecalculateBounds();
				var collider = go.AddComponent<MeshCollider>();
				collider.convex = false;
				collider.sharedMesh = collision;
				/* As the generator saves its own prefabs: a scene's props reference the rock by GUID, and the art is
				 * generated on every machine, so the GUID and object IDs must come from its path, not from Unity. */
				var problems = new List<string>();
				GameObject prefab = ProceduralArtPayload.SavePrefab(go, path, problems);
				foreach (string problem in problems)
				{
					Debug.LogWarning($"[Cliffs] {problem}");
				}
				return prefab;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static bool SameRock(GameObject prefab, Mesh[] lodMeshes, Material material)
		{
			MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
			if (filters.Length != lodMeshes.Length)
			{
				return false;
			}
			for (int i = 0; i < filters.Length; i++)
			{
				if (filters[i].sharedMesh != lodMeshes[i] || filters[i].GetComponent<MeshRenderer>()?.sharedMaterial != material)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The rotation a placed rock's matrix holds, as a quaternion.</summary>
		public static Quaternion RotationOf(in PlacedCliffRock rock)
		{
			Vector3 forward = rock.Rotation.GetColumn(2), up = rock.Rotation.GetColumn(1);
			return Quaternion.LookRotation(forward, up);
		}

		/// <summary>
		/// One placed rock as scene objects: the rock itself on <paramref name="layer"/>, carrying only
		/// its non-convex <see cref="MeshCollider"/> (the server keeps it); and a <see cref="VisualName"/>
		/// child under <see cref="ClientOnlyObject"/> with the <see cref="LODGroup"/> and a renderer child
		/// per level of detail (server builds strip it).
		/// </summary>
		public static GameObject BuildRock(Transform parent, in PlacedCliffRock rock, Mesh[] lodMeshes, Mesh collision, Material material, int layer, float[] lodHeights)
		{
			var go = new GameObject((rock.Talus ? "Talus " : "Crag ") + CliffRocks.BaseName(in rock.Piece)) { layer = layer };
			go.transform.SetParent(parent, false);
			go.transform.SetPositionAndRotation(rock.Position, RotationOf(in rock));
			go.transform.localScale = rock.Scale;
			GameObjectUtility.SetStaticEditorFlags(go, Flags);
			var collider = go.AddComponent<MeshCollider>();
			collider.convex = false;
			collider.sharedMesh = collision;

			var visual = new GameObject(VisualName) { layer = layer };
			visual.transform.SetParent(go.transform, false);
			visual.AddComponent<ClientOnlyObject>();
			GameObjectUtility.SetStaticEditorFlags(visual, Flags);
			int levels = Mathf.Min(lodMeshes.Length, lodHeights.Length);
			var lods = new LOD[levels];
			for (int lod = 0; lod < levels; lod++)
			{
				var child = new GameObject("LOD" + lod) { layer = layer };
				child.transform.SetParent(visual.transform, false);
				GameObjectUtility.SetStaticEditorFlags(child, Flags);
				child.AddComponent<MeshFilter>().sharedMesh = lodMeshes[lod];
				var renderer = child.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = lod < levels - 1 || !rock.Talus ? ShadowCastingMode.On : ShadowCastingMode.Off;
				renderer.receiveShadows = true;
				renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
				lods[lod] = new LOD(lodHeights[lod], new Renderer[] { renderer });
			}
			var group = visual.AddComponent<LODGroup>();
			group.SetLODs(lods);
			group.fadeMode = LODFadeMode.CrossFade;
			group.animateCrossFading = true;
			group.RecalculateBounds();
			return go;
		}

		/// <summary>Destroys every cliff root in a scene, lowering the ground its talus cones raised. Returns how many there were.</summary>
		public static int Clear(Scene scene) => Clear(scene, out _);

		/// <summary>As <see cref="Clear(Scene)"/>; <paramref name="loweredGround"/> says whether any cone was taken out of a heightmap.</summary>
		public static int Clear(Scene scene, out bool loweredGround)
		{
			int removed = 0;
			loweredGround = false;
			if (!scene.IsValid())
			{
				return 0;
			}
			ScenePropBaker.Clear(scene, PropSource);
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root != null && root.name == RootName && root.GetComponent<GeneratedCliffs>() != null)
				{
					foreach (GeneratedCliffTalus talus in root.GetComponentsInChildren<GeneratedCliffTalus>(true))
					{
						loweredGround |= LowerCones(talus);
					}
					UnityEngine.Object.DestroyImmediate(root);
					removed++;
				}
			}
			return removed;
		}

		/// <summary>Takes a placement's cone raises back out of the heightmaps and re-seats the trees on the lowered ground.</summary>
		public static bool LowerCones(GeneratedCliffTalus talus)
		{
			bool any = false;
			foreach (GeneratedCliffTalus.TerrainEdit edit in talus.Edits)
			{
				TerrainData data = edit.Data;
				if (data == null || data.heightmapResolution != edit.Resolution || edit.Samples.Count != edit.Deltas.Count)
				{
					continue;
				}
				int res = data.heightmapResolution;
				float[,] heights = data.GetHeights(0, 0, res, res);
				for (int k = 0; k < edit.Samples.Count; k++)
				{
					int s = edit.Samples[k];
					heights[s / res, s % res] = Mathf.Clamp01(heights[s / res, s % res] - edit.Deltas[k]);
				}
				// Each tree follows the ground under it down, keeping its height over it: the scatter sinks every tree by
				// design, and a snap would stand it on the surface instead.
				TreeInstance[] trees = data.treeInstances;
				var under = new float[trees.Length];
				for (int t = 0; t < trees.Length; t++)
				{
					under[t] = data.GetInterpolatedHeight(trees[t].position.x, trees[t].position.z);
				}
				data.SetHeights(0, 0, heights);
				for (int t = 0; t < trees.Length; t++)
				{
					float moved = data.GetInterpolatedHeight(trees[t].position.x, trees[t].position.z) - under[t];
					trees[t].position.y = Mathf.Clamp01(trees[t].position.y + moved / data.size.y);
				}
				data.SetTreeInstances(trees, false);
				EditorUtility.SetDirty(data);
				any = true;
			}
			return any;
		}

		/// <summary>
		/// The ground family a palette cliff entry paints: the spec table's entry for its biome and
		/// slot, else the generated terrain layer's name (<c>Ground_&lt;Family&gt;</c>). Null when
		/// neither says (hand-made art under another name).
		/// </summary>
		public static string FamilyOf(SceneTerrainPalette.Entry entry)
		{
			if (entry?.Biome != null && entry.Slot != null && entry.Slot.StartsWith(SceneTerrainPalette.SlotCliff, StringComparison.Ordinal) &&
				int.TryParse(entry.Slot.Substring(SceneTerrainPalette.SlotCliff.Length), out int slot))
			{
				BiomeArtSpec.Entry spec = BiomeArtSpec.For(entry.Biome.name);
				if (spec != null && slot >= 0 && slot < spec.Cliffs.Length)
				{
					return spec.Cliffs[slot].Family;
				}
			}
			string layer = entry?.Source?.terrainLayer != null ? entry.Source.terrainLayer.name : null;
			const string prefix = "Ground_";
			return layer != null && layer.StartsWith(prefix, StringComparison.Ordinal) ? layer.Substring(prefix.Length) : null;
		}

		// ── Terrain edits ─────────────────────────────────────────────

		/// <summary>
		/// Adds the cones' raised height to every tile's heightmap and paints the scree with the biome's
		/// cliff layer (its weight the debris share × 0.85, the other layers scaled down to keep the sum).
		/// </summary>
		private static List<GeneratedCliffTalus.TerrainEdit> RaiseCones(TerrainGround ground, CliffRockPlan plan, SceneBiomeField field, int[] paintLayer, float[] scratch)
		{
			var edits = new List<GeneratedCliffTalus.TerrainEdit>();
			foreach (TerrainGround.Tile tile in ground.Tiles)
			{
				TerrainData data = tile.Terrain.terrainData;
				int res = data.heightmapResolution;
				float[,] heights = data.GetHeights(0, 0, res, res);
				bool changed = false;
				var edit = new GeneratedCliffTalus.TerrainEdit { Data = data, Resolution = res };
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						float x = tile.Origin.x + i / (float)(res - 1) * tile.Size.x, z = tile.Origin.z + j / (float)(res - 1) * tile.Size.z;
						float d = plan.HeightDelta(x, z);
						if (d > 0.005f)
						{
							float before = heights[j, i];
							heights[j, i] = Mathf.Clamp01(before + d / tile.Size.y);
							edit.Samples.Add(j * res + i);
							edit.Deltas.Add(heights[j, i] - before);
							changed = true;
						}
					}
				}
				if (!changed)
				{
					continue;
				}
				data.SetHeights(0, 0, heights);
				edits.Add(edit);
				int ares = data.alphamapResolution, layers = data.alphamapLayers;
				float[,,] maps = data.GetAlphamaps(0, 0, ares, ares);
				for (int j = 0; j < ares; j++)
				{
					for (int i = 0; i < ares; i++)
					{
						float x = tile.Origin.x + (i + 0.5f) / ares * tile.Size.x, z = tile.Origin.z + (j + 0.5f) / ares * tile.Size.z;
						float debris = plan.Debris(x, z);
						if (debris < 0.05f)
						{
							continue;
						}
						int b = field.DominantIndexAt(x, z, scratch);
						int target = b >= 0 && b < paintLayer.Length ? paintLayer[b] : -1;
						if (target < 0 || target >= layers)
						{
							continue;
						}
						float w = Mathf.Clamp01(debris * 0.85f);
						if (maps[j, i, target] >= w)
						{
							continue;
						}
						float rest = 1f - maps[j, i, target], keep = rest > 1e-5f ? (1f - w) / rest : 0f;
						for (int l = 0; l < layers; l++)
						{
							maps[j, i, l] = l == target ? w : maps[j, i, l] * keep;
						}
					}
				}
				data.SetAlphamaps(0, 0, maps);
				EditorUtility.SetDirty(data);
			}
			return edits;
		}

		/// <summary>
		/// Trees inside a rock or on scree are removed; trees on raised ground are re-seated on it; detail
		/// texels under rock or on scree are cleared. So no tree or grass stands inside a rock or floats
		/// over (or is buried under) a cone.
		/// </summary>
		private static void CleanScatter(TerrainGround ground, CliffRockPlan plan, CliffPlacerReport report)
		{
			foreach (TerrainGround.Tile tile in ground.Tiles)
			{
				TerrainData data = tile.Terrain.terrainData;
				TreeInstance[] trees = data.treeInstances;
				if (trees.Length > 0)
				{
					var kept = new List<TreeInstance>(trees.Length);
					bool changed = false;
					foreach (TreeInstance tree in trees)
					{
						float x = tile.Origin.x + tree.position.x * tile.Size.x, z = tile.Origin.z + tree.position.z * tile.Size.z;
						if (plan.UnderRock(x, z, 0.3f) || plan.Debris(x, z) > 0.5f)
						{
							report.TreesRemoved++;
							changed = true;
							continue;
						}
						TreeInstance t = tree;
						float raised = plan.HeightDelta(x, z);
						if (raised > 0.01f)
						{
							// Up by what the cone raised the ground under it, keeping the sink the scatter gave it.
							t.position = new Vector3(t.position.x, Mathf.Clamp01(t.position.y + raised / tile.Size.y), t.position.z);
							report.TreesReseated++;
							changed = true;
						}
						kept.Add(t);
					}
					if (changed)
					{
						// Not snapped: a snap would stand every tree on the surface and undo the sink the scatter gave it.
						// Old cones' trees were already carried down with the ground (LowerCones).
						data.SetTreeInstances(kept.ToArray(), false);
					}
				}
				int dres = data.detailResolution;
				int[] layers = data.GetSupportedLayers(0, 0, dres, dres);
				if (dres <= 0 || layers.Length == 0)
				{
					continue;
				}
				var clear = new bool[dres, dres];
				bool any = false;
				for (int j = 0; j < dres; j++)
				{
					for (int i = 0; i < dres; i++)
					{
						float x = tile.Origin.x + (i + 0.5f) / dres * tile.Size.x, z = tile.Origin.z + (j + 0.5f) / dres * tile.Size.z;
						if (plan.UnderRock(x, z, 0.15f) || plan.Debris(x, z) > 0.5f)
						{
							clear[j, i] = true;
							any = true;
						}
					}
				}
				if (!any)
				{
					continue;
				}
				foreach (int layer in layers)
				{
					int[,] map = data.GetDetailLayer(0, 0, dres, dres, layer);
					bool changed = false;
					for (int j = 0; j < dres; j++)
					{
						for (int i = 0; i < dres; i++)
						{
							if (clear[j, i] && map[j, i] != 0)
							{
								map[j, i] = 0;
								report.DetailTexelsCleared++;
								changed = true;
							}
						}
					}
					if (changed)
					{
						data.SetDetailLayer(0, 0, layer, map);
					}
				}
				EditorUtility.SetDirty(data);
			}
		}

		// ── The ground, from the tiles ────────────────────────────────

		/// <summary>Heights, holes and cliff paint of a scene's tiles, read once.</summary>
		private sealed class TerrainGround : ICliffRockGround
		{
			public sealed class Tile
			{
				public Terrain Terrain;
				public Vector3 Origin, Size;
				public bool[,] Solid;
				public int HoleResolution;
				public int AlphaResolution;
				public readonly Dictionary<int, float[]> Paint = new Dictionary<int, float[]>();
			}

			public readonly List<Tile> Tiles = new List<Tile>();
			public Rect Area;
			public int Count => Tiles.Count;

			public TerrainGround(IReadOnlyList<Terrain> terrains)
			{
				float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
				foreach (Terrain t in terrains)
				{
					if (t == null || t.terrainData == null)
					{
						continue;
					}
					TerrainData data = t.terrainData;
					var tile = new Tile { Terrain = t, Origin = t.transform.position, Size = data.size, AlphaResolution = data.alphamapResolution };
					int holes = data.holesResolution;
					if (holes > 0)
					{
						bool[,] solid = data.GetHoles(0, 0, holes, holes);
						foreach (bool s in solid)
						{
							if (!s)
							{
								tile.Solid = solid;
								tile.HoleResolution = holes;
								break;
							}
						}
					}
					Tiles.Add(tile);
					x0 = Mathf.Min(x0, tile.Origin.x);
					z0 = Mathf.Min(z0, tile.Origin.z);
					x1 = Mathf.Max(x1, tile.Origin.x + tile.Size.x);
					z1 = Mathf.Max(z1, tile.Origin.z + tile.Size.z);
				}
				Area = Tiles.Count > 0 ? new Rect(x0, z0, x1 - x0, z1 - z0) : default;
			}

			/// <summary>Reads one alphamap channel of every tile, once.</summary>
			public void NeedLayer(int layer)
			{
				foreach (Tile tile in Tiles)
				{
					if (tile.Paint.ContainsKey(layer))
					{
						continue;
					}
					TerrainData data = tile.Terrain.terrainData;
					int res = tile.AlphaResolution;
					if (layer >= data.alphamapLayers || res <= 0)
					{
						tile.Paint[layer] = null;
						continue;
					}
					float[,,] maps = data.GetAlphamaps(0, 0, res, res);
					var flat = new float[res * res];
					for (int y = 0; y < res; y++)
					{
						for (int x = 0; x < res; x++)
						{
							flat[y * res + x] = maps[y, x, layer];
						}
					}
					tile.Paint[layer] = flat;
				}
			}

			private Tile At(float x, float z)
			{
				foreach (Tile t in Tiles)
				{
					if (x >= t.Origin.x && z >= t.Origin.z && x <= t.Origin.x + t.Size.x && z <= t.Origin.z + t.Size.z)
					{
						return t;
					}
				}
				return null;
			}

			public bool TryHeight(float x, float z, out float height)
			{
				height = 0f;
				Tile t = At(x, z);
				if (t == null)
				{
					return false;
				}
				if (t.Solid != null)
				{
					int hx = Mathf.Clamp((int)((x - t.Origin.x) / t.Size.x * t.HoleResolution), 0, t.HoleResolution - 1);
					int hz = Mathf.Clamp((int)((z - t.Origin.z) / t.Size.z * t.HoleResolution), 0, t.HoleResolution - 1);
					if (!t.Solid[hz, hx])
					{
						return false;
					}
				}
				// SampleHeight is the collider's surface, relative to the tile's own position.
				height = t.Terrain.SampleHeight(new Vector3(x, 0f, z)) + t.Origin.y;
				return true;
			}

			/// <summary>A painted channel's weight at a point (nearest texel), 0 off the ground.</summary>
			public float Paint(float x, float z, int layer)
			{
				Tile t = At(x, z);
				if (t == null || layer < 0 || !t.Paint.TryGetValue(layer, out float[] flat) || flat == null)
				{
					return 0f;
				}
				int res = t.AlphaResolution;
				int px = Mathf.Clamp(Mathf.RoundToInt((x - t.Origin.x) / t.Size.x * (res - 1)), 0, res - 1);
				int pz = Mathf.Clamp(Mathf.RoundToInt((z - t.Origin.z) / t.Size.z * (res - 1)), 0, res - 1);
				return flat[pz * res + px];
			}
		}

		// ── Assets ────────────────────────────────────────────────────

		/// <summary>The generated meshes and materials one placement references, loaded once each; never made here.</summary>
		private sealed class RockAssets
		{
			private readonly Dictionary<(CliffPiece, int), Mesh> meshes = new Dictionary<(CliffPiece, int), Mesh>();
			private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
			private readonly Func<string, Material, Material> materialFor;

			public RockAssets(Func<string, Material, Material> materialFor)
			{
				this.materialFor = materialFor;
			}

			/// <summary>A piece's mesh at a level, or null when not generated.</summary>
			public Mesh Mesh(in CliffPiece piece, int lod)
			{
				if (!meshes.TryGetValue((piece, lod), out Mesh mesh))
				{
					meshes[(piece, lod)] = mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(in piece, lod));
				}
				return mesh;
			}

			/// <summary>A type's material, or null when not generated.</summary>
			public Material Material(string type)
			{
				if (!materials.TryGetValue(type, out Material material))
				{
					string name = CliffRocks.MaterialName(type);
					material = name != null ? AssetDatabase.LoadAssetAtPath<Material>(ProceduralArtCatalogue.MaterialPath(name)) : null;
					if (materialFor != null && name != null)
					{
						material = materialFor(name, material);
					}
					materials[type] = material;
				}
				return material;
			}
		}
	}
}
#endif
