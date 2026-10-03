#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What <see cref="TerrainScatter.Scatter"/> placed, what it refused, and why.</summary>
	/// <remarks>
	/// Every refusal is counted by its reason, so a rule that places nothing says whether its
	/// texture never dominated, its biome was never there, its height band was never met or a
	/// budget stopped it — the four answers that each need a different fix.
	/// </remarks>
	public sealed class TerrainScatterReport
	{
		/// <summary>One rule's results over the whole scene.</summary>
		public sealed class RuleOutcome
		{
			public string Biome;
			public string Slot;
			public string RuleName;
			public string StableGuid;
			public PrefabSpawnChannel Channel;
			/// <summary>The alphamap channel the rule is gated on.</summary>
			public int LayerIndex;
			/// <summary>Prototypes the rule's valid prefabs became.</summary>
			public int Prototypes;
			/// <summary>True for a detail rule placed as a continuous carpet rather than point-sampled.</summary>
			public bool Carpet;

			/// <summary>True when the rule never ran; <see cref="SkipReason"/> says why.</summary>
			public bool Skipped;
			public string SkipReason;

			/// <summary>Instances placed (trees) or detail cells covered (details, carpets included).</summary>
			public long Placed;
			/// <summary>Carpets: the summed coverage (0–1 per cell) of the cells they cover; over <see cref="Placed"/> it is the mean.</summary>
			public double CoverageSum;
			/// <summary>Carpets: cells where carpets together would have passed a full cell, so this one gave up a share.</summary>
			public long SharedCells;
			/// <summary>Carpets: mean coverage of the cells covered, 0–1.</summary>
			public double MeanCoverage => Placed > 0 ? CoverageSum / Placed : 0d;
			/// <summary>Candidates whose texture weight was below <c>minTextureWeight</c>.</summary>
			public long RejectedTextureWeight;
			/// <summary>Candidates the density draw turned down: the expected thinning, not a fault.</summary>
			public long RejectedChance;
			/// <summary>Candidates where the rule's biome does not reach.</summary>
			public long RejectedBiome;
			public long RejectedHeight;
			public long RejectedSlope;
			/// <summary>Candidates in a terrain hole.</summary>
			public long RejectedHole;
			/// <summary>Candidates closer than <c>minSpacing</c> to one already placed, across tiles too.</summary>
			public long RejectedSpacing;
			/// <summary>Tiles on which a budget stopped the rule before it had visited every cell.</summary>
			public int BudgetStops;

			public override string ToString()
			{
				string head = $"{Biome} / {Slot} / '{RuleName}' ({Channel}, layer {LayerIndex})";
				if (Skipped)
				{
					return $"{head}: skipped — {SkipReason}";
				}
				if (Carpet)
				{
					return $"{head}: carpet over {Placed:N0} cells, mean coverage {MeanCoverage:P0}, shared {SharedCells:N0}; rejected weight {RejectedTextureWeight:N0}, " +
						$"biome {RejectedBiome:N0}, height {RejectedHeight:N0}, slope {RejectedSlope:N0}, hole {RejectedHole:N0}";
				}
				return $"{head}: placed {Placed:N0}; rejected weight {RejectedTextureWeight:N0}, chance {RejectedChance:N0}, " +
					$"biome {RejectedBiome:N0}, height {RejectedHeight:N0}, slope {RejectedSlope:N0}, hole {RejectedHole:N0}, " +
					$"spacing {RejectedSpacing:N0}; budget stops {BudgetStops}";
			}
		}

		/// <summary>Every enabled rule reached through the palette, in the order they were run.</summary>
		public readonly List<RuleOutcome> Rules = new List<RuleOutcome>();

		/// <summary>Prefabs refused as prototypes, one line each: rule, prefab and the reason.</summary>
		public readonly List<string> InvalidPrefabs = new List<string>();

		/// <summary>Each time a budget stopped a rule on a tile: which budget, which tile, which rule.</summary>
		public readonly List<string> BudgetCaps = new List<string>();

		/// <summary>Anything that ran but deserves a look: fallbacks, coerced settings, duplicate GUIDs.</summary>
		public readonly List<string> Warnings = new List<string>();

		public int Tiles;
		public int DetailPrototypes;
		public int TreePrototypes;
		/// <summary>Tree prototypes whose prefab carries a collider, and so collide through the terrain collider.</summary>
		public int TreePrototypesWithColliders;
		public int DetailResolution;
		public DetailScatterMode DetailScatterMode;
		public long TreesPlaced;
		/// <summary>Detail cells covered by point-sampled (Scattered) rules: what the detail budgets count.</summary>
		public long DetailCellsCovered;
		/// <summary>Detail cells any carpet covers, counted once however many carpets share them.</summary>
		public long CarpetCellsCovered;
		/// <summary>Summed coverage (each cell's carpets together, at most 1) over <see cref="CarpetCellsCovered"/>.</summary>
		public double CarpetCoverageSum;
		public TimeSpan Elapsed;

		/// <summary>A multi-line summary for the generator's log.</summary>
		public override string ToString()
		{
			var text = new StringBuilder();
			text.AppendLine($"[Terrain scatter] {Tiles} tile(s), {Elapsed.TotalSeconds:0.00} s: {TreesPlaced:N0} trees from {TreePrototypes} prototype(s) " +
				$"({TreePrototypesWithColliders} colliding), {DetailCellsCovered:N0} detail cells from {DetailPrototypes} prototype(s) " +
				$"at {DetailResolution}² ({DetailScatterMode}); carpets cover {CarpetCellsCovered:N0} cells at a mean {(CarpetCellsCovered > 0 ? CarpetCoverageSum / CarpetCellsCovered : 0d):P0}.");
			foreach (RuleOutcome rule in Rules)
			{
				text.Append("  ").AppendLine(rule.ToString());
			}
			Append(text, "Invalid prefabs", InvalidPrefabs);
			Append(text, "Budget caps", BudgetCaps);
			Append(text, "Warnings", Warnings);
			return text.ToString();
		}

		private static void Append(StringBuilder text, string heading, List<string> lines)
		{
			if (lines.Count == 0)
			{
				return;
			}
			text.AppendLine($"  {heading}:");
			foreach (string line in lines)
			{
				text.Append("    ").AppendLine(line);
			}
		}
	}
}
#endif
