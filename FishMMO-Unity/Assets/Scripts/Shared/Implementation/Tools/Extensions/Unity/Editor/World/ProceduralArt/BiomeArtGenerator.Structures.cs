#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The structure kit (<see cref="StructurePieces"/>): surface textures, materials, meshes and prefabs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two hooks in <see cref="StepsOf"/>, as the rocks have: <see cref="StructureSurfaceSteps"/> before the texture
	/// import, so the shared import step imports these PNGs with the rest, and <see cref="StructureSteps"/> after the
	/// deadwood. Everything goes through the generator's own writers, so payload GUIDs, prefab file IDs and the ledger
	/// behave as for every other generated asset, and every path is listed in ProceduralArtCatalogue.Structures.cs.
	/// </para>
	/// <para>
	/// <b>Prefabs.</b> One per variant per finish, under <c>Prefabs/Structures/</c>: a root <see cref="LODGroup"/> over
	/// three levels, one material per submesh, and (unless the piece is ground clutter) a non-convex mesh collider from
	/// the last level on the root, as the boulders and deadwood have — the baked props merge it per streamed chunk. A
	/// finish is the same meshes in that finish's materials.
	/// </para>
	/// <para>
	/// <b>Materials</b> are FishMMO/Weather Lit (the rocks' shader), so wetness and snow lie on structures as on rock.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>Screen heights at which a structure steps to its next level; the last one culls.</summary>
		/// <remarks>A 10 m house: level 1 at ≈ 58 m, level 2 at ≈ 175 m, culled at ≈ 1.1 km; a crate at ≈ 5 / 14 / 90 m.</remarks>
		private static readonly float[] StructureLodHeights = { 0.15f, 0.05f, 0.008f };

		/// <summary>The steps <see cref="StructureSurfaceSteps"/> and <see cref="StructureSteps"/> yield.</summary>
		private static int StructureStepCount() => 1 + 1 + StructurePieces.All.Count + CaveGlowStepCount() + CaveCoverStepCount();

		/// <summary>The structure surfaces and their finish albedos: one step, before the texture import.</summary>
		private static IEnumerable<string> StructureSurfaceSteps(Context c)
		{
			yield return "Structure surfaces";
			WriteStructureSurfaces(c);
			// The cave kit's glow and ore maps (BiomeArtGenerator.CaveGlow.cs), imported with these.
			foreach (string step in CaveGlowSurfaceSteps(c))
			{
				yield return step;
			}
		}

		/// <summary>Materials, then one step a piece: after the deadwood.</summary>
		private static IEnumerable<string> StructureSteps(Context c)
		{
			yield return "Structure materials";
			WriteStructureMaterials(c);
			// The house roofs the palettes in use build at another pitch than the kit's (StructureRoofs, 2026-10-18, I6e), each once:
			// written with their piece.
			List<StructureRoofVariant> roofs = StructureRoofs.Needed(PalettesInUse());
			if (roofs.Count > 0)
			{
				Debug.Log($"[Biome art] Structure roofs: {roofs.Count} house roof variant(s) a palette builds at another pitch than the kit's, "
					+ $"{roofs.Count * StructurePieces.Finishes.Length} prefab(s) with their finishes: {string.Join(", ", roofs)}.");
			}
			foreach (StructurePiece piece in StructurePieces.All)
			{
				yield return "Structure: " + piece.Id;
				WriteStructurePiece(c, piece, roofs);
			}
			// The cave kit's glowing crystals, fungi, glow-worms and ore veins (BiomeArtGenerator.CaveGlow.cs): after the
			// pieces' materials, whose fieldstone a crystal's matrix wears.
			foreach (string step in CaveGlowSteps(c))
			{
				yield return step;
			}
			// The run-time cave cover's one mesh (BiomeArtGenerator.CaveCover.cs, 2026-10-20).
			foreach (string step in CaveCoverSteps(c))
			{
				yield return step;
			}
		}

		private static void WriteStructureSurfaces(Context c)
		{
			int size = StructureSurfaces.Size;
			foreach (string surface in StructureSurfaces.Names)
			{
				string albedo = ProceduralArtCatalogue.StructureTexture(surface, "Albedo");
				string normal = ProceduralArtCatalogue.StructureTexture(surface, "Normal");
				string mask = ProceduralArtCatalogue.StructureTexture(surface, "Mask");
				c.Report.TextureBytes += (3 + StructurePieces.Finishes.Length - 1) * CompressedBytes(size, size);
				bool due = ShouldWrite(c, albedo) || ShouldWrite(c, normal) || ShouldWrite(c, mask);
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					due |= finish != StructureFinish.None && ShouldWrite(c, ProceduralArtCatalogue.StructureTexture(surface, "Albedo", finish));
				}
				if (!due)
				{
					continue;
				}
				SurfaceMaps maps = StructureSurfaces.Generate(surface, size, c.Seed);
				if (ShouldWrite(c, albedo)) WritePng(c, albedo, maps.Albedo, maps.Size, maps.Size);
				if (ShouldWrite(c, normal)) WritePng(c, normal, maps.Normal, maps.Size, maps.Size);
				if (ShouldWrite(c, mask)) WritePng(c, mask, maps.Mask, maps.Size, maps.Size);
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					string path = ProceduralArtCatalogue.StructureTexture(surface, "Albedo", finish);
					if (finish != StructureFinish.None && ShouldWrite(c, path))
					{
						WritePng(c, path, StructureSurfaces.Finish(maps, surface, finish, c.Seed), maps.Size, maps.Size);
					}
				}
			}
		}

		private static void WriteStructureMaterials(Context c)
		{
			WorldEditorAssets.EnsureFolder(ProceduralArtCatalogue.StructurePrefabsFolder);
			Shader shader = RockShader;
			if (shader == null)
			{
				c.Report.Problems.Add("FishMMO/Weather Lit and URP Lit were not found; the structure materials were not written.");
				return;
			}
			foreach (StructureMaterial m in ProceduralArtCatalogue.StructureMaterials())
			{
				WriteStructureMaterial(c, shader, StructureSurfaceKey.Kit(m));
			}
			// The structure palettes' own materials (2026-10-16, I6a): one a key (material, tint, surface variant) and finish that
			// any palette lays, deduplicated: every spec palette's (what authoring writes onto the templates) and every template's
			// as it stands (a hand edit's too). No prefab or mesh is made per palette: the cut lays the kit's prefabs in these.
			List<StructureSurfaceKey> keys = StructurePalettes.KeysOf(PalettesInUse());
			int variants = 0;
			foreach (StructureSurfaceKey key in keys)
			{
				WriteStructureMaterial(c, shader, key);
				variants += key.Surface.Length > 0 ? 1 : 0;
			}
			if (keys.Count > 0)
			{
				Debug.Log($"[Biome art] Structure palettes: {keys.Count} material key(s) beyond the kit's, {keys.Count * StructurePieces.Finishes.Length} material(s) with their finishes"
					+ (variants > 0 ? $"; {variants} name a surface variant, drawn with the shared surface until the per-biome surface variants are generated" : string.Empty) + ".");
			}
		}

		/// <summary>
		/// Every structure palette a cut may lay: each spec entry's, then each biome template's in the project, then each world
		/// body's cloud-world palette (its sky docks, airships and rope bridges, 2026-10-18).
		/// </summary>
		private static IEnumerable<FishMMO.Shared.Biomes.BiomeStructurePalette> PalettesInUse()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				yield return entry.StructurePalette();
				// The linear features that name their own material (2026-10-18, I2f): their walls', fences' and gates' keys.
				foreach (FishMMO.Shared.Biomes.BiomeStructurePalette linear in StructurePieces.LinearPalettes(entry.LinearFeatures(null)))
				{
					yield return linear;
				}
			}
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FishMMO.Shared.Biomes.BiomeTemplate)))
			{
				var biome = AssetDatabase.LoadAssetAtPath<FishMMO.Shared.Biomes.BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome != null && biome.StructurePalette != null && !biome.StructurePalette.IsEmpty)
				{
					yield return biome.StructurePalette;
				}
				if (biome != null)
				{
					foreach (FishMMO.Shared.Biomes.BiomeStructurePalette linear in StructurePieces.LinearPalettes(biome.LinearFeatures))
					{
						yield return linear;
					}
				}
			}
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FishMMO.Shared.Celestial.WorldBody)))
			{
				var body = AssetDatabase.LoadAssetAtPath<FishMMO.Shared.Celestial.WorldBody>(AssetDatabase.GUIDToAssetPath(guid));
				FishMMO.Shared.Biomes.BiomeStructurePalette palette = body != null && body.CloudWorldStructures != null ? body.CloudWorldStructures.Palette : null;
				if (palette != null && !palette.IsEmpty)
				{
					yield return palette;
				}
			}
		}

		/// <summary>
		/// One key's material in every finish: its material's surface maps, its tint as the base colour, its moss. A kit key is
		/// named and built exactly as the kit's materials always were.
		/// </summary>
		private static void WriteStructureMaterial(Context c, Shader shader, StructureSurfaceKey key)
		{
			StructureMaterial material = key.Material;
			// The surface variant (I9's per-biome texture variants) names the material; until those textures exist it is drawn
			// with the surface every biome shares.
			string surface = StructureSurfaces.SurfaceOf(material);
			Texture2D normal = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Normal"));
			Texture2D mask = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Mask"));
			float scale = 1f / StructureSurfaces.TileMetres(surface);
			Color tint = key.Colour;
			Texture2D plainAlbedo = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Albedo"));
			foreach (StructureFinish f in StructurePieces.Finishes)
			{
				StructureFinish finish = f;
				Texture2D albedo = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Albedo", finish));
				WriteMaterial(c, key.MaterialName(finish), shader, mat =>
				{
					// Moss as the material and its finish allow (MossAmounts): stone, timber and thatch green over in a
					// damp scene; the Mossy finish keeps its baked moss and takes the most of the layer on top.
					RockLit(mat, albedo, normal, mask, scale, StructureSurfaces.MossOf(material, finish));
					mat.SetColor("_BaseColor", tint);
					// Weathered as a building, not set into the ground as a rock (FishStructureWeathering.hlsl); a pane lit at night.
					mat.SetFloat("_FishStructure", StructureSurfaces.StructureFlag(material));
					if (material == StructureMaterial.Glass)
					{
						Glass(mat, tint);
					}
					else if (material == StructureMaterial.Lamplight)
					{
						Lamplight(mat, plainAlbedo);
					}
					else if (StructureSurfaces.TryGlow(material, out SceneGlowHue glow))
					{
						Glowlight(mat, plainAlbedo, glow);
					}
				});
			}
		}

		/// <summary>
		/// A lantern's lit pane (<see cref="StructureMaterial.Lamplight"/>, 2026-10-13): the plain surface's own albedo as its
		/// emission map, so the glow is a little uneven as horn is, in a warm HDR colour, lit always. Emission is added after
		/// the lighting (and after the cave darkness's occlusion), so it reads as a light deep underground where the sky's
		/// ambient is all but gone; in every finish the plain albedo, so a charred or mossy ruin's lamp burns as bright.
		/// </summary>
		private static void Lamplight(Material mat, Texture2D plainAlbedo)
		{
			mat.SetTexture("_EmissionMap", plainAlbedo);
			mat.SetColor("_EmissionColor", new Color(1.9f, 1.05f, 0.42f, 1f));
			mat.EnableKeyword("_EMISSION");
			mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			mat.SetFloat("_Smoothness", 0.45f);
			mat.SetFloat("_FishMoss", 0f);
		}

		/// <summary>
		/// A cold glow's part (a rune stone's runes, a glowjar's mote; 2026-10-18, I6g): lit as a lamp's pane by its emission, in
		/// its glow's own cool colour (SceneGlow.Colour) a little over one, which the shader scales by the very pulse and dusk
		/// fade its piece's light has (_FishStructure = 3, FishStructureWeathering.hlsl FishStructureGlowLight): dark by day,
		/// faint from before dusk, full at night and underground.
		/// </summary>
		private static void Glowlight(Material mat, Texture2D plainAlbedo, SceneGlowHue glow)
		{
			Color c = SceneGlow.Colour(glow);
			mat.SetTexture("_EmissionMap", plainAlbedo);
			mat.SetColor("_EmissionColor", new Color(c.r * 1.7f, c.g * 1.7f, c.b * 1.7f, 1f));
			mat.EnableKeyword("_EMISSION");
			mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			mat.SetFloat("_Smoothness", 0.5f);
			mat.SetFloat("_FishMoss", 0f);
		}

		/// <summary>
		/// A window pane you can see through (2026-10-11): alpha-blended, no depth written and no shadow cast, so a lit
		/// room shows through it and the GPU occlusion (built from the opaque depth) still sees the room behind it — what is
		/// inside draws when a window or a door shows it and is culled when the walls hide it. Its night glow is unchanged.
		/// </summary>
		private static ShadowCastingMode[] NoShadows(int count)
		{
			var none = new ShadowCastingMode[count];
			for (int i = 0; i < count; i++)
			{
				none[i] = ShadowCastingMode.Off;
			}
			return none;
		}

		private static void Glass(Material mat, Color tint)
		{
			mat.SetFloat("_Surface", 1f);
			mat.SetFloat("_Blend", 0f);
			mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
			mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
			mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			mat.SetFloat("_ZWrite", 0f);
			mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			mat.DisableKeyword("_ALPHATEST_ON");
			mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			mat.SetOverrideTag("RenderType", "Transparent");
			mat.SetShaderPassEnabled("ShadowCaster", false);
			mat.SetShaderPassEnabled("DepthOnly", false);
			mat.SetShaderPassEnabled("DepthNormals", false);
			// Old crown glass: greenish, a little cloudy, more than half see-through.
			mat.SetColor("_BaseColor", new Color(tint.r * 0.9f, tint.g, tint.b * 0.95f, 0.32f));
			mat.SetFloat("_Smoothness", 0.85f);
		}

		/// <summary>
		/// A piece's meshes (every variant, every level) and its prefabs (every variant in every finish), then the roof variants
		/// of its houses that <paramref name="roofs"/> names (StructureRoofs), each the same in every way but its roof.
		/// </summary>
		private static void WriteStructurePiece(Context c, StructurePiece piece, List<StructureRoofVariant> roofs)
		{
			ShadowCastingMode[] shadows = LodShadows(StructurePieces.LevelCount);
			for (int v = 0; v < piece.Variants; v++)
			{
				WriteStructureVariant(c, piece, v, null, shadows);
			}
			if (roofs == null)
			{
				return;
			}
			foreach (StructureRoofVariant roof in roofs)
			{
				if (string.Equals(roof.Piece, piece.Id, StringComparison.Ordinal) && roof.Variant >= 0 && roof.Variant < piece.Variants)
				{
					WriteStructureVariant(c, piece, roof.Variant, roof.Pitch, shadows);
				}
			}
		}

		/// <summary>One variant's meshes and its prefab in every finish, its house roofs at <paramref name="roof"/> (null: the kit's).</summary>
		private static void WriteStructureVariant(Context c, StructurePiece piece, int v, StructureRoofPitch? roof, ShadowCastingMode[] shadows)
		{
			var meshes = new Mesh[StructurePieces.LevelCount];
			var materials = new StructureMaterial[StructurePieces.LevelCount][];
			for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
			{
				// Always built (milliseconds): the prefab needs each level's material list even when the mesh is current.
				StructureMesh built = StructurePieces.Build(piece, v, lod, c.Seed, roof);
				materials[lod] = built.Materials;
				meshes[lod] = WriteMesh(c, built.Mesh, StructurePieces.MeshName(piece.Id, v, lod, roof), true);
			}
			foreach (StructureFinish f in StructurePieces.Finishes)
			{
				StructureFinish finish = f;
				var mats = new Material[meshes.Length][];
				for (int lod = 0; lod < meshes.Length; lod++)
				{
					mats[lod] = Array.ConvertAll(materials[lod], m => MaterialOrNull(c, StructurePieces.MaterialName(m, finish)));
				}
				bool collides = piece.Collides;
				WritePrefab(c, StructurePieces.PrefabName(piece.Id, v, finish, roof), root =>
				{
					// Furniture stands under a roof that already shades it: it casts no sun shadow (nor costs the shadow map one).
					Lods(root, meshes, mats, StructureLodHeights, piece.HasTag("furniture") ? NoShadows(meshes.Length) : shadows);
					if (collides)
					{
						AddBoulderCollider(root, meshes);
					}
				});
			}
		}

		// ── Structures alone ──────────────────────────────────────────

		/// <summary>
		/// Generates only the structure kit — its surfaces, materials, meshes and prefabs — start to finish, for a batch run
		/// that must not spend minutes on the rest of the art (<see cref="StructureKitGenerator.GenerateFromCommandLine"/>).
		/// </summary>
		/// <remarks>
		/// Never records a complete generation: the rest of the art was not looked at, so the editor-load check still
		/// regenerates everything when the generator's sources changed (which they did when this kit was added).
		/// </remarks>
		internal static Report GenerateStructuresOnly(ProceduralArtMode mode, int seed)
		{
			if (job != null)
			{
				Finish(job, "superseded by a structures-only generation");
			}
			var c = new Context
			{
				Report = new Report { Mode = mode },
				Mode = mode,
				Seed = seed,
				Fingerprint = ProceduralArtPayload.CurrentFingerprint(seed),
			};
			foreach (string folder in new[] { ProceduralArtCatalogue.TexturesFolder, ProceduralArtCatalogue.MeshesFolder,
				ProceduralArtCatalogue.MaterialsFolder, ProceduralArtCatalogue.StructurePrefabsFolder })
			{
				WorldEditorAssets.EnsureFolder(folder);
			}
			Stopwatch clock = Stopwatch.StartNew();
			AssetDatabase.DisallowAutoRefresh();
			EditorApplication.LockReloadAssemblies();
			TerrainArrayAutoBake.Suspend();
			string label = "Starting";
			int index = 0, total = StructureStepCount() + 2;
			try
			{
				using (IEnumerator<string> steps = StructuresOnlySteps(c).GetEnumerator())
				{
					while (true)
					{
						EditorUtility.DisplayProgressBar(ProgressTitle, label, Mathf.Clamp01(index / (float)total));
						long started = Stopwatch.GetTimestamp();
						bool more = steps.MoveNext();
						Flush(c);
						c.Report.StepSeconds.Add(new KeyValuePair<string, double>(label, (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency));
						if (!more)
						{
							break;
						}
						label = steps.Current;
						index++;
					}
				}
				c.Report.Completed = true;
			}
			catch (Exception e)
			{
				c.Report.Stopped = $"failed at '{label}': {e.Message}";
				c.Report.Problems.Add(e.ToString());
				Debug.LogException(e);
			}
			finally
			{
				try
				{
					Flush(c);
					ImportPendingTextures(c);
					AssetDatabase.SaveAssets();
					if (c.Built != null)
					{
						ProceduralArtPayload.RecordBuilt(c.Built);
					}
					ProceduralArtPayload.ClearStaging();
					ProceduralArtVariants.Ensure(c.Report.Problems);
				}
				finally
				{
					EditorUtility.ClearProgressBar();
					AssetDatabase.AllowAutoRefresh();
					EditorApplication.UnlockReloadAssemblies();
					TerrainArrayAutoBake.Resume();
					c.Report.Seconds = clock.Elapsed.TotalSeconds;
				}
			}
			return c.Report;
		}

		private static IEnumerable<string> StructuresOnlySteps(Context c)
		{
			foreach (string step in StructureSurfaceSteps(c))
			{
				yield return step;
			}
			yield return "Importing textures";
			ImportPendingTextures(c);
			yield return "Verifying textures";
			VerifyTextures(c);
			foreach (string step in StructureSteps(c))
			{
				yield return step;
			}
		}
	}
}
#endif
