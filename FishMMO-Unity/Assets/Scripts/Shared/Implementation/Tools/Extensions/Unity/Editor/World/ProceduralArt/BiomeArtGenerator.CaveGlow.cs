#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cave kit's glow art (<see cref="CaveGlowMeshes"/>, ProceduralArtCatalogue.Cave.cs, 2026-10-13): its glow and ore
	/// maps, a material a hue, and a prefab a variant of every kind in every hue.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Built with the structure kit.</b> Its steps run inside the structures' (BiomeArtGenerator.Structures.cs: the maps
	/// with the structure surfaces, the rest after the pieces), so "Generate biome art", the structures-only run and the
	/// Structures family's fingerprint all cover it; its paths are listed with the structures'. Everything goes through
	/// the generator's own writers, so payload GUIDs, prefab file IDs and the ledger behave as for every other asset.
	/// </para>
	/// <para>
	/// <b>Prefabs.</b> Under <c>Prefabs/Cave/</c>: a root <see cref="LODGroup"/> over three levels and, for what a player
	/// would walk into (crystals, giant fungi), a mesh collider from the last level, as the boulders have; the baked props
	/// draw them on the GPU and stream their collision (ScenePropBaker). A crystal wears the structure kit's fieldstone on its
	/// matrix, a seam the gangue quartz on its band.
	/// </para>
	/// <para>
	/// <b>Materials</b> are FishMMO/Weather Lit with <c>_EMISSION</c> on for whatever glows: URP adds the emission after the
	/// lighting, so neither the cave darkness's occlusion nor the missing sky dims it. They are not structures
	/// (<c>_FishStructure</c> 0: no weathering grime or moss on a crystal), not wetted or snowed on, and grow no moss.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>The steps the glow art adds to the structures': its maps, its materials, one a kind.</summary>
		private static int CaveGlowStepCount() => 3 + ProceduralArtCatalogue.CaveGlowFamilies.Length;

		/// <summary>The glow ramps and ore grains: one step with the structure surfaces, before the texture import.</summary>
		private static IEnumerable<string> CaveGlowSurfaceSteps(Context c)
		{
			yield return "Structure surfaces: cave glow";
			WriteCaveGlowMaps(c);
		}

		/// <summary>The materials, then one step a kind: after the structure pieces.</summary>
		private static IEnumerable<string> CaveGlowSteps(Context c)
		{
			yield return "Structure: cave glow materials";
			WriteCaveGlowMaterials(c);
			foreach (CaveGlowFamily family in ProceduralArtCatalogue.CaveGlowFamilies)
			{
				yield return "Structure: cave " + family.Kind;
				WriteCaveGlow(c, family);
			}
			yield return "Structure: cave crystals";
			WriteCaveMinerals(c);
		}

		private static void WriteCaveGlowMaps(Context c)
		{
			int size = ProceduralArtCatalogue.CaveGlowTextureSize;
			c.Report.TextureBytes += 3 * CompressedBytes(size, size);
			// Written as the cave glow look array (ProceduralArtLookArrays, 2026-10-28): all three when it is due.
			if (!LookArraysDue(c, ProceduralArtLookArrays.CaveGlowSetName))
			{
				return;
			}
			PutSurfaceMap(c, ProceduralArtCatalogue.CaveGlowAlbedoPath, CaveGlowMeshes.GlowRamp(size, c.Seed, false), size);
			PutSurfaceMap(c, ProceduralArtCatalogue.CaveGlowEmissionPath, CaveGlowMeshes.GlowRamp(size, c.Seed, true), size);
			PutSurfaceMap(c, ProceduralArtCatalogue.CaveOreAlbedoPath, CaveGlowMeshes.OreGrains(size, c.Seed), size);
			WriteLookArrays(c, ProceduralArtLookArrays.CaveGlowSetName);
		}

		/// <summary>Whether a material set is textured as ore (grains in metres) rather than through the glow ramps.</summary>
		private static bool IsOreSet(string set) => set == "Ore" || set == "GlowOre";

		private static void WriteCaveGlowMaterials(Context c)
		{
			WorldEditorAssets.EnsureFolder(ProceduralArtCatalogue.CavePrefabsFolder);
			Shader shader = RockShader;
			if (shader == null)
			{
				c.Report.Problems.Add("FishMMO/Weather Lit and URP Lit were not found; the cave glow materials were not written.");
				return;
			}
			// The maps by their payload paths: slices of the cave glow look array (SetSurfaceMap).
			string albedo = ProceduralArtCatalogue.CaveGlowAlbedoPath;
			string emission = ProceduralArtCatalogue.CaveGlowEmissionPath;
			string ore = ProceduralArtCatalogue.CaveOreAlbedoPath;
			var seen = new HashSet<string>();
			foreach (CaveGlowFamily family in ProceduralArtCatalogue.CaveGlowFamilies)
			{
				bool isOre = IsOreSet(family.MaterialSet);
				// The glow-worms too (2026-10-26, I2n-c, decision 86 (h)): marked as a steady crystal, so the crystals' glow mode of the
				// biome over them (an EYELID, …) reaches them; unmoded, a −2 shines exactly as made.
				bool isCrystal = family.MaterialSet == CrystalSet || family.MaterialSet == GlowWormSet;
				foreach (CaveGlowHue h in family.Hues)
				{
					CaveGlowHue hue = h;
					string name = ProceduralArtCatalogue.CaveGlowMaterial(family.MaterialSet, hue.Name);
					if (seen.Add(name))
					{
						WriteMaterial(c, name, shader, m =>
						{
							CaveGlowLit(c, m, isOre ? ore : albedo, isOre ? ore : emission, in hue, isOre);
							// A glowing crystal (2026-10-25, I6i): marked so its biome's glow mode reaches it (FishGlowModes.hlsl).
							if (isCrystal && hue.Emission.maxColorComponent > 0f)
							{
								m.SetFloat("_FishStructure", SteadyCrystalFlag);
							}
						});
					}
				}
			}
			CaveGlowHue gangue = ProceduralArtCatalogue.Gangue;
			WriteMaterial(c, ProceduralArtCatalogue.CaveGangueMaterial, shader, m => CaveGlowLit(c, m, ore, ore, in gangue, true));
			// The cave crystals' own (2026-10-16, I6c): through the glow ramps as the glow art's crystals, glowing only where the kind does.
			foreach (CaveMineral mineral in ProceduralArtCatalogue.CaveMinerals)
			{
				if (!mineral.Formation)
				{
					CaveGlowHue hue = mineral.Hue;
					bool flickers = mineral.Flickers;
					WriteMaterial(c, ProceduralArtCatalogue.CaveMineralMaterial(mineral.Kind), shader, m =>
					{
						CaveGlowLit(c, m, albedo, emission, in hue, false);
						// A crystal whose glow flickers (stormglass, 2026-10-18, decision 33): _FishStructure −1, not weathered as a
						// building, its emission crackling with the light beside it (FishWeatherLitForwardPass.hlsl FishGlowFlicker).
						if (flickers)
						{
							m.SetFloat("_FishStructure", FlickeringCrystalFlag);
						}
						else if (hue.Emission.maxColorComponent > 0f)
						{
							// A steady glowing crystal (2026-10-25, I6i): −2, shining as made unless its biome names a glow mode.
							m.SetFloat("_FishStructure", SteadyCrystalFlag);
						}
					});
				}
			}
			// The biomes' own looks of shared kinds (2026-10-26, NEW-CRYSTALS): the kind's glow, the look's colour.
			foreach (CaveMineralLook look in ProceduralArtCatalogue.CaveMineralLooks)
			{
				CaveGlowHue hue = ProceduralArtCatalogue.CaveMineralLookHue(in look);
				bool flickers = ProceduralArtCatalogue.CaveMineralOf(look.Kind).Flickers;
				WriteMaterial(c, ProceduralArtCatalogue.CaveMineralLookMaterial(in look), shader, m =>
				{
					CaveGlowLit(c, m, albedo, emission, in hue, false);
					if (flickers)
					{
						m.SetFloat("_FishStructure", FlickeringCrystalFlag);
					}
					else if (hue.Emission.maxColorComponent > 0f)
					{
						m.SetFloat("_FishStructure", SteadyCrystalFlag);
					}
				});
			}
		}

		/// <summary>
		/// The cave crystals (ProceduralArtCatalogue.CaveMinerals, 2026-10-16, I6c): each habit's meshes once, then a prefab a
		/// variant of every kind built in its own habit, its rock on the kit's fieldstone and its crystal in the kind's material,
		/// collided with as the glow art's crystals are. Selenite and frost crystal are the rock art's formations: nothing here.
		/// </summary>
		private static void WriteCaveMinerals(Context c)
		{
			WorldEditorAssets.EnsureFolder(ProceduralArtCatalogue.CavePrefabsFolder);
			int variants = ProceduralArtCatalogue.CaveMineralVariants;
			var meshes = new Dictionary<CaveMineralHabit, Mesh[][]>();
			foreach (CaveMineralHabit habit in ProceduralArtCatalogue.CaveMineralHabits())
			{
				var levels = new Mesh[variants][];
				for (int v = 0; v < variants; v++)
				{
					levels[v] = new Mesh[CaveGlowMeshes.Levels];
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						levels[v][lod] = WriteMesh(c, CaveGlowMeshes.BuildMineral(habit, v, lod, c.Seed), ProceduralArtCatalogue.CaveMineralMesh(habit, v, lod), true);
					}
				}
				meshes[habit] = levels;
			}
			Material rock = MaterialOrNull(c, StructurePieces.MaterialName(StructureMaterial.Fieldstone));
			ShadowCastingMode[] shadows = LodShadows(CaveGlowMeshes.Levels);
			foreach (CaveMineral mineral in ProceduralArtCatalogue.CaveMinerals)
			{
				if (mineral.Formation)
				{
					continue;
				}
				Material crystal = MaterialOrNull(c, ProceduralArtCatalogue.CaveMineralMaterial(mineral.Kind));
				Mesh[][] habitMeshes = meshes[mineral.Habit];
				for (int v = 0; v < variants; v++)
				{
					Mesh[] levels = habitMeshes[v];
					WritePrefab(c, ProceduralArtCatalogue.CaveMineralPrefab(mineral.Kind, v), root =>
					{
						var mats = new Material[levels.Length][];
						for (int lod = 0; lod < levels.Length; lod++)
						{
							mats[lod] = new[] { rock, crystal };
						}
						Lods(root, levels, mats, ProceduralArtCatalogue.CaveGlowLodHeights, shadows);
						AddBoulderCollider(root, levels);
					});
				}
			}
			// A biome's own look of a shared kind (2026-10-26, NEW-CRYSTALS): its habit's meshes in its own material.
			foreach (CaveMineralLook look in ProceduralArtCatalogue.CaveMineralLooks)
			{
				Material crystal = MaterialOrNull(c, ProceduralArtCatalogue.CaveMineralLookMaterial(in look));
				Mesh[][] habitMeshes = meshes[look.Habit];
				for (int v = 0; v < variants; v++)
				{
					Mesh[] levels = habitMeshes[v];
					WritePrefab(c, ProceduralArtCatalogue.CaveMineralLookPrefab(in look, v), root =>
					{
						var mats = new Material[levels.Length][];
						for (int lod = 0; lod < levels.Length; lod++)
						{
							mats[lod] = new[] { rock, crystal };
						}
						Lods(root, levels, mats, ProceduralArtCatalogue.CaveGlowLodHeights, shadows);
						AddBoulderCollider(root, levels);
					});
				}
			}
		}

		/// <summary>
		/// A cave glow material: its ramp (or the ore grains, tiled every 0.4 m with each grain's smoothness from the albedo's
		/// alpha) tinted by the hue, and, when the hue glows, the glow ramp (or the grains) as its emission map in the hue's
		/// HDR colour. No weathering, wetting, snow or moss.
		/// </summary>
		private static void CaveGlowLit(Context c, Material m, string albedo, string glow, in CaveGlowHue hue, bool ore)
		{
			SetSurfaceMap(c, m, "_BaseMap", albedo);
			m.SetTextureScale("_BaseMap", ore ? new Vector2(2.5f, 2.5f) : Vector2.one);
			m.SetColor("_BaseColor", hue.Albedo);
			m.SetFloat("_Smoothness", hue.Smoothness);
			m.SetFloat("_Metallic", hue.Metallic);
			if (ore)
			{
				m.SetFloat("_SmoothnessTextureChannel", 1f);
				m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
			}
			if (hue.Emission.maxColorComponent > 0f)
			{
				SetSurfaceMap(c, m, "_EmissionMap", glow);
				m.SetColor("_EmissionColor", hue.Emission);
				m.EnableKeyword("_EMISSION");
				m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			}
			else
			{
				m.SetColor("_EmissionColor", Color.black);
				m.DisableKeyword("_EMISSION");
			}
			m.SetFloat("_FishStructure", 0f);
			m.SetFloat("_FishWeatherAmount", 0f);
			m.SetFloat("_FishMoss", 0f);
		}

		/// <summary>The <c>_FishStructure</c> mark of a flickering crystal's material (FishWeatherLitForwardPass.hlsl: below −0.5).</summary>
		public const float FlickeringCrystalFlag = -1f;

		/// <summary>
		/// The <c>_FishStructure</c> mark of a steady glowing crystal's material (2026-10-25, I6i; FishWeatherLitForwardPass.hlsl: below
		/// −1.5): it shines as made (exactly as an unmarked one did) unless its biome names a glow mode for its crystals.
		/// </summary>
		public const float SteadyCrystalFlag = -2f;

		/// <summary>The cave glow art's crystal material set (Cave_Crystal_*): the glowing crystals the glow modes reach.</summary>
		private const string CrystalSet = "Crystal";

		/// <summary>The glow-worms' material set (Cave_GlowWorm_*): marked as steady crystals, so the crystals' glow modes reach them.</summary>
		public const string GlowWormSet = "GlowWorm";

		/// <summary>A kind's meshes (when they are its own) and its prefabs, a variant in every hue.</summary>
		private static void WriteCaveGlow(Context c, CaveGlowFamily family)
		{
			WorldEditorAssets.EnsureFolder(ProceduralArtCatalogue.CavePrefabsFolder);
			bool own = ProceduralArtCatalogue.OwnsShape(in family);
			var meshes = new Mesh[family.Variants][];
			if (own)
			{
				for (int v = 0; v < family.Variants; v++)
				{
					meshes[v] = new Mesh[CaveGlowMeshes.Levels];
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						meshes[v][lod] = WriteMesh(c, CaveGlowMeshes.Build(family.Shape, v, lod, c.Seed), ProceduralArtCatalogue.CaveGlowMesh(family.Shape, v, lod), true);
					}
				}
			}
			bool twoParts = CaveGlowMeshes.Submeshes(family.Shape) > 1;
			Material second = !twoParts ? null
				: IsOreSet(family.MaterialSet) ? MaterialOrNull(c, ProceduralArtCatalogue.CaveGangueMaterial)
				: MaterialOrNull(c, StructurePieces.MaterialName(StructureMaterial.Fieldstone));
			ShadowCastingMode[] shadows = family.CastsShadows ? LodShadows(CaveGlowMeshes.Levels) : NoShadows(CaveGlowMeshes.Levels);
			bool collides = family.Collides;
			CaveGlowKind shape = family.Shape;
			foreach (CaveGlowHue hue in family.Hues)
			{
				Material glow = MaterialOrNull(c, ProceduralArtCatalogue.CaveGlowMaterial(family.MaterialSet, hue.Name));
				for (int v = 0; v < family.Variants; v++)
				{
					int variant = v;
					WritePrefab(c, ProceduralArtCatalogue.CaveGlowPrefab(family.Kind, hue.Name, v), root =>
					{
						// A kind wearing another's meshes reads them from their files, written by that kind's step.
						Mesh[] levels = own ? meshes[variant] : LoadCaveGlowMeshes(shape, variant);
						var mats = new Material[levels.Length][];
						for (int lod = 0; lod < levels.Length; lod++)
						{
							mats[lod] = twoParts ? new[] { second, glow } : new[] { glow };
						}
						Lods(root, levels, mats, ProceduralArtCatalogue.CaveGlowLodHeights, shadows);
						if (collides)
						{
							AddBoulderCollider(root, levels);
						}
					});
				}
			}
		}

		private static Mesh[] LoadCaveGlowMeshes(CaveGlowKind shape, int variant)
		{
			var levels = new Mesh[CaveGlowMeshes.Levels];
			for (int lod = 0; lod < levels.Length; lod++)
			{
				levels[lod] = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.CaveGlowMesh(shape, variant, lod)));
			}
			return levels;
		}
	}
}
#endif
