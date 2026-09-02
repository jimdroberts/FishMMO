using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the one relationship that decides whether a throttled transform still looks like
	/// motion: the interpolation buffer must be able to bridge the send interval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>NetworkTransform._interpolation</c> is a count of RECEIVED TICKS the client buffers
	/// before it starts playing them out. An observer that <c>NetworkTransformDistanceLod</c> has
	/// banded to every Nth tick drains that buffer in <c>_interpolation</c> ticks and then has
	/// nothing left to move toward until the next sample lands. The render is play out, stall,
	/// snap — reported twice from live play as NPCs "teleporting" or "rubber banding" rather than
	/// walking.
	/// </para>
	/// <para>
	/// The asymmetry is what makes this easy to get wrong: <c>_interpolation</c> is per OBJECT and
	/// the interval is per OBSERVER. An object cannot size its buffer for whichever observer is
	/// furthest away, so the buffer has to cover the worst interval ANY observer could be handed —
	/// the largest authored band interval, multiplied by <c>intervalScale</c>.
	/// </para>
	/// <para>
	/// Read from the prefab YAML rather than from the component defaults on purpose. The serialized
	/// value is what ships; a code default that disagrees with it changes nothing for a player, and
	/// that exact gap is how the 2026-09-01 retune reached the code and not the prefabs.
	/// </para>
	/// </remarks>
	public class NetworkTransformLodBufferTests
	{
		/// <summary>Script GUID of <c>NetworkTransformDistanceLod</c>, as prefabs reference it.</summary>
		private const string LodScriptGuid = "93091dc8efed97c08aae1898cd50ebc3";

		/// <summary>One prefab's serialized level-of-detail settings.</summary>
		private struct LodPrefab
		{
			public string Path;
			public int Interpolation;
			public List<int> Intervals;
			public int IntervalScale;
		}

		/// <summary>
		/// Reads the serialized LOD and interpolation values out of every prefab that carries the
		/// component.
		/// </summary>
		/// <remarks>
		/// Text, not <c>AssetDatabase</c>. Both fields are private and serialized, so reading them
		/// through the component would need reflection against a loaded prefab; the YAML is the
		/// artefact that actually ships and is the thing worth asserting on.
		/// </remarks>
		private static List<LodPrefab> ReadPrefabs()
		{
			List<LodPrefab> results = new List<LodPrefab>();

			foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
				{
					continue;
				}

				string text;
				try
				{
					text = File.ReadAllText(path);
				}
				catch
				{
					continue;
				}

				if (!text.Contains(LodScriptGuid))
				{
					continue;
				}

				List<int> intervals = new List<int>();
				foreach (Match m in Regex.Matches(text, @"^\s+Interval:\s*(\d+)\s*$", RegexOptions.Multiline))
				{
					intervals.Add(int.Parse(m.Groups[1].Value));
				}

				Match interpolation = Regex.Match(text, @"^\s+_interpolation:\s*(\d+)\s*$", RegexOptions.Multiline);
				Match scale = Regex.Match(text, @"^\s+intervalScale:\s*(\d+)\s*$", RegexOptions.Multiline);

				results.Add(new LodPrefab
				{
					Path = path,
					Interpolation = interpolation.Success ? int.Parse(interpolation.Groups[1].Value) : -1,
					Intervals = intervals,
					IntervalScale = scale.Success ? int.Parse(scale.Groups[1].Value) : 1,
				});
			}

			return results;
		}

		/// <summary>A prefab with the component but no readable settings would make this vacuous.</summary>
		[Test]
		public void EveryLodPrefabDeclaresBothHalvesOfTheRelationship()
		{
			List<LodPrefab> prefabs = ReadPrefabs();

			Assert.IsNotEmpty(prefabs,
				"No prefab carries NetworkTransformDistanceLod; this suite would prove nothing.");

			foreach (LodPrefab prefab in prefabs)
			{
				Assert.Greater(prefab.Interpolation, 0,
					$"'{prefab.Path}' has the level-of-detail component but no serialized " +
					"_interpolation, so the buffer it needs cannot be checked.");

				Assert.IsNotEmpty(prefab.Intervals,
					$"'{prefab.Path}' has the level-of-detail component but no serialized bands.");
			}
		}

		/// <summary>
		/// The invariant: the buffer must cover the worst interval any observer can be given.
		/// </summary>
		[Test]
		public void NoBandOutrunsTheInterpolationBuffer()
		{
			foreach (LodPrefab prefab in ReadPrefabs())
			{
				int worst = 0;
				for (int i = 0; i < prefab.Intervals.Count; ++i)
				{
					int effective = prefab.Intervals[i] * prefab.IntervalScale;
					if (effective > worst)
					{
						worst = effective;
					}
				}

				Assert.LessOrEqual(worst, prefab.Interpolation,
					$"'{prefab.Path}' sends its furthest observers every {worst} ticks but buffers " +
					$"only {prefab.Interpolation}. That observer drains the buffer and stalls until " +
					"the next sample, which renders as the object teleporting rather than moving. " +
					"Either lower the band interval or raise _interpolation — they are one setting " +
					"in two places.");
			}
		}

		/// <summary>
		/// Bands must still get coarser with distance, or the table is not a level of detail.
		/// </summary>
		/// <remarks>
		/// Capping the far band is what this change did, and capping is fine; INVERTING it is not.
		/// A nearer band that sends less often than a farther one spends bandwidth exactly where it
		/// buys the least.
		/// </remarks>
		[Test]
		public void BandsNeverGetFinerWithDistance()
		{
			foreach (LodPrefab prefab in ReadPrefabs())
			{
				for (int i = 1; i < prefab.Intervals.Count; ++i)
				{
					Assert.GreaterOrEqual(prefab.Intervals[i], prefab.Intervals[i - 1],
						$"'{prefab.Path}' band {i} sends more often than band {i - 1}; " +
						"a farther observer must never be cheaper to serve than a nearer one.");
				}
			}
		}
	}
}
