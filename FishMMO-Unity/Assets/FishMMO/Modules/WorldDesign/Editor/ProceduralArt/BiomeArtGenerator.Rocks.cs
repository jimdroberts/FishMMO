#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Water;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Rock formations, ice and cliff rocks: their surface textures, materials, meshes and prefabs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two hooks in <see cref="StepsOf"/>: <see cref="RockSurfaceSteps"/> before the texture import
	/// (so the shared import step imports these PNGs with the rest) and <see cref="RockSteps"/> after
	/// the legacy rocks. Everything goes through the generator's own writers, so payload GUIDs,
	/// fileIDs and the ledger behave exactly as for every other generated asset.
	/// </para>
	/// <para>
	/// <b>Prefabs.</b> Formations, ice boulders and seracs are tree-channel props like the legacy
	/// boulders: a root <see cref="LODGroup"/> with a mesh collider from the last level (none for a
	/// colliderless formation), one material per submesh. The mineral, ice and alien formations
	/// (2026-10-10) are formations like any other; snow and ice types wear ice's material set-up. Icebergs, sea ice and pressure ridges are scene objects: a
	/// root holding a still, non-convex <see cref="MeshCollider"/> from LOD1 (no rigidbody), and a
	/// child "Visual" holding the <see cref="LODGroup"/>. A berg or floe root also carries a
	/// <see cref="WaterFloater"/> whose body is LOD0's measured one and whose target is "Visual",
	/// so the renderers ride the sea while the collider stays where the scene put it.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>The steps <see cref="RockSurfaceSteps"/> and <see cref="RockSteps"/> yield.</summary>
		private static int RockStepCount() => 1 + 1 + ProceduralArtCatalogue.AllRockMaterials.Length + RockTypes.All.Length + 3;

		/// <summary>The rock and ice surface maps: one step, before the texture import.</summary>
		private static IEnumerable<string> RockSurfaceSteps(Context c)
		{
			yield return "Rock and ice surfaces";
			// Written as the rock and ice look arrays (ProceduralArtLookArrays, 2026-10-28): every surface when its set's arrays are
			// due, none when they are not.
			bool rocks = LookArraysDue(c, ProceduralArtLookArrays.RockSetName);
			foreach (RockType type in RockTypes.All)
			{
				string name = type.Name;
				WriteRockSurface(c, type.Surface, map => RockArtNames.RockSurfaceTexture(name, map), rocks);
			}
			WriteLookArrays(c, ProceduralArtLookArrays.RockSetName);
			bool ice = LookArraysDue(c, ProceduralArtLookArrays.IceSetName);
			foreach (SurfaceRecipe recipe in IceSurfaces.Recipes)
			{
				string name = recipe.Name;
				WriteRockSurface(c, recipe, map => RockArtNames.IceSurfaceTexture(name, map), ice);
			}
			WriteLookArrays(c, ProceduralArtLookArrays.IceSetName);
		}

		/// <summary>
		/// Materials, stones material by material, formations type by type, ice, cliff rocks, then cliff sections: after
		/// the shared materials step, which writes the stones' own materials (a stone wearing its rock type's waits for
		/// this one's).
		/// </summary>
		private static IEnumerable<string> RockSteps(Context c)
		{
			yield return "Rock, ice and cliff materials";
			WriteRockMaterials(c);
			foreach (RockMaterialSpec stone in ProceduralArtCatalogue.AllRockMaterials)
			{
				// "Rocks" is the Rocks family's step (ProceduralArtFamilies.FamiliesOfStep).
				yield return "Rocks: " + stone.Name;
				WriteStones(c, stone);
			}
			foreach (RockType type in RockTypes.All)
			{
				yield return "Rock formations: " + type.Name;
				WriteFormations(c, type);
			}
			yield return "Ice";
			WriteIce(c);
			yield return "Cliff rocks";
			WriteCliffRocks(c);
			yield return "Cliff sections";
			WriteCliffSections(c);
			ReportRockOrphans(c);
		}

		// ── Surfaces ──────────────────────────────────────────────────

		/// <param name="arraysDue">Whether the surface's look arrays are written this run: its maps go into them, never a PNG of their own.</param>
		private static void WriteRockSurface(Context c, SurfaceRecipe recipe, Func<string, string> pathOf, bool arraysDue)
		{
			int size = ProceduralArtCatalogue.RockSurfaceSize;
			string albedo = pathOf("Albedo"), normal = pathOf("Normal"), mask = pathOf("Mask");
			c.Report.TextureBytes += 3 * CompressedBytes(size, size);
			if (!arraysDue)
			{
				return;
			}
			SurfaceMaps maps = SurfaceSynth.Generate(in recipe, size, c.Seed);
			PutSurfaceMap(c, albedo, maps.Albedo, maps.Size);
			PutSurfaceMap(c, normal, maps.Normal, maps.Size);
			PutSurfaceMap(c, mask, maps.Mask, maps.Size);
		}

		/// <summary>A surface map into its look array; a map no array takes (none today) as its own PNG, as before.</summary>
		private static void PutSurfaceMap(Context c, string path, Color32[] pixels, int size)
		{
			if (!PutLook(c, path, pixels, size, size) && ShouldWrite(c, path))
			{
				WritePng(c, path, pixels, size, size);
			}
		}

		// ── Materials ─────────────────────────────────────────────────

		private static void WriteRockMaterials(Context c)
		{
			Shader rock = RockShader;
			if (rock == null)
			{
				c.Report.Problems.Add("FishMMO/Weather Lit and URP Lit were not found; rock, ice and cliff materials were not written.");
				return;
			}
			foreach (RockType t in RockTypes.All)
			{
				RockType type = t;
				if (!RockArtNames.HasOwnMaterial(in type))
				{
					continue; // Wears its legacy rock material (Granite: Rock_Grey), written with the legacy rocks.
				}
				string albedo = RockArtNames.RockSurfaceTexture(type.Name, "Albedo");
				string normal = RockArtNames.RockSurfaceTexture(type.Name, "Normal");
				string mask = RockArtNames.RockSurfaceTexture(type.Name, "Mask");
				if (!string.IsNullOrEmpty(type.Ice.Surface))
				{
					// Snow and ice (Firn, WaterIce): ice's own set-up over the type's surface.
					IceMaterialProposal ice = type.Ice;
					WriteMaterial(c, ProceduralArtCatalogue.RockMaterial(type.Name), rock, m => IceLit(c, m, ice, albedo, normal, mask));
					continue;
				}
				// Moss as the stone allows (MossAmounts); the scene's dampness decides how much of it grows.
				float moss = RegionalRocks.MossAmount(type.Name) ?? MossAmounts.ForRockType(type.Name);
				WriteMaterial(c, ProceduralArtCatalogue.RockMaterial(type.Name), rock, m => RockLit(c, m, albedo, normal, mask, 1f, moss));
			}
			foreach (IceMaterialProposal p in IceSurfaces.Materials)
			{
				IceMaterialProposal proposal = p;
				string albedo = RockArtNames.IceSurfaceTexture(proposal.Surface, "Albedo");
				string normal = RockArtNames.IceSurfaceTexture(proposal.Surface, "Normal");
				string mask = RockArtNames.IceSurfaceTexture(proposal.Surface, "Mask");
				WriteMaterial(c, RockArtNames.IceMaterial(proposal.Surface), rock, m => IceLit(c, m, proposal, albedo, normal, mask));
			}
			// Cliff rocks wear their rock type's formation material (or glacier ice): nothing of their own.
		}

		/// <summary>The legacy rock material's setup, with a texture scale (cliffs draw rock at the terrain's size) and the
		/// moss it may grow (FishMoss.hlsl's _FishMoss, MossAmounts; 0 none). The maps by their payload paths: slices of their look
		/// arrays (<see cref="SetSurfaceMap"/>).</summary>
		private static void RockLit(Context c, Material m, string albedo, string normal, string mask, float scale, float moss = 0f)
		{
			SetSurfaceMap(c, m, "_BaseMap", albedo);
			m.SetTextureScale("_BaseMap", new Vector2(scale, scale));
			m.SetColor("_BaseColor", Color.white);
			SetSurfaceMap(c, m, "_BumpMap", normal);
			m.SetFloat("_BumpScale", 1f);
			m.EnableKeyword("_NORMALMAP");
			// The surface mask is R metallic, G occlusion, B height, A smoothness: URP Lit's metallic-gloss (R, A) and occlusion (G).
			SetSurfaceMap(c, m, "_MetallicGlossMap", mask);
			m.EnableKeyword("_METALLICSPECGLOSSMAP");
			SetSurfaceMap(c, m, "_OcclusionMap", mask);
			m.SetFloat("_OcclusionStrength", 1f);
			m.EnableKeyword("_OCCLUSIONMAP");
			m.SetFloat("_Smoothness", 1f);
			m.SetFloat("_Metallic", 0f);
			m.SetFloat("_FishWeatherAmount", 1f);
			m.SetFloat("_FishMoss", moss);
		}

		/// <summary>
		/// <see cref="IceSurfaces"/>' proposed setup on the Lit fork: Specular workflow at ice's own
		/// 1.8 % reflectance, smoothness from the proposal with the albedo alpha's variation, and a
		/// faint emission of the albedo for the light ice scatters back. Clear coat needs Complex Lit
		/// and is left out.
		/// </summary>
		private static void IceLit(Context c, Material m, IceMaterialProposal p, string albedo, string normal, string mask)
		{
			SetSurfaceMap(c, m, "_BaseMap", albedo);
			m.SetColor("_BaseColor", Color.white);
			m.SetFloat("_WorkflowMode", 0f);
			m.EnableKeyword("_SPECULAR_SETUP");
			m.SetColor("_SpecColor", p.Specular);
			m.SetFloat("_Smoothness", p.Smoothness);
			m.SetFloat("_SmoothnessTextureChannel", 1f);
			m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
			SetSurfaceMap(c, m, "_BumpMap", normal);
			m.SetFloat("_BumpScale", 1f);
			m.EnableKeyword("_NORMALMAP");
			SetSurfaceMap(c, m, "_OcclusionMap", mask);
			m.SetFloat("_OcclusionStrength", 1f);
			m.EnableKeyword("_OCCLUSIONMAP");
			if (p.EmissionShare > 0f)
			{
				SetSurfaceMap(c, m, "_EmissionMap", albedo);
				m.SetColor("_EmissionColor", new Color(p.EmissionShare, p.EmissionShare, p.EmissionShare, 1f));
				m.EnableKeyword("_EMISSION");
				m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			}
			m.SetFloat("_FishWeatherAmount", 1f);
			// Ice holds no moss.
			m.SetFloat("_FishMoss", 0f);
		}

		// ── Stones: each material's boulders, small rocks and pebbles ─

		/// <summary>
		/// A stone material's own shapes (<see cref="StoneStyles"/>, 2026-10-16): its three boulders, tree-channel props
		/// with a mesh collider from the last level as before, and its small rocks and pebbles, one-mesh detail prefabs
		/// casting no shadow as before. The prefab names are the ones they always had.
		/// </summary>
		private static void WriteStones(Context c, RockMaterialSpec stone)
		{
			Material material = MaterialOrNull(c, ProceduralArtCatalogue.StoneMaterial(in stone));
			StoneSet set = StoneStyles.For(stone.Name);
			if (!StoneStyles.Has(stone.Name))
			{
				c.Report.Problems.Add($"Stone '{stone.Name}' has no shapes of its own in StoneStyles; it was built with granite's.");
			}
			int[] res = ProceduralArtCatalogue.BoulderResolution;
			foreach (RockShape slot in ProceduralArtCatalogue.BoulderShapes)
			{
				StoneShape shape = set.Boulder(slot.Name);
				var meshes = new Mesh[res.Length];
				for (int lod = 0; lod < res.Length; lod++)
				{
					string name = ProceduralArtCatalogue.BoulderMesh(stone.Name, slot.Name, lod);
					MeshBuilder built = ShouldWrite(c, ProceduralArtCatalogue.MeshPath(name)) ? StoneMeshes.Build(in shape, res[lod], c.Seed) : null;
					meshes[lod] = WriteOrLoadMesh(c, built, name, true);
				}
				WriteProp(c, ProceduralArtCatalogue.BoulderPrefab(stone.Name, slot.Name), meshes, material);
			}
			// The small rocks and pebbles in every variant the stone cycles (RockVariantCounts.StoneDetails, 2026-10-26): variant 0 is
			// the mesh it always had, the rest bare-mesh children the GPU detail scatter picks one of per instance.
			int variants = RockVariantCounts.StoneDetailsOf(stone.Name);
			var rocks = new Mesh[variants];
			var pebbles = new Mesh[variants];
			for (int v = 0; v < variants; v++)
			{
				string rocksName = ProceduralArtCatalogue.SmallRocksVariantMesh(stone.Name, v);
				string pebblesName = ProceduralArtCatalogue.PebblesVariantMesh(stone.Name, v);
				rocks[v] = WriteOrLoadMesh(c, ShouldWrite(c, ProceduralArtCatalogue.MeshPath(rocksName)) ? StoneMeshes.BuildGroup(StoneStyles.DetailVariant(set.Rocks, v), c.Seed) : null, rocksName, true);
				pebbles[v] = WriteOrLoadMesh(c, ShouldWrite(c, ProceduralArtCatalogue.MeshPath(pebblesName)) ? StoneMeshes.BuildGroup(StoneStyles.DetailVariant(set.Pebbles, v), c.Seed) : null, pebblesName, true);
			}
			WritePrefab(c, ProceduralArtCatalogue.SmallRocksPrefab(stone.Name), root => AddStoneDetail(root, rocks, material));
			WritePrefab(c, ProceduralArtCatalogue.PebblesPrefab(stone.Name), root => AddStoneDetail(root, pebbles, material));
		}

		/// <summary>
		/// A stone detail prefab: the root drawing variant 0 casting no shadow, as before, and each further variant on a bare
		/// MeshFilter child (<see cref="ProceduralArtCatalogue.DetailVariantChild"/>), which only the GPU detail scatter reads.
		/// </summary>
		private static void AddStoneDetail(GameObject root, Mesh[] variants, Material material)
		{
			AddRenderer(root, variants[0], new[] { material }, ShadowCastingMode.Off);
			for (int v = 1; v < variants.Length; v++)
			{
				GameObject child = Child(root, ProceduralArtCatalogue.DetailVariantChild(v));
				Ensure<MeshFilter>(child).sharedMesh = variants[v];
			}
		}

		// ── Orphans: generated rock and stone files nothing makes any more ─

		/// <summary>
		/// Lists (never deletes) the generated rock, stone, ice and cliff files on disk that no family makes any more
		/// (<see cref="ProceduralArtOrphans"/>: the shared boulders, small rocks and pebbles I6b replaced, a variant a lowered
		/// count drops…), so the owner can remove them by hand. Logged once a run, after the cliff sections.
		/// </summary>
		private static void ReportRockOrphans(Context c)
		{
			List<string> orphans = ProceduralArtOrphans.RockOrphansOnDisk();
			if (orphans.Count == 0)
			{
				return;
			}
			Debug.LogWarning($"[Biome art] {orphans.Count} generated rock/stone file(s) are no longer made by any family (nothing was deleted; delete them by hand if wanted):\n" + string.Join("\n", orphans));
		}

		// ── Props: formations, ice boulders, seracs ───────────────────

		private static void WriteFormations(Context c, RockType type)
		{
			Material material = MaterialOrNull(c, RockArtNames.RockTypeMaterial(in type));
			int[] res = ProceduralArtCatalogue.FormationResolution;
			int variants = RockVariantCounts.Of(type.Name);
			foreach (FormationShape s in type.Shapes)
			{
				FormationShape shape = s;
				// A shape dressed in another surface, or carrying a second body (a glacier table's boulder, a frost
				// cap), wears one material per submesh; every other shape its type's, as before.
				Material[] bySubmesh = { material };
				if (shape.Dress != null || shape.Cap != null)
				{
					bySubmesh = Array.ConvertAll(RockArtNames.FormationMaterials(in type, in shape), name => MaterialOrNull(c, name));
				}
				// The due levels of every variant (the rock's own count, RockVariantCounts) on all cores; a current one is loaded, not rebuilt.
				var due = new List<(int variant, int lod)>();
				for (int v = 0; v < variants; v++)
				{
					for (int lod = 0; lod < res.Length; lod++)
					{
						if (ShouldWrite(c, ProceduralArtCatalogue.MeshPath(RockArtNames.FormationMesh(type.Name, shape.Name, v, lod))))
						{
							due.Add((v, lod));
						}
					}
				}
				RockType rockType = type;
				int seed = c.Seed;
				MeshBuilder[] built = BuildAll(due, d => RockFormations.Build(in rockType, in shape, res[d.lod], d.variant, seed));
				for (int v = 0; v < variants; v++)
				{
					var meshes = new Mesh[res.Length];
					for (int lod = 0; lod < res.Length; lod++)
					{
						int at = due.IndexOf((v, lod));
						meshes[lod] = WriteOrLoadMesh(c, at >= 0 ? built[at] : null, RockArtNames.FormationMesh(type.Name, shape.Name, v, lod), true);
					}
					WriteProp(c, RockArtNames.FormationPrefab(type.Name, shape.Name, v), meshes, bySubmesh, !shape.Colliderless);
				}
			}
		}

		/// <summary>A tree-channel prop: LODGroup root with a mesh collider on its lowest level (whether it collides in a scene is the bake's call, by size).</summary>
		private static void WriteProp(Context c, string prefab, Mesh[] meshes, Material material) => WriteProp(c, prefab, meshes, new[] { material }, true);

		/// <summary>
		/// A tree-channel prop wearing one material per submesh, with the boulders' mesh collider unless it is ground
		/// cover a player walks over (<see cref="FormationShape.Colliderless"/>: sastrugi, salt polygons, frost flowers).
		/// </summary>
		private static void WriteProp(Context c, string prefab, Mesh[] meshes, Material[] bySubmesh, bool collider)
		{
			Material[][] mats = LodMaterials(meshes, bySubmesh);
			ShadowCastingMode[] shadows = LodShadows(meshes.Length);
			float[] heights = ProceduralArtCatalogue.BoulderLodHeights;
			WritePrefab(c, prefab, root =>
			{
				Lods(root, meshes, mats, heights, shadows);
				if (collider)
				{
					AddBoulderCollider(root, meshes);
				}
			});
		}

		/// <summary>Per level, one material per submesh: <paramref name="bySubmesh"/> in order, its last repeated.</summary>
		private static Material[][] LodMaterials(Mesh[] meshes, params Material[] bySubmesh)
		{
			var mats = new Material[meshes.Length][];
			for (int i = 0; i < meshes.Length; i++)
			{
				int count = meshes[i] != null ? Mathf.Max(1, meshes[i].subMeshCount) : bySubmesh.Length;
				mats[i] = new Material[count];
				for (int s = 0; s < count; s++)
				{
					mats[i][s] = bySubmesh[Mathf.Min(s, bySubmesh.Length - 1)];
				}
			}
			return mats;
		}

		/// <summary>Every level casts shadows but the last, as the legacy boulders do.</summary>
		private static ShadowCastingMode[] LodShadows(int count)
		{
			var shadows = new ShadowCastingMode[count];
			for (int i = 0; i < count; i++)
			{
				shadows[i] = i < count - 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
			}
			return shadows;
		}

		// ── Ice ───────────────────────────────────────────────────────

		private static void WriteIce(Context c)
		{
			int[] res = IceMeshes.Resolution;
			foreach (IceBoulderShape s in IceMeshes.Boulders)
			{
				IceBoulderShape shape = s;
				var meshes = new Mesh[res.Length];
				for (int lod = 0; lod < res.Length; lod++)
				{
					meshes[lod] = WriteMesh(c, IceMeshes.BuildBoulder(in shape, res[lod], c.Seed), IceMeshes.IceBoulderMesh(in shape, lod), true);
				}
				WriteProp(c, RockArtNames.IceBoulderPrefab(in shape), meshes, MaterialOrNull(c, RockArtNames.IceMaterial(ProceduralArtCatalogue.IceBoulderSurface(in shape))));
			}
			Material serac = MaterialOrNull(c, RockArtNames.IceMaterial(ProceduralArtCatalogue.SeracSurface));
			foreach (SeracShape s in IceMeshes.Seracs)
			{
				SeracShape shape = s;
				var meshes = new Mesh[res.Length];
				for (int lod = 0; lod < res.Length; lod++)
				{
					meshes[lod] = WriteMesh(c, IceMeshes.BuildSerac(in shape, res[lod], c.Seed), IceMeshes.SeracMesh(in shape, lod), true);
				}
				WriteProp(c, RockArtNames.SeracPrefab(in shape), meshes, serac);
			}

			var berg = new Material[ProceduralArtCatalogue.IcebergSurfaces.Length];
			for (int i = 0; i < berg.Length; i++)
			{
				berg[i] = MaterialOrNull(c, RockArtNames.IceMaterial(ProceduralArtCatalogue.IcebergSurfaces[i]));
			}
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				IcebergShape shape = s;
				var meshes = new Mesh[res.Length];
				FloatingBody body = default;
				for (int lod = 0; lod < res.Length; lod++)
				{
					FloatingIce ice = IceMeshes.BuildIceberg(in shape, res[lod], c.Seed);
					if (lod == 0)
					{
						body = ice.Body;
					}
					meshes[lod] = WriteMesh(c, ice.Mesh, IceMeshes.IcebergMesh(in shape, lod), true);
				}
				WriteFloatingIce(c, RockArtNames.IcebergPrefab(in shape), meshes, berg, body);
			}

			Material sea = MaterialOrNull(c, RockArtNames.IceMaterial(ProceduralArtCatalogue.SeaIceSurface));
			foreach (SeaIceShape s in IceMeshes.SeaIce)
			{
				SeaIceShape shape = s;
				var meshes = new Mesh[res.Length];
				FloatingBody body = default;
				for (int lod = 0; lod < res.Length; lod++)
				{
					FloatingIce ice = IceMeshes.BuildSeaIce(in shape, res[lod], c.Seed);
					if (lod == 0)
					{
						body = ice.Body;
					}
					meshes[lod] = WriteMesh(c, ice.Mesh, IceMeshes.SeaIceMesh(in shape, lod), true);
				}
				WriteFloatingIce(c, RockArtNames.SeaIcePrefab(in shape), meshes, new[] { sea }, body);
			}
			foreach (PressureRidgeShape s in IceMeshes.Ridges)
			{
				PressureRidgeShape shape = s;
				var meshes = new Mesh[res.Length];
				for (int lod = 0; lod < res.Length; lod++)
				{
					// A heap of blocks, which may touch and interpenetrate: not asserted closed.
					meshes[lod] = WriteMesh(c, IceMeshes.BuildPressureRidge(in shape, lod, c.Seed), IceMeshes.RidgeMesh(in shape, lod), false);
				}
				// Frozen into the level ice around it: no floater.
				WriteFloatingIce(c, RockArtNames.RidgePrefab(in shape), meshes, new[] { sea }, null);
			}
		}

		/// <summary>
		/// A floating-ice scene object: a still, non-convex mesh collider from LOD1 on the root, the
		/// renderers on a child "Visual", and — with a body — a <see cref="WaterFloater"/> that moves
		/// "Visual" as the measured body would ride the sea.
		/// </summary>
		private static void WriteFloatingIce(Context c, string prefab, Mesh[] meshes, Material[] bySubmesh, FloatingBody? body)
		{
			Material[][] mats = LodMaterials(meshes, bySubmesh);
			ShadowCastingMode[] shadows = LodShadows(meshes.Length);
			float[] heights = ProceduralArtCatalogue.FloatingIceLodHeights;
			Mesh collision = meshes[Mathf.Min(1, meshes.Length - 1)];
			WritePrefab(c, prefab, root =>
			{
				if (collision != null)
				{
					var collider = Ensure<MeshCollider>(root);
					collider.convex = false;
					collider.sharedMesh = collision;
				}
				GameObject visual = Child(root, "Visual");
				Lods(visual, meshes, mats, heights, shadows);
				if (body.HasValue)
				{
					var floater = Ensure<WaterFloater>(root);
					floater.Body = body.Value;
					floater.Target = visual.transform;
				}
			});
		}

		// ── Cliff rocks ───────────────────────────────────────────────

		/// <summary>
		/// Every cliff rock mesh the placer may load (<see cref="CliffRocks.AllMeshes"/>): rock formations
		/// built at cliff size, every level validated as closed (the collider is the last level). Files
		/// that are current are not rebuilt.
		/// </summary>
		private static void WriteCliffRocks(Context c)
		{
			foreach ((CliffPiece piece, int lod) in CliffRocks.AllMeshes())
			{
				string name = CliffRocks.MeshName(in piece, lod);
				if (!ShouldWrite(c, ProceduralArtCatalogue.MeshPath(name)))
				{
					continue;
				}
				WriteMesh(c, CliffRocks.Build(in piece, lod, c.Seed), name, true);
			}
		}

		// ── Cliff sections ────────────────────────────────────────────

		/// <summary>
		/// Every cliff section level (<see cref="CliffSections.AllMeshes"/>): a variant's levels are built together (one
		/// field, one net) when any of them is due, each validated as closed. Files that are current are not rebuilt.
		/// </summary>
		private static void WriteCliffSections(Context c)
		{
			var due = new List<(string style, int variant)>();
			foreach (string style in CliffSections.Styles)
			{
				for (int v = 0; v < CliffSections.VariantCount; v++)
				{
					for (int lod = 0; lod < CliffSections.LevelCount; lod++)
					{
						if (ShouldWrite(c, ProceduralArtCatalogue.MeshPath(CliffSections.MeshName(style, v, lod))))
						{
							due.Add((style, v));
							break;
						}
					}
				}
			}
			// The fields and their decimation on all cores; written here, in order.
			int seed = c.Seed;
			var seconds = new double[due.Count];
			var indices = new List<int>();
			for (int i = 0; i < due.Count; i++)
			{
				indices.Add(i);
			}
			MeshBuilder[][] built = BuildAll(indices, i =>
			{
				long started = System.Diagnostics.Stopwatch.GetTimestamp();
				MeshBuilder[] levels = CliffSections.BuildMeshes(due[i].style, due[i].variant, seed, out _);
				seconds[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency;
				return levels;
			});
			if (due.Count > 0)
			{
				// The slowest variants bound this step's time on many cores; named so the next optimisation knows where to look.
				var order = new List<int>(indices);
				order.Sort((a, b) => seconds[b].CompareTo(seconds[a]));
				var slowest = new System.Text.StringBuilder();
				for (int k = 0; k < Math.Min(6, order.Count); k++)
				{
					slowest.Append($" {due[order[k]].style} {due[order[k]].variant} {seconds[order[k]]:0.0}s;");
				}
				double total = 0;
				foreach (double t in seconds)
				{
					total += t;
				}
				Debug.Log($"[Biome art] Cliff sections: {due.Count} variants, {total:0.0}s of building on {Workers} workers; slowest:{slowest}");
			}
			for (int i = 0; i < due.Count; i++)
			{
				for (int lod = 0; lod < built[i].Length; lod++)
				{
					WriteMesh(c, built[i][lod], CliffSections.MeshName(due[i].style, due[i].variant, lod), true);
				}
			}
		}
	}
}
#endif
