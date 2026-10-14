#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The generation's heavy pure work on every core: the cliff sections' fields and decimation, the rock formations, the
	/// trees and their software-rendered billboards, the bushes, the ground surfaces. A full run spent most of its ten
	/// minutes there on one thread (cliff sections 220 s, trees 111 s, formations 64 s, bushes 48 s, ground 25 s).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Build in parallel, write in order.</b> The builders are pure C# (mesh builders, pixel arrays, seeded by the item,
	/// no shared state but read-only tables), so they run on worker threads; everything the engine owns — a Mesh, a
	/// Texture2D, the asset database — stays on the editor's thread, written in the same order as before, so every file,
	/// path and GUID comes out exactly as a serial run made it.
	/// </para>
	/// <para>
	/// <b>Only what is due.</b> An item is built only when one of its files is to be written (<see cref="DueFor"/>: its
	/// family rebuilds, or the file is missing); a current one is loaded from disk instead, where it was rebuilt and thrown
	/// away before.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.BiomeArtGenerator.GenerateFromCommandLine</c>: generates the art the
		/// generator's changes call for (<c>-artEverything</c>: every family), logs the report and, in batch mode, exits 0
		/// when it completed without problems, 1 otherwise.
		/// </summary>
		public static void GenerateFromCommandLine()
		{
			int code = 1;
			try
			{
				int seed = ProceduralArtCatalogue.DefaultSeed;
				bool everything = Array.IndexOf(Environment.GetCommandLineArgs(), "-artEverything") >= 0;
				ProceduralArtPayload.State state = ProceduralArtPayload.Check(seed);
				UnityEngine.Debug.Log("[Biome art] Before: " + state);
				Report report = Generate(ProceduralArtMode.Full, seed, everything ? null : FamiliesToRebuild(state));
				UnityEngine.Debug.Log("[Biome art] " + report);
				code = report.Completed && report.Problems.Count == 0 ? 0 : 1;
			}
			catch (Exception e)
			{
				UnityEngine.Debug.LogException(e);
			}
			if (Application.isBatchMode)
			{
				UnityEditor.EditorApplication.Exit(code);
			}
		}

		/// <summary>Worker threads for the pure builds: every core but one, which the editor keeps.</summary>
		private static int Workers => Math.Max(1, Environment.ProcessorCount - 1);

		/// <summary>
		/// Builds every item across the worker threads and returns the results in the items' order. The first failure is
		/// thrown again here, on the calling thread, with its own stack.
		/// </summary>
		public static TResult[] BuildAll<TItem, TResult>(IReadOnlyList<TItem> items, Func<TItem, TResult> build)
		{
			var results = new TResult[items.Count];
			if (items.Count == 0)
			{
				return results;
			}
			if (items.Count == 1 || Workers == 1)
			{
				for (int i = 0; i < items.Count; i++)
				{
					results[i] = build(items[i]);
				}
				return results;
			}
			ExceptionDispatchInfo failure = null;
			Parallel.For(0, items.Count, new ParallelOptions { MaxDegreeOfParallelism = Workers }, (i, state) =>
			{
				try
				{
					results[i] = build(items[i]);
				}
				catch (Exception e)
				{
					Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(e), null);
					state.Stop();
				}
			});
			failure?.Throw();
			return results;
		}

		/// <summary>Whether the step <paramref name="stepLabel"/> would write <paramref name="path"/> (<see cref="ShouldWrite"/> as that step).</summary>
		private static bool DueFor(Context c, string stepLabel, string path)
		{
			string was = c.StepLabel;
			c.StepLabel = stepLabel;
			try
			{
				return ShouldWrite(c, path);
			}
			finally
			{
				c.StepLabel = was;
			}
		}

		/// <summary>A mesh: written when due, else the one on disk (nothing built for it).</summary>
		private static Mesh WriteOrLoadMesh(Context c, MeshBuilder built, string name, bool closed)
		{
			return built != null ? WriteMesh(c, built, name, closed) : UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralArtCatalogue.MeshPath(name));
		}

		// ── Ground ────────────────────────────────────────────────────

		/// <summary>Every due ground surface, synthesised on all cores at the first ground step; each step takes its own.</summary>
		private static void PrebuildGround(Context c)
		{
			if (c.GroundMaps != null)
			{
				return;
			}
			c.GroundMaps = new Dictionary<string, SurfaceMaps>(StringComparer.Ordinal);
			var due = new List<SurfaceRecipe>();
			foreach (SurfaceRecipe recipe in SurfaceCatalogue.GroundRecipes)
			{
				string step = "Ground: " + recipe.Name;
				if (DueFor(c, step, ProceduralArtCatalogue.GroundTexture(recipe.Name, "Albedo"))
					|| DueFor(c, step, ProceduralArtCatalogue.GroundTexture(recipe.Name, "Normal"))
					|| DueFor(c, step, ProceduralArtCatalogue.GroundTexture(recipe.Name, "Mask")))
				{
					due.Add(recipe);
				}
			}
			int seed = c.Seed, size = ProceduralArtCatalogue.GroundSize;
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			SurfaceMaps[] maps = BuildAll(due, r => SurfaceSynth.Generate(in r, size, seed));
			if (due.Count > 0)
			{
				UnityEngine.Debug.Log($"[Biome art] Ground: {due.Count} surfaces synthesised on {Workers} workers in {(System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency:0.0}s.");
			}
			for (int i = 0; i < due.Count; i++)
			{
				c.GroundMaps[due[i].Name] = maps[i];
			}
		}

		// ── Bushes ────────────────────────────────────────────────────

		/// <summary>Every due bush level, built on all cores at the first bush step, keyed by mesh name.</summary>
		private static void PrebuildBushes(Context c)
		{
			if (c.BushLevels != null)
			{
				return;
			}
			c.BushLevels = new Dictionary<string, MeshBuilder>(StringComparer.Ordinal);
			var due = new List<(BushSpecies species, int lod, string name)>();
			foreach (BushSpecies species in ProceduralArtCatalogue.Bushes)
			{
				for (int lod = 0; lod < BushMeshes.Levels; lod++)
				{
					string name = ProceduralArtCatalogue.BushMesh(species.Name, lod);
					if (DueFor(c, "Bush: " + species.Name, ProceduralArtCatalogue.MeshPath(name)))
					{
						due.Add((species, lod, name));
					}
				}
			}
			int seed = c.Seed;
			MeshBuilder[] built = BuildAll(due, d => BushMeshes.Build(in d.species, d.lod, seed));
			for (int i = 0; i < due.Count; i++)
			{
				c.BushLevels[due[i].name] = built[i];
			}
		}

		// ── Trees ─────────────────────────────────────────────────────

		/// <summary>A tree's pure work: its two levels, its software-rendered billboard and its trunk's collision shape.</summary>
		internal sealed class TreeBuild
		{
			public MeshBuilder Lod0, Lod1, Trunk;
			public BillboardImpostor.Result Billboard;
			public bool Leaves;
		}

		/// <summary>The paths a tree writes (any due rebuilds the whole tree: they are made together).</summary>
		private static IEnumerable<string> TreePaths(TreeSpecies species)
		{
			string prefix = ProceduralArtCatalogue.TreePrefab(species.Name);
			yield return ProceduralArtCatalogue.BillboardTexture(species.Name);
			yield return ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.BillboardMaterial(species.Name));
			yield return ProceduralArtCatalogue.MeshPath(prefix + "_LOD0");
			yield return ProceduralArtCatalogue.MeshPath(prefix + "_LOD1");
			yield return ProceduralArtCatalogue.MeshPath(prefix + "_Billboard");
			// Not in the catalogue's list, but a tree that had one must not lose it to a skipped rebuild.
			yield return ProceduralArtCatalogue.MeshPath(prefix + "_Collision");
			yield return ProceduralArtCatalogue.PrefabPath(prefix);
		}

		/// <summary>Whether any of a tree's files is due.</summary>
		private static bool TreeDue(Context c, TreeSpecies species)
		{
			string step = "Tree: " + species.Name;
			foreach (string path in TreePaths(species))
			{
				if (DueFor(c, step, path))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Every due tree, built (and its billboard rendered) on all cores at the first tree step.</summary>
		private static void PrebuildTrees(Context c)
		{
			if (c.TreeBuilds != null)
			{
				return;
			}
			c.TreeBuilds = new Dictionary<string, TreeBuild>(StringComparer.Ordinal);
			var due = new List<TreeSpecies>();
			foreach (TreeSpecies species in ProceduralArtCatalogue.Trees)
			{
				if (TreeDue(c, species))
				{
					due.Add(species);
				}
			}
			int seed = c.Seed;
			// Read only on the workers: the step that made them is done.
			Dictionary<string, Color32[]> barks = c.BarkPixels;
			Color32[] atlas = c.AtlasPixels;
			TreeBuild[] built = BuildAll(due, s => BuildTree(in s, seed, barks.TryGetValue(s.BarkFamily, out Color32[] bp) ? bp : null, atlas));
			for (int i = 0; i < due.Count; i++)
			{
				c.TreeBuilds[due[i].Name] = built[i];
			}
		}

		/// <summary>A tree's pure work (<see cref="TreeBuild"/>): pure C#, safe on a worker thread.</summary>
		internal static TreeBuild BuildTree(in TreeSpecies species, int seed, Color32[] bark, Color32[] atlas)
		{
			MeshBuilder lod0 = TreeMeshes.Build(in species, 0, seed);
			// The reduced level keeps the full tree's foliage cover (fewer, wider cards), or distant trees go bare.
			MeshBuilder lod1 = TreeMeshes.BuildReduced(in species, 1, seed, lod0);
			bool leaves = lod0.Submeshes[TreeMeshes.LeafSubmesh].Count > 0;
			if (!leaves)
			{
				lod0.Submeshes.RemoveAt(TreeMeshes.LeafSubmesh);
				lod1.Submeshes.RemoveAt(TreeMeshes.LeafSubmesh);
			}
			// The billboard: the full tree, rendered in software from the same textures.
			BillboardImpostor.Result billboard = BillboardImpostor.Render(lod0, (sub, uv, vc) =>
			{
				Color tex = sub == TreeMeshes.BarkSubmesh
					? (bark != null ? FoliageAtlas.Sample(bark, ProceduralArtCatalogue.BarkSize, uv, true) : Color.gray)
					: (atlas != null ? FoliageAtlas.Sample(atlas, ProceduralArtCatalogue.AtlasSize, uv, false) : Color.green);
				if (sub == TreeMeshes.BarkSubmesh)
				{
					tex.a = 1f;
				}
				// The vertex colour is stored linear (PlantParts.C32) and the textures in sRGB: taken back to sRGB to be
				// multiplied with them, so the baked billboard is the colour the shader draws the tree.
				Color tint = vc.gamma;
				return new Color(tex.r * tint.r, tex.g * tint.g, tex.b * tint.b, tex.a);
			}, ProceduralArtCatalogue.BillboardHeight, species.Height, species.Deciduous ? 0.35f : 1f);
			// The trunk as drawn at the first reduced level: its own shape, at the fewest triangles that still are it.
			MeshBuilder trunk = TrunkCollision(lod1, in species);
			return new TreeBuild { Lod0 = lod0, Lod1 = lod1, Billboard = billboard, Trunk = trunk, Leaves = leaves };
		}
	}
}
#endif
