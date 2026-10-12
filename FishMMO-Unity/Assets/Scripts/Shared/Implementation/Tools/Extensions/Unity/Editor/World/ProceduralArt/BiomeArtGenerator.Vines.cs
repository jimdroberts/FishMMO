#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The vines (<see cref="VineMeshes"/>, 2026-10-12): their atlas, two materials a species, and a prefab a variant of
	/// every hanging, mat and spill piece and of every trunk climber.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Uses the generator's own writers, so GUIDs, file IDs and the ledger behave as for every other generated asset; every
	/// path written is listed in ProceduralArtCatalogue.VinePayloadPaths / VineWrapperPaths / VinePrefabNames.
	/// </para>
	/// <para>
	/// <b>Placed as props, never collided with.</b> Each prefab is a root <see cref="LODGroup"/> over three renderers and
	/// nothing else: the sites, the cliffs and the tree bake lay them as baked props (drawn instanced on the GPU with the
	/// rocks and trees), and a prop whose prefab has no collider gets no collision (ScenePropBaker). A curtain over a cave's
	/// mouth is walked through.
	/// </para>
	/// <para>
	/// <b>Two materials a species.</b> Both are the vegetation shader with <c>_Clinging</c> set: a vine is not rooted where
	/// its origin is (a curtain hangs from a lintel, a mat stands on a wall), so the ground's colour under its origin and the
	/// contact band where a trunk meets the terrain are not for it — the leaves of a spill lying on the ground would
	/// otherwise be painted as the terrain they lie on. The hanging/mat/spill material holds every shape still (no VegVary: a
	/// curtain leant off true would come away from its lintel) and fades by nothing (a prop has its own draw distance). The
	/// climber's carries the trees' variation figures and fades as a tree does, so it thickens, leans, twists and goes with
	/// the trunk it is placed on, and sways by the bark's own amount.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>Screen heights at which a vine piece steps to its next level; the last one culls.</summary>
		private static readonly float[] VineLodHeights = { 0.25f, 0.08f, 0.012f };

		/// <summary>The atlas, written with the foliage atlas so the import step imports it with the rest.</summary>
		private static void WriteVineAtlas(Context c)
		{
			c.Report.TextureBytes += CompressedBytes(VineAtlas.Width, VineAtlas.Height);
			if (ShouldWrite(c, ProceduralArtCatalogue.VineAtlasPath))
			{
				WritePng(c, ProceduralArtCatalogue.VineAtlasPath, VineAtlas.Generate(c.Seed), VineAtlas.Width, VineAtlas.Height);
			}
		}

		/// <summary>The vegetation material for a vine; <paramref name="climber"/> for one on a tree's trunk.</summary>
		private static void VineMaterial(Material m, Texture2D atlas, in VineSpecies species, bool climber)
		{
			// The bark's own sway (1) for a climber, so it moves with its trunk; a curtain's strands carry their sway in metres hung.
			float sway = climber ? 1f : 0.5f;
			Vegetation(m, atlas, 0.45f, true, sway, 0.025f, 0.4f, new Color(0.94f, 0.98f, 0.94f), new Color(0.82f, 0.76f, 0.56f), 0.18f, 0f,
				species.Deciduous, distanceFade: climber ? FadeTree : FadeNone, tintPatchMetres: 18f);
			m.SetColor("_AutumnColor", species.Autumn);
			m.SetFloat("_Clinging", 1f);
			if (!climber)
			{
				// Held still: the shape variation would turn a curtain off its lintel or a mat off its wall (VegVary). Colour only.
				m.SetFloat("_VaryLean", 0f);
				m.SetFloat("_VaryTwist", 0f);
				m.SetFloat("_VaryCrown", 0f);
				m.SetFloat("_VaryHeight", 0f);
				m.SetFloat("_VaryGirth", 0f);
				m.SetFloat("_VaryPartSwing", 0f);
				m.SetFloat("_VaryPartDroop", 0f);
				m.SetFloat("_VaryPartLength", 0f);
				m.SetVector("_VaryFullness", new Vector4(1f, 1f, 0f, 0f));
				m.SetVector("_VaryColour", new Vector4(0.1f, 0.06f, 0.1f, 0.08f));
			}
		}

		/// <summary>One species: its two materials and a prefab a variant of every piece.</summary>
		private static void WriteVines(Context c, VineSpecies species)
		{
			Shader veg = VegetationShader;
			if (veg == null)
			{
				c.Report.Problems.Add("FishMMO/Vegetation and URP Lit were not found; the vines were not written.");
				return;
			}
			WorldEditorAssets.EnsureFolder(ProceduralArtCatalogue.VinePrefabsFolder);
			Texture2D atlas = LoadTexture(ProceduralArtCatalogue.VineAtlasPath);
			VineSpecies sp = species;
			Material hung = WriteMaterial(c, ProceduralArtCatalogue.VineMaterial(sp.Name), veg, m => VineMaterial(m, atlas, in sp, false));
			WriteMaterial(c, ProceduralArtCatalogue.VineMaterial(sp.Name, true), veg, m => VineMaterial(m, atlas, in sp, true));
			foreach (VinePiece piece in ProceduralArtCatalogue.VinePieces)
			{
				for (int v = 0; v < piece.Variants; v++)
				{
					string name = ProceduralArtCatalogue.VineName(sp.Name, in piece, v);
					var meshes = new Mesh[VineMeshes.Levels];
					for (int lod = 0; lod < meshes.Length; lod++)
					{
						meshes[lod] = WriteMesh(c, VineMeshes.Build(in sp, in piece, v, lod, c.Seed), ProceduralArtCatalogue.VineMesh(name, lod), false);
					}
					WriteVinePrefab(c, name, meshes, hung, VineLodHeights);
				}
			}
		}

		/// <summary>
		/// Every climber: for each tree species vines climb (ProceduralArtCatalogue.VineHosts), its trunk read from the tree's
		/// own level 0 at this seed (the mesh the tree prefab draws), and each vine built up it.
		/// </summary>
		private static void WriteVineClimbers(Context c)
		{
			WorldEditorAssets.EnsureFolder(ProceduralArtCatalogue.VinePrefabsFolder);
			foreach (ProceduralArtCatalogue.VineHost host in ProceduralArtCatalogue.VineHosts)
			{
				TreeSpecies tree = Array.Find(ProceduralArtCatalogue.Trees, t => t.Name == host.Tree);
				if (string.IsNullOrEmpty(tree.Name))
				{
					c.Report.Problems.Add($"Vine host '{host.Tree}' is not a tree species; its climbers were not written.");
					continue;
				}
				MeshBuilder trunkMesh = TreeMeshes.Build(in tree, 0, c.Seed);
				VineMeshes.TrunkProfile trunk = VineMeshes.TrunkProfile.From(trunkMesh);
				if (!trunk.Valid)
				{
					c.Report.Problems.Add($"Tree '{host.Tree}' has no trunk the climbers can read (rings marked round its centre line); its climbers were not written.");
					continue;
				}
				float treeSize = LodSize(trunkMesh.Bounds);
				foreach (string vine in host.Vines)
				{
					if (!ProceduralArtCatalogue.TryVine(vine, out VineSpecies sp))
					{
						c.Report.Problems.Add($"Vine host '{host.Tree}' names '{vine}', which is not a vine species.");
						continue;
					}
					Material material = MaterialOrNull(c, ProceduralArtCatalogue.VineMaterial(vine, true));
					for (int v = 0; v < ProceduralArtCatalogue.ClimberReach.Length; v++)
					{
						string name = ProceduralArtCatalogue.VineClimberName(vine, host.Tree, v);
						var meshes = new Mesh[VineMeshes.Levels];
						float climberSize = 1f;
						for (int lod = 0; lod < meshes.Length; lod++)
						{
							MeshBuilder built = VineMeshes.BuildClimber(in sp, trunk, tree.Height, ProceduralArtCatalogue.ClimberReach[v],
								ProceduralArtCatalogue.ClimberMaxMetres[v], host.Tree, v, lod, c.Seed);
							if (lod == 0)
							{
								climberSize = LodSize(built.Bounds);
							}
							meshes[lod] = WriteMesh(c, built, ProceduralArtCatalogue.VineMesh(name, lod), false);
						}
						WriteVinePrefab(c, name, meshes, material, ClimberLodHeights(climberSize, treeSize));
					}
				}
			}
		}

		/// <summary>
		/// A climber's level switches, so that it changes level where its tree does and is gone where the tree turns to its
		/// billboard (which has no ivy drawn on it): a level switches at a screen height of the group's size, so the climber's,
		/// a fraction of its tree's size, are that fraction of the tree's (ProceduralArtCatalogue.TreeLodHeights).
		/// </summary>
		private static float[] ClimberLodHeights(float climberSize, float treeSize)
		{
			float k = Mathf.Clamp(climberSize / Mathf.Max(0.1f, treeSize), 0.05f, 1f);
			float[] tree = ProceduralArtCatalogue.TreeLodHeights;
			return new[] { tree[0] * k, Mathf.Lerp(tree[0], tree[1], 0.6f) * k, tree[1] * k };
		}

		/// <summary>A LOD group's size: the largest side of its bounds.</summary>
		private static float LodSize(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

		/// <summary>A vine prefab: a LOD group over three renderers, no collider (players walk through vines).</summary>
		private static void WriteVinePrefab(Context c, string name, Mesh[] meshes, Material material, float[] heights)
		{
			var mats = new Material[meshes.Length][];
			var shadows = new ShadowCastingMode[meshes.Length];
			for (int lod = 0; lod < meshes.Length; lod++)
			{
				mats[lod] = new[] { material };
				// The far level's few big cards would throw blotchy shadows: it casts none (the near levels do).
				shadows[lod] = lod < meshes.Length - 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
			}
			WritePrefab(c, ProceduralArtCatalogue.VinePrefab(name), root => Lods(root, meshes, mats, heights, shadows));
		}
	}
}
#endif
