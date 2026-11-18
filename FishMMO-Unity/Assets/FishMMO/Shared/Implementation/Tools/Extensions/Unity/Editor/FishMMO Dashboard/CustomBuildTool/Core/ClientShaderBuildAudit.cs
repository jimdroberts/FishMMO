#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Pipeline;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// CLI audit of client Weather shader filtering and native cache reuse. Compiles a
	/// bounded sample twice, or all retained variants with -fishmmoShaderAuditFull.
	/// Disables the SBP output cache without changing normal client stripping.
	/// This audits shader content, not a complete client player or its world-map bakes.
	/// </summary>
	public static class ClientShaderBuildAudit
	{
		[Serializable]
		public sealed class PassRecord
		{
			public int round;
			public string shader, pass, stage;
			public int afterClientStripping, sampled;
			public string[] sampleKeys;
		}

		[Serializable]
		private sealed class Report
		{
			public string unity, target, pipeline, completedUtc, error;
			public string[] shaders, excludedShaders;
			public bool succeeded, withinBudget;
			public double budgetSeconds = 600;
			public List<double> roundSeconds = new List<double>();
			public string cache = "Native shader cache preserved. A cold result requires a fresh isolated project/cache.";
			public string scope = "Weather/vegetation shader/SVC client content; one variant per platform/tier/pass/stage; two writes with SBP cache disabled. Native shader cache remains enabled.";
			public int learnedVariants;
			public bool inventoryOnly, full, allWeather, allClient;
			public int materialAssets, computeAssets;
			public List<PassRecord> passes = new List<PassRecord>();
		}

		internal static List<PassRecord> Records;
		internal static int Round;
		internal static bool InventoryOnly, Full;
		internal static HashSet<Shader> AuditedShaders;

		public static void RunCLI()
		{
			string output = "Library/FishMMO/ClientShaderAudit";
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i + 1 < args.Length; i++)
				if (args[i] == "-fishmmoShaderAuditOutput") output = args[i + 1];
			string onlyShader = null;
			for (int i = 0; i + 1 < args.Length; i++)
				if (args[i] == "-fishmmoShaderAuditShader") onlyShader = args[i + 1];
			InventoryOnly = args.Contains("-fishmmoShaderAuditInventoryOnly");
			Full = args.Contains("-fishmmoShaderAuditFull");
			bool allClient = args.Contains("-fishmmoShaderAuditAllClient");
			bool allWeather = allClient || args.Contains("-fishmmoShaderAuditAllWeather");
			var report = new Report { allClient = allClient, allWeather = allWeather, unity = Application.unityVersion, target = EditorUserBuildSettings.activeBuildTarget.ToString(), inventoryOnly = InventoryOnly, full = Full };
			string folder = "Assets/ClientShaderAudit_" + Guid.NewGuid().ToString("N");
			int exitCode = 1;
			try
			{
				if ((EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL && BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) != BuildTargetGroup.Standalone) ||
					EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player)
					throw new BuildFailedException("Start Unity with -buildTarget WebGL or Linux64 and -standaloneBuildSubtarget Player for this client audit.");
				if (GraphicsSettings.defaultRenderPipeline == null)
					throw new BuildFailedException("The client audit requires the project's normal render pipeline to remain assigned.");
				if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
				{
					PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
					PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.WebGPU });
				}
				Directory.CreateDirectory(output);
				Directory.CreateDirectory(folder);
				AssetDatabase.Refresh();
				var collection = new ShaderVariantCollection();
				var shaders = new[] { "FishWeatherLit.shader", "FishWeatherLitIndirect.shader", "FishVegetation.shader", "FishVegetationIndirect.shader", "FishClouds.shader" }
					.Select(name => AssetDatabase.LoadAssetAtPath<Shader>("Assets/Prefabs/Client/Weather/Shaders/" + name)).ToArray();
				if (shaders.Any(shader => shader == null)) throw new BuildFailedException("Weather shader assets were not found.");
				if (allWeather) shaders = AssetDatabase.FindAssets("t:Shader", new[] { allClient ? "Assets" : "Assets/Prefabs/Client/Weather/Shaders" })
					.Select(guid => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid))).Where(shader => shader != null).ToArray();
				if (!string.IsNullOrEmpty(onlyShader)) shaders = shaders.Where(shader => shader.name == onlyShader).ToArray();
				string pipelineTag = GraphicsSettings.defaultRenderPipeline.renderPipelineShaderTag;
				report.excludedShaders = shaders.Where(shader => !HasCompatibleSubshader(shader, pipelineTag))
					.Select(shader => shader.name + " (no subshader compatible with " + pipelineTag + ")").ToArray();
				shaders = shaders.Where(shader => HasCompatibleSubshader(shader, pipelineTag)).ToArray();
				if (shaders.Length == 0) throw new BuildFailedException("Requested audit shader was not found: " + onlyShader);
				report.shaders = shaders.Select(shader => shader.name).ToArray();
				AuditedShaders = new HashSet<Shader>(shaders);
				string warmList = "Library/FishMMO/ShaderWarmList.txt";
				foreach (string line in File.Exists(warmList) ? File.ReadAllLines(warmList) : Array.Empty<string>())
				{
					string[] parts = line.Split('\t');
					if (parts.Length < 3 || !int.TryParse(parts[1], out int passType)) continue;
					Shader shader = shaders.FirstOrDefault(candidate => candidate.name == parts[0]);
					if (shader == null) continue;
					string[] keywords = parts[2].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					keywords = keywords.Where(keyword => shader.keywordSpace.FindKeyword(keyword).isValid && !shader.keywordSpace.FindKeyword(keyword).isDynamic).ToArray();
					if (keywords.Contains(ClientShaderBuildProcessor.DiagnosticsKeyword)) continue;
					collection.Add(new ShaderVariantCollection.ShaderVariant(shader, (PassType)passType, keywords));
				}
				AddGeneratedMaterialVariants(collection, shaders);
				foreach (Shader shader in shaders)
				{
					string[] keys = shader.name.EndsWith(" Indirect", StringComparison.Ordinal) ? new[] { "PROCEDURAL_INSTANCING_ON" } : Array.Empty<string>();
					// Fullscreen effects use Normal, lit surfaces use ScriptableRenderPipeline.
					bool added = false;
					foreach (PassType pass in new[] { PassType.Normal, PassType.ScriptableRenderPipeline, PassType.ForwardBase })
					{
						try { collection.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keys)); added = true; }
						catch (ArgumentException) { }
					}
					if (!added) throw new BuildFailedException("No audited pass type found for " + shader.name);
				}
				string asset = folder + "/ObservedVariants.shadervariants";
				AssetDatabase.CreateAsset(collection, asset);
				AssetDatabase.SaveAssets();
				report.pipeline = AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline);
				report.learnedVariants = collection.variantCount;
				if (Full) report.scope = "All retained variants for four Weather/vegetation shaders and all cloud passes, using learned states and generated indirect material features. Two content writes; SBP output cache disabled. This is not a complete player or all game shaders.";
				if (InventoryOnly) report.scope = "Variant inventory after normal client stripping; no shader compilation. Includes weather and vegetation, regular and indirect.";
				var bundleAssets = new List<string> { asset };
				if (allWeather)
				{
					var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
						.Where(path => AuditedShaders.Contains(AssetDatabase.LoadAssetAtPath<Material>(path)?.shader)).ToArray();
					var computes = !string.IsNullOrEmpty(onlyShader) ? Array.Empty<string>() : AssetDatabase.FindAssets("t:ComputeShader", new[] { allClient ? "Assets" : "Assets/Prefabs/Client/Weather/Shaders" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
					bundleAssets.AddRange(materials);
					if (!InventoryOnly) bundleAssets.AddRange(computes);
					report.materialAssets = materials.Length; report.computeAssets = computes.Length;
					report.scope = "All Weather-folder shaders and compute shaders, learned/generated variants and serialized materials; SBP cache disabled. Native cache preserved. Excludes water/UI/other shader folders and player compilation.";
				}
				if (allClient) report.scope = "All Assets-folder shaders and compute shaders with learned/generated variants and serialized materials; SBP cache disabled, native cache preserved. Includes weather/water/UI. Excludes complete player compilation.";
				Records = report.passes;
				using (new WebGLRenderPipelineScope(EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL))
				using (new ServerContentBuildScope(false))
				{
					for (Round = 1; Round <= (InventoryOnly ? 1 : 2); Round++)
					{
						string destination = Path.Combine(output, "round-" + Round);
						Directory.CreateDirectory(destination);
						Debug.Log("[Client shader audit] BEGIN ROUND " + Round);
						var timer = System.Diagnostics.Stopwatch.StartNew();
						var manifest = CompatibilityBuildPipeline.BuildAssetBundles(destination,
							new[] { new AssetBundleBuild { assetBundleName = "weather-sample", assetNames = bundleAssets.ToArray() } },
							BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
							EditorUserBuildSettings.activeBuildTarget);
						report.roundSeconds.Add(timer.Elapsed.TotalSeconds);
						if (manifest == null) throw new BuildFailedException("Client shader audit content write failed.");
						foreach (Shader shader in shaders)
							if (ShaderUtil.ShaderHasError(shader)) throw new BuildFailedException("Shader compiler errors: " + shader.name);
						foreach (string path in bundleAssets)
						{
							var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
							if (compute != null && ShaderUtil.GetComputeShaderMessages(compute).Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
								throw new BuildFailedException("Compute shader compiler errors: " + path);
						}
						Debug.Log("[Client shader audit] ROUND SECONDS " + Round + ": " + timer.Elapsed.TotalSeconds);
						Debug.Log("[Client shader audit] END ROUND " + Round);
					}
				}
				var unobserved = shaders.Where(shader => !Records.Any(record => record.shader == shader.name && record.afterClientStripping > 0)).Select(shader => shader.name).ToArray();
				if (unobserved.Length > 0)
					throw new BuildFailedException("The audit did not observe retained variants for: " + string.Join(", ", unobserved));
				string Signature(PassRecord record) => record.shader + "|" + record.pass + "|" + record.stage + "|" + string.Join(";", record.sampleKeys);
				if (!InventoryOnly && !Records.Where(record => record.round == 1).Select(Signature).OrderBy(value => value)
					.SequenceEqual(Records.Where(record => record.round == 2).Select(Signature).OrderBy(value => value)))
					throw new BuildFailedException("The two rounds chose different variants; their cache statistics are not comparable.");
				report.withinBudget = !InventoryOnly && report.roundSeconds.All(seconds => seconds <= report.budgetSeconds);
				if (Full && !InventoryOnly && !report.withinBudget) throw new BuildFailedException("Full shader audit exceeded the 600-second budget; see variants.json for measured times.");
				report.succeeded = true;
				Debug.Log("[Client shader audit] SUCCESS: " + Path.GetFullPath(output));
				exitCode = 0;
			}
			catch (Exception exception) { report.error = exception.ToString(); Debug.LogException(exception); }
			finally
			{
				Records = null;
				AuditedShaders = null;
				report.completedUtc = DateTime.UtcNow.ToString("O");
				Directory.CreateDirectory(output);
				File.WriteAllText(Path.Combine(output, "variants.json"), JsonUtility.ToJson(report, true));
				AssetDatabase.DeleteAsset(folder);
				if (Application.isBatchMode) EditorApplication.Exit(exitCode);
			}
		}

		/// <summary>Ignore explicitly incompatible pipelines, while retaining untagged and unknown shaders conservatively.</summary>
		public static bool HasCompatibleSubshader(Shader shader, string pipelineTag)
		{
			var data = ShaderUtil.GetShaderData(shader);
			if (data.SerializedSubshaderCount == 0) return !GraphTargetsDifferentPipeline(shader, pipelineTag);
			var tag = new ShaderTagId("RenderPipeline");
			for (int i = 0; i < data.SerializedSubshaderCount; i++)
			{
				string value = data.GetSerializedSubshader(i).FindTagValue(tag).name;
				if (string.IsNullOrEmpty(value) || value == pipelineTag) return true;
			}
			return false;
		}

		[Serializable] private sealed class GraphReference { public string m_Id; }
		[Serializable] private sealed class GraphObject
		{
			public string m_Type, m_ObjectId;
			public GraphReference[] m_ActiveTargets;
		}

		private static bool GraphTargetsDifferentPipeline(Shader shader, string pipelineTag)
		{
			string path = AssetDatabase.GetAssetPath(shader);
			if (!path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)) return false;
			try
			{
				// An unavailable graph target can import as an empty fallback shader. Read its
				// active target references rather than guessing from the asset/shader name.
				var objects = ReadGraphObjects(File.ReadAllText(path)).ToArray();
				var graph = objects.FirstOrDefault(item => item.m_ActiveTargets != null);
				if (graph == null || graph.m_ActiveTargets.Length == 0) return false;
				foreach (var reference in graph.m_ActiveTargets)
				{
					string type = objects.FirstOrDefault(item => item.m_ObjectId == reference.m_Id)?.m_Type;
					string targetTag = type == "UnityEditor.Rendering.HighDefinition.ShaderGraph.HDTarget" ? "HDRenderPipeline" :
						type == "UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget" ? "UniversalPipeline" : null;
					if (targetTag == null || targetTag == pipelineTag) return false;
				}
				return true;
			}
			catch (ArgumentException) { return false; } // Unknown/malformed graphs remain audited.
			catch (IOException) { return false; }
		}

		private static IEnumerable<GraphObject> ReadGraphObjects(string source)
		{
			// Shader Graph serializes several adjacent JSON objects, with braces also in strings.
			int depth = 0, start = 0;
			bool quoted = false, escaped = false;
			for (int i = 0; i < source.Length; i++)
			{
				char c = source[i];
				if (quoted)
				{
					if (escaped) escaped = false;
					else if (c == '\\') escaped = true;
					else if (c == '"') quoted = false;
				}
				else if (c == '"') quoted = true;
				else if (c == '{') { if (depth++ == 0) start = i; }
				else if (c == '}' && --depth == 0)
					yield return JsonUtility.FromJson<GraphObject>(source.Substring(start, i - start + 1));
			}
		}

		private static void AddGeneratedMaterialVariants(ShaderVariantCollection collection, Shader[] shaders)
		{
			const string path = "Assets/Prefabs/Shared/Biomes/Generated/Variants/FishIndirectVariants.shadervariants";
			var generated = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
			if (generated == null) throw new BuildFailedException("Generate indirect material variants before auditing: " + path);
			using var serialized = new SerializedObject(generated);
			var entries = serialized.FindProperty("m_Shaders");
			for (int i = 0; entries != null && i < entries.arraySize; i++)
			{
				var entry = entries.GetArrayElementAtIndex(i);
				var indirect = entry.FindPropertyRelative("first").objectReferenceValue as Shader;
				if (indirect == null || !shaders.Contains(indirect)) continue;
				var regular = shaders.FirstOrDefault(shader => shader.name == indirect.name.Replace(" Indirect", ""));
				var variants = entry.FindPropertyRelative("second.variants");
				for (int v = 0; variants != null && v < variants.arraySize; v++)
				{
					var variant = variants.GetArrayElementAtIndex(v);
					var pass = (PassType)variant.FindPropertyRelative("passType").intValue;
					var keys = (variant.FindPropertyRelative("keywords").stringValue ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					Add(indirect, keys.Concat(new[] { "PROCEDURAL_INSTANCING_ON" }));
					if (regular != null)
					{
						var materialKeys = keys.Where(key => key != "PROCEDURAL_INSTANCING_ON" && key != "INSTANCING_ON" && key != "DOTS_INSTANCING_ON");
						Add(regular, materialKeys);
						Add(regular, materialKeys.Concat(new[] { "INSTANCING_ON" }));
					}
					void Add(Shader shader, IEnumerable<string> input)
					{
						string[] valid = input.Distinct().Where(key => shader.keywordSpace.FindKeyword(key).isValid && !shader.keywordSpace.FindKeyword(key).isDynamic).ToArray();
						collection.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, valid));
					}
				}
			}
		}

	}

	public sealed class ClientShaderAuditProcessor : IPreprocessShaders
	{
		private static readonly BuiltinShaderDefine[] PlatformDefines = (BuiltinShaderDefine[])Enum.GetValues(typeof(BuiltinShaderDefine));
		public int callbackOrder => int.MaxValue;
		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
		{
			if (ClientShaderBuildAudit.Records == null || !ClientShaderBuildAudit.AuditedShaders.Contains(shader)) return;
			string Key(ShaderCompilerData variant) => variant.shaderCompilerPlatform + "/" + variant.graphicsTier + "/" + variant.shaderRequirements + "/" +
				string.Join(" ", variant.shaderKeywordSet.GetShaderKeywords().Select(keyword => keyword.name).OrderBy(name => name)) + "/" +
				string.Join(" ", PlatformDefines.Where(define => variant.platformKeywordSet.IsEnabled(define)).Select(define => define.ToString()));
			var selected = ClientShaderBuildAudit.InventoryOnly ? Array.Empty<ShaderCompilerData>() : ClientShaderBuildAudit.Full ? data.ToArray() : data.OrderBy(Key).GroupBy(variant => (variant.shaderCompilerPlatform, variant.graphicsTier))
				.Select(group => group.First()).ToArray();
			ClientShaderBuildAudit.Records.Add(new ClientShaderBuildAudit.PassRecord { round = ClientShaderBuildAudit.Round,
				shader = shader.name, pass = snippet.passName, stage = snippet.shaderType.ToString(), afterClientStripping = data.Count,
				sampled = selected.Length, sampleKeys = selected.Select(Key).ToArray() });
			data.Clear();
			foreach (ShaderCompilerData variant in selected) data.Add(variant);
		}
	}
}
#endif
