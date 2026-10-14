#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What glows, or shines, on a cave's rock (the cave kit's glow art, <see cref="CaveGlowMeshes"/>).</summary>
	/// <remarks>Append-only: names are in generated prefab paths.</remarks>
	public enum CaveGlowKind
	{
		/// <summary>A cluster of glowing crystal points on a lump of host rock, 0.6–1.4 m.</summary>
		Crystal,
		/// <summary>Giant glowing crystal columns, 3–5.5 m: a cavern's centrepiece.</summary>
		GiantCrystal,
		/// <summary>A tuft of small luminous bonnet caps on slender stalks (Mycena), 0.1–0.5 m.</summary>
		Fungus,
		/// <summary>A giant glowcap, 1–2.7 m: a fantasy cavern's mushroom, gills glowing under a broad cap.</summary>
		GiantFungus,
		/// <summary>Tiers of luminous bracket fungi on a wall (Panellus).</summary>
		ShelfFungus,
		/// <summary>A patch of glow-worm snares hung from a roof (Arachnocampa): silk threads beaded with droplets, the larvae glowing at their tops.</summary>
		GlowWorms,
		/// <summary>An ore seam in a wall: copper, iron, silver or gold in a band of quartz.</summary>
		OreVein,
		/// <summary>A seam of a glowing fantasy ore (starmetal, emberstone) in quartz.</summary>
		GlowOreVein,
	}

	/// <summary>How a piece of glow art is set against the cave's rock.</summary>
	public enum CaveMount
	{
		/// <summary>On a floor or a ledge: origin at its foot, standing up +y; its base reaches a little below y = 0 to seat on uneven rock.</summary>
		Floor,
		/// <summary>On a wall: origin on the wall's surface, its back toward +z (into the rock), standing out along −z, up +y.</summary>
		Wall,
		/// <summary>From a roof: origin on the roof's surface, hanging down −y; it reaches up into the rock to seat on an uneven roof.</summary>
		Roof,
	}

	/// <summary>One colour (or ore) a kind of glow art is made in.</summary>
	public struct CaveGlowHue
	{
		public string Name;
		/// <summary>The surface's colour under a lamp (<c>_BaseColor</c>).</summary>
		public Color Albedo;
		/// <summary>Its glow, HDR (<c>_EmissionColor</c>, over the glow map): black for an ore that only shines.</summary>
		public Color Emission;
		public float Smoothness, Metallic;
		/// <summary>The colour of the light it casts, for the placer's emitters (black for none).</summary>
		public Color Light;
		/// <summary>
		/// The hue bits (8–9 of the seed) a <see cref="SceneEmitterKind.Crystal"/> emitter carries for this light: 0 violet,
		/// 1 blue, 2 green, 3 amber (SiteLightPool); −1 for no light.
		/// </summary>
		public int EmitterHue;
	}

	/// <summary>One kind of glow art: how it is mounted, how many shapes it has, the hues it comes in and what it costs.</summary>
	public struct CaveGlowFamily
	{
		public CaveGlowKind Kind;
		public CaveMount Mount;
		/// <summary>The kind whose meshes it wears (a glowing ore vein wears the ore veins').</summary>
		public CaveGlowKind Shape;
		/// <summary>Distinct shapes; each in every hue.</summary>
		public int Variants;
		public CaveGlowHue[] Hues;
		/// <summary>The materials' group: <c>Cave_{MaterialSet}_{Hue}</c>.</summary>
		public string MaterialSet;
		/// <summary>A mesh collider from the last level (what a player would walk into); none for what is brushed past.</summary>
		public bool Collides;
		public bool CastsShadows;
		/// <summary>How far a light placed with it should reach, metres (the placer adds the light, for the brightest few); 0 for none.</summary>
		public float Reach;
		/// <summary>One line for designers.</summary>
		public string Description;
	}

	/// <summary>
	/// The cave kit's glow art (2026-10-13: "Crystals, Luminous fungi, Glow-worm ceilings, Ore veins in mines"): its kinds,
	/// hues and shapes, names and paths. Built in the structure kit's steps (BiomeArtGenerator.CaveGlow.cs), so a
	/// structures-only generation makes it too and the Structures family's fingerprint covers it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it glows in the dark.</b> Every glowing material is FishMMO/Weather Lit with <c>_EMISSION</c> on: URP adds the
	/// emission after the lighting, so the cave darkness (which takes the occlusion, the ambient and the sun away) leaves it
	/// whole. Its brightness is not flat paint: the mesh's first texture coordinate is a glow level (0 dark .. 1 brightest,
	/// u) and a mottle coordinate (v), read through two shared ramps (<c>Structure_CaveGlow_Albedo</c>,
	/// <c>Structure_CaveGlow_Emission</c>), so a crystal brightens to its tip, a cap's gills outshine its top and a
	/// glow-worm's larva outshines its threads; the material only colours it.
	/// </para>
	/// <para>
	/// <b>Ore</b> is textured in metres instead (<c>Structure_CaveOre_Albedo</c>: crystalline grains), tinted and made
	/// metallic per ore; a glowing ore uses the same grains as its glow map, so its crystals shine unevenly.
	/// </para>
	/// </remarks>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>Every cave glow mesh, material and prefab starts with this.</summary>
		public const string CavePrefix = "Cave_";

		/// <summary>The sub-folder of the generated prefabs the cave glow prefabs are written to.</summary>
		public const string CavePrefabFolderName = "Cave";

		public const string CavePrefabsFolder = PrefabsFolder + "/" + CavePrefabFolderName;

		/// <summary>The glow ramps and the ore grains: one small tileable set shared by every cave glow material.</summary>
		public const int CaveGlowTextureSize = 256;

		/// <summary>Screen heights at which glow art steps to its next level; the last culls (later than a structure's: a light is seen from afar in the dark).</summary>
		public static readonly float[] CaveGlowLodHeights = { 0.12f, 0.04f, 0.004f };

		/// <summary>The crystals' hues: what cave crystals glow in (quartz-ice, beryl green, amethyst violet, citrine amber).</summary>
		public static readonly CaveGlowHue[] CrystalHues =
		{
			new CaveGlowHue { Name = "IceBlue", Albedo = new Color(0.62f, 0.8f, 0.95f), Emission = new Color(0.32f, 0.68f, 1.6f), Smoothness = 0.88f, Light = new Color(0.45f, 0.75f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Green", Albedo = new Color(0.55f, 0.88f, 0.62f), Emission = new Color(0.3f, 1.35f, 0.55f), Smoothness = 0.88f, Light = new Color(0.45f, 1f, 0.6f), EmitterHue = 2 },
			new CaveGlowHue { Name = "Violet", Albedo = new Color(0.72f, 0.58f, 0.92f), Emission = new Color(0.95f, 0.45f, 1.6f), Smoothness = 0.88f, Light = new Color(0.68f, 0.45f, 1f), EmitterHue = 0 },
			new CaveGlowHue { Name = "Amber", Albedo = new Color(0.95f, 0.75f, 0.42f), Emission = new Color(1.6f, 0.85f, 0.28f), Smoothness = 0.88f, Light = new Color(1f, 0.7f, 0.35f), EmitterHue = 3 },
		};

		/// <summary>
		/// The fungi's hues: the green of real luminous fungi (Mycena, Panellus, Omphalotus glow 520–530 nm), a fantasy blue,
		/// and a pale gold.
		/// </summary>
		public static readonly CaveGlowHue[] FungusHues =
		{
			new CaveGlowHue { Name = "Teal", Albedo = new Color(0.78f, 0.9f, 0.82f), Emission = new Color(0.32f, 1.25f, 0.82f), Smoothness = 0.4f, Light = new Color(0.4f, 1f, 0.75f), EmitterHue = 2 },
			new CaveGlowHue { Name = "Blue", Albedo = new Color(0.78f, 0.85f, 0.95f), Emission = new Color(0.3f, 0.7f, 1.45f), Smoothness = 0.4f, Light = new Color(0.4f, 0.65f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Gold", Albedo = new Color(0.95f, 0.88f, 0.7f), Emission = new Color(1.35f, 1.0f, 0.42f), Smoothness = 0.4f, Light = new Color(1f, 0.85f, 0.5f), EmitterHue = 3 },
		};

		/// <summary>Glow-worm light: the larvae's blue-green (Arachnocampa, about 487 nm).</summary>
		public static readonly CaveGlowHue[] GlowWormHues =
		{
			new CaveGlowHue { Name = "Blue", Albedo = new Color(0.8f, 0.88f, 0.9f), Emission = new Color(0.25f, 0.95f, 1.5f), Smoothness = 0.85f, Light = new Color(0.35f, 0.85f, 1f), EmitterHue = 1 },
		};

		/// <summary>
		/// The ores, as their seams show in a wall: copper as malachite green over its native metal, iron as rusty hematite,
		/// silver and gold native in their quartz. They shine under a lamp (metallic) and do not glow.
		/// </summary>
		public static readonly CaveGlowHue[] OreHues =
		{
			new CaveGlowHue { Name = "Copper", Albedo = new Color(0.2f, 0.52f, 0.36f), Smoothness = 0.55f, Metallic = 0.25f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Iron", Albedo = new Color(0.42f, 0.17f, 0.1f), Smoothness = 0.4f, Metallic = 0.35f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Silver", Albedo = new Color(0.78f, 0.79f, 0.8f), Smoothness = 0.7f, Metallic = 0.95f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Gold", Albedo = new Color(1f, 0.78f, 0.34f), Smoothness = 0.75f, Metallic = 1f, EmitterHue = -1 },
		};

		/// <summary>The fantasy ores that glow: starmetal's cold blue-white, emberstone's banked-fire red.</summary>
		public static readonly CaveGlowHue[] GlowOreHues =
		{
			new CaveGlowHue { Name = "Starmetal", Albedo = new Color(0.75f, 0.82f, 0.95f), Emission = new Color(0.55f, 0.75f, 1.7f), Smoothness = 0.75f, Metallic = 0.7f, Light = new Color(0.6f, 0.75f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Emberstone", Albedo = new Color(0.5f, 0.16f, 0.08f), Emission = new Color(1.7f, 0.5f, 0.14f), Smoothness = 0.5f, Metallic = 0.3f, Light = new Color(1f, 0.5f, 0.22f), EmitterHue = 3 },
		};

		/// <summary>The milky quartz an ore seam runs in (<c>Cave_Gangue</c>, the seams' first submesh).</summary>
		public static readonly CaveGlowHue Gangue = new CaveGlowHue { Name = "Gangue", Albedo = new Color(0.82f, 0.8f, 0.74f), Smoothness = 0.55f, EmitterHue = -1 };

		/// <summary>Every kind of glow art, in <see cref="CaveGlowKind"/> order.</summary>
		public static readonly CaveGlowFamily[] CaveGlowFamilies =
		{
			new CaveGlowFamily { Kind = CaveGlowKind.Crystal, Mount = CaveMount.Floor, Shape = CaveGlowKind.Crystal, Variants = 3, Hues = CrystalHues, MaterialSet = "Crystal",
				Collides = true, CastsShadows = true, Reach = 6f,
				Description = "Glowing crystal points on a lump of host rock: v0 a druse of eleven, v1 five stout points, v2 a spray of slender needles; about 1 m across, 0.6–1.3 m tall." },
			new CaveGlowFamily { Kind = CaveGlowKind.GiantCrystal, Mount = CaveMount.Floor, Shape = CaveGlowKind.GiantCrystal, Variants = 2, Hues = CrystalHues, MaterialSet = "Crystal",
				Collides = true, CastsShadows = true, Reach = 12f,
				Description = "Giant crystal columns: v0 one leaning 4.6 m column with lesser points at its foot, v1 three diverging columns up to 5.2 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.Fungus, Mount = CaveMount.Floor, Shape = CaveGlowKind.Fungus, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 3.5f,
				Description = "Bonnet caps on slender stalks (Mycena): v0 a tight tuft 0.1–0.25 m, v1 a spread patch 0.9 m across, v2 a tall clump to 0.5 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.GiantFungus, Mount = CaveMount.Floor, Shape = CaveGlowKind.GiantFungus, Variants = 2, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = true, CastsShadows = true, Reach = 9f,
				Description = "Giant glowcaps with a skirt on the stalk, glowing gills and spots on the cap: v0 one 2.7 m tall, v1 a group of three, 2.2, 1.6 and 1 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.ShelfFungus, Mount = CaveMount.Wall, Shape = CaveGlowKind.ShelfFungus, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 3f,
				Description = "Overlapping bracket fungi (Panellus) standing out of a wall, pores glowing underneath: v0 a cluster 0.4 m, v1 a column of tiers 0.8 m tall, v2 a patch 1 m across." },
			new CaveGlowFamily { Kind = CaveGlowKind.GlowWorms, Mount = CaveMount.Roof, Shape = CaveGlowKind.GlowWorms, Variants = 3, Hues = GlowWormHues, MaterialSet = "GlowWorm",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "A glow-worm colony hung from a roof: silk threads 0.1–0.6 m beaded with droplets, a glowing larva at the top of each; v0 2 m across, v1 3 m, v2 4 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.OreVein, Mount = CaveMount.Wall, Shape = CaveGlowKind.OreVein, Variants = 4, Hues = OreHues, MaterialSet = "Ore",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "An ore seam along x in a band of quartz, standing 2–6 cm out of the wall: v0 1.5 m, v1 2.5 m, v2 4 m, v3 3 m with a branch." },
			new CaveGlowFamily { Kind = CaveGlowKind.GlowOreVein, Mount = CaveMount.Wall, Shape = CaveGlowKind.OreVein, Variants = 4, Hues = GlowOreHues, MaterialSet = "GlowOre",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "A seam of glowing ore in quartz, the ore veins' shapes: v0 1.5 m, v1 2.5 m, v2 4 m, v3 3 m with a branch." },
		};

		/// <summary>The family of a kind.</summary>
		public static CaveGlowFamily CaveGlow(CaveGlowKind kind) => CaveGlowFamilies[(int)kind];

		// ── Names ─────────────────────────────────────────────────────

		/// <summary>A cave glow texture: <c>Structure_CaveGlow_Albedo</c>, <c>Structure_CaveGlow_Emission</c>, <c>Structure_CaveOre_Albedo</c>.</summary>
		public static string CaveGlowTexture(string surface, string map) => StructureTexture(surface, map);

		public static string CaveGlowAlbedoPath => CaveGlowTexture("CaveGlow", "Albedo");
		public static string CaveGlowEmissionPath => CaveGlowTexture("CaveGlow", "Emission");
		public static string CaveOreAlbedoPath => CaveGlowTexture("CaveOre", "Albedo");

		/// <summary>A level's payload mesh: <c>Cave_Crystal_0_LOD0</c> (shared by every hue).</summary>
		public static string CaveGlowMesh(CaveGlowKind shape, int variant, int lod) => $"{CavePrefix}{shape}_{variant}_LOD{lod}";

		/// <summary>A prefab's name under the generated prefabs folder, with its sub-folder: <c>Cave/Cave_Crystal_Violet_2</c>.</summary>
		public static string CaveGlowPrefab(CaveGlowKind kind, string hue, int variant) => $"{CavePrefabFolderName}/{CavePrefix}{kind}_{hue}_{variant}";

		/// <summary>A hue's material: <c>Cave_Crystal_Violet</c>, <c>Cave_Ore_Gold</c>.</summary>
		public static string CaveGlowMaterial(string set, string hue) => $"{CavePrefix}{set}_{hue}";

		/// <summary>The quartz an ore seam runs in.</summary>
		public static string CaveGangueMaterial => CavePrefix + Gangue.Name;

		/// <summary>Whether a kind's meshes are its own (a glowing ore vein wears the ore veins').</summary>
		public static bool OwnsShape(in CaveGlowFamily family) => family.Shape == family.Kind;

		/// <summary>Every cave glow payload file: the three maps, and three meshes a variant of each shape.</summary>
		public static IEnumerable<string> CaveGlowPayloadPaths()
		{
			yield return CaveGlowAlbedoPath;
			yield return CaveGlowEmissionPath;
			yield return CaveOreAlbedoPath;
			foreach (CaveGlowFamily f in CaveGlowFamilies)
			{
				if (!OwnsShape(in f))
				{
					continue;
				}
				for (int v = 0; v < f.Variants; v++)
				{
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						yield return MeshPath(CaveGlowMesh(f.Shape, v, lod));
					}
				}
			}
		}

		/// <summary>Every cave glow material: one a hue of each material set, and the gangue.</summary>
		public static IEnumerable<string> CaveGlowWrapperPaths()
		{
			var seen = new HashSet<string>();
			foreach (CaveGlowFamily f in CaveGlowFamilies)
			{
				foreach (CaveGlowHue hue in f.Hues)
				{
					string name = CaveGlowMaterial(f.MaterialSet, hue.Name);
					if (seen.Add(name))
					{
						yield return MaterialPath(name);
					}
				}
			}
			yield return MaterialPath(CaveGangueMaterial);
		}

		/// <summary>Every cave glow prefab name (with its sub-folder): each variant of each kind in each of its hues.</summary>
		public static IEnumerable<string> CaveGlowPrefabNames()
		{
			foreach (CaveGlowFamily f in CaveGlowFamilies)
			{
				foreach (CaveGlowHue hue in f.Hues)
				{
					for (int v = 0; v < f.Variants; v++)
					{
						yield return CaveGlowPrefab(f.Kind, hue.Name, v);
					}
				}
			}
		}
	}
}
#endif
