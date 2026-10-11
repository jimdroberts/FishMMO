#if UNITY_EDITOR
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The stable names of the rock-formation and ice assets, built from <see cref="RockTypes"/>,
	/// <see cref="IceMeshes"/> and <see cref="IceSurfaces"/> so that no name is typed twice.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The spec table (<see cref="BiomeArtSpec"/>), the catalogue and the art generator all build
	/// names here. Committed terrain data references a tree prototype by its prefab's path-derived
	/// GUID and fixed file ID, so a name, once generated into a scene, must not change.
	/// </para>
	/// <para>
	/// <b>Formations</b>: meshes <c>Formation_{Type}_{Shape}_{v}_LOD{n}</c>, prefabs
	/// <c>Formation_{Type}_{Shape}_{v}</c> (a mesh collider from the last level, none for a
	/// <see cref="FormationShape.Colliderless"/> shape), surface
	/// textures <c>RockSurface_{Type}_{Albedo|Normal|Mask}</c>. A type's material is
	/// <c>Rock_{Type}</c>, except that the four types standing for a legacy rock material reuse it:
	/// Granite wears <c>Rock_Grey</c>, and Sandstone, Basalt and Limestone their own names. A shape may
	/// wear another surface (<see cref="FormationShape.Dress"/>) and carry a second body in another
	/// (<see cref="FormationShape.Cap"/>): <see cref="FormationMaterials"/>. The mineral, ice and alien
	/// types (2026-10-10: Halite, Gypsum, Sulphur, Sinter, Scoria, Breccia, Anorthosite, Sulphide, Pyrite,
	/// DarkLag, Termitaria, Firn, WaterIce) are named the same way; none is called Ice, Nodule or Coral,
	/// whose <c>Rock_</c> names belong to small-rock materials.
	/// </para>
	/// <para>
	/// <b>Ice</b>: prefabs are the <see cref="IceMeshes"/> mesh names without the level-of-detail
	/// suffix (<c>IceBoulder_Rounded</c>, <c>Serac_Tower</c>, <c>Iceberg_DomeSmall</c>,
	/// <c>SeaIce_Floe</c>, <c>SeaIce_Ridge</c>); materials <c>Ice_GlacialBlue</c>,
	/// <c>Ice_BubblyWhite</c>, <c>Ice_Sea</c>, <c>Ice_BasalDebris</c>; textures
	/// <c>IceSurface_{GlacialBlue|…}_{map}</c>.
	/// </para>
	/// </remarks>
	public static class RockArtNames
	{
		// ── Rock formations ───────────────────────────────────────────

		public static string FormationPrefab(string type, string shape, int variant) => $"Formation_{type}_{shape}_{variant}";
		public static string FormationMesh(string type, string shape, int variant, int lod) => $"{FormationPrefab(type, shape, variant)}_LOD{lod}";
		public static string RockSurfaceTexture(string type, string map) => $"{ProceduralArtCatalogue.TexturesFolder}/RockSurface_{type}_{map}.png";

		/// <summary>The material a rock type's formations wear: its legacy material when it stands for one, else <c>Rock_{Type}</c>.</summary>
		public static string RockTypeMaterial(in RockType type) => ProceduralArtCatalogue.RockMaterial(type.LegacyMaterial ?? type.Name);

		/// <summary>True when a rock type's material is a new one rather than a legacy rock material.</summary>
		public static bool HasOwnMaterial(in RockType type) => type.LegacyMaterial == null;

		/// <summary>
		/// The material a surface key names: a <see cref="RockTypes"/> name wears that type's material
		/// (<see cref="RockTypeMaterial"/>), an <see cref="IceSurfaces"/> recipe name its ice material; null for
		/// neither. The keys of <see cref="FormationShape.Dress"/> and <see cref="FormationShape.Cap"/>.
		/// </summary>
		public static string SurfaceMaterial(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return null;
			}
			if (RockTypes.TryGet(key, out RockType type))
			{
				return RockTypeMaterial(in type);
			}
			return IceSurfaces.TryGet(key, out _) ? IceMaterial(key) : null;
		}

		/// <summary>
		/// The materials a formation's submeshes wear, in order: its <see cref="FormationShape.Dress"/> or else its type's
		/// material, then its <see cref="FormationShape.Cap"/>'s when it has a second body.
		/// </summary>
		public static string[] FormationMaterials(in RockType type, in FormationShape shape)
		{
			string main = SurfaceMaterial(shape.Dress) ?? RockTypeMaterial(in type);
			string cap = SurfaceMaterial(shape.Cap);
			return cap != null ? new[] { main, cap } : new[] { main };
		}

		/// <summary>
		/// Legacy rock materials that wear their rock type's own surface instead of a ground family's
		/// textures.
		/// </summary>
		/// <remarks>
		/// <b>All four.</b> Each legacy material wore its ground family's textures (Grey the generic
		/// <c>Rock</c>), so a legacy boulder, a formation of the same rock (which wears the legacy
		/// material) and a cliff of it (which wears <c>RockSurface_{Type}</c>) were different stones side
		/// by side. On their rock type's surface — Grey on granite, the rest on their namesakes — all
		/// three are the same rock. The ground families keep their own textures for the terrain.
		/// </remarks>
		private static readonly string[] LegacyOnRockSurface = { "Grey", "Sandstone", "Basalt", "Limestone" };

		/// <summary>
		/// Newer small-rock materials that wear a rock type's surface: the ice cobbles (<see cref="ProceduralArtCatalogue.IceRockMaterials"/>)
		/// are water ice, the same stone as the ice formations, rather than the Ice ground family's sheet ice.
		/// </summary>
		private static readonly (string Material, string Type)[] NewerOnRockSurface = { ("Ice", "WaterIce") };

		/// <summary>
		/// The rock type whose surface a small-rock material wears, or null when it keeps its ground
		/// family's textures (<see cref="LegacyOnRockSurface"/>, <see cref="NewerOnRockSurface"/>).
		/// </summary>
		public static string RockSurfaceTypeForLegacy(string legacyMaterial)
		{
			foreach ((string material, string type) in NewerOnRockSurface)
			{
				if (material == legacyMaterial)
				{
					return type;
				}
			}
			if (System.Array.IndexOf(LegacyOnRockSurface, legacyMaterial) < 0)
			{
				return null;
			}
			foreach (RockType type in RockTypes.All)
			{
				if (type.LegacyMaterial == legacyMaterial)
				{
					return type.Name;
				}
			}
			return null;
		}

		/// <summary>
		/// The prefabs of every variant of the named shapes of a type, for a scatter rule. A shape the
		/// type does not have still yields names, which the generator does not make; the spec-table
		/// test reports them.
		/// </summary>
		public static string[] Formations(in RockType type, params string[] shapes)
		{
			var names = new string[shapes.Length * RockTypes.VariantCount];
			for (int s = 0; s < shapes.Length; s++)
			{
				for (int v = 0; v < RockTypes.VariantCount; v++)
				{
					names[s * RockTypes.VariantCount + v] = FormationPrefab(type.Name, shapes[s], v);
				}
			}
			return names;
		}

		/// <summary>Every formation the generator makes: each type, shape and variant.</summary>
		public static IEnumerable<(RockType Type, FormationShape Shape, int Variant)> AllFormations()
		{
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes)
				{
					for (int v = 0; v < RockTypes.VariantCount; v++)
					{
						yield return (type, shape, v);
					}
				}
			}
		}

		// ── Ice ───────────────────────────────────────────────────────

		public static string IceBoulderPrefab(in IceBoulderShape shape) => "IceBoulder_" + shape.Name;
		public static string SeracPrefab(in SeracShape shape) => "Serac_" + shape.Name;
		public static string IcebergPrefab(in IcebergShape shape) => "Iceberg_" + shape.Name;
		public static string SeaIcePrefab(in SeaIceShape shape) => "SeaIce_" + shape.Name;
		public static string RidgePrefab(in PressureRidgeShape shape) => "SeaIce_" + shape.Name;

		/// <summary>An ice surface recipe's short name: <c>IceGlacialBlue</c> → <c>GlacialBlue</c>.</summary>
		public static string IceShort(string surface) => surface.StartsWith("Ice") ? surface.Substring(3) : surface;
		public static string IceMaterial(string surface) => "Ice_" + IceShort(surface);
		public static string IceSurfaceTexture(string surface, string map) => $"{ProceduralArtCatalogue.TexturesFolder}/IceSurface_{IceShort(surface)}_{map}.png";

		/// <summary>
		/// The prefabs of the named ice boulders, or of every one when none are named. A name with no
		/// shape still yields a prefab name, which the generator does not make; the spec-table test
		/// reports it rather than the rule silently going empty.
		/// </summary>
		public static string[] IceBoulders(params string[] names)
		{
			if (names.Length == 0)
			{
				return System.Array.ConvertAll(IceMeshes.Boulders, s => IceBoulderPrefab(in s));
			}
			return System.Array.ConvertAll(names, n => IceBoulderPrefab(new IceBoulderShape { Name = n }));
		}

		/// <summary>The prefabs of the named seracs, or of every one when none are named (unknown names as for <see cref="IceBoulders"/>).</summary>
		public static string[] Seracs(params string[] names)
		{
			if (names.Length == 0)
			{
				return System.Array.ConvertAll(IceMeshes.Seracs, s => SeracPrefab(in s));
			}
			return System.Array.ConvertAll(names, n => SeracPrefab(new SeracShape { Name = n }));
		}
	}
}
#endif
