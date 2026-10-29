using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.Rendering;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The vegetation's pipelines in the opaque pass (draw-call round 2026-10-28). A per-material choice that does not
	/// change fixed-function state is a uniform branch, not a keyword: bark (normal map) and deadwood (none) are one shader
	/// variant, one pipeline. <c>_ALPHATEST_ON</c> stays a keyword (a discard turns early depth off for every draw of the
	/// variant, and leaves and bark differ in cull mode anyway). The primed and unprimed alpha-tested lit draws differ in
	/// depth writes, so they sit in render queues of their own instead of alternating in one.
	/// </summary>
	public class VegetationVariantTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Passes = Shaders + "FishVegetationPasses.hlsl";
		private const string Prime = Shaders + "FishVegetationPrime.shader";
		private const string Scatter = "Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterRenderer.cs";
		private const string Gpu = "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs";
		private static readonly string[] VegetationShaders =
		{
			Shaders + "FishVegetation.shader", Shaders + "FishVegetationIndirect.shader", Prime,
		};

		private static string NoNormalMapKeyword(string code)
		{
			if (Regex.IsMatch(code, @"#pragma\s+(shader_feature|multi_compile)\w*\s[^\n]*\b_NORMALMAP\b"))
			{
				return "a pass declares _NORMALMAP again: bark and deadwood split into two variants";
			}
			if (code.Contains("Toggle(_NORMALMAP)"))
			{
				return "_UseNormalMap drives the _NORMALMAP keyword again";
			}
			return null;
		}

		[Test]
		public void TheNormalMap_IsAUniform_NotAKeyword()
		{
			foreach (string shader in VegetationShaders)
			{
				SourceScanPins.HoldsAndFires(shader, SourceScanPins.ReadCode(shader), NoNormalMapKeyword,
					SourceScanPins.Replace("#pragma vertex ", "#pragma shader_feature_local _NORMALMAP\n            #pragma vertex "),
					"the keyword declared again");
			}

			string passes = SourceScanPins.ReadCode(Passes);
			SourceScanPins.HoldsAndFires("UnityPerMaterial", passes,
				code => SourceScanPins.InOrder(code, "CBUFFER_START(UnityPerMaterial)", "half _UseNormalMap;", "CBUFFER_END"),
				SourceScanPins.Replace("half _UseNormalMap;", string.Empty), "the switch outside the material's constants (breaks the SRP batcher)");
			SourceScanPins.HoldsAndFires("VegNormal", passes,
				code =>
				{
					string body = SourceScanPins.Body(code, "half3 VegNormal(");
					if (body == null)
					{
						return "VegNormal is gone";
					}
					if (body.Contains("_NORMALMAP"))
					{
						return "VegNormal reads the keyword";
					}
					return SourceScanPins.InOrder(body, "UNITY_BRANCH", "if (_UseNormalMap > 0.5h)", "SAMPLE_TEXTURE2D(_BumpMap");
				},
				SourceScanPins.Replace("if (_UseNormalMap > 0.5h)", "#if defined(_NORMALMAP)\n    if (true)"), "the keyword back in VegNormal");
		}

		[Test]
		public void PrimedAndUnprimedAlphaTestedDraws_KeepQueuesOfTheirOwn()
		{
			Assert.That(TerrainGpuRenderer.PrimeQueueOffset, Is.LessThan(TerrainGpuRenderer.UnprimedAlphaTestQueueOffset),
				"the primes lay the trees' depth before any bush is shaded");
			Assert.That(TerrainGpuRenderer.UnprimedAlphaTestQueueOffset, Is.LessThan(0),
				"the unprimed bushes draw before the primed lit draws, not among them");
			int primeQueue = (int)RenderQueue.AlphaTest + TerrainGpuRenderer.PrimeQueueOffset;
			Assert.That(primeQueue, Is.GreaterThan((int)RenderQueue.Geometry), "after the opaque geometry (the bark, the rocks)");
			Assert.That((int)RenderQueue.AlphaTest + TerrainGpuRenderer.UnprimedAlphaTestQueueOffset, Is.LessThanOrEqualTo((int)RenderQueue.GeometryLast),
				"inside the opaque pass's range");

			// The prime shader's own tag says the same queue (a material made from it without the renderer's setup).
			Match tag = Regex.Match(SourceScanPins.ReadCode(Prime), "\"Queue\"\\s*=\\s*\"AlphaTest(-\\d+)?\"");
			Assert.That(tag.Success, Is.True, "the prime's Queue tag");
			int tagged = (int)RenderQueue.AlphaTest + (tag.Groups[1].Success ? int.Parse(tag.Groups[1].Value) : 0);
			Assert.That(tagged, Is.EqualTo(primeQueue), "FishVegetationPrime's Queue tag");

			SourceScanPins.HoldsAndFires("PrimeOf", SourceScanPins.ReadCode(Gpu),
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private Material PrimeOf("), "prime.renderQueue = clone.renderQueue + PrimeQueueOffset;"),
				SourceScanPins.Replace("clone.renderQueue + PrimeQueueOffset", "clone.renderQueue - 1"), "the prime one queue ahead: among the bushes");
			SourceScanPins.HoldsAndFires("DetailScatterRenderer.CloneOf", SourceScanPins.ReadCode(Scatter),
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private Material CloneOf("),
					"clone.IsKeywordEnabled(\"_ALPHATEST_ON\")", "clone.renderQueue = material.renderQueue + TerrainGpuRenderer.UnprimedAlphaTestQueueOffset;", "clones.Add(material, clone);"),
				SourceScanPins.Replace("clone.renderQueue = material.renderQueue + TerrainGpuRenderer.UnprimedAlphaTestQueueOffset;", string.Empty),
				"the bushes back in the trees' queue");
		}

		/// <summary>
		/// The look is unchanged only if every vegetation material that drew the normal-map variant has the switch on, and
		/// none that drew the plain one has it on. Reads the generated materials on disk (gitignored art: skipped when absent).
		/// </summary>
		[Test]
		public void EveryVegetationMaterial_SwitchesItsNormalMapAsItsKeywordDid()
		{
			string vegetationGuid = ShaderGuid(Shaders + "FishVegetation.shader");
			var shaderLine = new Regex(@"m_Shader: \{fileID: \d+, guid: " + vegetationGuid);
			var keywordBlock = new Regex(@"m_(?:Valid|Invalid)Keywords:(.*?)\n  m_", RegexOptions.Singleline);
			var useNormalMap = new Regex(@"\n    - _UseNormalMap: ([-0-9.eE]+)\n");
			int seen = 0;
			var wrong = new List<string>();
			foreach (string path in Directory.EnumerateFiles("Assets", "*.mat", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(path).Replace("\r\n", "\n");
				if (!shaderLine.IsMatch(text))
				{
					continue;
				}
				++seen;
				bool keyword = false;
				foreach (Match block in keywordBlock.Matches(text))
				{
					keyword |= Regex.IsMatch(block.Groups[1].Value, @"-\s+_NORMALMAP\b");
				}
				Match number = useNormalMap.Match(text);
				bool on = number.Success && float.Parse(number.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > 0.5f;
				// A material from before the change carries the keyword: the switch must agree, or its bark goes flat. A fresh
				// one carries none, and its switch is the generator's (BiomeArtGenerator.Vegetation sets both from one bool).
				if (keyword != on && keyword)
				{
					wrong.Add($"{path}: drew the normal-map variant, _UseNormalMap is off");
				}
				if (keyword != on && !keyword && Regex.IsMatch(text, @"\n    - _BumpMap:\n        m_Texture: \{fileID: 0\}"))
				{
					wrong.Add($"{path}: _UseNormalMap is on with no normal map: it would sample the flat default");
				}
			}
			Assume.That(seen, Is.GreaterThan(0), "no generated vegetation materials on this machine");
			Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
		}

		private static string ShaderGuid(string shaderPath)
		{
			Match guid = Regex.Match(File.ReadAllText(shaderPath + ".meta"), @"guid: ([0-9a-f]{32})");
			Assert.That(guid.Success, Is.True, shaderPath + ".meta");
			return guid.Groups[1].Value;
		}
	}
}
