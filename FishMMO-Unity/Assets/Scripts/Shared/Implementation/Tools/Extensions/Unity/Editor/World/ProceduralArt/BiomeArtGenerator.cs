#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How much a generation writes.</summary>
	public enum ProceduralArtMode
	{
		/// <summary>Only generated files with nothing on their path, or with the wrong GUID. Changes nothing that is current.</summary>
		MissingOnly,
		/// <summary>
		/// Every generated file — payload and wrappers — rebuilt, and rewritten wherever its bytes
		/// change. What the editor-load check and the build run when the generator has changed.
		/// </summary>
		Full,
	}

	/// <summary>
	/// Writes every procedural biome asset: ground textures and terrain layers, bark, the foliage
	/// atlas, rocks, detail plants and trees, with their materials and prefabs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing generated is committed (Jim, 2026-10-02).</b> Textures and meshes (the payload) and
	/// the terrain layers, materials and prefabs built on them (the wrappers) are all build output:
	/// gitignored, regenerated on every machine — on editor load and first in every build — and
	/// each with a GUID that is a function of its path (<see cref="ProceduralArtPayload"/>) and object
	/// IDs that are functions of what they are: the fixed main ID of a texture, mesh, material or
	/// terrain layer, and for a prefab an ID per object from its place in the hierarchy
	/// (<see cref="ProceduralArtFileIds"/>). So committed scenes, terrain data and biomes that
	/// reference generated art resolve on any clone once it has generated, with nothing rewritten.
	/// </para>
	/// <para>
	/// <b>Everything generated is the generator's.</b> There is no "somebody replaced this wrapper,
	/// keep it" rule any more: a hand edit to a generated file would live on one machine only. Art is
	/// replaced through <c>Assets/LOCAL</c> (a <see cref="FishMMO.Shared.Biomes.BiomeLocalArt"/> sidecar,
	/// read by the terrain arrays) or by pointing a biome slot or scatter rule at a committed asset
	/// outside the generated folders — which the authoring pass then treats as authored, as it never
	/// treats a generated one. Every generated file is rebuilt from scratch (a fresh material, layer or
	/// prefab hierarchy, never an edit of the old one, so the result cannot depend on history) and
	/// written only where its bytes change.
	/// </para>
	/// <para>
	/// <b>Old random IDs are migrated first.</b> Generated assets from before deterministic IDs carry
	/// random GUIDs that committed files reference; every generation starts by running
	/// <see cref="ProceduralArtMigration"/> when <see cref="ProceduralArtMigration.NeedsMigration"/>
	/// finds one, and stops rather than regenerate over them when it cannot.
	/// </para>
	/// <para>
	/// <b>Deterministic.</b> The same seed gives the same bytes: every asset seeds itself from its
	/// own name (<see cref="ProceduralNoise.SeedFor"/>), so adding an asset never changes another.
	/// The parallel loops write disjoint rows and reduce nothing, so thread scheduling cannot change
	/// a result; nothing reads the clock, <c>UnityEngine.Random</c> or a dictionary's iteration order.
	/// </para>
	/// <para>
	/// <b>A job or a call.</b> <see cref="StartGenerate"/> works through the art one step per editor
	/// update behind a cancellable progress bar, like the world map bake; the editor-load check
	/// (<see cref="ProceduralArtAutoGenerate"/>) uses it. <see cref="Generate(ProceduralArtMode, int)"/>
	/// runs the same steps to the end before returning, for the build and the authoring pass.
	/// </para>
	/// <para>
	/// <b>Cost, and the asset database.</b> The computing is minutes: the ground textures dominate
	/// (thirty-four 1024² families, a few noise octaves per texel on every core), and meshes, the
	/// billboards' software render and prefabs are seconds. What made a full run take 2604 s
	/// (2026-10-02) was the asset database: every write outside a
	/// <c>StartAssetEditing</c>/<c>StopAssetEditing</c> batch is a refresh of its own, each about
	/// 0.33 s of fixed cost (the editor log's "PostAssetChangesProfiler"), and the run made 7538
	/// of them — about four per new mesh (create staged, save, delete staged, import), three per
	/// prefab (save staged, delete staged, import) and one per label. So nothing is written to
	/// the asset database one file at a time any more: writers queue their work on the
	/// <see cref="Context"/> and each step ends with <see cref="Flush"/>, which saves the step's
	/// changed assets in one batch, creates its new ones in two (stage all, then import every
	/// copy), saves its prefabs (in one batch where Unity allows) and imports them and deletes the
	/// staged files in one more. Labels are written into the .meta text. The report times every
	/// step and counts the trips.
	/// </para>
	/// <para>
	/// <b>Never a wrapper with an empty slot.</b> The textures are imported and checked as 2D
	/// textures before any wrapper is built ("Verifying textures"); a material, terrain layer or
	/// prefab whose required texture, mesh or material is missing is not written — the previous
	/// file is kept, the run reports it and stays stale, so the next one retries — and after every
	/// run <see cref="ProceduralArtWrapperCheck"/> scans every generated material and terrain layer
	/// on disk for an empty required slot.
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>What a generation did.</summary>
		public sealed class Report
		{
			public ProceduralArtMode Mode;
			public readonly List<string> Wrote = new List<string>();
			public readonly List<string> Problems = new List<string>();
			public readonly SortedDictionary<string, int> Triangles = new SortedDictionary<string, int>(StringComparer.Ordinal);
			/// <summary>Files regenerated whose bytes came out exactly as they were.</summary>
			public int Unchanged;
			public long TextureBytes;
			public double Seconds;
			/// <summary>True when every step ran.</summary>
			public bool Completed;
			/// <summary>Why it stopped early (cancelled, failed), or null.</summary>
			public string Stopped;
			/// <summary>Seconds per step, in order, its flush included.</summary>
			public readonly List<KeyValuePair<string, double>> StepSeconds = new List<KeyValuePair<string, double>>();
			/// <summary>
			/// Asset-database round trips this run asked for: batches, and the single writes that could
			/// not be batched. Each costs a refresh (about 0.3 s of fixed overhead in this project), so
			/// this, not the file count, is what a run's time scales with.
			/// </summary>
			public int AssetDatabaseTrips;

			/// <summary>Time by step group ("Tree: Oak" and "Tree: Pine" are both "Tree"), slowest first.</summary>
			public List<KeyValuePair<string, double>> SecondsByGroup()
			{
				var groups = new Dictionary<string, double>(StringComparer.Ordinal);
				foreach (KeyValuePair<string, double> step in StepSeconds)
				{
					int colon = step.Key.IndexOf(':');
					string group = colon > 0 ? step.Key.Substring(0, colon) : step.Key;
					groups.TryGetValue(group, out double t);
					groups[group] = t + step.Value;
				}
				var list = new List<KeyValuePair<string, double>>(groups);
				list.Sort((a, b) => b.Value.CompareTo(a.Value));
				return list;
			}

			public override string ToString()
			{
				var sb = new StringBuilder();
				string state = Completed ? "complete" : $"stopped ({Stopped ?? "unknown"})";
				sb.AppendLine($"Biome art ({Mode}, {state}): wrote {Wrote.Count} assets in {Seconds:0.0}s; {Unchanged} regenerated unchanged; {Problems.Count} problems; {AssetDatabaseTrips} asset-database trips.");
				sb.AppendLine($"Generated texture memory (GPU, block-compressed with mips): {TextureBytes / (1024f * 1024f):0.0} MB.");
				if (StepSeconds.Count > 0)
				{
					sb.Append("Time by step:");
					foreach (KeyValuePair<string, double> g in SecondsByGroup())
					{
						sb.Append($" {g.Key} {g.Value:0.0}s;");
					}
					sb.AppendLine();
				}
				foreach (string p in Problems) sb.AppendLine("PROBLEM " + p);
				foreach (string w in Wrote) sb.AppendLine("wrote " + w);
				foreach (KeyValuePair<string, int> t in Triangles) sb.AppendLine($"tris {t.Key}: {t.Value}");
				return sb.ToString();
			}
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Generate biome art", Section = "Art", Order = 0,
			Tooltip = "Rebuilds every procedural texture, mesh, terrain layer, material and prefab (all gitignored build output with path-derived IDs) and rewrites each file whose bytes changed. Runs by itself on editor load and before builds when the generator changed; this forces it. A cancellable job of a few minutes; its report times every step.",
			Confirm = "Regenerate all procedural biome art? Every generated file is rebuilt and rewritten where it changed. Nothing outside the generated folders is touched, except that generated art made before deterministic IDs is first migrated, rewriting committed references to it (backed up under Library/FishMMO).")]
		public static void GenerateFromDashboard()
		{
			StartGenerate(ProceduralArtMode.Full, ProceduralArtCatalogue.DefaultSeed, null);
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Generate missing biome art", Section = "Art", Order = 1,
			Tooltip = "Writes only the procedural biome assets that do not exist yet (or carry the wrong GUID), and any missing built terrain layer. Changes nothing else on disk.")]
		public static void GenerateMissingFromDashboard()
		{
			StartGenerate(ProceduralArtMode.MissingOnly, ProceduralArtCatalogue.DefaultSeed, null);
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Check biome art payload", Section = "Art", Order = 2,
			Tooltip = "Reports whether any generated art is missing, has wrong GUIDs, needs migrating to deterministic IDs or was made by older generator code. Changes nothing.")]
		public static void CheckFromDashboard()
		{
			Debug.Log("[Biome art] " + ProceduralArtPayload.Check(ProceduralArtCatalogue.DefaultSeed));
		}

		// ── Running ───────────────────────────────────────────────────

		private const string ProgressTitle = "Generating biome art";

		/// <summary>One generation in progress.</summary>
		private sealed class Run
		{
			public Context Context;
			public IEnumerator<string> Steps;
			public string Label = "Starting";
			public int Index;
			public int Total;
			public Stopwatch Clock;
			public Action<Report> Done;
			public float Fraction => Total <= 0 ? 1f : Mathf.Clamp01(Index / (float)Total);

			/// <summary>
			/// Does the next step — the work after the current label's <c>yield</c> — then flushes what
			/// it queued, and times both against that label. False when there are none left.
			/// </summary>
			public bool Advance()
			{
				string ran = Label;
				long started = Stopwatch.GetTimestamp();
				bool more = Steps.MoveNext();
				if (more)
				{
					Label = Steps.Current;
					Index++;
				}
				Flush(Context);
				Context.Report.StepSeconds.Add(new KeyValuePair<string, double>(ran, (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency));
				if (!more)
				{
					Context.Report.Completed = true;
				}
				return more;
			}
		}

		private static Run job;

		/// <summary>True while a generation job runs. The build and other tools wait or take it over.</summary>
		public static bool IsBusy => job != null;

		/// <summary>Raised when a generation job starts or finishes.</summary>
		public static event Action BusyChanged;

		/// <summary>Generates the art, start to finish, before returning.</summary>
		/// <param name="missingOnly">Write only paths with nothing on them; otherwise a full generation.</param>
		public static Report Generate(bool missingOnly, int seed) => Generate(missingOnly ? ProceduralArtMode.MissingOnly : ProceduralArtMode.Full, seed);

		/// <summary>
		/// Generates the art, start to finish, before returning. A job already running is stopped
		/// first; this run redoes everything it would have done.
		/// </summary>
		public static Report Generate(ProceduralArtMode mode, int seed)
		{
			if (job != null)
			{
				Finish(job, "superseded by a synchronous generation");
			}
			Run run = Begin(mode, seed);
			string problem = null;
			try
			{
				do
				{
					EditorUtility.DisplayProgressBar(ProgressTitle, run.Label, run.Fraction);
				}
				while (run.Advance());
			}
			catch (Exception e)
			{
				problem = "failed: " + e.Message;
				run.Context.Report.Problems.Add(e.ToString());
				Debug.LogException(e);
			}
			finally
			{
				End(run, problem);
			}
			return run.Context.Report;
		}

		/// <summary>
		/// Starts a generation that does one step per editor update behind a cancellable progress
		/// bar. <paramref name="done"/> receives the report. Returns false when one is already running.
		/// </summary>
		public static bool StartGenerate(ProceduralArtMode mode, int seed, Action<Report> done)
		{
			if (job != null)
			{
				Debug.Log("[Biome art] A generation is already running.");
				return false;
			}
			Run run = Begin(mode, seed);
			run.Done = done;
			job = run;
			EditorApplication.update += Tick;
			EditorApplication.playModeStateChanged += OnPlayMode;
			BusyChanged?.Invoke();
			return true;
		}

		private static void Tick()
		{
			Run run = job;
			if (run == null)
			{
				EditorApplication.update -= Tick;
				return;
			}
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				return;
			}
			if (EditorUtility.DisplayCancelableProgressBar(ProgressTitle, run.Label, run.Fraction))
			{
				Finish(run, "cancelled");
				return;
			}
			try
			{
				if (!run.Advance())
				{
					Finish(run, null);
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				run.Context.Report.Problems.Add(e.ToString());
				Finish(run, $"failed at '{run.Label}': {e.Message}");
			}
		}

		private static void OnPlayMode(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.ExitingEditMode && job != null)
			{
				Finish(job, "cancelled by entering play mode");
			}
		}

		private static void Finish(Run run, string problem)
		{
			EditorApplication.update -= Tick;
			EditorApplication.playModeStateChanged -= OnPlayMode;
			try
			{
				End(run, problem);
			}
			finally
			{
				if (job == run)
				{
					job = null;
				}
				BusyChanged?.Invoke();
				Report report = run.Context.Report;
				if (report.Completed && report.Problems.Count == 0)
				{
					Debug.Log("[Biome art] " + report);
				}
				else
				{
					Debug.LogWarning("[Biome art] " + report);
				}
				run.Done?.Invoke(report);
			}
		}

		private static Run Begin(ProceduralArtMode mode, int seed)
		{
			var context = new Context
			{
				Report = new Report { Mode = mode },
				Mode = mode,
				Seed = seed,
				Fingerprint = ProceduralArtPayload.CurrentFingerprint(seed),
			};
			foreach (string folder in new[] { ProceduralArtCatalogue.TexturesFolder, ProceduralArtCatalogue.TerrainLayersFolder,
				ProceduralArtCatalogue.MeshesFolder, ProceduralArtCatalogue.MaterialsFolder, ProceduralArtCatalogue.PrefabsFolder })
			{
				WorldEditorAssets.EnsureFolder(folder);
			}
			ProceduralArtLedger.DeleteLegacy();

			/* Held for the whole run, released in End whatever happens. No automatic refresh may
			 * import a file half-way through being written, no script reload may pull the job out
			 * from under itself, and the terrain arrays must not rebake from half-generated ground
			 * textures — they look again when this resumes them. */
			AssetDatabase.DisallowAutoRefresh();
			EditorApplication.LockReloadAssemblies();
			TerrainArrayAutoBake.Suspend();

			return new Run
			{
				Context = context,
				Steps = StepsOf(context).GetEnumerator(),
				Total = StepCount(),
				Clock = Stopwatch.StartNew(),
			};
		}

		private static void End(Run run, string problem)
		{
			Context c = run.Context;
			c.Report.Stopped = problem;
			if (problem != null)
			{
				c.Report.Completed = false;
			}
			try
			{
				// Whatever the last step queued is written, and anything written but not yet imported is
				// imported, so the generated files on disk are consistent even after a cancel.
				try
				{
					Flush(c);
				}
				catch (Exception e)
				{
					c.Report.Problems.Add($"{ProceduralArtCatalogue.Root}: writing the last step's queued assets failed: {e.Message}");
					Debug.LogException(e);
				}
				ImportPendingTextures(c);
				AssetDatabase.SaveAssets();
				// Whatever the cause, a material or terrain layer with an empty required slot draws grey or flat:
				// each is reported, which also keeps the art stale (generatedFailed below) so the next run retries.
				foreach (string line in ProceduralArtWrapperCheck.Scan(ProceduralArtCatalogue.WrapperPaths()))
				{
					c.Report.Problems.Add(line + " after generating");
				}
				/* Only a complete full run vouches for every generated file: a missing-only run leaves
				 * older files as they were, and a cancelled or failed one leaves the rest. */
				bool generatedFailed = c.Report.Problems.Exists(p => p.Contains(ProceduralArtCatalogue.Root) || p.Contains(BiomeTerrainLayers.BuiltFolder));
				if (c.Report.Completed && c.Mode == ProceduralArtMode.Full && !generatedFailed)
				{
					ProceduralArtPayload.RecordComplete(c.Fingerprint, c.Seed, c.Built);
				}
				else if (c.Built != null)
				{
					ProceduralArtPayload.RecordBuilt(c.Built);
				}
				ProceduralArtPayload.ClearStaging();
				/* Last, after the materials are saved: the indirect shaders' variants follow the keyword sets
				 * on disk, whatever this run wrote. After the record on purpose — a collection problem must
				 * not make the art itself stale, since a regeneration would not fix it. */
				ProceduralArtVariants.Ensure(c.Report.Problems);
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.AllowAutoRefresh();
				EditorApplication.UnlockReloadAssemblies();
				TerrainArrayAutoBake.Resume();
				c.Report.Seconds = run.Clock.Elapsed.TotalSeconds;
			}
			if (File.Exists(ProceduralArtPayload.LedgerPath))
			{
				AssetDatabase.ImportAsset(ProceduralArtPayload.LedgerPath);
			}
		}

		private sealed class Context
		{
			public Report Report;
			public ProceduralArtMode Mode;
			public int Seed;
			public string Fingerprint;
			public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
			public readonly Dictionary<string, Color32[]> BarkPixels = new Dictionary<string, Color32[]>();
			public readonly List<string> PendingTextures = new List<string>();
			/// <summary>Payload textures that would not load as 2D textures even after a forced reimport.</summary>
			public readonly HashSet<string> UnloadableTextures = new HashSet<string>(StringComparer.Ordinal);
			/// <summary>Existing generated assets updated in memory this step, saved together by <see cref="Flush"/>.</summary>
			public readonly List<PendingSave> Saves = new List<PendingSave>();
			/// <summary>New native assets (meshes, materials, terrain layers) created together by <see cref="Flush"/>.</summary>
			public readonly List<PendingNative> NewNatives = new List<PendingNative>();
			/// <summary>Prefabs to build, built and written by <see cref="Flush"/> once the step's assets exist.</summary>
			public readonly List<PendingPrefab> Prefabs = new List<PendingPrefab>();
			/// <summary>
			/// While a flush runs: each new asset's stand-in (the in-memory object its writer returned) →
			/// the asset loaded from its file. Prefabs built in the flush have their references swapped through it.
			/// </summary>
			public readonly Dictionary<Object, Object> Resolved = new Dictionary<Object, Object>();
			public Color32[] AtlasPixels;
			/// <summary>The built terrain layers ensured by this run, or null when that step did not run.</summary>
			public List<string> Built;
		}

		private static int StepCount()
		{
			// Migration, ground, bark, atlas, import, verify, layers, materials, rocks, details, trees, built
			// layers; and the rock-formation, ice and cliff-piece steps (BiomeArtGenerator.Rocks.cs).
			return SurfaceCatalogue.GroundRecipes.Length + SurfaceCatalogue.BarkRecipes.Length + 9 + ProceduralArtCatalogue.Trees.Length + RockStepCount();
		}

		/// <summary>The generation as steps; each <c>yield</c> names the step whose work follows it.</summary>
		private static IEnumerable<string> StepsOf(Context c)
		{
			yield return "Migrating old generated art to deterministic IDs";
			Migrate(c);
			foreach (SurfaceRecipe recipe in SurfaceCatalogue.GroundRecipes)
			{
				yield return "Ground: " + recipe.Name;
				WriteGround(c, recipe);
			}
			foreach (SurfaceRecipe recipe in SurfaceCatalogue.BarkRecipes)
			{
				yield return "Bark: " + recipe.Name;
				WriteBark(c, recipe);
			}
			// Rock and ice surface maps, before the import step imports them with the rest.
			foreach (string step in RockSurfaceSteps(c))
			{
				yield return step;
			}
			yield return "Foliage atlas";
			WriteAtlas(c);
			yield return "Importing textures";
			ImportPendingTextures(c);
			// Before any wrapper: every texture a wrapper will name must load as a 2D texture now.
			yield return "Verifying textures";
			VerifyTextures(c);
			yield return "Terrain layers";
			WriteTerrainLayers(c);
			yield return "Materials";
			WriteMaterials(c);
			yield return "Rocks";
			WriteRocks(c);
			// Rock formations, ice and cliff pieces, with their materials.
			foreach (string step in RockSteps(c))
			{
				yield return step;
			}
			yield return "Detail plants";
			WriteDetails(c);
			foreach (TreeSpecies species in ProceduralArtCatalogue.Trees)
			{
				yield return "Tree: " + species.Name;
				WriteTree(c, species);
			}
			yield return "Built terrain layers";
			c.Built = BiomeTerrainLayers.EnsureAll(c.Report.Problems);
		}

		/// <summary>
		/// Re-addresses generated art made before IDs were deterministic, before anything is written
		/// over it: <see cref="ProceduralArtPayload.Prepare(string, long, List{string}, out bool)"/>
		/// would otherwise delete each random-GUID wrapper and every committed reference to it would
		/// dangle. Throws when it cannot, which stops the generation.
		/// </summary>
		private static void Migrate(Context c)
		{
			if (!ProceduralArtMigration.NeedsMigration())
			{
				return;
			}
			ProceduralArtMigration.Result result = ProceduralArtMigration.Migrate(!Application.isBatchMode);
			Debug.Log("[Biome art] " + result);
			if (!result.Applied || ProceduralArtMigration.NeedsMigration())
			{
				throw new InvalidOperationException("generated art made before deterministic IDs could not be migrated, so nothing was regenerated over it: "
					+ (result.Problems.Count > 0 ? result.Problems[0] : "see the migration report above"));
			}
		}

		/// <summary>
		/// Whether a path is written: always in a full run, and in a missing-only run when it is missing
		/// or carries the wrong GUID. Every generated file — payload or wrapper — is the generator's.
		/// </summary>
		private static bool ShouldWrite(Context c, string path)
		{
			return c.Mode == ProceduralArtMode.Full || !ProceduralArtPayload.HasExpectedGuid(path);
		}

		// ── Textures ──────────────────────────────────────────────────

		private static long CompressedBytes(int width, int height) => (long)(width * height * (4f / 3f));

		/// <summary>Encodes and writes a payload PNG when its bytes changed; it is imported with the rest at the next import step.</summary>
		private static void WritePng(Context c, string path, Color32[] pixels, int width, int height)
		{
			byte[] bytes;
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
			try
			{
				texture.SetPixels32(pixels);
				texture.Apply(false, false);
				bytes = texture.EncodeToPNG();
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
			if (ProceduralArtPayload.WriteBytes(path, bytes, c.Report.Problems))
			{
				c.PendingTextures.Add(path);
			}
			else if (File.Exists(path))
			{
				c.Report.Unchanged++;
			}
		}

		private static void ImportPendingTextures(Context c)
		{
			if (c.PendingTextures.Count == 0)
			{
				return;
			}
			var paths = new List<string>(c.PendingTextures);
			c.PendingTextures.Clear();
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (string path in paths)
				{
					AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (string path in paths)
			{
				ProceduralArtLedger.Record(path);
				c.Report.Wrote.Add(path);
			}
		}

		private static void WriteGround(Context c, SurfaceRecipe recipe)
		{
			string albedo = ProceduralArtCatalogue.GroundTexture(recipe.Name, "Albedo");
			string normal = ProceduralArtCatalogue.GroundTexture(recipe.Name, "Normal");
			string mask = ProceduralArtCatalogue.GroundTexture(recipe.Name, "Mask");
			bool wa = ShouldWrite(c, albedo), wn = ShouldWrite(c, normal), wm = ShouldWrite(c, mask);
			c.Report.TextureBytes += 3 * CompressedBytes(ProceduralArtCatalogue.GroundSize, ProceduralArtCatalogue.GroundSize);
			if (!wa && !wn && !wm)
			{
				return;
			}
			SurfaceMaps maps = SurfaceSynth.Generate(in recipe, ProceduralArtCatalogue.GroundSize, c.Seed);
			if (wa) WritePng(c, albedo, maps.Albedo, maps.Size, maps.Size);
			if (wn) WritePng(c, normal, maps.Normal, maps.Size, maps.Size);
			if (wm) WritePng(c, mask, maps.Mask, maps.Size, maps.Size);
		}

		private static void WriteBark(Context c, SurfaceRecipe recipe)
		{
			// Always synthesised: the billboards are rendered from it even when the files are current.
			SurfaceMaps maps = SurfaceSynth.Generate(in recipe, ProceduralArtCatalogue.BarkSize, c.Seed);
			c.BarkPixels[recipe.Name] = maps.Albedo;
			c.Report.TextureBytes += 2 * CompressedBytes(ProceduralArtCatalogue.BarkSize, ProceduralArtCatalogue.BarkSize);
			string albedo = ProceduralArtCatalogue.BarkTexture(recipe.Name, "Albedo");
			string normal = ProceduralArtCatalogue.BarkTexture(recipe.Name, "Normal");
			if (ShouldWrite(c, albedo)) WritePng(c, albedo, maps.Albedo, maps.Size, maps.Size);
			if (ShouldWrite(c, normal)) WritePng(c, normal, maps.Normal, maps.Size, maps.Size);
		}

		private static void WriteAtlas(Context c)
		{
			c.AtlasPixels = FoliageAtlas.Generate(ProceduralArtCatalogue.AtlasSize, c.Seed);
			c.Report.TextureBytes += CompressedBytes(ProceduralArtCatalogue.AtlasSize, ProceduralArtCatalogue.AtlasSize);
			if (ShouldWrite(c, ProceduralArtCatalogue.AtlasPath))
			{
				WritePng(c, ProceduralArtCatalogue.AtlasPath, c.AtlasPixels, ProceduralArtCatalogue.AtlasSize, ProceduralArtCatalogue.AtlasSize);
			}
		}

		private static Texture2D LoadTexture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

		/// <summary>
		/// Makes sure every payload texture on disk imports as a <see cref="Texture2D"/> before any
		/// wrapper is built from it: one that does not (an import that is out of date, or one made as
		/// something else) is force-reimported, all in one batch, and one still wrong is reported and
		/// remembered in <see cref="Context.UnloadableTextures"/>, so no wrapper is written naming it.
		/// </summary>
		/// <remarks>
		/// Asks the asset database for each main asset's TYPE, which loads nothing. This is the check
		/// that would have caught 2026-10-02's failure at its source: every payload PNG had imported
		/// as a Cubemap (<see cref="ProceduralArtTextureImport"/>), so <c>LoadAssetAtPath&lt;Texture2D&gt;</c>
		/// answered null for files that were there, and the wrappers were written regardless.
		/// </remarks>
		private static void VerifyTextures(Context c)
		{
			var wrong = new List<string>();
			foreach (string path in ProceduralArtCatalogue.PayloadPaths())
			{
				if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
					&& AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(Texture2D))
				{
					wrong.Add(path);
				}
			}
			if (wrong.Count == 0)
			{
				return;
			}
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (string path in wrong)
				{
					AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (string path in wrong)
			{
				System.Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
				if (type == typeof(Texture2D))
				{
					continue;
				}
				c.UnloadableTextures.Add(path);
				string shape = AssetImporter.GetAtPath(path) is TextureImporter importer ? $", texture shape {importer.textureShape}" : string.Empty;
				c.Report.Problems.Add($"{path}: does not import as a 2D texture even after a forced reimport (imports as {(type != null ? type.Name : "nothing")}{shape}); no wrapper naming it is written");
			}
		}

		/// <summary>
		/// Reports a wrapper that is not written because something it needs is missing, and answers the
		/// file already on disk (or null). The art stays stale: the problem names a generated path.
		/// </summary>
		private static T KeepPrevious<T>(Context c, string path, string why) where T : Object
		{
			c.Report.Problems.Add($"{path}: not written — {why}; the previous file is kept and the art stays stale, so the next generation retries");
			return ProceduralArtPayload.HasExpectedGuid(path) ? AssetDatabase.LoadAssetAtPath<T>(path) : null;
		}

		// ── Terrain layers ────────────────────────────────────────────

		private static void WriteTerrainLayers(Context c)
		{
			foreach (SurfaceRecipe recipe in SurfaceCatalogue.GroundRecipes)
			{
				string path = ProceduralArtCatalogue.GroundLayerPath(recipe.Name);
				if (!ShouldWrite(c, path))
				{
					continue;
				}
				var layer = new TerrainLayer
				{
					name = ProceduralArtCatalogue.GroundLayerName(recipe.Name),
					diffuseTexture = LoadTexture(ProceduralArtCatalogue.GroundTexture(recipe.Name, "Albedo")),
					normalMapTexture = LoadTexture(ProceduralArtCatalogue.GroundTexture(recipe.Name, "Normal")),
					maskMapTexture = LoadTexture(ProceduralArtCatalogue.GroundTexture(recipe.Name, "Mask")),
					tileSize = new Vector2(recipe.TileMetres, recipe.TileMetres),
					tileOffset = Vector2.zero,
					normalScale = recipe.NormalScale,
					metallic = recipe.Metallic,
					smoothness = recipe.Smoothness,
					specular = Color.black,
					// The mask's channels already hold the real values: no remapping.
					diffuseRemapMin = Vector4.zero,
					diffuseRemapMax = Vector4.one,
					maskMapRemapMin = Vector4.zero,
					maskMapRemapMax = Vector4.one,
				};
				List<string> missing = ProceduralArtWrapperCheck.MissingInTerrainLayer(layer);
				if (missing.Count > 0)
				{
					Object.DestroyImmediate(layer);
					KeepPrevious<TerrainLayer>(c, path, $"{string.Join(", ", missing)} would be empty (its {recipe.Name} ground texture is missing or not loadable)");
					continue;
				}
				WriteNative(c, layer, path, ProceduralArtPayload.TerrainLayerFileId);
			}
		}

		/// <summary>
		/// Writes a freshly built native asset (terrain layer, material, mesh). One already on disk with
		/// its derived GUID is overwritten in place from the fresh object — GUID and main file ID kept,
		/// and nothing left over from how an older generator built it, because every serialized field
		/// is copied — and returned at once, its save queued for the step's flush. Any other is queued
		/// to be created with its derived GUID, and <paramref name="fresh"/> itself is returned as its
		/// stand-in: prefabs queued in the same step may reference it, and the flush swaps each such
		/// reference for the asset loaded from the new file. <paramref name="fresh"/> is consumed.
		/// </summary>
		private static T WriteNative<T>(Context c, T fresh, string path, long mainFileId) where T : Object
		{
			T existing = ProceduralArtPayload.HasExpectedGuid(path) ? AssetDatabase.LoadAssetAtPath<T>(path) : null;
			if (existing != null)
			{
				string before = ProceduralArtLedger.HashFile(path);
				string name = fresh.name;
				EditorUtility.CopySerialized(fresh, existing);
				Object.DestroyImmediate(fresh);
				existing.name = name;
				EditorUtility.SetDirty(existing);
				c.Saves.Add(new PendingSave { Asset = existing, Path = path, HashBefore = before });
				return existing;
			}
			c.NewNatives.Add(new PendingNative { Object = fresh, Type = typeof(T), Path = path, MainFileId = mainFileId });
			return fresh;
		}

		/// <summary>Records a wrapper write, counting it as unchanged when its bytes are what they were.</summary>
		private static void RecordWrapper(Context c, string path, string hashBefore)
		{
			ProceduralArtLedger.Record(path);
			if (hashBefore != null && File.Exists(path) && string.Equals(hashBefore, ProceduralArtLedger.HashFile(path), StringComparison.Ordinal))
			{
				c.Report.Unchanged++;
				return;
			}
			c.Report.Wrote.Add(path);
		}

		// ── Materials ─────────────────────────────────────────────────

		private static Shader VegetationShader => Shader.Find("FishMMO/Vegetation") ?? Shader.Find("Universal Render Pipeline/Lit");
		private static Shader RockShader => Shader.Find("FishMMO/Weather Lit") ?? Shader.Find("Universal Render Pipeline/Lit");

		private static Material WriteMaterial(Context c, string name, Shader shader, Action<Material> setup)
		{
			string path = ProceduralArtCatalogue.MaterialPath(name);
			if (!ShouldWrite(c, path))
			{
				var current = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (current == null)
				{
					c.Report.Problems.Add($"{path}: not written and not loadable");
				}
				c.Materials[name] = current;
				return current;
			}
			var material = new Material(shader) { name = name };
			setup(material);
			material.enableInstancing = true;
			List<string> missing = ProceduralArtWrapperCheck.MissingInMaterial(material);
			if (missing.Count > 0)
			{
				Object.DestroyImmediate(material);
				Material previous = KeepPrevious<Material>(c, path, $"{string.Join(", ", missing)} would be empty (a payload texture it wears is missing or not loadable)");
				c.Materials[name] = previous;
				return previous;
			}
			material = WriteNative(c, material, path, ProceduralArtPayload.MaterialFileId);
			c.Materials[name] = material;
			return material;
		}

		/// <summary>The vegetation shader's <c>_DistanceFade</c> classes: what a material fades out by.</summary>
		private const float FadeNone = 0f, FadeDetail = 1f, FadeTree = 2f;

		/// <summary>The vegetation shader's default <c>_TintPatchMetres</c>, and what trees and the odd details keep.</summary>
		private const float DefaultTintPatchMetres = 12f;

		/// <summary>
		/// How big the healthy/dry patches of a detail are (<c>_TintPatchMetres</c>). Ground dries out by its
		/// moisture and soil, which change over metres, and each kind of plant answers on its own scale:
		/// grass (and the carpets made of it) and reeds browns in broad swathes; flowers in small drifts, so a
		/// meadow keeps its pockets of colour; shrubs and ferns in the widest stands, because a few plants
		/// cover a lot of ground and a small patch would make each bush its own colour. The rest keep the
		/// shader's default.
		/// </summary>
		private static float DetailTintPatchMetres(in DetailSpec spec)
		{
			switch (spec.Plant.Kind)
			{
				case DetailKind.Grass:
				case DetailKind.Reeds:
					return 14f;
				case DetailKind.Flowers:
					return 6f;
				case DetailKind.Shrub:
				case DetailKind.DryShrub:
				case DetailKind.Fern:
					return 18f;
				default:
					return DefaultTintPatchMetres;
			}
		}

		/// <summary>Vegetation material settings; also sensible on URP Lit if the vegetation shader is missing.</summary>
		/// <param name="expectNormal">The material is meant to wear a normal map: its keyword is on whether or not <paramref name="normal"/> loaded, so an unloadable normal map is caught as an empty slot rather than silently dropped.</param>
		/// <param name="distanceFade"><see cref="FadeDetail"/> for terrain details (gone before their patch is culled), <see cref="FadeTree"/> for tree parts (gone before the terrain's tree distance).</param>
		/// <param name="facingCamera">A tree billboard: the shader turns its quad to face the camera about the tree's up axis.</param>
		/// <param name="groundSink">Metres the shader pushes the plant into the ground, min..max per instance (details; <see cref="DetailSinkRange"/>).</param>
		/// <param name="tintPatchMetres">The size of the healthy/dry patches the shader browns a stand in (<c>_TintPatchMetres</c>; details take <see cref="DetailTintPatchMetres"/>).</param>
		private static void Vegetation(Material m, Texture2D map, float cutoff, bool twoSided, float sway, float flutter, float translucency,
			Color healthy, Color dry, float tintSpread, float snowBury, bool deciduous, Texture2D normal = null, bool clip = true,
			bool expectNormal = false, float distanceFade = FadeNone, bool facingCamera = false, Vector2 groundSink = default,
			float tintPatchMetres = DefaultTintPatchMetres)
		{
			m.SetTexture("_BaseMap", map);
			m.SetColor("_BaseColor", Color.white);
			m.SetFloat("_Cutoff", cutoff);
			m.SetFloat("_AlphaClip", clip ? 1f : 0f);
			if (clip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
			m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
			m.SetFloat("_BackfaceFlip", 0f);
			m.SetFloat("_Smoothness", 0.25f);
			m.SetFloat("_Translucency", translucency);
			m.SetFloat("_WindSway", sway);
			m.SetFloat("_WindFlutter", flutter);
			m.SetFloat("_WindFrequency", 1.2f);
			m.SetColor("_HealthyColor", healthy);
			m.SetColor("_DryColor", dry);
			m.SetFloat("_TintSpread", tintSpread);
			m.SetFloat("_SnowBury", snowBury);
			m.SetFloat("_Deciduous", deciduous ? 1f : 0f);
			m.SetColor("_AutumnColor", new Color(1.6f, 0.8f, 0.3f, 1f));
			m.SetFloat("_FishWeatherAmount", 1f);
			m.SetTexture("_BumpMap", normal);
			bool normalMap = normal != null || expectNormal;
			m.SetFloat("_UseNormalMap", normalMap ? 1f : 0f);
			m.SetFloat("_BumpScale", 1f);
			if (normalMap) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
			m.SetFloat("_DistanceFade", distanceFade);
			m.SetFloat("_FacingCamera", facingCamera ? 1f : 0f);
			m.SetVector("_GroundSink", new Vector4(groundSink.x, groundSink.y, 0f, 0f));
			m.SetFloat("_TintPatchMetres", tintPatchMetres);
			m.renderQueue = clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
		}

		private static void WriteMaterials(Context c)
		{
			Shader veg = VegetationShader;
			Shader rock = RockShader;
			if (veg == null || rock == null)
			{
				c.Report.Problems.Add("A required shader (FishMMO/Vegetation, FishMMO/Weather Lit or URP Lit) was not found; materials were not written.");
				return;
			}
			Texture2D atlas = LoadTexture(ProceduralArtCatalogue.AtlasPath);
			var white = new Color(1f, 1f, 1f, 1f);

			foreach (SurfaceRecipe bark in SurfaceCatalogue.BarkRecipes)
			{
				Texture2D albedo = LoadTexture(ProceduralArtCatalogue.BarkTexture(bark.Name, "Albedo"));
				Texture2D normal = LoadTexture(ProceduralArtCatalogue.BarkTexture(bark.Name, "Normal"));
				// Bark is only ever a tree's (the barrel cactus detail has a material of its own).
				WriteMaterial(c, ProceduralArtCatalogue.BarkMaterial(bark.Name), veg, m => Vegetation(m, albedo, 0.5f, false, 1f, 0f, 0f, white, white, 0f, 0f, false, normal, clip: false,
					expectNormal: true, distanceFade: FadeTree));
			}

			foreach (RockMaterialSpec spec in ProceduralArtCatalogue.RockMaterials)
			{
				// Each wears its rock type's surface (Grey: granite), the one its formations and cliffs wear, so
				// the three read as one stone (RockArtNames.RockSurfaceTypeForLegacy).
				string surface = RockArtNames.RockSurfaceTypeForLegacy(spec.Name);
				Texture2D albedo = LoadTexture(surface != null ? RockArtNames.RockSurfaceTexture(surface, "Albedo") : ProceduralArtCatalogue.GroundTexture(spec.GroundFamily, "Albedo"));
				Texture2D normal = LoadTexture(surface != null ? RockArtNames.RockSurfaceTexture(surface, "Normal") : ProceduralArtCatalogue.GroundTexture(spec.GroundFamily, "Normal"));
				Texture2D mask = LoadTexture(surface != null ? RockArtNames.RockSurfaceTexture(surface, "Mask") : ProceduralArtCatalogue.GroundTexture(spec.GroundFamily, "Mask"));
				WriteMaterial(c, ProceduralArtCatalogue.RockMaterial(spec.Name), rock, m =>
				{
					m.SetTexture("_BaseMap", albedo);
					m.SetColor("_BaseColor", Color.white);
					m.SetTexture("_BumpMap", normal);
					m.SetFloat("_BumpScale", 1f);
					m.EnableKeyword("_NORMALMAP");
					// The ground mask is R metallic, G occlusion, B height, A smoothness — exactly what
					// URP Lit reads from its metallic-gloss (R, A) and occlusion (G) maps.
					m.SetTexture("_MetallicGlossMap", mask);
					m.EnableKeyword("_METALLICSPECGLOSSMAP");
					m.SetTexture("_OcclusionMap", mask);
					m.SetFloat("_OcclusionStrength", 1f);
					m.EnableKeyword("_OCCLUSIONMAP");
					m.SetFloat("_Smoothness", 1f);
					m.SetFloat("_Metallic", 0f);
					m.SetFloat("_FishWeatherAmount", 1f);
				});
			}

			foreach (DetailSpec d in ProceduralArtCatalogue.Details)
			{
				DetailSpec spec = d;
				Vector2 sink = DetailSinkRange(in spec);
				if (ProceduralArtCatalogue.WearsBark(in spec))
				{
					// The cactus tree's bark, on a material of the detail's own so it fades as a detail does.
					Texture2D cactus = LoadTexture(ProceduralArtCatalogue.BarkTexture(Bark.Cactus, "Albedo"));
					Texture2D cactusNormal = LoadTexture(ProceduralArtCatalogue.BarkTexture(Bark.Cactus, "Normal"));
					WriteMaterial(c, ProceduralArtCatalogue.DetailPrefab(spec.Name), veg, m => Vegetation(m, cactus, 0.5f, false, 1f, 0f, 0f, white, white, 0f, spec.SnowBury, false, cactusNormal, clip: false,
						expectNormal: true, distanceFade: FadeDetail, groundSink: sink));
					continue;
				}
				bool grassy = spec.Plant.Kind == DetailKind.Grass || spec.Plant.Kind == DetailKind.Reeds;
				float sway = grassy ? 0.35f : spec.Plant.Kind == DetailKind.Kelp ? 0.25f : 0.2f;
				WriteMaterial(c, ProceduralArtCatalogue.DetailPrefab(spec.Name), veg, m =>
					Vegetation(m, atlas, 0.45f, true, sway, 0.02f, 0.5f, spec.Healthy, spec.Dry, 0.35f, spec.SnowBury, false, distanceFade: FadeDetail, groundSink: sink,
						tintPatchMetres: DetailTintPatchMetres(in spec)));
			}

			foreach (TreeSpecies t in ProceduralArtCatalogue.Trees)
			{
				TreeSpecies species = t;
				if (!ProceduralArtCatalogue.HasLeaves(in species))
				{
					continue;
				}
				WriteMaterial(c, ProceduralArtCatalogue.LeavesMaterial(species.Name), veg, m =>
					Vegetation(m, atlas, 0.5f, true, 0.35f, 0.04f, 0.6f, new Color(0.95f, 1f, 0.95f), new Color(0.8f, 0.75f, 0.55f), 0.25f, 0f, species.Deciduous, distanceFade: FadeTree));
			}
		}

		/// <summary>
		/// The sink range a detail's material applies (<c>_GroundSink</c>): the scatter rules'
		/// (<see cref="BiomeArtSpec.Scatter.Sink"/>) for the rules that scatter this detail, widened to
		/// cover them all — a material is shared by every biome that scatters its prefab — or, for a
		/// detail no rule scatters yet, <see cref="ProceduralArtCatalogue.DetailSink"/> as 0.5–1× of it.
		/// </summary>
		/// <remarks>The spec is the one place sinks are authored (trees, rocks and details alike); that is why BiomeArtSpec.cs is hashed into the generator's fingerprint.</remarks>
		private static Vector2 DetailSinkRange(in DetailSpec spec)
		{
			string prefab = ProceduralArtCatalogue.DetailPrefab(spec.Name);
			bool any = false;
			var range = new Vector2(float.MaxValue, 0f);
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					if (rule.Prefabs == null || Array.IndexOf(rule.Prefabs, prefab) < 0 || rule.Sink.y <= 0f)
					{
						continue;
					}
					any = true;
					range.x = Mathf.Min(range.x, Mathf.Max(0f, Mathf.Min(rule.Sink.x, rule.Sink.y)));
					range.y = Mathf.Max(range.y, Mathf.Max(rule.Sink.x, rule.Sink.y));
				}
			}
			if (any)
			{
				return range;
			}
			float fallback = ProceduralArtCatalogue.DetailSink(in spec);
			return new Vector2(fallback * 0.5f, fallback);
		}

		private static Material MaterialOrNull(Context c, string name) => c.Materials.TryGetValue(name, out Material m) ? m : AssetDatabase.LoadAssetAtPath<Material>(ProceduralArtCatalogue.MaterialPath(name));

		// ── Meshes ────────────────────────────────────────────────────

		/// <summary>
		/// Writes a payload mesh: an existing one with the right GUID is overwritten in place (GUID and
		/// file ID kept); a new one, or one under the wrong GUID, is created with its deterministic GUID
		/// at the step's flush — until then the returned in-memory mesh stands in for it (see
		/// <see cref="WriteNative{T}"/>).
		/// </summary>
		private static Mesh WriteMesh(Context c, MeshBuilder builder, string name, bool closed)
		{
			string path = ProceduralArtCatalogue.MeshPath(name);
			c.Report.Triangles[name] = builder.TriangleCount;
			foreach (string problem in builder.Validate(closed))
			{
				c.Report.Problems.Add($"{name}: {problem}");
				break;
			}
			if (!ShouldWrite(c, path))
			{
				return AssetDatabase.LoadAssetAtPath<Mesh>(path);
			}
			return WriteNative(c, builder.ToMesh(name), path, ProceduralArtPayload.MeshFileId);
		}

		// ── Prefabs ───────────────────────────────────────────────────

		/// <summary>The object's component of this type: the one it has, or a new one.</summary>
		private static T Ensure<T>(GameObject go) where T : Component
		{
			T component = go.GetComponent<T>();
			if (component == null)
			{
				component = go.AddComponent<T>();
			}
			return component;
		}

		/// <summary>The named child — the existing one, or a new one — at the parent's origin.</summary>
		private static GameObject Child(GameObject parent, string name)
		{
			Transform found = parent.transform.Find(name);
			GameObject child = found != null ? found.gameObject : new GameObject(name);
			child.transform.SetParent(parent.transform, false);
			child.transform.localPosition = Vector3.zero;
			child.transform.localRotation = Quaternion.identity;
			child.transform.localScale = Vector3.one;
			return child;
		}

		/// <summary>
		/// Queues a prefab wrapper, built from scratch at the step's flush in a preview scene (so the
		/// open scene is neither touched nor dirtied, and the result cannot depend on an older build),
		/// once every mesh and material the step wrote exists as a file. Unity saves it to the staging
		/// folder with random object IDs; <see cref="ProceduralArtFileIds"/> then renumbers every object
		/// from its place in the hierarchy and sorts the documents, and the result is written beside a
		/// .meta carrying the path's GUID — only when its bytes changed. So the root GameObject's file
		/// ID, which committed terrain data's tree and detail prototypes reference with the GUID, is the
		/// same on every machine and in every regeneration. A prefab whose mesh or material is missing
		/// is not written (<see cref="CheckPrefab"/>).
		/// </summary>
		/// <remarks>
		/// <paramref name="build"/> runs later than this call, so it must read its meshes and materials
		/// when it runs, not when it was made: the arrays the callers capture are complete by then.
		/// </remarks>
		private static void WritePrefab(Context c, string name, Action<GameObject> build)
		{
			string path = ProceduralArtCatalogue.PrefabPath(name);
			if (!ShouldWrite(c, path))
			{
				return;
			}
			c.Prefabs.Add(new PendingPrefab { Name = name, Path = path, Build = build });
		}

		// ── Flushing a step's writes ──────────────────────────────────

		/// <summary>An existing generated asset changed in memory, to be saved with the step.</summary>
		private sealed class PendingSave
		{
			public Object Asset;
			public string Path;
			public string HashBefore;
		}

		/// <summary>A new native asset, to be created with the step.</summary>
		private sealed class PendingNative
		{
			public Object Object;
			public System.Type Type;
			public string Path;
			public long MainFileId;
			public string Staged;
		}

		/// <summary>A prefab, to be built and written with the step.</summary>
		private sealed class PendingPrefab
		{
			public string Name;
			public string Path;
			public Action<GameObject> Build;
			public string Staged;
		}

		/// <summary>
		/// Whether Unity writes a prefab saved inside an asset-editing batch: learned from the first
		/// prefab of the session (which is saved in a batch of one) and used for every later one. Null
		/// until then. A prefab the batch did not write is always saved again on its own, so a wrong
		/// guess costs time, never a prefab.
		/// </summary>
		private static bool? prefabsSaveInBatch;

		private static string StagedPath(string path) => $"{ProceduralArtPayload.StagingFolder}/{Path.GetFileName(path)}";

		/// <summary>
		/// Writes what the step queued, in as few asset-database round trips as it can: the changed
		/// assets saved in one batch, the new ones created in two (all staged, then every copy at its
		/// real path imported), the prefabs built and saved (in one batch where Unity allows), and
		/// their imports with the deletion of every staged file in a last one.
		/// </summary>
		private static void Flush(Context c)
		{
			if (c.Saves.Count == 0 && c.NewNatives.Count == 0 && c.Prefabs.Count == 0)
			{
				return;
			}
			var staged = new List<string>();
			try
			{
				FlushSaves(c);
				FlushNatives(c, staged);
				FlushPrefabs(c, staged);
			}
			finally
			{
				c.Resolved.Clear();
			}
		}

		private static void FlushSaves(Context c)
		{
			if (c.Saves.Count == 0)
			{
				return;
			}
			var saves = new List<PendingSave>(c.Saves);
			c.Saves.Clear();
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (PendingSave save in saves)
				{
					if (save.Asset != null)
					{
						// Only these assets: SaveAssets would also write whatever else the person has unsaved.
						AssetDatabase.SaveAssetIfDirty(save.Asset);
					}
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (PendingSave save in saves)
			{
				if (save.Asset == null)
				{
					c.Report.Problems.Add($"{save.Path}: was unloaded before it could be saved");
					continue;
				}
				RecordWrapper(c, save.Path, save.HashBefore);
			}
		}

		private static void FlushNatives(Context c, List<string> staged)
		{
			if (c.NewNatives.Count == 0)
			{
				return;
			}
			var items = new List<PendingNative>();
			foreach (PendingNative item in c.NewNatives)
			{
				if (!ProceduralArtPayload.Prepare(item.Path, item.MainFileId, c.Report.Problems, out _))
				{
					Object.DestroyImmediate(item.Object);
					continue;
				}
				item.Staged = StagedPath(item.Path);
				items.Add(item);
			}
			c.NewNatives.Clear();
			if (items.Count == 0)
			{
				return;
			}
			WorldEditorAssets.EnsureFolder(ProceduralArtPayload.StagingFolder);
			foreach (PendingNative item in items)
			{
				if (File.Exists(item.Staged))
				{
					ProceduralArtPayload.Delete(item.Staged); // Left by an interrupted run.
				}
			}

			// CreateAsset picks a random GUID, hence the staging (ProceduralArtPayload.CreateNative explains).
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (PendingNative item in items)
				{
					AssetDatabase.CreateAsset(item.Object, item.Staged);
					AssetDatabase.SaveAssetIfDirty(item.Object);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}

			var copied = new List<PendingNative>();
			foreach (PendingNative item in items)
			{
				staged.Add(item.Staged);
				if (!File.Exists(item.Staged) && item.Object != null && !EditorUtility.IsPersistent(item.Object))
				{
					// Not saved inside the batch: alone, then.
					c.Report.AssetDatabaseTrips++;
					AssetDatabase.CreateAsset(item.Object, item.Staged);
				}
				if (ProceduralArtPayload.CopyStaged(item.Staged, item.Path, item.MainFileId, c.Report.Problems))
				{
					copied.Add(item);
				}
			}
			if (copied.Count == 0)
			{
				return;
			}
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (PendingNative item in copied)
				{
					AssetDatabase.ImportAsset(item.Path, ImportAssetOptions.ForceUpdate);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (PendingNative item in copied)
			{
				Object loaded = AssetDatabase.LoadAssetAtPath(item.Path, item.Type);
				if (loaded == null)
				{
					c.Report.Problems.Add($"{item.Path}: written but not loadable after import");
					continue;
				}
				c.Resolved[item.Object] = loaded;
				ProceduralArtLedger.Record(item.Path);
				c.Report.Wrote.Add(item.Path);
			}
			// Later steps read materials from here: the files, not the stand-ins (whose staged copies go below).
			foreach (string key in new List<string>(c.Materials.Keys))
			{
				Material m = c.Materials[key];
				if (m != null && c.Resolved.TryGetValue(m, out Object file))
				{
					c.Materials[key] = (Material)file;
				}
			}
		}

		private static void FlushPrefabs(Context c, List<string> staged)
		{
			var prefabs = new List<PendingPrefab>(c.Prefabs);
			c.Prefabs.Clear();
			var saved = new List<PendingPrefab>();
			if (prefabs.Count > 0)
			{
				WorldEditorAssets.EnsureFolder(ProceduralArtPayload.StagingFolder);
				foreach (PendingPrefab prefab in prefabs)
				{
					prefab.Staged = StagedPath(prefab.Path);
					if (File.Exists(prefab.Staged))
					{
						ProceduralArtPayload.Delete(prefab.Staged); // Left by an interrupted run.
					}
				}
				int done = 0;
				if (prefabsSaveInBatch == null)
				{
					// The first prefab of the session in a batch of its own, to learn whether a batch writes it.
					SaveBatch(c, prefabs, 0, 1);
					done = 1;
					// A probe refused by CheckPrefab was never saved, so it teaches nothing: ask again next time.
					if (prefabs[0].Staged != null)
					{
						prefabsSaveInBatch = File.Exists(prefabs[0].Staged);
					}
				}
				if (prefabsSaveInBatch == true && done < prefabs.Count)
				{
					SaveBatch(c, prefabs, done, prefabs.Count);
				}
				for (int i = 0; i < prefabs.Count; i++)
				{
					PendingPrefab prefab = prefabs[i];
					if (prefab.Staged == null)
					{
						continue; // Refused by CheckPrefab; reported.
					}
					if (!File.Exists(prefab.Staged))
					{
						// Not written by the batch, or past the probe when batches do not write: saved alone.
						c.Report.AssetDatabaseTrips++;
						SaveStagedPrefab(c, prefab, false);
					}
					if (prefab.Staged != null && File.Exists(prefab.Staged))
					{
						saved.Add(prefab);
					}
					else if (prefab.Staged != null)
					{
						c.Report.Problems.Add($"{prefab.Path}: could not be saved");
					}
				}
			}

			var imports = new List<string>();
			foreach (PendingPrefab prefab in saved)
			{
				staged.Add(prefab.Staged);
				string yaml = File.ReadAllText(prefab.Staged);
				if (!ProceduralArtFileIds.TryRewrite(prefab.Path, yaml, out string rewritten, out _, out string error))
				{
					c.Report.Problems.Add($"{prefab.Path}: its object IDs could not be made deterministic ({error}); not written");
					continue;
				}
				int problems = c.Report.Problems.Count;
				if (ProceduralArtPayload.WriteFile(prefab.Path, Encoding.UTF8.GetBytes(rewritten), ProceduralArtPayload.PrefabMetaText, c.Report.Problems))
				{
					imports.Add(prefab.Path);
				}
				else if (c.Report.Problems.Count == problems)
				{
					c.Report.Unchanged++;
				}
			}

			if (imports.Count == 0 && staged.Count == 0)
			{
				return;
			}
			// One batch: the written prefabs imported, and every staged file of the step (assets and prefabs) gone.
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (string path in imports)
				{
					AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
				}
				foreach (string path in staged)
				{
					AssetDatabase.DeleteAsset(path);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (string path in staged)
			{
				// DeleteAsset can report success with the file still there (ProceduralArtPayload.Delete).
				if (File.Exists(path)) File.Delete(path);
				if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
			}
			foreach (string path in imports)
			{
				ProceduralArtLedger.Record(path);
				c.Report.Wrote.Add(path);
			}
		}

		/// <summary>Saves prefabs [<paramref name="from"/>, <paramref name="to"/>) to staging inside one asset-editing batch.</summary>
		private static void SaveBatch(Context c, List<PendingPrefab> prefabs, int from, int to)
		{
			c.Report.AssetDatabaseTrips++;
			AssetDatabase.StartAssetEditing();
			try
			{
				for (int i = from; i < to; i++)
				{
					SaveStagedPrefab(c, prefabs[i], true);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
		}

		/// <summary>
		/// Builds a queued prefab in a preview scene and saves it to its staging path, after its
		/// references to this step's new assets are swapped for the files and it is checked. Clears
		/// <see cref="PendingPrefab.Staged"/> when it is refused.
		/// </summary>
		/// <param name="inBatch">
		/// Inside an asset-editing batch: a save Unity refuses there (by throwing) is left unwritten,
		/// and the caller saves it again on its own, where a failure is a real one and propagates.
		/// </param>
		private static void SaveStagedPrefab(Context c, PendingPrefab prefab, bool inBatch)
		{
			Scene preview = EditorSceneManager.NewPreviewScene();
			try
			{
				var root = new GameObject(prefab.Name);
				SceneManager.MoveGameObjectToScene(root, preview);
				prefab.Build(root);
				if (!CheckPrefab(c, root, prefab.Path))
				{
					prefab.Staged = null;
					return;
				}
				try
				{
					PrefabUtility.SaveAsPrefabAsset(root, prefab.Staged, out _);
				}
				catch (Exception) when (inBatch)
				{
					// Not written: saved again alone after the batch.
				}
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(preview);
			}
		}

		/// <summary>
		/// Swaps every reference to a stand-in for a new asset (<see cref="Context.Resolved"/>) for the
		/// asset itself, and refuses the prefab — reported, the previous file kept — when a renderer
		/// has no mesh or material, a collider no mesh, or anything still points at an asset that is
		/// not a file (a stand-in whose creation failed).
		/// </summary>
		private static bool CheckPrefab(Context c, GameObject root, string path)
		{
			var missing = new List<string>();
			foreach (Component component in root.GetComponentsInChildren<Component>(true))
			{
				if (component == null)
				{
					continue;
				}
				var so = new SerializedObject(component);
				SerializedProperty property = so.GetIterator();
				bool changed = false;
				while (property.Next(true))
				{
					if (property.propertyType != SerializedPropertyType.ObjectReference)
					{
						continue;
					}
					Object value = property.objectReferenceValue;
					if (value == null || !(value is Mesh || value is Material || value is Texture))
					{
						continue;
					}
					if (c.Resolved.TryGetValue(value, out Object file))
					{
						property.objectReferenceValue = file;
						changed = true;
					}
					else if (!EditorUtility.IsPersistent(value) || AssetDatabase.GetAssetPath(value).StartsWith(ProceduralArtPayload.StagingFolder, StringComparison.Ordinal))
					{
						missing.Add($"{component.GetType().Name} on '{component.name}' names {value.name}, which was not written");
					}
				}
				if (changed)
				{
					so.ApplyModifiedPropertiesWithoutUndo();
				}
			}
			foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
			{
				if (filter.sharedMesh == null) missing.Add($"'{filter.name}' has no mesh");
			}
			foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
			{
				if (collider.sharedMesh == null) missing.Add($"the collider on '{collider.name}' has no mesh");
			}
			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
			{
				foreach (Material material in renderer.sharedMaterials)
				{
					if (material == null)
					{
						missing.Add($"'{renderer.name}' has an empty material slot");
						break;
					}
				}
			}
			if (missing.Count == 0)
			{
				return true;
			}
			KeepPrevious<GameObject>(c, path, string.Join("; ", missing));
			return false;
		}

		private static MeshRenderer AddRenderer(GameObject go, Mesh mesh, Material[] materials, ShadowCastingMode shadows)
		{
			Ensure<MeshFilter>(go).sharedMesh = mesh;
			var renderer = Ensure<MeshRenderer>(go);
			renderer.sharedMaterials = materials;
			renderer.shadowCastingMode = shadows;
			renderer.receiveShadows = true;
			renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
			return renderer;
		}

		private static void Lods(GameObject root, Mesh[] meshes, Material[][] materials, float[] heights, ShadowCastingMode[] shadows)
		{
			var lods = new LOD[meshes.Length];
			for (int i = 0; i < meshes.Length; i++)
			{
				GameObject child = Child(root, $"LOD{i}");
				MeshRenderer r = AddRenderer(child, meshes[i], materials[i], shadows[i]);
				lods[i] = new LOD(heights[i], new Renderer[] { r });
			}
			var group = Ensure<LODGroup>(root);
			group.SetLODs(lods);
			group.fadeMode = LODFadeMode.CrossFade;
			group.animateCrossFading = true;
			group.RecalculateBounds();
		}

		// ── Rocks ─────────────────────────────────────────────────────

		private static void WriteRocks(Context c)
		{
			var boulderMeshes = new Dictionary<string, Mesh[]>();
			foreach (RockShape shape in ProceduralArtCatalogue.BoulderShapes)
			{
				var meshes = new Mesh[ProceduralArtCatalogue.BoulderResolution.Length];
				for (int lod = 0; lod < meshes.Length; lod++)
				{
					RockShape s = shape;
					MeshBuilder b = RockMeshes.Build(in s, ProceduralArtCatalogue.BoulderResolution[lod], c.Seed);
					meshes[lod] = WriteMesh(c, b, ProceduralArtCatalogue.BoulderMesh(shape.Name, lod), true);
				}
				boulderMeshes[shape.Name] = meshes;
			}
			RockShape small = ProceduralArtCatalogue.SmallRock, pebble = ProceduralArtCatalogue.Pebble;
			Mesh smallMesh = WriteMesh(c, RockMeshes.Build(in small, 3, c.Seed), ProceduralArtCatalogue.SmallRocksMesh, true);
			Mesh pebbleMesh = WriteMesh(c, RockMeshes.BuildCluster(in pebble, 3, 2, 0.18f, c.Seed), ProceduralArtCatalogue.PebblesMesh, false);

			foreach (RockMaterialSpec spec in ProceduralArtCatalogue.RockMaterials)
			{
				Material material = MaterialOrNull(c, ProceduralArtCatalogue.RockMaterial(spec.Name));
				foreach (RockShape shape in ProceduralArtCatalogue.BoulderShapes)
				{
					Mesh[] meshes = boulderMeshes[shape.Name];
					WritePrefab(c, ProceduralArtCatalogue.BoulderPrefab(spec.Name, shape.Name), root =>
					{
						var mats = new Material[meshes.Length][];
						var shadows = new ShadowCastingMode[meshes.Length];
						for (int i = 0; i < meshes.Length; i++)
						{
							mats[i] = new[] { material };
							shadows[i] = i < meshes.Length - 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
						}
						Lods(root, meshes, mats, new[] { 0.25f, 0.08f, 0.01f }, shadows);
						AddBoulderCollider(root, meshes[0]);
					});
				}
				WritePrefab(c, ProceduralArtCatalogue.SmallRocksPrefab(spec.Name), root =>
					AddRenderer(root, smallMesh, new[] { material }, ShadowCastingMode.Off));
				WritePrefab(c, ProceduralArtCatalogue.PebblesPrefab(spec.Name), root =>
					AddRenderer(root, pebbleMesh, new[] { material }, ShadowCastingMode.Off));
			}
		}

		/// <summary>
		/// A capsule along the boulder's longest horizontal axis. A capsule because it is the
		/// collider every terrain tree instance supports; a mesh collider on a tree prototype is not.
		/// </summary>
		private static void AddBoulderCollider(GameObject root, Mesh mesh)
		{
			if (mesh == null)
			{
				return;
			}
			Bounds b = mesh.bounds;
			var capsule = Ensure<CapsuleCollider>(root);
			bool alongX = b.size.x >= b.size.z;
			bool upright = b.size.y > Mathf.Max(b.size.x, b.size.z);
			capsule.direction = upright ? 1 : alongX ? 0 : 2;
			float across = upright ? Mathf.Min(b.size.x, b.size.z) : Mathf.Min(b.size.y, alongX ? b.size.z : b.size.x);
			capsule.radius = across * 0.45f;
			capsule.height = Mathf.Max(capsule.radius * 2f, (upright ? b.size.y : alongX ? b.size.x : b.size.z) * 0.9f);
			capsule.center = b.center;
		}

		// ── Details ───────────────────────────────────────────────────

		private static void WriteDetails(Context c)
		{
			foreach (DetailSpec d in ProceduralArtCatalogue.Details)
			{
				DetailSpec spec = d;
				DetailPlant plant = spec.Plant;
				MeshBuilder b = VegetationMeshes.Build(in plant, c.Seed);
				string name = ProceduralArtCatalogue.DetailPrefab(spec.Name);
				Mesh mesh = WriteMesh(c, b, name, false);
				Material material = MaterialOrNull(c, name);
				ShadowCastingMode shadows = spec.CastsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
				WritePrefab(c, name, root => AddRenderer(root, mesh, new[] { material }, shadows));
			}
		}

		// ── Trees ─────────────────────────────────────────────────────

		private static void WriteTree(Context c, TreeSpecies species)
		{
			MeshBuilder lod0 = TreeMeshes.Build(in species, 0, c.Seed);
			MeshBuilder lod1 = TreeMeshes.Build(in species, 1, c.Seed);
			bool leaves = lod0.Submeshes[TreeMeshes.LeafSubmesh].Count > 0;
			if (!leaves)
			{
				lod0.Submeshes.RemoveAt(TreeMeshes.LeafSubmesh);
				lod1.Submeshes.RemoveAt(TreeMeshes.LeafSubmesh);
			}

			// The billboard: the full tree, rendered in software from the same textures.
			Color32[] bark = c.BarkPixels.TryGetValue(species.BarkFamily, out Color32[] bp) ? bp : null;
			Color32[] atlas = c.AtlasPixels;
			BillboardImpostor.Result billboard = BillboardImpostor.Render(lod0, (sub, uv, vc) =>
			{
				Color tex = sub == TreeMeshes.BarkSubmesh
					? (bark != null ? FoliageAtlas.Sample(bark, ProceduralArtCatalogue.BarkSize, uv, true) : Color.gray)
					: (atlas != null ? FoliageAtlas.Sample(atlas, ProceduralArtCatalogue.AtlasSize, uv, false) : Color.green);
				if (sub == TreeMeshes.BarkSubmesh)
				{
					tex.a = 1f;
				}
				return new Color(tex.r * vc.r, tex.g * vc.g, tex.b * vc.b, tex.a);
			}, ProceduralArtCatalogue.BillboardHeight, species.Height, species.Deciduous ? 0.35f : 1f);

			string billboardPath = ProceduralArtCatalogue.BillboardTexture(species.Name);
			c.Report.TextureBytes += CompressedBytes(billboard.Width, billboard.Height);
			if (ShouldWrite(c, billboardPath))
			{
				// Imported at once: the billboard material below loads it.
				WritePng(c, billboardPath, billboard.Pixels, billboard.Width, billboard.Height);
				ImportPendingTextures(c);
			}
			Texture2D billboardTexture = LoadTexture(billboardPath);
			// Turned to face the camera by the shader (_FacingCamera). Still two-sided: a shadow or reflection
			// camera may see it from behind, and a culled back face would drop the tree there.
			Material billboardMaterial = WriteMaterial(c, ProceduralArtCatalogue.BillboardMaterial(species.Name), VegetationShader, m =>
				Vegetation(m, billboardTexture, 0.5f, true, 0.35f, 0f, 0.3f, new Color(0.95f, 1f, 0.95f), new Color(0.8f, 0.75f, 0.55f), 0.25f, 0f, species.Deciduous,
					distanceFade: FadeTree, facingCamera: true));

			string prefix = ProceduralArtCatalogue.TreePrefab(species.Name);
			Mesh m0 = WriteMesh(c, lod0, prefix + "_LOD0", false);
			Mesh m1 = WriteMesh(c, lod1, prefix + "_LOD1", false);
			Mesh m2 = WriteMesh(c, billboard.Mesh, prefix + "_Billboard", false);
			if (m2 != null && ShouldWrite(c, ProceduralArtCatalogue.MeshPath(prefix + "_Billboard")))
			{
				// The shader turns the quad about the trunk, so it sweeps a cylinder: bound that, or the
				// renderer is culled at the screen's edge while the turned quad would still be on it.
				Bounds b = m2.bounds;
				float reach = Mathf.Max(b.extents.x, b.extents.z);
				m2.bounds = new Bounds(new Vector3(0f, b.center.y, 0f), new Vector3(2f * reach, b.size.y, 2f * reach));
			}

			Material barkMaterial = MaterialOrNull(c, ProceduralArtCatalogue.BarkMaterial(species.BarkFamily));
			Material leafMaterial = leaves ? MaterialOrNull(c, ProceduralArtCatalogue.LeavesMaterial(species.Name)) : null;
			Material[] treeMaterials = leaves ? new[] { barkMaterial, leafMaterial } : new[] { barkMaterial };
			var meshes = new[] { m0, m1, m2 };
			var mats = new[] { treeMaterials, treeMaterials, new[] { billboardMaterial } };
			var shadows = new[] { ShadowCastingMode.On, ShadowCastingMode.On, ShadowCastingMode.Off };

			WritePrefab(c, ProceduralArtCatalogue.TreePrefab(species.Name), root =>
			{
				Lods(root, meshes, mats, ProceduralArtCatalogue.TreeLodHeights, shadows);
				AddTrunkCollider(root, in species);
			});
			WritePrefab(c, ProceduralArtCatalogue.TreeDecorPrefab(species.Name), root =>
				Lods(root, meshes, mats, ProceduralArtCatalogue.TreeLodHeights, shadows));
		}

		/// <summary>The trunk as a capsule: what a player walks into. Branches and leaves do not collide.</summary>
		private static void AddTrunkCollider(GameObject root, in TreeSpecies species)
		{
			var capsule = Ensure<CapsuleCollider>(root);
			capsule.direction = 1;
			float radius = species.Form == TreeForm.Bamboo ? species.CrownWidth * species.Height * 0.25f : species.TrunkRadius * 1.15f;
			float height = species.Form == TreeForm.Cactus || species.Form == TreeForm.Bamboo ? species.Height : species.Height * Mathf.Max(0.35f, species.CrownBase + 0.2f);
			capsule.radius = Mathf.Max(0.1f, radius);
			capsule.height = Mathf.Max(capsule.radius * 2f, height);
			capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
		}

		// ── Build and editor-load entry ───────────────────────────────

		/// <summary>
		/// Brings every generated asset — payload, wrappers, built terrain layers — up to date,
		/// synchronously, for a build. Runs a missing-only pass when the generator is unchanged and only
		/// files are missing, a full pass otherwise (migrating random-ID art first), and nothing when all
		/// is current. Then the indirect shaders' variant collection (<see cref="ProceduralArtVariants"/>).
		/// </summary>
		/// <exception cref="InvalidOperationException">Something generated is still missing, mis-addressed or has an empty texture slot afterwards: the build would ship trees with no meshes or textures, or terrain pointing at nothing.</exception>
		public static void EnsureCurrentForBuild(List<string> log)
		{
			int seed = ProceduralArtCatalogue.DefaultSeed;
			ProceduralArtPayload.State state = ProceduralArtPayload.Check(seed);
			if (!state.StaleArt)
			{
				log?.Add("Procedural biome art is current.");
				EnsureVariantsForBuild(log);
				return;
			}
			log?.Add("Procedural biome art is stale: " + state);
			ProceduralArtMode mode = state.NeedsFull ? ProceduralArtMode.Full : ProceduralArtMode.MissingOnly;
			Report report = Generate(mode, seed);
			log?.Add(report.ToString());
			ProceduralArtPayload.State after = ProceduralArtPayload.Check(seed);
			// An empty texture slot ships grey trees and flat ground as surely as a missing file.
			if (after.MissingPayload.Count > 0 || after.WrongGuid.Count > 0 || after.MissingWrappers.Count > 0 || after.NeedsMigration || after.UnboundWrappers.Count > 0)
			{
				throw new InvalidOperationException("Procedural biome art could not be generated: " + after);
			}
			EnsureVariantsForBuild(log);
		}

		/// <summary>
		/// The indirect shaders' variant collection, current for the build (the generation above already
		/// ensured it when it ran; this is a byte comparison then).
		/// </summary>
		/// <exception cref="InvalidOperationException">It could not be written: the GPU terrain renderer would draw grass as solid quads.</exception>
		private static void EnsureVariantsForBuild(List<string> log)
		{
			var problems = new List<string>();
			ProceduralArtVariants.Ensure(problems);
			if (problems.Count > 0)
			{
				throw new InvalidOperationException("The indirect shader variant collection could not be brought up to date: " + string.Join("; ", problems));
			}
			log?.Add("Indirect shader variant collection is current: " + ProceduralArtVariants.AssetPath);
		}
	}
}
#endif
