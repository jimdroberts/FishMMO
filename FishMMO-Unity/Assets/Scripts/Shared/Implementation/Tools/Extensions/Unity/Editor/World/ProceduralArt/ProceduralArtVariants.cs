#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The shader variants the GPU-driven terrain renderer needs and no build material asks for: every
	/// keyword set a material of <c>FishMMO/Vegetation</c> or <c>FishMMO/Weather Lit</c> enables, mapped
	/// onto that shader's indirect twin, as one generated ShaderVariantCollection at <see cref="AssetPath"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> TerrainGpuRenderer draws a prototype by cloning its material at runtime and swapping the
	/// shader for the twin ("FishMMO/Vegetation" → "FishMMO/Vegetation Indirect", "FishMMO/Weather Lit" →
	/// "FishMMO/Weather Lit Indirect"). No material in the build uses a twin, so a build compiles only the
	/// twins' all-<c>shader_feature</c>-off variants and a clone with <c>_ALPHATEST_ON</c> or
	/// <c>_NORMALMAP</c> falls back to that: grass draws as solid quads. A collection the build reaches (the
	/// weather render profile references it) lists the variants the build would otherwise strip.
	/// </para>
	/// <para>
	/// <b>Pass types.</b> A collection entry names a <see cref="PassType"/>, not a pass, and Unity derives a
	/// pass's type from its LightMode tag (<see cref="PassTypeOf"/>). For URP's tags, "ShadowCaster" is
	/// <see cref="PassType.ShadowCaster"/>, "MotionVectors" is <see cref="PassType.MotionVectors"/>, "Meta" is
	/// <see cref="PassType.Meta"/>, an untagged or "SRPDefaultUnlit" pass is
	/// <see cref="PassType.ScriptableRenderPipelineDefaultUnlit"/>, and every other tag — UniversalForward,
	/// DepthOnly, DepthNormals, UniversalGBuffer, Universal2D, XRMotionVectors — is
	/// <see cref="PassType.ScriptableRenderPipeline"/>. So "every non-ShadowCaster pass is
	/// ScriptableRenderPipeline" is nearly but not quite true: URP's own stripper
	/// (ShaderScriptableStripper.StripUnusedFeatures_CrossFadeLod, StripUnusedPass_Meta) tells MotionVectors
	/// and Meta passes apart by pass type. The twins are read for their actual passes, so Vegetation Indirect
	/// gets ScriptableRenderPipeline + ShadowCaster and Weather Lit Indirect also MotionVectors. Meta is
	/// left out on purpose: it only feeds the lightmapper, which never sees a runtime clone, and URP strips
	/// it from players without Enlighten realtime GI.
	/// </para>
	/// <para>
	/// <b>Keywords per pass type.</b> One entry covers every pass of its type, and a keyword the type's
	/// passes do not declare (<c>_NORMALMAP</c> in a shadow caster) makes no variant, so each set is cut down
	/// to the keywords the twin's passes of that type declare (<see cref="ShaderUtil.GetPassKeywords(Shader, in PassIdentifier, UnityEditor.Rendering.ShaderType)"/>,
	/// vertex and fragment stages), then deduplicated.
	/// </para>
	/// <para>
	/// <b>Generated like the rest of the art.</b> The file is under <see cref="ProceduralArtCatalogue.Root"/>
	/// (gitignored), with the path-derived GUID (<see cref="ProceduralArtPayload.GuidFor"/>) so the profile's
	/// committed reference resolves on every machine. It is written as text rather than through
	/// <c>AssetDatabase.CreateAsset</c>: the content is a sorted function of the materials on disk, and
	/// writing it ourselves gives a byte-for-byte comparison, so an unchanged collection is never rewritten
	/// or reimported. Its main object is <see cref="ProceduralArtPayload.ShaderVariantCollectionFileId"/>.
	/// Materials under <c>Assets/LOCAL</c> are included. A project may build scenes from
	/// <c>Assets/LOCAL/Scenes</c> (the "Enable Local Directory" option) dressed with its own LOCAL
	/// vegetation and rock materials, and those clones need their keyword sets as much as any other.
	/// Reading them is safe: the collection names shaders and keyword strings, never a material, and it
	/// is itself gitignored build output, rebuilt on each machine like the terrain arrays.
	/// </para>
	/// <para>
	/// <b>When.</b> The last step of every generation (after the materials), the cheap
	/// <see cref="IsStale"/> look in <see cref="ProceduralArtPayload.Check"/> on every script reload (an
	/// authored material with a new keyword set rewrites it with no generation), and before every build
	/// (<see cref="BiomeArtGenerator.EnsureCurrentForBuild"/>).
	/// </para>
	/// </remarks>
	public static class ProceduralArtVariants
	{
		/// <summary>The folder the collection lives in.</summary>
		public const string Folder = ProceduralArtCatalogue.Root + "/Variants";

		/// <summary>The generated collection. Its GUID is <see cref="ProceduralArtPayload.GuidFor"/> of this path.</summary>
		public const string AssetPath = Folder + "/FishIndirectVariants.shadervariants";

		/// <summary>The gitignored LOCAL prefix (Assets/LOCAL and its siblings Assets/LOCAL*). Its materials ARE read: see the class remarks.</summary>
		public const string LocalPrefix = "Assets/LOCAL";

		/// <summary>
		/// Each source shader and its indirect twin, as TerrainGpuRenderer swaps them. Kept beside the
		/// renderer's table rather than shared: the renderer is client runtime code this editor assembly does not reference.
		/// </summary>
		public static readonly IReadOnlyList<KeyValuePair<string, string>> IndirectTwins = new[]
		{
			new KeyValuePair<string, string>("FishMMO/Vegetation", "FishMMO/Vegetation Indirect"),
			new KeyValuePair<string, string>("FishMMO/Weather Lit", "FishMMO/Weather Lit Indirect"),
		};

		/// <summary>The pass types listed when a twin's passes cannot be read: the two every lit, shadowed draw uses, and motion vectors.</summary>
		public static readonly PassType[] DefaultPassTypes = { PassType.ScriptableRenderPipeline, PassType.ShadowCaster, PassType.MotionVectors };

		/// <summary>The twin for a source shader name, or null.</summary>
		public static string TwinOf(string shaderName)
		{
			foreach (KeyValuePair<string, string> pair in IndirectTwins)
			{
				if (string.Equals(pair.Key, shaderName, StringComparison.Ordinal))
				{
					return pair.Value;
				}
			}
			return null;
		}

		/// <summary>True for a path under <see cref="LocalPrefix"/>.</summary>
		public static bool IsLocal(string path)
		{
			return !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(LocalPrefix, StringComparison.Ordinal);
		}

		/// <summary>
		/// The pass type Unity gives a pass with this LightMode tag, or null for one the collection leaves
		/// out (Meta). See the class remarks for where the mapping comes from.
		/// </summary>
		public static PassType? PassTypeOf(string lightMode)
		{
			if (string.IsNullOrEmpty(lightMode) || lightMode == "SRPDefaultUnlit")
			{
				return PassType.ScriptableRenderPipelineDefaultUnlit;
			}
			switch (lightMode)
			{
				case "ShadowCaster":
					return PassType.ShadowCaster;
				case "MotionVectors":
					return PassType.MotionVectors;
				case "Meta":
					return null;
				default:
					return PassType.ScriptableRenderPipeline;
			}
		}

		// ── The pure part ─────────────────────────────────────────────

		/// <summary>One material as the collection sees it: where it is, its shader's name and its enabled keywords.</summary>
		public readonly struct MaterialKeywords
		{
			public readonly string Path;
			public readonly string Shader;
			public readonly IReadOnlyList<string> Keywords;

			public MaterialKeywords(string path, string shader, IReadOnlyList<string> keywords)
			{
				Path = path;
				Shader = shader;
				Keywords = keywords ?? Array.Empty<string>();
			}
		}

		/// <summary>One collection entry: an indirect shader, a pass type and its keywords (sorted, space-separated, as Unity stores them).</summary>
		public readonly struct Variant : IEquatable<Variant>, IComparable<Variant>
		{
			public readonly string Shader;
			public readonly PassType PassType;
			public readonly string Keywords;

			public Variant(string shader, PassType passType, string keywords)
			{
				Shader = shader;
				PassType = passType;
				Keywords = keywords ?? string.Empty;
			}

			public bool Equals(Variant other) => string.Equals(Shader, other.Shader, StringComparison.Ordinal) && PassType == other.PassType && string.Equals(Keywords, other.Keywords, StringComparison.Ordinal);
			public override bool Equals(object obj) => obj is Variant v && Equals(v);
			public override int GetHashCode() => ((Shader?.GetHashCode() ?? 0) * 397 ^ (int)PassType) * 397 ^ Keywords.GetHashCode();

			/// <summary>Shader name, then pass type, then keywords, all ordinal: the collection's order.</summary>
			public int CompareTo(Variant other)
			{
				int c = string.CompareOrdinal(Shader, other.Shader);
				if (c != 0)
				{
					return c;
				}
				c = ((int)PassType).CompareTo((int)other.PassType);
				return c != 0 ? c : string.CompareOrdinal(Keywords, other.Keywords);
			}

			public override string ToString() => $"{Shader} [{PassType}] {Keywords}";
		}

		/// <summary>
		/// The collection's entries for these materials: each material of a source shader, outside
		/// <see cref="LocalPrefix"/>, contributes its keywords mapped onto the twin, once per pass type the
		/// twin has, cut down to the keywords that type's passes declare. Deduplicated and sorted
		/// (<see cref="Variant.CompareTo"/>), so the same materials always give the same list.
		/// </summary>
		/// <param name="passesOf">
		/// For a twin's name, its pass types and the keywords each declares. Null, or a null answer, means
		/// unknown: <see cref="DefaultPassTypes"/>, keywords uncut.
		/// </param>
		public static List<Variant> Variants(IEnumerable<MaterialKeywords> materials, Func<string, IReadOnlyDictionary<PassType, HashSet<string>>> passesOf = null)
		{
			var set = new HashSet<Variant>();
			var passCache = new Dictionary<string, IReadOnlyDictionary<PassType, HashSet<string>>>(StringComparer.Ordinal);
			if (materials != null)
			{
				foreach (MaterialKeywords material in materials)
				{
					string twin = TwinOf(material.Shader);
					if (twin == null)
					{
						continue;
					}
					if (!passCache.TryGetValue(twin, out IReadOnlyDictionary<PassType, HashSet<string>> passes))
					{
						passes = passesOf?.Invoke(twin);
						passCache[twin] = passes;
					}
					if (passes == null || passes.Count == 0)
					{
						foreach (PassType type in DefaultPassTypes)
						{
							set.Add(new Variant(twin, type, JoinKeywords(material.Keywords, null)));
						}
						continue;
					}
					foreach (KeyValuePair<PassType, HashSet<string>> pass in passes)
					{
						set.Add(new Variant(twin, pass.Key, JoinKeywords(material.Keywords, pass.Value)));
					}
				}
			}
			var list = new List<Variant>(set);
			list.Sort();
			return list;
		}

		/// <summary>The keywords trimmed, kept only where <paramref name="declared"/> has them (all when null), deduplicated, ordinal-sorted and space-joined.</summary>
		public static string JoinKeywords(IEnumerable<string> keywords, ICollection<string> declared)
		{
			var kept = new SortedSet<string>(StringComparer.Ordinal);
			if (keywords != null)
			{
				foreach (string raw in keywords)
				{
					string k = raw?.Trim();
					if (!string.IsNullOrEmpty(k) && (declared == null || declared.Contains(k)))
					{
						kept.Add(k);
					}
				}
			}
			return string.Join(" ", kept);
		}

		/// <summary>
		/// The collection file for these entries, as Unity serialises one: shaders by GUID in the entries'
		/// order. An entry whose shader has no GUID in <paramref name="shaderGuids"/> is left out.
		/// </summary>
		public static string Serialize(IReadOnlyList<Variant> variants, IReadOnlyDictionary<string, string> shaderGuids)
		{
			var sb = new StringBuilder();
			sb.Append("%YAML 1.1\n");
			sb.Append("%TAG !u! tag:unity3d.com,2011:\n");
			sb.Append("--- !u!200 &").Append(ProceduralArtPayload.ShaderVariantCollectionFileId).Append('\n');
			sb.Append("ShaderVariantCollection:\n");
			sb.Append("  m_ObjectHideFlags: 0\n");
			sb.Append("  m_CorrespondingSourceObject: {fileID: 0}\n");
			sb.Append("  m_PrefabInstance: {fileID: 0}\n");
			sb.Append("  m_PrefabAsset: {fileID: 0}\n");
			sb.Append("  m_Name: ").Append(Path.GetFileNameWithoutExtension(AssetPath)).Append('\n');

			var body = new StringBuilder();
			string current = null;
			foreach (Variant v in variants ?? Array.Empty<Variant>())
			{
				if (shaderGuids == null || !shaderGuids.TryGetValue(v.Shader, out string guid) || string.IsNullOrEmpty(guid))
				{
					continue;
				}
				if (!string.Equals(current, v.Shader, StringComparison.Ordinal))
				{
					current = v.Shader;
					body.Append("  - first: {fileID: 4800000, guid: ").Append(guid).Append(", type: 3}\n");
					body.Append("    second:\n");
					body.Append("      variants:\n");
				}
				body.Append("      - keywords:");
				if (v.Keywords.Length > 0)
				{
					body.Append(' ').Append(v.Keywords);
				}
				body.Append('\n');
				body.Append("        passType: ").Append((int)v.PassType).Append('\n');
			}
			if (body.Length == 0)
			{
				sb.Append("  m_Shaders: []\n");
			}
			else
			{
				sb.Append("  m_Shaders:\n").Append(body);
			}
			return sb.ToString();
		}

		/// <summary>
		/// Reads a text-serialised .mat: its shader's GUID and its enabled keywords (<c>m_ValidKeywords</c>,
		/// or the pre-2021.2 <c>m_ShaderKeywords</c> line). False when the text is not a material's.
		/// </summary>
		public static bool TryParseMaterial(string text, out string shaderGuid, out List<string> keywords)
		{
			shaderGuid = null;
			keywords = new List<string>();
			if (string.IsNullOrEmpty(text) || !text.StartsWith("%YAML", StringComparison.Ordinal))
			{
				return false;
			}
			string[] lines = text.Replace("\r\n", "\n").Split('\n');
			bool inMaterial = false;
			bool sawValid = false;
			List<string> legacy = null;
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line.StartsWith("--- ", StringComparison.Ordinal))
				{
					// Only the Material object: a URP material also carries an AssetVersion MonoBehaviour.
					inMaterial = i + 1 < lines.Length && lines[i + 1].StartsWith("Material:", StringComparison.Ordinal);
					continue;
				}
				if (!inMaterial)
				{
					continue;
				}
				string trimmed = line.TrimStart();
				if (shaderGuid == null && trimmed.StartsWith("m_Shader:", StringComparison.Ordinal))
				{
					int g = trimmed.IndexOf("guid:", StringComparison.Ordinal);
					if (g >= 0)
					{
						string rest = trimmed.Substring(g + 5).TrimStart();
						int end = rest.IndexOfAny(new[] { ',', '}' });
						shaderGuid = (end >= 0 ? rest.Substring(0, end) : rest).Trim().ToLowerInvariant();
					}
				}
				else if (trimmed.StartsWith("m_ValidKeywords:", StringComparison.Ordinal))
				{
					sawValid = true;
					string after = trimmed.Substring("m_ValidKeywords:".Length).Trim();
					if (after.Length > 0)
					{
						// A flow list: [] or [A, B].
						foreach (string k in after.Trim('[', ']').Split(','))
						{
							if (k.Trim().Length > 0)
							{
								keywords.Add(k.Trim());
							}
						}
						continue;
					}
					int indent = line.Length - trimmed.Length;
					while (i + 1 < lines.Length)
					{
						string next = lines[i + 1];
						string nt = next.TrimStart();
						if (!nt.StartsWith("- ", StringComparison.Ordinal) || next.Length - nt.Length < indent)
						{
							break;
						}
						keywords.Add(nt.Substring(2).Trim());
						i++;
					}
				}
				else if (trimmed.StartsWith("m_ShaderKeywords:", StringComparison.Ordinal))
				{
					legacy = new List<string>(trimmed.Substring("m_ShaderKeywords:".Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
				}
			}
			if (!sawValid && legacy != null)
			{
				keywords = legacy;
			}
			return shaderGuid != null;
		}

		// ── The editor part ───────────────────────────────────────────

		private static readonly ShaderTagId LightModeTag = new ShaderTagId("LightMode");

		/// <summary>
		/// A shader's pass types and the keywords each declares (vertex and fragment stages), Meta left
		/// out. Null when nothing could be read, which <see cref="Variants"/> treats as unknown.
		/// </summary>
		public static IReadOnlyDictionary<PassType, HashSet<string>> PassesOf(Shader shader)
		{
			if (!Readable(shader, out ShaderData data))
			{
				return null;
			}
			var result = new Dictionary<PassType, HashSet<string>>();
			int declared = 0;
			// GetPassKeywords LOGS "Invalid pass identifier." (an error, not an exception, so no catch can
			// quiet it) for a subshader or pass index outside the shader's own parsed form, and that is its
			// only check: neither the stage nor the platform makes an identifier invalid. Readable has made the
			// runtime counts used here agree with the parsed form's (see there).
			for (int s = 0; s < Math.Min(shader.subshaderCount, data.SerializedSubshaderCount); s++)
			{
				int passes = Math.Min(shader.GetPassCountInSubshader(s), data.GetSubshader(s).PassCount);
				for (int p = 0; p < passes; p++)
				{
					PassType? type = PassTypeOf(shader.FindPassTagValue(s, p, LightModeTag).name);
					if (type == null)
					{
						continue;
					}
					if (!result.TryGetValue(type.Value, out HashSet<string> keywords))
					{
						keywords = new HashSet<string>(StringComparer.Ordinal);
						result[type.Value] = keywords;
					}
					var id = new PassIdentifier((uint)s, (uint)p);
					ShaderData.Pass compiled = data.GetSubshader(s).GetPass(p);
					foreach (UnityEditor.Rendering.ShaderType stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
					{
						// A pass with no program for the stage has no keywords there to read.
						if (!compiled.HasShaderStage(stage))
						{
							continue;
						}
						try
						{
							foreach (LocalKeyword k in ShaderUtil.GetPassKeywords(shader, id, stage))
							{
								keywords.Add(k.name);
								declared++;
							}
						}
						catch (ArgumentException)
						{
							// A pass without that stage.
						}
					}
				}
			}
			// A twin that declares no keyword at all did not compile or parse; cutting every set to nothing would hide that.
			return result.Count == 0 || declared == 0 ? null : result;
		}

		/// <summary>
		/// True when a shader's passes can be read now: it has no compile error, and its own parsed form
		/// (<see cref="ShaderData.SerializedSubshaderCount"/>, the only form
		/// <see cref="ShaderUtil.GetPassKeywords(Shader, in PassIdentifier, UnityEditor.Rendering.ShaderType)"/> checks a
		/// <see cref="PassIdentifier"/> against) is not empty and not larger than its runtime form (what
		/// <c>Shader.subshaderCount</c>, <see cref="ShaderData.GetSubshader"/> and <c>PassCount</c> report). A runtime
		/// form LARGER than the parsed one is normal: it carries the FallBack chain's subshaders after the shader's
		/// own (Vegetation Indirect: 1 own + URP FallbackError + Core FallbackError = 3 runtime, 1 parsed, steadily),
		/// and the readers only walk the first min(runtime, parsed) subshaders, the shader's own. A parsed form
		/// emptier than the runtime one (seen after a domain reload) would make every pass log an invalid identifier;
		/// that shader is reported once and left unread.
		/// </summary>
		private static bool Readable(Shader shader, out ShaderData data)
		{
			data = null;
			if (shader == null || ShaderUtil.ShaderHasError(shader))
			{
				return false;
			}
			data = ShaderUtil.GetShaderData(shader);
			if (data == null || data.SubshaderCount <= 0)
			{
				return false;
			}
			if (data.SerializedSubshaderCount <= 0 || data.SerializedSubshaderCount > data.SubshaderCount)
			{
				if (FormMismatchReported.Add(shader.name))
				{
					Debug.LogWarning($"{AssetPath}: '{shader.name}' has {data.SubshaderCount} runtime subshader(s) but {data.SerializedSubshaderCount} in its parsed form, so its passes cannot be read and the collection is not judged (a parsed form smaller than the runtime one is only its FallBack chain; this is the other way round). Reimport the shader if this persists.");
				}
				return false;
			}
			return true;
		}

		/// <summary>Shaders <see cref="Readable"/> has already reported this domain, so a stuck one warns once per reload, not per check.</summary>
		private static readonly HashSet<string> FormMismatchReported = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>
		/// True when every indirect twin is found and readable. While one is not (still importing, or not
		/// compiling), the collection cannot be judged: <see cref="IsStale"/> says no and <see cref="Ensure(List{string})"/>
		/// leaves the file alone, rather than rewriting it from shaders that could not be read.
		/// </summary>
		public static bool TwinsReady()
		{
			foreach (KeyValuePair<string, string> pair in IndirectTwins)
			{
				if (!Readable(Shader.Find(pair.Value), out _))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Every material in the project outside <see cref="LocalPrefix"/> whose shader is a source shader, with its enabled keywords.</summary>
		public static List<MaterialKeywords> CollectMaterials(IReadOnlyDictionary<string, string> sourceShaderByGuid)
		{
			var result = new List<MaterialKeywords>();
			if (sourceShaderByGuid == null || sourceShaderByGuid.Count == 0)
			{
				return result;
			}
			var paths = new SortedSet<string>(StringComparer.Ordinal);
			foreach (string guid in AssetDatabase.FindAssets("t:Material"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!string.IsNullOrEmpty(path) && path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
				{
					paths.Add(path);
				}
			}
			foreach (string path in paths)
			{
				string text = File.Exists(path) ? File.ReadAllText(path) : null;
				if (TryParseMaterial(text, out string shaderGuid, out List<string> keywords))
				{
					if (sourceShaderByGuid.TryGetValue(shaderGuid, out string shaderName))
					{
						result.Add(new MaterialKeywords(path, shaderName, keywords));
					}
					continue;
				}
				// Not text (a binary material): ask Unity, which costs a load.
				Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (material != null && material.shader != null && TwinOf(material.shader.name) != null)
				{
					var names = new List<string>();
					foreach (LocalKeyword k in material.enabledKeywords)
					{
						names.Add(k.name);
					}
					result.Add(new MaterialKeywords(path, material.shader.name, names));
				}
			}
			return result;
		}

		/// <summary>What the collection file should hold now. Problems (a twin missing) are added to <paramref name="problems"/>.</summary>
		public static string CurrentText(List<string> problems)
		{
			var sourceByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
			var twinGuids = new Dictionary<string, string>(StringComparer.Ordinal);
			var twinPasses = new Dictionary<string, IReadOnlyDictionary<PassType, HashSet<string>>>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, string> pair in IndirectTwins)
			{
				string sourceGuid = GuidOfShader(Shader.Find(pair.Key));
				if (sourceGuid != null)
				{
					sourceByGuid[sourceGuid] = pair.Key;
				}
				Shader twin = Shader.Find(pair.Value);
				string twinGuid = GuidOfShader(twin);
				if (twinGuid == null)
				{
					problems?.Add($"{AssetPath}: the shader '{pair.Value}' was not found, so materials of '{pair.Key}' get no indirect variants (the GPU terrain renderer will draw them with every shader_feature off).");
					continue;
				}
				twinGuids[pair.Value] = twinGuid;
				IReadOnlyDictionary<PassType, HashSet<string>> passes = PassesOf(twin);
				if (passes == null)
				{
					problems?.Add($"{AssetPath}: the passes of '{pair.Value}' could not be read (does it compile?); its variants list the default pass types with every material keyword.");
				}
				twinPasses[pair.Value] = passes;
			}
			List<Variant> variants = Variants(CollectMaterials(sourceByGuid), name => twinPasses.TryGetValue(name, out IReadOnlyDictionary<PassType, HashSet<string>> p) ? p : null);
			return Serialize(variants, twinGuids);
		}

		private static string GuidOfShader(Shader shader)
		{
			if (shader == null)
			{
				return null;
			}
			string path = AssetDatabase.GetAssetPath(shader);
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			string guid = AssetDatabase.AssetPathToGUID(path);
			return string.IsNullOrEmpty(guid) ? null : guid;
		}

		/// <summary>
		/// True when the collection on disk is missing, carries another GUID than its path's, or differs by a
		/// byte from what the materials now call for. Writes nothing: one material search, a hundred-odd
		/// small text reads and two shaders' pass tables, cheap enough for every script reload.
		/// </summary>
		public static bool IsStale()
		{
			if (!TwinsReady())
			{
				return false;   // judged on the next reload, once the twins are imported and compile
			}
			if (!ProceduralArtPayload.HasExpectedGuid(AssetPath))
			{
				return true;
			}
			byte[] want = Encoding.UTF8.GetBytes(CurrentText(null));
			byte[] have = File.ReadAllBytes(AssetPath);
			if (want.Length != have.Length)
			{
				return true;
			}
			for (int i = 0; i < want.Length; i++)
			{
				if (want[i] != have[i])
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Brings the collection up to date, logging any problem. True when it is current and nothing went wrong.</summary>
		public static bool Ensure() => Ensure(null);

		/// <summary>
		/// Brings the collection up to date: written (and imported) only when its bytes or .meta change.
		/// Problems go to <paramref name="problems"/>, or to the console when it is null. True when nothing went wrong.
		/// </summary>
		public static bool Ensure(List<string> problems)
		{
			var own = new List<string>();
			if (!TwinsReady())
			{
				own.Add($"{AssetPath}: an indirect shader is still importing or does not compile; the collection is left as it is until it does.");
				if (problems != null)
				{
					problems.AddRange(own);
				}
				else
				{
					Debug.LogWarning("[Biome art] " + own[0]);
				}
				return false;
			}
			string text = CurrentText(own);
			WorldEditorAssets.EnsureFolder(Folder);
			bool changed = ProceduralArtPayload.WriteFile(AssetPath, Encoding.UTF8.GetBytes(text),
				guid => ProceduralArtPayload.MetaText(guid, ProceduralArtPayload.ShaderVariantCollectionFileId), own);
			if (changed)
			{
				AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
				ShaderVariantCollection collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetPath);
				if (collection == null)
				{
					own.Add($"{AssetPath}: written but not loadable as a ShaderVariantCollection after import");
				}
				else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(collection, out string _, out long fileId) && fileId != ProceduralArtPayload.ShaderVariantCollectionFileId)
				{
					own.Add($"{AssetPath}: Unity reads its main object as {fileId}, not the expected {ProceduralArtPayload.ShaderVariantCollectionFileId}; update the constant in ProceduralArtPayload.");
				}
				else
				{
					Debug.Log($"[Biome art] Wrote {AssetPath}: {collection.shaderCount} indirect shader(s), {collection.variantCount} variant(s).");
				}
			}
			if (problems != null)
			{
				problems.AddRange(own);
			}
			else
			{
				foreach (string p in own)
				{
					Debug.LogWarning("[Biome art] " + p);
				}
			}
			return own.Count == 0;
		}
	}
}
#endif
