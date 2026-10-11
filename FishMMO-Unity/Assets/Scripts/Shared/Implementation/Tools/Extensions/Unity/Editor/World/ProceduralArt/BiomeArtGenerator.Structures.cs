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
		private static int StructureStepCount() => 1 + 1 + StructurePieces.All.Count;

		/// <summary>The structure surfaces and their finish albedos: one step, before the texture import.</summary>
		private static IEnumerable<string> StructureSurfaceSteps(Context c)
		{
			yield return "Structure surfaces";
			WriteStructureSurfaces(c);
		}

		/// <summary>Materials, then one step a piece: after the deadwood.</summary>
		private static IEnumerable<string> StructureSteps(Context c)
		{
			yield return "Structure materials";
			WriteStructureMaterials(c);
			foreach (StructurePiece piece in StructurePieces.All)
			{
				yield return "Structure: " + piece.Id;
				WriteStructurePiece(c, piece);
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
				StructureMaterial material = m;
				string surface = StructureSurfaces.SurfaceOf(material);
				Texture2D normal = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Normal"));
				Texture2D mask = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Mask"));
				float scale = 1f / StructureSurfaces.TileMetres(surface);
				Color tint = StructureSurfaces.TintOf(material);
				foreach (StructureFinish f in StructurePieces.Finishes)
				{
					StructureFinish finish = f;
					Texture2D albedo = LoadTexture(ProceduralArtCatalogue.StructureTexture(surface, "Albedo", finish));
					WriteMaterial(c, StructurePieces.MaterialName(material, finish), shader, mat =>
					{
						RockLit(mat, albedo, normal, mask, scale);
						mat.SetColor("_BaseColor", tint);
					});
				}
			}
		}

		/// <summary>A piece's meshes (every variant, every level) and its prefabs (every variant in every finish).</summary>
		private static void WriteStructurePiece(Context c, StructurePiece piece)
		{
			ShadowCastingMode[] shadows = LodShadows(StructurePieces.LevelCount);
			for (int v = 0; v < piece.Variants; v++)
			{
				var meshes = new Mesh[StructurePieces.LevelCount];
				var materials = new StructureMaterial[StructurePieces.LevelCount][];
				for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
				{
					// Always built (milliseconds): the prefab needs each level's material list even when the mesh is current.
					StructureMesh built = StructurePieces.Build(piece, v, lod, c.Seed);
					materials[lod] = built.Materials;
					meshes[lod] = WriteMesh(c, built.Mesh, StructurePieces.MeshName(piece.Id, v, lod), true);
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
					WritePrefab(c, StructurePieces.PrefabName(piece.Id, v, finish), root =>
					{
						Lods(root, meshes, mats, StructureLodHeights, shadows);
						if (collides)
						{
							AddBoulderCollider(root, meshes);
						}
					});
				}
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
