using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The GPU-driven copies of the vegetation and weather-lit shaders (FishMMO/Vegetation Indirect,
	/// FishMMO/Weather Lit Indirect), which TerrainTreeInstancing draws with RenderMeshIndirect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each is its original plus procedural instancing, and nothing else: the renderer clones a material
	/// and swaps the shader, so a property the copy lacks silently resets on the clone, and a pass that
	/// drifts from the original draws trees that look different the moment they become GPU-driven. The
	/// originals must stay free of the procedural option, or every DrawMeshInstanced / LODGroup /
	/// detail-painting draw Unity makes with them loses its matrices.
	/// </para>
	/// <para>
	/// Every pin runs with a control (<see cref="SourceScanPins.HoldsAndFires"/>): the defect is put back
	/// into a copy of the source and the pin must go red on it.
	/// </para>
	/// </remarks>
	public class IndirectShaderTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Include = Shaders + "FishIndirectInstancing.hlsl";
		private const string LodFade = Shaders + "FishLodFade.hlsl";
		private const string ProceduralPragma = "#pragma instancing_options procedural:FishIndirectSetup";
		private const string IndirectInclude = "#include \"FishIndirectInstancing.hlsl\"";

		private struct Pair
		{
			public string Original;
			public string Indirect;
			public string OriginalName;
			public string IndirectName;
			public string[] InstancedPasses;
		}

		private static readonly Pair[] Pairs =
		{
			new Pair
			{
				Original = Shaders + "FishVegetation.shader",
				Indirect = Shaders + "FishVegetationIndirect.shader",
				OriginalName = "FishMMO/Vegetation",
				IndirectName = "FishMMO/Vegetation Indirect",
				InstancedPasses = new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" },
			},
			new Pair
			{
				Original = Shaders + "FishWeatherLit.shader",
				Indirect = Shaders + "FishWeatherLitIndirect.shader",
				OriginalName = "FishMMO/Weather Lit",
				IndirectName = "FishMMO/Weather Lit Indirect",
				InstancedPasses = new[] { "ForwardLit", "ShadowCaster", "GBuffer", "DepthOnly", "DepthNormals", "MotionVectors", "XRMotionVectors" },
			},
		};

		// ── Parsing ──────────────────────────────────────────────────────

		/// <summary>Each pass's name and its HLSLPROGRAM block, in order.</summary>
		private static List<(string Name, string Program)> Passes(string shader)
		{
			var passes = new List<(string, string)>();
			foreach (Match pass in Regex.Matches(shader, "Name \"([^\"]+)\"(.*?)ENDHLSL", RegexOptions.Singleline))
			{
				int program = pass.Groups[2].Value.IndexOf("HLSLPROGRAM", StringComparison.Ordinal);
				passes.Add((pass.Groups[1].Value, program < 0 ? string.Empty : pass.Groups[2].Value.Substring(program)));
			}
			return passes;
		}

		/// <summary>The shader from its <c>Shader "…"</c> line on (the header comments differ, as they should).</summary>
		private static string FromShaderLine(string shader)
		{
			int at = shader.IndexOf("Shader \"", StringComparison.Ordinal);
			return at < 0 ? null : shader.Substring(at);
		}

		/// <summary>The indirect shader with its two per-pass additions removed and its name put back.</summary>
		private static string WithoutIndirectAdditions(string indirect, Pair pair)
		{
			string body = FromShaderLine(indirect);
			if (body == null)
			{
				return null;
			}
			var kept = new List<string>();
			foreach (string line in body.Split('\n'))
			{
				string t = line.Trim();
				if (t == ProceduralPragma || t == IndirectInclude)
				{
					continue;
				}
				kept.Add(line);
			}
			return string.Join("\n", kept).Replace($"Shader \"{pair.IndirectName}\"", $"Shader \"{pair.OriginalName}\"");
		}

		private static string PropertiesBlock(string shader)
		{
			string body = FromShaderLine(shader);
			return body == null ? null : SourceScanPins.Body(body, "Properties");
		}

		// ── Checks (source in, reason or null out) ───────────────────────

		private static string EveryInstancedPassIsProcedural(string indirect, Pair pair)
		{
			if (!indirect.Contains($"Shader \"{pair.IndirectName}\""))
			{
				return $"the shader is not named {pair.IndirectName}; the renderer finds it by that name";
			}
			var seen = new HashSet<string>();
			foreach ((string name, string program) in Passes(SourceScanPins.CodeOnly(indirect)))
			{
				bool instanced = program.Contains("#pragma multi_compile_instancing");
				bool procedural = program.Contains(ProceduralPragma);
				int include = program.IndexOf(IndirectInclude, StringComparison.Ordinal);
				if (!instanced)
				{
					if (procedural || include >= 0)
					{
						return $"{name} is not instanced but declares the procedural setup";
					}
					continue;
				}
				if (!procedural)
				{
					return $"{name} has no `{ProceduralPragma}`: RenderMeshIndirect would draw every instance at the draw's own matrix";
				}
				if (include < 0)
				{
					return $"{name} does not include FishIndirectInstancing.hlsl, so FishIndirectSetup is never defined";
				}
				// Before the pass code: FishLodFade only reads the indirect fade when it finds the include already there.
				foreach (Match other in Regex.Matches(program, "#include(?:_with_pragmas)? \"([^\"]+)\""))
				{
					string file = other.Groups[1].Value;
					bool passCode = (file.StartsWith("Fish", StringComparison.Ordinal) && file != "FishWeatherLitInput.hlsl" && file != "FishIndirectInstancing.hlsl")
						|| file.Contains("/Shaders/") || file.EndsWith("ObjectMotionVectors.hlsl", StringComparison.Ordinal);
					if (passCode && other.Index < include)
					{
						return $"{name} includes {file} before FishIndirectInstancing.hlsl; the procedural LOD fade would read 0";
					}
				}
				seen.Add(name);
			}
			foreach (string required in pair.InstancedPasses)
			{
				if (!seen.Contains(required))
				{
					return $"{required} is missing or not procedural";
				}
			}
			return null;
		}

		private static string MatchesTheOriginal(string indirect, string original, Pair pair)
		{
			string indirectProperties = PropertiesBlock(indirect);
			string originalProperties = PropertiesBlock(original);
			if (indirectProperties == null || originalProperties == null)
			{
				return "a Properties block was not found";
			}
			if (indirectProperties != originalProperties)
			{
				return "the Properties blocks differ: a cloned material with the shader swapped would lose or reset values";
			}
			string stripped = WithoutIndirectAdditions(indirect, pair);
			string expected = FromShaderLine(original);
			if (stripped != expected)
			{
				return "beyond the procedural pragma and the include, the indirect shader differs from the original (a pragma, render state or include drifted)";
			}
			return null;
		}

		private static string OriginalHasNoProceduralOption(string original)
		{
			string code = SourceScanPins.CodeOnly(original);
			if (code.Contains("procedural:"))
			{
				return "the original declares a procedural instancing option: Unity's own instanced draws with it would lose their matrices";
			}
			if (code.Contains(IndirectInclude))
			{
				return "the original includes FishIndirectInstancing.hlsl";
			}
			return null;
		}

		// ── Tests ────────────────────────────────────────────────────────

		[Test]
		public void BothIndirectShaders_AreProceduralInEveryInstancedPass()
		{
			foreach (Pair pair in Pairs)
			{
				Pair p = pair;
				SourceScanPins.HoldsAndFires(p.IndirectName, SourceScanPins.ReadSource(p.Indirect),
					s => EveryInstancedPassIsProcedural(s, p),
					RemoveFirstLine(ProceduralPragma),
					"a pass without the procedural option");
				SourceScanPins.HoldsAndFires(p.IndirectName + " (include order)", SourceScanPins.ReadSource(p.Indirect),
					s => EveryInstancedPassIsProcedural(s, p),
					MoveIncludeToEndOfFirstProgram(),
					"the include after the pass code");
			}
		}

		[Test]
		public void BothIndirectShaders_MatchTheirOriginalsExceptForTheProceduralSetup()
		{
			foreach (Pair pair in Pairs)
			{
				Pair p = pair;
				string original = SourceScanPins.ReadSource(p.Original);
				SourceScanPins.HoldsAndFires(p.IndirectName + " (Properties)", SourceScanPins.ReadSource(p.Indirect),
					s => MatchesTheOriginal(s, original, p),
					SourceScanPins.Replace("    Properties\n    {\n", "    Properties\n    {\n        _FishNotInTheOriginal(\"x\", Float) = 0\n"),
					"a property the original does not have");
				SourceScanPins.HoldsAndFires(p.IndirectName + " (passes)", SourceScanPins.ReadSource(p.Indirect),
					s => MatchesTheOriginal(s, original, p),
					SourceScanPins.RegexReplaceFirst(@"#pragma multi_compile _ LOD_FADE_CROSSFADE\n", string.Empty),
					"a pass that dropped a keyword the original compiles");
			}
		}

		[Test]
		public void TheOriginals_HaveNoProceduralOption()
		{
			foreach (Pair pair in Pairs)
			{
				SourceScanPins.HoldsAndFires(pair.OriginalName, SourceScanPins.ReadSource(pair.Original),
					OriginalHasNoProceduralOption,
					SourceScanPins.InsertBefore("#pragma multi_compile_instancing", ProceduralPragma + "\n            "),
					"the procedural option on the original");
			}
		}

		[Test]
		public void TheVegetationTint_HasItsPatchSizeInBothShaders()
		{
			// VegTintVariation reads _TintPatchMetres from UnityPerMaterial; without the property a
			// material never gets the 12 m default, and the patches collapse to plant-by-plant salt and pepper.
			foreach (string path in new[] { Pairs[0].Original, Pairs[0].Indirect })
			{
				StringAssert.Contains("_TintPatchMetres(", PropertiesBlock(SourceScanPins.ReadSource(path)), $"{path} lost _TintPatchMetres");
			}
			string passes = SourceScanPins.ReadSource(Shaders + "FishVegetationPasses.hlsl");
			string buffer = passes.Substring(passes.IndexOf("CBUFFER_START(UnityPerMaterial)", StringComparison.Ordinal));
			buffer = buffer.Substring(0, buffer.IndexOf("CBUFFER_END", StringComparison.Ordinal));
			StringAssert.Contains("_TintPatchMetres", buffer, "outside UnityPerMaterial the SRP batcher drops every vegetation material");
		}

		[Test]
		public void TheContract_KeepsItsNamesAndTheFadeSource()
		{
			string include = SourceScanPins.ReadSource(Include);
			string code = SourceScanPins.CodeOnly(include);
			// The renderer binds these by name (TerrainGpuRenderer); the compute writes the structs.
			StringAssert.Contains("StructuredBuffer<FishInstance> _FishInstances;", code);
			StringAssert.Contains("StructuredBuffer<FishVisible> _FishVisibleInstances;", code);
			StringAssert.Contains("StructuredBuffer<uint> _FishCommandBases;", code);
			StringAssert.Contains("GetCommandID(0)", code);
			StringAssert.Contains("#include \"UnityIndirect.cginc\"", code);
			StringAssert.Contains("void FishIndirectSetup()", code);
			// "_FishVisible" is Weather Lit's day/night dissolve property; a buffer of that name collides with it.
			SourceScanPins.HoldsAndFires("buffer names", code,
				s => Regex.IsMatch(s, @"StructuredBuffer<\w+>\s+_FishVisible\s*;") ? "a buffer named _FishVisible collides with FishMMO/Weather Lit's dissolve property" : null,
				SourceScanPins.Replace("_FishVisibleInstances;", "_FishVisible;"),
				"the buffer renamed back to _FishVisible");
			// Every matrix URP reads under procedural instancing is the instance's, the previous one too.
			SourceScanPins.HoldsAndFires("matrices", code,
				s => SourceScanPins.InOrder(SourceScanPins.Body(s, "void FishIndirectSetup()"),
					"unity_ObjectToWorld =", "unity_WorldToObject =", "unity_MatrixPreviousM =", "unity_MatrixPreviousMI =", "FishIndirectLodFade ="),
				SourceScanPins.Replace("unity_MatrixPreviousMI =", "float4x4 unusedPreviousMI ="),
				"the previous inverse matrix left to whatever the indirect call put there");

			// FishLodFade picks the indirect value in procedural variants, and keeps its instancing buffer otherwise.
			string lodFade = SourceScanPins.CodeOnly(SourceScanPins.ReadSource(LodFade));
			SourceScanPins.HoldsAndFires("fade source", lodFade,
				s => SourceScanPins.InOrder(SourceScanPins.Body(s, "float FishLodFadeValue()"), "FISH_INDIRECT_INSTANCED", "return FishIndirectLodFade;", "UNITY_ACCESS_INSTANCED_PROP(FishLodFadeProps, _FishLodFade)"),
				SourceScanPins.Replace("return FishIndirectLodFade;", "return 0.0;"),
				"procedural draws reading no fade");
		}

		// ── Mutations ────────────────────────────────────────────────────

		/// <summary>Removes the first line holding <paramref name="line"/> in the shader's code (after its <c>Shader "…"</c> line, so the header's prose about it is not what goes).</summary>
		private static Func<string, string> RemoveFirstLine(string line) =>
			s =>
			{
				int shader = s.IndexOf("Shader \"", StringComparison.Ordinal);
				int at = shader < 0 ? -1 : s.IndexOf(line, shader, StringComparison.Ordinal);
				if (at < 0)
				{
					return s;
				}
				int start = s.LastIndexOf('\n', at) + 1;
				int end = s.IndexOf('\n', at);
				return s.Remove(start, end - start + 1);
			};

		private static Func<string, string> MoveIncludeToEndOfFirstProgram() =>
			s =>
			{
				string withoutFirst = RemoveFirstLine(IndirectInclude)(s);
				int end = withoutFirst.IndexOf("ENDHLSL", StringComparison.Ordinal);
				return end < 0 ? s : withoutFirst.Insert(end, IndirectInclude + "\n            ");
			};
	}
}
