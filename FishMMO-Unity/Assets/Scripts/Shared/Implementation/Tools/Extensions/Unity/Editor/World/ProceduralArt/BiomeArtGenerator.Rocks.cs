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
	/// boulders: a root <see cref="LODGroup"/> with a <see cref="CapsuleCollider"/>, and a
	/// <c>_Decor</c> twin without one. Icebergs, sea ice and pressure ridges are scene objects: a
	/// root holding a still, non-convex <see cref="MeshCollider"/> from LOD1 (no rigidbody), and a
	/// child "Visual" holding the <see cref="LODGroup"/>. A berg or floe root also carries a
	/// <see cref="WaterFloater"/> whose body is LOD0's measured one and whose target is "Visual",
	/// so the renderers ride the sea while the collider stays where the scene put it.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>The steps <see cref="RockSurfaceSteps"/> and <see cref="RockSteps"/> yield.</summary>
		private static int RockStepCount() => 1 + 1 + RockTypes.All.Length + 3;

		/// <summary>The rock and ice surface maps: one step, before the texture import.</summary>
		private static IEnumerable<string> RockSurfaceSteps(Context c)
		{
			yield return "Rock and ice surfaces";
			foreach (RockType type in RockTypes.All)
			{
				string name = type.Name;
				WriteRockSurface(c, type.Surface, map => RockArtNames.RockSurfaceTexture(name, map));
			}
			foreach (SurfaceRecipe recipe in IceSurfaces.Recipes)
			{
				string name = recipe.Name;
				WriteRockSurface(c, recipe, map => RockArtNames.IceSurfaceTexture(name, map));
			}
		}

		/// <summary>Materials, formations type by type, ice, cliff rocks, then cliff sections: after the legacy rocks.</summary>
		private static IEnumerable<string> RockSteps(Context c)
		{
			yield return "Rock, ice and cliff materials";
			WriteRockMaterials(c);
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
		}

		// ── Surfaces ──────────────────────────────────────────────────

		private static void WriteRockSurface(Context c, SurfaceRecipe recipe, Func<string, string> pathOf)
		{
			int size = ProceduralArtCatalogue.RockSurfaceSize;
			string albedo = pathOf("Albedo"), normal = pathOf("Normal"), mask = pathOf("Mask");
			bool wa = ShouldWrite(c, albedo), wn = ShouldWrite(c, normal), wm = ShouldWrite(c, mask);
			c.Report.TextureBytes += 3 * CompressedBytes(size, size);
			if (!wa && !wn && !wm)
			{
				return;
			}
			SurfaceMaps maps = SurfaceSynth.Generate(in recipe, size, c.Seed);
			if (wa) WritePng(c, albedo, maps.Albedo, maps.Size, maps.Size);
			if (wn) WritePng(c, normal, maps.Normal, maps.Size, maps.Size);
			if (wm) WritePng(c, mask, maps.Mask, maps.Size, maps.Size);
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
				Texture2D albedo = LoadTexture(RockArtNames.RockSurfaceTexture(type.Name, "Albedo"));
				Texture2D normal = LoadTexture(RockArtNames.RockSurfaceTexture(type.Name, "Normal"));
				Texture2D mask = LoadTexture(RockArtNames.RockSurfaceTexture(type.Name, "Mask"));
				WriteMaterial(c, ProceduralArtCatalogue.RockMaterial(type.Name), rock, m => RockLit(m, albedo, normal, mask, 1f));
			}
			foreach (IceMaterialProposal p in IceSurfaces.Materials)
			{
				IceMaterialProposal proposal = p;
				Texture2D albedo = LoadTexture(RockArtNames.IceSurfaceTexture(proposal.Surface, "Albedo"));
				Texture2D normal = LoadTexture(RockArtNames.IceSurfaceTexture(proposal.Surface, "Normal"));
				Texture2D mask = LoadTexture(RockArtNames.IceSurfaceTexture(proposal.Surface, "Mask"));
				WriteMaterial(c, RockArtNames.IceMaterial(proposal.Surface), rock, m => IceLit(m, proposal, albedo, normal, mask));
			}
			// Cliff rocks wear their rock type's formation material (or glacier ice): nothing of their own.
		}

		/// <summary>The legacy rock material's setup, with a texture scale (cliffs draw rock at the terrain's size).</summary>
		private static void RockLit(Material m, Texture2D albedo, Texture2D normal, Texture2D mask, float scale)
		{
			m.SetTexture("_BaseMap", albedo);
			m.SetTextureScale("_BaseMap", new Vector2(scale, scale));
			m.SetColor("_BaseColor", Color.white);
			m.SetTexture("_BumpMap", normal);
			m.SetFloat("_BumpScale", 1f);
			m.EnableKeyword("_NORMALMAP");
			// The surface mask is R metallic, G occlusion, B height, A smoothness: URP Lit's metallic-gloss (R, A) and occlusion (G).
			m.SetTexture("_MetallicGlossMap", mask);
			m.EnableKeyword("_METALLICSPECGLOSSMAP");
			m.SetTexture("_OcclusionMap", mask);
			m.SetFloat("_OcclusionStrength", 1f);
			m.EnableKeyword("_OCCLUSIONMAP");
			m.SetFloat("_Smoothness", 1f);
			m.SetFloat("_Metallic", 0f);
			m.SetFloat("_FishWeatherAmount", 1f);
		}

		/// <summary>
		/// <see cref="IceSurfaces"/>' proposed setup on the Lit fork: Specular workflow at ice's own
		/// 1.8 % reflectance, smoothness from the proposal with the albedo alpha's variation, and a
		/// faint emission of the albedo for the light ice scatters back. Clear coat needs Complex Lit
		/// and is left out.
		/// </summary>
		private static void IceLit(Material m, IceMaterialProposal p, Texture2D albedo, Texture2D normal, Texture2D mask)
		{
			m.SetTexture("_BaseMap", albedo);
			m.SetColor("_BaseColor", Color.white);
			m.SetFloat("_WorkflowMode", 0f);
			m.EnableKeyword("_SPECULAR_SETUP");
			m.SetColor("_SpecColor", p.Specular);
			m.SetFloat("_Smoothness", p.Smoothness);
			m.SetFloat("_SmoothnessTextureChannel", 1f);
			m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
			m.SetTexture("_BumpMap", normal);
			m.SetFloat("_BumpScale", 1f);
			m.EnableKeyword("_NORMALMAP");
			m.SetTexture("_OcclusionMap", mask);
			m.SetFloat("_OcclusionStrength", 1f);
			m.EnableKeyword("_OCCLUSIONMAP");
			if (p.EmissionShare > 0f)
			{
				m.SetTexture("_EmissionMap", albedo);
				m.SetColor("_EmissionColor", new Color(p.EmissionShare, p.EmissionShare, p.EmissionShare, 1f));
				m.EnableKeyword("_EMISSION");
				m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			}
			m.SetFloat("_FishWeatherAmount", 1f);
		}

		// ── Props: formations, ice boulders, seracs ───────────────────

		private static void WriteFormations(Context c, RockType type)
		{
			Material material = MaterialOrNull(c, RockArtNames.RockTypeMaterial(in type));
			int[] res = ProceduralArtCatalogue.FormationResolution;
			foreach (FormationShape s in type.Shapes)
			{
				FormationShape shape = s;
				for (int v = 0; v < RockTypes.VariantCount; v++)
				{
					var meshes = new Mesh[res.Length];
					for (int lod = 0; lod < res.Length; lod++)
					{
						MeshBuilder b = RockFormations.Build(in type, in shape, res[lod], v, c.Seed);
						meshes[lod] = WriteMesh(c, b, RockArtNames.FormationMesh(type.Name, shape.Name, v, lod), true);
					}
					WriteProp(c, RockArtNames.FormationPrefab(type.Name, shape.Name, v), meshes, material);
				}
			}
		}

		/// <summary>A tree-channel prop: LODGroup root with a mesh collider on its lowest level (whether it collides in a scene is the bake's call, by size).</summary>
		private static void WriteProp(Context c, string prefab, Mesh[] meshes, Material material)
		{
			Material[][] mats = LodMaterials(meshes, material);
			ShadowCastingMode[] shadows = LodShadows(meshes.Length);
			float[] heights = ProceduralArtCatalogue.BoulderLodHeights;
			WritePrefab(c, prefab, root =>
			{
				Lods(root, meshes, mats, heights, shadows);
				AddBoulderCollider(root, meshes);
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
			foreach (string style in CliffSections.Styles)
			{
				for (int v = 0; v < CliffSections.VariantCount; v++)
				{
					bool due = false;
					for (int lod = 0; lod < CliffSections.LevelCount; lod++)
					{
						due |= ShouldWrite(c, ProceduralArtCatalogue.MeshPath(CliffSections.MeshName(style, v, lod)));
					}
					if (!due)
					{
						continue;
					}
					MeshBuilder[] levels = CliffSections.BuildMeshes(style, v, c.Seed, out _);
					for (int lod = 0; lod < levels.Length; lod++)
					{
						WriteMesh(c, levels[lod], CliffSections.MeshName(style, v, lod), true);
					}
				}
			}
		}
	}
}
#endif
