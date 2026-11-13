using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The cached main-light shadows redraw when a frame's shadow work reaches an instance the last redraw did not hold
	/// (ShadowCasterCoverage, TerrainGpuRenderer.ShadowContentVersion): the coverage answer is the brute-force one.
	/// </summary>
	public class ShadowCasterCoverageTests
	{
		[Test]
		public void Coverage_IsWhatEveryInstanceSays()
		{
			var random = new System.Random(4021);
			var held = new List<Vector2Int>();
			var source = new List<Vector2Int>();
			int covered = 0, uncovered = 0;
			for (int trial = 0; trial < 400; trial++)
			{
				const int Instances = 600;
				var inside = new bool[Instances];
				source.Clear();
				int n = random.Next(0, 12);
				for (int i = 0; i < n; i++)
				{
					int start = random.Next(0, Instances - 1), end = Mathf.Min(Instances, start + random.Next(0, 80));
					source.Add(new Vector2Int(start, end));
					for (int k = start; k < end; k++)
					{
						inside[k] = true;
					}
				}
				ShadowCasterCoverage.Merge(source, held);
				for (int h = 1; h < held.Count; h++)
				{
					LogAssert.IsTrue(held[h].x > held[h - 1].y, $"trial {trial}: merged ranges stay apart: {held[h - 1]} then {held[h]}");
				}
				for (int q = 0; q < 50; q++)
				{
					int start = random.Next(0, Instances - 1), end = Mathf.Min(Instances, start + 1 + random.Next(0, 60));
					bool expected = true;
					for (int k = start; k < end; k++)
					{
						expected &= inside[k];
					}
					bool got = ShadowCasterCoverage.Covers(held, start, end);
					LogAssert.IsTrue(got == expected, $"trial {trial}: [{start}, {end}) covered {got}, every instance says {expected}");
					covered += expected ? 1 : 0;
					uncovered += expected ? 0 : 1;
				}
			}
			LogAssert.IsTrue(covered > 500 && uncovered > 500, $"exercised: {covered} covered, {uncovered} not");
		}

		[Test]
		public void AWorkRange_StraddlingTwoTouchingRanges_IsCovered_AndAnEmptyHoldCoversNothing()
		{
			var held = new List<Vector2Int>();
			ShadowCasterCoverage.Merge(new List<Vector2Int> { new Vector2Int(10, 20), new Vector2Int(0, 10) }, held);
			LogAssert.IsTrue(held.Count == 1 && held[0] == new Vector2Int(0, 20), $"touching ranges join: {string.Join(",", held)}");
			LogAssert.IsTrue(ShadowCasterCoverage.Covers(held, new List<Vector2Int> { new Vector2Int(5, 15) }), "a range across the join is held");
			LogAssert.IsTrue(!ShadowCasterCoverage.Covers(new List<Vector2Int>(), new List<Vector2Int> { new Vector2Int(0, 1) }), "nothing held: a redraw");
			LogAssert.IsTrue(ShadowCasterCoverage.Covers(new List<Vector2Int>(), new List<Vector2Int>()), "no shadow work: nothing to redraw for");
		}

		[Test]
		public void TheCache_RedrawsWhenItsShadowWorkGrows_AndHoldsWhatItCulled()
		{
			string cache = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.ShadowCache.cs");
			LogAssert.IsTrue(cache.Contains("HashCode.Combine(layoutSerial, flushSerial, CoverageSerial())"), "the content version counts the coverage");
			LogAssert.IsTrue(cache.Contains("ShadowCasterCoverage.Merge(shadowWorkRanges, heldShadowRanges);"), "a redraw's cull holds its shadow work");
			string gpu = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs");
			LogAssert.IsTrue(gpu.Contains("shadowWorkRanges.Add(new Vector2Int(offset, offset + count));") && gpu.Contains("shadowWorkRanges.Clear();"),
				"every camera's shadow work is listed afresh");
		}
	}
}
