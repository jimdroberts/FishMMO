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

		/// <summary>Every structure payload file: three maps and three finish albedos a surface, three meshes a variant.</summary>
		public static IEnumerable<string> StructurePayloadPaths()
		{
			foreach (string surface in StructureSurfaces.Names)
			{
				yield return StructureTexture(surface, "Albedo");
				yield return StructureTexture(surface, "Normal");
				yield return StructureTexture(surface, "Mask");
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					if (finish != StructureFinish.None)
					{
						yield return StructureTexture(surface, "Albedo", finish);
					}
				}
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
		}

		/// <summary>Every structure material: each material in each finish.</summary>
		public static IEnumerable<string> StructureWrapperPaths()
		{
			foreach (StructureMaterial material in StructureMaterials())
			{
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					yield return MaterialPath(StructurePieces.MaterialName(material, finish));
				}
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
