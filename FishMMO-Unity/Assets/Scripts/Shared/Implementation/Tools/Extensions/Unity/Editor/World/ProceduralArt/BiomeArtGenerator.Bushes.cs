#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The shrubs (<see cref="BushMeshes"/>): three meshes, a material and a detail prefab a species.</summary>
	public static partial class BiomeArtGenerator
	{
		/// <summary>How far a shrub is pushed into the ground when no scatter rule says (<c>_GroundSink</c>): a woody root crown.</summary>
		private static readonly Vector2 BushSinkFallback = new Vector2(0.04f, 0.1f);

		/// <summary>
		/// A shrub prefab: a detail prototype (the scatter places it on a detail layer, so it has no collider, and players
		/// walk into it and hide), whose root draws level 0 wherever Unity or the CPU chunk renderer draws details, with
		/// its reduced levels on the children <c>LOD1</c> and <c>LOD2</c> as bare MeshFilters: only the client's GPU detail
		/// scatter reads them (Unity's detail prototypes take one mesh and validate only that it has one material).
		/// </summary>
		/// <remarks>
		/// The material fades by nothing of its own (<c>_DistanceFade</c> 0): the detail fade follows each player's grass
		/// distance, and a bush must stand at the same distance for everyone, so the GPU scatter dissolves it at its own
		/// fixed draw distance instead (the LOD cross-fade's dither, DetailScatterSettings).
		/// </remarks>
		private static void WriteBush(Context c, BushSpecies species)
		{
			var meshes = new Mesh[BushMeshes.Levels];
			for (int lod = 0; lod < BushMeshes.Levels; lod++)
			{
				MeshBuilder b = BushMeshes.Build(in species, lod, c.Seed);
				meshes[lod] = WriteMesh(c, b, ProceduralArtCatalogue.BushMesh(species.Name, lod), false);
			}

			string name = ProceduralArtCatalogue.BushPrefab(species.Name);
			Texture2D atlas = LoadTexture(ProceduralArtCatalogue.AtlasPath);
			Vector2 sink = BushSinkRange(name);
			Material material = WriteMaterial(c, name, VegetationShader, m =>
			{
				Vegetation(m, atlas, 0.45f, true, 0.3f, 0.03f, 0.45f, new Color(0.92f, 0.97f, 0.92f), new Color(0.8f, 0.76f, 0.58f), 0.25f, 0.15f,
					species.Deciduous, distanceFade: FadeNone, groundSink: sink, tintPatchMetres: 24f);
				BushVariation(m);
			});

			WritePrefab(c, name, root =>
			{
				AddRenderer(root, meshes[0], new[] { material }, ShadowCastingMode.On);
				for (int lod = 1; lod < BushMeshes.Levels; lod++)
				{
					GameObject child = Child(root, ProceduralArtCatalogue.BushLevelChild(lod));
					Ensure<MeshFilter>(child).sharedMesh = meshes[lod];
				}
			});
		}

		/// <summary>
		/// How far each bush differs from the next (VegVary): a few degrees of lean and twist, crowns and heights a sixth
		/// either way; each stem swung and drooped only a little, because a bush's stems share one closed crown and turning
		/// them far opens gaps in the cover; every stem always drawn, and leaves from about two thirds of the spares to all.
		/// </summary>
		private static void BushVariation(Material m)
		{
			m.SetFloat("_VaryLean", 4f);
			m.SetFloat("_VaryTwist", 20f);
			m.SetFloat("_VaryCrown", 0.16f);
			m.SetFloat("_VaryHeight", 0.16f);
			m.SetFloat("_VaryGirth", 0f);
			m.SetFloat("_VaryPartSwing", 6f);
			m.SetFloat("_VaryPartDroop", 4f);
			m.SetFloat("_VaryPartLength", 0.08f);
			m.SetVector("_VaryFullness", new Vector4(1f, 0.6f, 0f, 0f));
			m.SetVector("_VaryColour", new Vector4(0.1f, 0.06f, 0.08f, 0.08f));
		}

		/// <summary>The sink the spec's rules give a shrub (widened to cover them all), or the fallback.</summary>
		private static Vector2 BushSinkRange(string prefab)
		{
			bool any = false;
			var range = new Vector2(float.MaxValue, 0f);
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					if (rule.Prefabs == null || Array.IndexOf(rule.Prefabs, prefab) < 0 || rule.Sink.y <= 0f)
					{
						continue;
					}
					any = true;
					range.x = Mathf.Min(range.x, Mathf.Max(0f, Mathf.Min(rule.Sink.x, rule.Sink.y)));
					range.y = Mathf.Max(range.y, Mathf.Max(rule.Sink.x, rule.Sink.y));
				}
			}
			return any ? range : BushSinkFallback;
		}
	}
}
#endif
