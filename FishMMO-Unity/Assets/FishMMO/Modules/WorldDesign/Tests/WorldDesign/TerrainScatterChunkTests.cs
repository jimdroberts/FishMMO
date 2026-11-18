using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The scatter's hot loop works a rule's cells out a chunk at a time in parallel, then walks them in the visiting
	/// order for the spacing and the budgets (TerrainScatter.SampleRule). Visiting the cells one by one on one thread,
	/// as it did before, must place exactly the same trees and details, stop at the same place and count the same
	/// refusals. Pure: a synthetic tile, no terrain.
	/// </summary>
	[TestFixture]
	public class TerrainScatterChunkTests
	{
		private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
		private static readonly Type Scatter = typeof(TerrainScatter);

		private static object Make(Type t, params object[] args) => Activator.CreateInstance(t, Any, null, args, null);
		private static void Set(object o, string field, object value) => o.GetType().GetField(field, Any).SetValue(o, value);
		private static object Get(object o, string field) => o.GetType().GetField(field, Any).GetValue(o);
		private static object Call(string method, params object[] args) => Scatter.GetMethod(method, Any).Invoke(null, args);

		private static float Noise(int a, int b, int c)
		{
			unchecked
			{
				uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
				h ^= h >> 13;
				h *= 0x5bd1e995;
				h ^= h >> 15;
				return (h & 0xFFFFFF) / 16777216f;
			}
		}

		[Test]
		public void SampleRule_InChunks_PlacesWhatVisitingOneByOneDid()
		{
			FieldInfo oneByOne = Scatter.GetField("VisitOneByOne", Any);
			Assert.That(oneByOne, Is.Not.Null, "the scatter's one-by-one switch");
			string chunked = Run();
			try
			{
				oneByOne.SetValue(null, true);
				string serial = Run();
				Assert.That(chunked, Is.EqualTo(serial));
			}
			finally
			{
				oneByOne.SetValue(null, false);
			}
			Assert.That(chunked, Does.Contain("stopped by rule's maxPerChunk"), "a budget stops a rule part-way, inside a chunk");
		}

		/// <summary>Every rule over one synthetic tile; returns what each placed, where it stopped, what it refused, bit for bit.</summary>
		private static string Run()
		{
			Type ruleType = Scatter.GetNestedType("ScatterRule", Any);
			Type workType = Scatter.GetNestedType("Workspace", Any);
			Type frameType = Scatter.GetNestedType("TileFrame", Any);
			Type budgetType = Scatter.GetNestedType("Budget", Any);
			Type spacingType = Scatter.Assembly.GetType("FishMMO.Shared.WorldDesign.ScatterSpacing");

			int heightRes = 129, alphaRes = 65, holes = 64, detailRes = 512;
			var origin = new Vector3(-475f, -37f, -44f);
			object frame = Activator.CreateInstance(frameType);
			Set(frame, "Origin", origin);
			Set(frame, "Size", new Vector3(1024f, 400f, 1024f));
			Set(frame, "HeightResolution", heightRes);
			Set(frame, "AlphaResolution", alphaRes);
			Set(frame, "HolesResolution", holes);

			object work = Make(workType, 2, 4);
			var heights = new float[heightRes * heightRes];
			var slopes = new float[heightRes * heightRes];
			for (int z = 0; z < heightRes; z++)
			{
				for (int x = 0; x < heightRes; x++)
				{
					heights[z * heightRes + x] = Mathf.Clamp01(0.3f + 0.25f * Mathf.Sin(x * 0.11f) * Mathf.Cos(z * 0.07f) + 0.05f * Noise(x, z, 1));
					slopes[z * heightRes + x] = 70f * Noise(x / 3, z / 3, 2) * Noise(x, z, 9);
				}
			}
			var solid = new bool[holes * holes];
			for (int i = 0; i < solid.Length; i++)
			{
				solid[i] = Noise(i, 5, 0) > 0.03f;
			}
			Set(work, "Heights", heights);
			Set(work, "Slopes", slopes);
			Set(work, "Solid", solid);
			Set(work, "TreeFootprints", new[] { 2.5f, 3.1f, 1.7f, 4f });
			var weights = new float[4][];
			var attribution = (float[][])Get(work, "Attribution");
			for (int l = 0; l < 4; l++)
			{
				weights[l] = new float[alphaRes * alphaRes];
				attribution[l] = new float[alphaRes * alphaRes];
				for (int i = 0; i < weights[l].Length; i++)
				{
					weights[l][i] = Mathf.Clamp01(Mathf.Sin(i * 0.013f * (l + 1)) * 0.6f + 0.5f * Noise(i, l, 3));
					attribution[l][i] = Noise(i / 7, l, 4) < 0.15f ? 0f : Mathf.Clamp01(0.4f + 0.7f * Noise(i / 3, l, 5));
				}
			}
			var buffers = (IList)Get(work, "DetailBuffers");
			var counts = (List<int>)Get(work, "DetailPlaced");
			for (int v = 0; v < 3; v++)
			{
				buffers.Add(new int[detailRes, detailRes]);
				counts.Add(0);
			}
			Func<float, float, float, float> normalized = (x, y, z) => Mathf.Clamp01((y + 50f) / 500f + 0.02f * Mathf.Sin(x * 0.01f));
			Func<float, float, bool> excluded = (x, z) => (x - origin.x - 300f) * (x - origin.x - 300f) + (z - origin.z - 500f) * (z - origin.z - 500f) < 80f * 80f;
			Func<float, float, float> inland = (x, z) => Mathf.Abs(z - origin.z - 700f - 30f * Mathf.Sin(x * 0.02f)) < 12f ? origin.y + 140f : float.NegativeInfinity;

			object canopy = Make(spacingType, 9f, 4096, true);
			var rules = new List<(object rule, bool tree, int cells, int[] budget, int layer)>();
			for (int f = 0; f < 2; f++)
			{
				var r = new PrefabSpawnRule
				{
					spawnChannel = PrefabSpawnChannel.TreeInstance,
					densityPer100m2 = 1.5f + f,
					minSpacing = 4f + f,
					forestMetres = 260f + 90f * f,
					forestMix = f == 0 ? 0.5f : 0f,
					clusterMetres = f == 1 ? 18f : 0f,
					sinkRange = new Vector2(0.1f, 0.4f),
					sinkSlopeFactor = 0.3f,
					useSlopeConstraint = true,
					slopeRange = new MinMaxRange(0f, 35f),
					minTextureWeight = 0.3f,
				};
				object rule = Rule(ruleType, r, "forest" + f, 0, f == 0 ? new[] { 0, 1, 3 } : new[] { 0, 1 }, excluded, inland);
				Set(rule, "Spacing", canopy);
				Set(rule, "Canopy", true);
				Set(rule, "CrownScale", new[] { 0.9f, 1.1f, 1.3f });
				rules.Add((rule, true, 256, new[] { 1000000, 700 + 300 * f, 1000000 }, 0));
			}
			for (int d = 0; d < 3; d++)
			{
				var r = new PrefabSpawnRule
				{
					spawnChannel = PrefabSpawnChannel.DetailLayer,
					densityPer100m2 = 30f + 10f * d,
					minSpacing = d == 2 ? 1.5f : 0f,
					clusterMetres = d == 0 ? 6f : 0f,
					clusterBackground = 0.1f,
					useHeightConstraint = d == 1,
					heightRange = new MinMaxRange(0.1f, 0.5f),
					useSlopeConstraint = d != 0,
					slopeRange = new MinMaxRange(5f, 40f),
					minTextureWeight = 0.25f + 0.1f * d,
				};
				object rule = Rule(ruleType, r, "detail" + d, 2 + (d & 1), d == 0 ? new[] { 0 } : new[] { 0, 1, 2 }, d == 1 ? null : excluded, d == 2 ? inland : null);
				if (r.minSpacing > 0f)
				{
					Set(rule, "Spacing", Make(spacingType, r.minSpacing, 1024));
				}
				rules.Add((rule, false, detailRes, d == 2 ? new[] { 30000, 1000000, 1000000 } : new[] { 1000000, 1000000, 120000 }, 2 + d % 2));
			}

			var trees = new List<TreeInstance>();
			var text = new StringBuilder();
			string F(float v) => BitConverter.SingleToInt32Bits(v).ToString("X8");
			foreach ((object rule, bool tree, int cells, int[] limits, int layer) in rules)
			{
				object budget = Activator.CreateInstance(budgetType);
				Set(budget, "RuleLeft", limits[0]);
				Set(budget, "BiomeLeft", limits[1]);
				Set(budget, "TileLeft", limits[2]);
				object[] args = { rule, frame, work, weights[layer], attribution[(int)Get(rule, "Attribution")], cells, cells, tree, normalized, budget, trees, buffers, 40, 255 };
				int placed = (int)Call("SampleRule", args);
				object left = args[9];
				object outcome = Get(rule, "Outcome");
				text.Append($"{Get(outcome, "RuleName")} placed {placed} stopped by {Get(left, "StoppedBy") ?? "-"} left {Get(left, "RuleLeft")}/{Get(left, "BiomeLeft")}/{Get(left, "TileLeft")}");
				foreach (string refusal in new[] { "RejectedTextureWeight", "RejectedChance", "RejectedBiome", "RejectedHole", "RejectedHeight", "RejectedSlope", "RejectedSpacing" })
				{
					text.Append(' ').Append(Get(outcome, refusal));
				}
				text.AppendLine();
				if (!tree)
				{
					int variants = ((List<int>)Get(rule, "Prototypes")).Count;
					for (int v = 0; v < variants; v++)
					{
						var buffer = (int[,])buffers[v];
						long hash = 17;
						foreach (int value in buffer)
						{
							hash = hash * 31 + value;
						}
						text.AppendLine($"  variant {v}: {counts[v]} cells, hash {hash}");
						Array.Clear(buffer, 0, buffer.Length);
						counts[v] = 0;
					}
				}
			}
			var sinks = (List<float>)Get(work, "TreeSinks");
			for (int t = 0; t < trees.Count; t++)
			{
				TreeInstance tree = trees[t];
				text.AppendLine($"{F(tree.position.x)}{F(tree.position.y)}{F(tree.position.z)} {F(tree.widthScale)}{F(tree.heightScale)}{F(tree.rotation)} {F(tree.color.r)}{F(tree.lightmapColor.r)} {tree.prototypeIndex} {F(sinks[t])}");
			}
			return text.ToString();
		}

		private static object Rule(Type ruleType, PrefabSpawnRule r, string guid, int attribution, int[] prototypes,
			Func<float, float, bool> excluded, Func<float, float, float> inland)
		{
			object rule = Activator.CreateInstance(ruleType, true);
			r.ruleName = guid;
			Set(rule, "Rule", r);
			Set(rule, "BiomeKey", "biome");
			Set(rule, "Guid", guid);
			uint seed = (uint)Call("RuleSeed", 4321u, guid, "biome", 0, 0);
			Set(rule, "Seed", seed);
			Set(rule, "ClusterSeed", (uint)Call("Mix", seed ^ 0x6C0B5E11u));
			TerrainScatter.ClusterShape(r, out float metres, out float pitch);
			Set(rule, "ClusterMetres", metres);
			Set(rule, "ClusterPitch", pitch);
			if (r.IsForested)
			{
				Set(rule, "Forest", true);
				Set(rule, "Stands", new TerrainScatter.StandField(TerrainScatter.StandSeed(4321u), r.forestMetres, r.forestCover, r.forestEdge, r.forestOpen));
				Set(rule, "MixSeed", (uint)Call("Mix", seed ^ 0x3C6EF372u));
				Set(rule, "VariantSeed", (uint)Call("Mix", seed ^ 0xA54FF53Au));
				Set(rule, "PatchMetres", Mathf.Max(30f, r.forestMetres * 0.25f));
				Set(rule, "SlopeReference", r.useSlopeConstraint ? Mathf.Max(5f, r.slopeRange.max) : 45f);
			}
			if (inland != null)
			{
				Set(rule, "Inland", inland);
				Set(rule, "InlandFrom", 0.2f);
				Set(rule, "InlandTo", 1.4f);
			}
			Set(rule, "Excluded", excluded);
			Set(rule, "Attribution", attribution);
			((List<int>)Get(rule, "Prototypes")).AddRange(prototypes);
			var outcome = new TerrainScatterReport.RuleOutcome { RuleName = guid };
			Set(rule, "Outcome", outcome);
			return rule;
		}
	}
}
