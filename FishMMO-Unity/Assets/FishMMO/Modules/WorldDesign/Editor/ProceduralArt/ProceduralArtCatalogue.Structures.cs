#if UNITY_EDITOR
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The structure kit's generated files (<see cref="StructurePieces"/>): surface textures, meshes, materials, prefabs.</summary>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>The folder every structure prefab is written to (a sub-folder of the generated prefabs).</summary>
		public const string StructurePrefabsFolder = PrefabsFolder + "/" + StructurePieces.PrefabFolderName;

		/// <summary>A structure surface's texture: <c>Structure_{surface}_{map}</c>, a finish's albedo <c>Structure_{surface}_{finish}_Albedo</c>.</summary>
		public static string StructureTexture(string surface, string map, StructureFinish finish = StructureFinish.None)
		{
			return finish == StructureFinish.None
				? $"{TexturesFolder}/{StructurePieces.Prefix}{surface}_{map}.png"
				: $"{TexturesFolder}/{StructurePieces.Prefix}{surface}_{finish}_{map}.png";
		}

		/// <summary>
		/// Every structure payload file: the surfaces' look arrays (their maps and finish albedos as slices, ProceduralArtLookArrays;
		/// no single map is written since 2026-10-28), three meshes a variant; and the cave glow art's.
		/// </summary>
		public static IEnumerable<string> StructurePayloadPaths()
		{
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.LayoutsOfSet(ProceduralArtLookArrays.StructureSetName))
			{
				yield return layout.Path;
			}
			foreach (StructurePiece p in StructurePieces.All)
			{
				for (int v = 0; v < p.Variants; v++)
				{
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						yield return MeshPath(StructurePieces.MeshName(p.Id, v, lod));
					}
				}
			}
			// The house roofs the spec's palettes build at another pitch (StructureRoofs; a template's hand-edited roof is written
			// by the generation too, but is not listed, as its materials are not).
			foreach (StructureRoofVariant roof in SpecRoofs())
			{
				for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
				{
					yield return MeshPath(StructurePieces.MeshName(roof.Piece, roof.Variant, lod, roof.Pitch));
				}
			}
			// The cave kit's glow art, built in the structures' steps (ProceduralArtCatalogue.Cave.cs).
			foreach (string path in CaveGlowPayloadPaths())
			{
				yield return path;
			}
			// The run-time cave cover's mesh, built in the structures' steps too (BiomeArtGenerator.CaveCover.cs, 2026-10-20).
			foreach (string path in CaveCoverPayloadPaths())
			{
				yield return path;
			}
		}

		/// <summary>
		/// Every structure material: each material in each finish, then each spec palette's own keys in each finish
		/// (StructurePalettes.KeysOf: what authoring writes onto the templates; a template's hand-edited key is written by the
		/// generation too, but is not listed here, so it never marks the art stale).
		/// </summary>
		public static IEnumerable<string> StructureWrapperPaths()
		{
			foreach (StructureMaterial material in StructureMaterials())
			{
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					yield return MaterialPath(StructurePieces.MaterialName(material, finish));
				}
			}
			foreach (StructureSurfaceKey key in StructurePalettes.KeysOf(SpecPalettes()))
			{
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					yield return MaterialPath(key.MaterialName(finish));
				}
			}
			foreach (string path in CaveGlowWrapperPaths())
			{
				yield return path;
			}
		}

		/// <summary>Every structure prefab name (with its sub-folder): each variant of each piece in each finish.</summary>
		public static IEnumerable<string> StructurePrefabNames()
		{
			foreach (StructurePiece p in StructurePieces.All)
			{
				for (int v = 0; v < p.Variants; v++)
				{
					foreach (StructureFinish finish in StructurePieces.Finishes)
					{
						yield return StructurePieces.PrefabName(p.Id, v, finish);
					}
				}
			}
			foreach (StructureRoofVariant roof in SpecRoofs())
			{
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					yield return StructurePieces.PrefabName(roof.Piece, roof.Variant, finish, roof.Pitch);
				}
			}
			foreach (string prefab in CaveGlowPrefabNames())
			{
				yield return prefab;
			}
		}

		/// <summary>The house roof variants the spec's palettes need (StructureRoofs.Needed): none while every biome builds as the kit.</summary>
		public static List<StructureRoofVariant> SpecRoofs() => StructureRoofs.Needed(SpecPalettes());

		/// <summary>Every spec entry's structure palette, in the spec's order.</summary>
		private static IEnumerable<FishMMO.Shared.Biomes.BiomeStructurePalette> SpecPalettes()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				yield return entry.StructurePalette();
			}
		}

		/// <summary>Every <see cref="StructureMaterial"/>, in order.</summary>
		public static IEnumerable<StructureMaterial> StructureMaterials()
		{
			foreach (StructureMaterial m in System.Enum.GetValues(typeof(StructureMaterial)))
			{
				yield return m;
			}
		}
	}
}
#endif
