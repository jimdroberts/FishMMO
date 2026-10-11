#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The deadwood props (<see cref="DeadwoodMeshes"/>): meshes, materials and prefabs, written in the generator's
	/// "Deadwood" step (after the trees, so the barks, rock surfaces and materials they wear exist).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Uses the generator's own writers (WriteMesh, WriteMaterial, WritePrefab, Lods, AddBoulderCollider), so GUIDs, file
	/// IDs and the ledger behave as for every other generated asset, and every path written is listed in
	/// ProceduralArtCatalogue.DeadwoodPayloadPaths / DeadwoodWrapperPaths / DeadwoodPrefabNames.
	/// </para>
	/// <para>
	/// <b>A prop, like a boulder.</b> Each prefab is a root <see cref="LODGroup"/> over three levels, each level a renderer
	/// with two materials (the bark, the exposed wood), and a non-convex mesh collider on the root from the last level —
	/// the baked props merge these per streamed chunk, so a log is walked on and stood behind as drawn.
	/// </para>
	/// <para>
	/// <b>Materials of their own, held still.</b> The trees' bark materials vary every tree's lean, twist, height and girth
	/// about its foot (VegVary); a log lying across its pivot would lift one end off the ground and slide away from its
	/// collider. So the deadwood wears its own materials — the same bark textures on the vegetation shader, faded as a
	/// tree is (<c>_DistanceFade</c> tree), with every shape variation zeroed and only the colour varied.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>Screen heights at which a deadwood prop steps to its next level; the last one culls (as a boulder's).</summary>
		private static readonly float[] DeadwoodLodHeights = { 0.25f, 0.08f, 0.01f };

		static partial void WriteDeadwood(Context c)
		{
			Shader veg = VegetationShader;
			if (veg == null)
			{
				c.Report.Problems.Add("FishMMO/Vegetation and URP Lit were not found; the deadwood was not written.");
				return;
			}
			var white = Color.white;
			foreach (string family in DeadwoodBarkFamilies())
			{
				Texture2D albedo = LoadTexture(ProceduralArtCatalogue.BarkTexture(family, "Albedo"));
				Texture2D normal = LoadTexture(ProceduralArtCatalogue.BarkTexture(family, "Normal"));
				WriteMaterial(c, ProceduralArtCatalogue.DeadwoodPrefix + "Bark_" + family, veg, m =>
				{
					Vegetation(m, albedo, 0.5f, false, 0f, 0f, 0f, white, white, 0f, 0f, false, normal, clip: false, expectNormal: true, distanceFade: FadeTree);
					HoldStill(m);
				});
			}
			if (Array.Exists(ProceduralArtCatalogue.Deadwood, d => d.Form == DeadwoodForm.PetrifiedLog))
			{
				// Silicified: the dead tree's bark texture in stone colours (the mesh's), with a little polish.
				Texture2D albedo = LoadTexture(ProceduralArtCatalogue.BarkTexture(Bark.Dead, "Albedo"));
				Texture2D normal = LoadTexture(ProceduralArtCatalogue.BarkTexture(Bark.Dead, "Normal"));
				WriteMaterial(c, ProceduralArtCatalogue.DeadwoodPrefix + "Petrified", veg, m =>
				{
					Vegetation(m, albedo, 0.5f, false, 0f, 0f, 0f, white, white, 0f, 0f, false, normal, clip: false, expectNormal: true, distanceFade: FadeTree);
					HoldStill(m);
					m.SetFloat("_Smoothness", 0.3f);
				});
			}
			Texture2D atlas = LoadTexture(ProceduralArtCatalogue.AtlasPath);
			Material wood = WriteMaterial(c, ProceduralArtCatalogue.DeadwoodWoodMaterial, veg, m =>
			{
				Vegetation(m, atlas, 0.5f, false, 0f, 0f, 0f, white, white, 0f, 0f, false, clip: false, distanceFade: FadeTree);
				HoldStill(m);
			});

			foreach (DeadwoodSpecies d in ProceduralArtCatalogue.Deadwood)
			{
				DeadwoodSpecies species = d;
				var meshes = new Mesh[DeadwoodMeshes.Levels];
				for (int lod = 0; lod < DeadwoodMeshes.Levels; lod++)
				{
					MeshBuilder b = DeadwoodMeshes.Build(in species, lod, c.Seed);
					meshes[lod] = WriteMesh(c, b, ProceduralArtCatalogue.DeadwoodMesh(species.Name, lod), false);
				}
				Material bark = MaterialOrNull(c, ProceduralArtCatalogue.DeadwoodBarkMaterial(in species));
				var materials = new Material[meshes.Length][];
				var shadows = new ShadowCastingMode[meshes.Length];
				for (int i = 0; i < meshes.Length; i++)
				{
					materials[i] = new[] { bark, wood };
					shadows[i] = i < meshes.Length - 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
				}
				WritePrefab(c, ProceduralArtCatalogue.DeadwoodPrefab(species.Name), root =>
				{
					Lods(root, meshes, materials, DeadwoodLodHeights, shadows);
					AddBoulderCollider(root, meshes);
				});
			}
		}

		/// <summary>The bark families the deadwood's own bark materials are made in (every prop's but a petrified log's).</summary>
		private static System.Collections.Generic.IEnumerable<string> DeadwoodBarkFamilies()
		{
			var seen = new System.Collections.Generic.HashSet<string>();
			foreach (DeadwoodSpecies d in ProceduralArtCatalogue.Deadwood)
			{
				if (d.Form != DeadwoodForm.PetrifiedLog && seen.Add(d.BarkFamily))
				{
					yield return d.BarkFamily;
				}
			}
		}

		/// <summary>
		/// No shape variation (VegVary) on a deadwood material: no lean, twist, crown, height or girth change and no part
		/// swing, so the drawn prop stays on its collider and on the ground. Only the colour still varies a little.
		/// </summary>
		private static void HoldStill(Material m)
		{
			m.SetFloat("_VaryLean", 0f);
			m.SetFloat("_VaryTwist", 0f);
			m.SetFloat("_VaryCrown", 0f);
			m.SetFloat("_VaryHeight", 0f);
			m.SetFloat("_VaryGirth", 0f);
			m.SetFloat("_VaryPartSwing", 0f);
			m.SetFloat("_VaryPartDroop", 0f);
			m.SetFloat("_VaryPartLength", 0f);
			m.SetVector("_VaryColour", new Vector4(0.08f, 0.04f, 0.05f, 0.06f));
		}
	}
}
#endif
