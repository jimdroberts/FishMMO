#if UNITY_EDITOR
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The rock-formation, ice and cliff-piece part of the catalogue: sizes, which surface each ice
	/// shape wears, and every payload file, material and prefab they add. Names are built by
	/// <see cref="RockArtNames"/> and <see cref="CliffRocks.MeshName"/>, never here.
	/// </summary>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>
		/// Rock and ice surface maps: 512² over a <see cref="RockMeshes.TextureMetres"/> (2 m) tile is
		/// 4 mm a texel on a formation — finer than any prop is seen — and a quarter of the bytes of
		/// <see cref="GroundSize"/> (about 1.6 MB a set of three PNGs against 6.3).
		/// </summary>
		public const int RockSurfaceSize = 512;

		/// <summary>
		/// Formation resolutions: <see cref="RockFormations.LightResolution"/>, the scattered-field
		/// set (mean LOD0 1 181 triangles against 2 668 at <see cref="BoulderResolution"/>, 38 MiB of
		/// meshes against 60 for the 540 meshes).
		/// </summary>
		public static readonly int[] FormationResolution = RockFormations.LightResolution;

		/// <summary>Screen heights for formations, ice boulders and seracs: the legacy boulders'.</summary>
		public static readonly float[] BoulderLodHeights = { 0.25f, 0.08f, 0.01f };

		/// <summary>Screen heights for icebergs and sea ice: large, seen from far out at sea.</summary>
		public static readonly float[] FloatingIceLodHeights = { 0.15f, 0.04f, 0.004f };

		/// <summary>An ice boulder's surface: a slab is basal ice, banded with silt; the rest are dense glacial blue.</summary>
		public static string IceBoulderSurface(in IceBoulderShape shape) => shape.Name == "Slab" ? IceSurfaces.BasalDebris : IceSurfaces.GlacialBlue;

		/// <summary>Seracs are fresh fracture faces of dense glacier ice.</summary>
		public const string SeracSurface = IceSurfaces.GlacialBlue;

		/// <summary>A berg's surfaces by submesh: 0 the weathered white top, 1 the washed band and keel (<see cref="IceMeshes.WashedSubmesh"/>).</summary>
		public static readonly string[] IcebergSurfaces = { IceSurfaces.BubblyWhite, IceSurfaces.GlacialBlue };

		/// <summary>Sea ice and pressure ridges.</summary>
		public const string SeaIceSurface = IceSurfaces.SeaIce;

		/// <summary>
		/// Ice cobbles and pebbles as a small-rock material (boulders, <c>Detail_Rocks_Ice</c>, <c>Detail_Pebbles_Ice</c>):
		/// Titan's rounded water-ice cobbles, cold-trap ice on a dead world, an ice cave's floor. Declared by the
		/// vegetation expansion (2026-10-10) and joined into <see cref="AllRockMaterials"/>. It wears the WaterIce rock
		/// type's surface (<see cref="RockArtNames.RockSurfaceTypeForLegacy"/>), so a cobble and an ice formation are the
		/// same ice; its ground family stays <see cref="Ground.Ice"/> for anything that asks. Constants only, so a field
		/// is safe here (see <see cref="Details"/> on the initialisation-order trap).
		/// </summary>
		public static readonly RockMaterialSpec[] IceRockMaterials =
		{
			new RockMaterialSpec { Name = "Ice", GroundFamily = Ground.Ice },
		};

		private static readonly string[] SurfaceMaps = { "Albedo", "Normal", "Mask" };

		/// <summary>Every rock, ice and cliff payload file: surface textures and meshes.</summary>
		public static IEnumerable<string> RockPayloadPaths()
		{
			foreach (RockType t in RockTypes.All)
			{
				foreach (string map in SurfaceMaps)
				{
					yield return RockArtNames.RockSurfaceTexture(t.Name, map);
				}
			}
			foreach (SurfaceRecipe r in IceSurfaces.Recipes)
			{
				foreach (string map in SurfaceMaps)
				{
					yield return RockArtNames.IceSurfaceTexture(r.Name, map);
				}
			}
			foreach ((RockType type, FormationShape shape, int variant) in RockArtNames.AllFormations())
			{
				for (int lod = 0; lod < FormationResolution.Length; lod++)
				{
					yield return MeshPath(RockArtNames.FormationMesh(type.Name, shape.Name, variant, lod));
				}
			}
			int lods = IceMeshes.Resolution.Length;
			foreach (IceBoulderShape s in IceMeshes.Boulders)
			{
				for (int lod = 0; lod < lods; lod++) yield return MeshPath(IceMeshes.IceBoulderMesh(in s, lod));
			}
			foreach (SeracShape s in IceMeshes.Seracs)
			{
				for (int lod = 0; lod < lods; lod++) yield return MeshPath(IceMeshes.SeracMesh(in s, lod));
			}
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				for (int lod = 0; lod < lods; lod++) yield return MeshPath(IceMeshes.IcebergMesh(in s, lod));
			}
			foreach (SeaIceShape s in IceMeshes.SeaIce)
			{
				for (int lod = 0; lod < lods; lod++) yield return MeshPath(IceMeshes.SeaIceMesh(in s, lod));
			}
			foreach (PressureRidgeShape s in IceMeshes.Ridges)
			{
				for (int lod = 0; lod < lods; lod++) yield return MeshPath(IceMeshes.RidgeMesh(in s, lod));
			}
			foreach ((CliffPiece piece, int lod) in CliffRocks.AllMeshes())
			{
				yield return MeshPath(CliffRocks.MeshName(in piece, lod));
			}
			foreach ((string style, int variant, int lod) in CliffSections.AllMeshes())
			{
				yield return MeshPath(CliffSections.MeshName(style, variant, lod));
			}
		}

		/// <summary>
		/// Every rock and ice material (cliff rocks wear these). The four rock types standing for a legacy rock material
		/// wear it (<see cref="RockArtNames.RockTypeMaterial"/>), which <see cref="RockMaterials"/> already lists.
		/// </summary>
		public static IEnumerable<string> RockMaterialNames()
		{
			foreach (RockType t in RockTypes.All)
			{
				if (RockArtNames.HasOwnMaterial(in t))
				{
					yield return RockMaterial(t.Name);
				}
			}
			foreach (SurfaceRecipe r in IceSurfaces.Recipes)
			{
				yield return RockArtNames.IceMaterial(r.Name);
			}
		}

		/// <summary>Every rock-formation and ice prefab: the tree-channel props and the floating ice.</summary>
		public static IEnumerable<string> RockPrefabNames()
		{
			foreach ((RockType type, FormationShape shape, int variant) in RockArtNames.AllFormations())
			{
				string prefab = RockArtNames.FormationPrefab(type.Name, shape.Name, variant);
				yield return prefab;
			}
			foreach (IceBoulderShape s in IceMeshes.Boulders)
			{
				yield return RockArtNames.IceBoulderPrefab(in s);
			}
			foreach (SeracShape s in IceMeshes.Seracs)
			{
				yield return RockArtNames.SeracPrefab(in s);
			}
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				yield return RockArtNames.IcebergPrefab(in s);
			}
			foreach (SeaIceShape s in IceMeshes.SeaIce)
			{
				yield return RockArtNames.SeaIcePrefab(in s);
			}
			foreach (PressureRidgeShape s in IceMeshes.Ridges)
			{
				yield return RockArtNames.RidgePrefab(in s);
			}
		}
	}
}
#endif
