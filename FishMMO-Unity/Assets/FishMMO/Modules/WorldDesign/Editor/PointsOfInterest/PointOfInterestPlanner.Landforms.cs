#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestPlanner
	{
		internal sealed partial class State
		{
			/// <summary>Per detector, the distance from every cell to the nearest place it finds, metres; worked out once (2026-10-27).</summary>
			private Dictionary<PointOfInterestDetector, float[]> detectedDistance;

			/// <summary>
			/// The distance field a rule's <see cref="PointOfInterestKindRule.NearDetector"/> gate reads: from every cell to the nearest place
			/// the detector finds, whether or not its kind was marked there (a pass the Pass row's cap left unmarked is still a pass).
			/// Null for a detector that finds nothing in the scene.
			/// </summary>
			/// <remarks>
			/// The detector runs as it does for its own kind, with that kind's row (its least area, its biomes), except where the biome is
			/// the asker's to judge: a sinkhole or a biome cluster is looked for on the asking kind's ground.
			/// </remarks>
			internal float[] DetectedDistance(PointOfInterestKindRule rule)
			{
				detectedDistance ??= new Dictionary<PointOfInterestDetector, float[]>();
				PointOfInterestDetector detector = rule.NearDetector;
				if (detectedDistance.TryGetValue(detector, out float[] field))
				{
					return field;
				}
				PointOfInterestKindRule finder = rule;
				if (detector != PointOfInterestDetector.Sinkholes && detector != PointOfInterestDetector.BiomeCluster
					&& detector != PointOfInterestDetector.BiomeClusterHighest && detector != PointOfInterestDetector.BiomeClusterLowest)
				{
					foreach (PointOfInterestKindRule other in Input.Rules.Ordered(PointOfInterestPlacement.Detected))
					{
						if (other.Detector == detector)
						{
							finder = other;
							break;
						}
					}
				}
				List<Site> sites = Found(detector, finder);
				field = null;
				if (sites.Count > 0)
				{
					var source = new bool[G.Height.Length];
					foreach (Site site in sites)
					{
						source[G.CellAt(site.X, site.Z)] = true;
					}
					field = new float[source.Length];
					G.Chamfer(field, c => source[c]);
				}
				detectedDistance[detector] = field;
				return field;
			}

			/// <summary>Everything a detector finds, unthinned and uncapped (what <see cref="Detect"/> hands <see cref="Accept"/>).</summary>
			internal List<Site> Found(PointOfInterestDetector detector, PointOfInterestKindRule rule)
			{
				var sites = new List<Site>();
				switch (detector)
				{
					case PointOfInterestDetector.Falls: Falls(sites); break;
					case PointOfInterestDetector.Rapids: Rapids(sites); break;
					case PointOfInterestDetector.Rivers: Rivers(sites); break;
					case PointOfInterestDetector.Lakes: Lakes(rule, sites); break;
					case PointOfInterestDetector.Springs: Springs(rule, sites, false); break;
					case PointOfInterestDetector.HotSprings: Springs(rule, sites, true); break;
					case PointOfInterestDetector.RiverMouths: Mouths(sites); break;
					case PointOfInterestDetector.Deltas: Deltas(sites); break;
					case PointOfInterestDetector.Peaks: Peaks(sites); break;
					case PointOfInterestDetector.Passes: Passes(sites); break;
					case PointOfInterestDetector.Gorges: Gorges(sites); break;
					case PointOfInterestDetector.Mesas: Plateaus(rule, sites, true); break;
					case PointOfInterestDetector.Buttes: Plateaus(rule, sites, false); break;
					case PointOfInterestDetector.Islands: Islands(rule, sites); break;
					case PointOfInterestDetector.Bays: Coast(sites, true); break;
					case PointOfInterestDetector.Headlands: Coast(sites, false); break;
					case PointOfInterestDetector.Sinkholes: Sinkholes(rule, sites); break;
					case PointOfInterestDetector.BiomeCluster:
					case PointOfInterestDetector.BiomeClusterHighest:
					case PointOfInterestDetector.BiomeClusterLowest:
						Clusters(rule, sites);
						break;
				}
				return sites;
			}
		}
	}
}
#endif
