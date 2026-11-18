#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The hot ground's sites as points of interest (2026-10-27): every geyser, hot spring, mud pot, fumarole and sulphur pool the
	/// cut dug (<see cref="GeothermalGroundCarve"/>) becomes a named, found-in-the-ground site on the world map, and the scene's
	/// Great Spout (the largest geyser dug, I2q decision 47 (d)) a landmark drawn further out than the rest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why here.</b> The sites are found on the biome map the biome stage writes (GeothermalVents.SceneSites, the client's own
	/// search), after the planner has run; the planner's only hot spring is one where a river rises in a hot biome, so a basin with no
	/// river had none, and its geysers, mud pots and the Great Spout no point of interest at all. The biome stage hands its plan here
	/// right after the carve, on the cut and on every repaint, and the place stage writes the records with the rest.
	/// </para>
	/// <para>
	/// <b>Which.</b> Only the dug sites: one under water the client never draws, and one in another site's footprint, are not places
	/// of their own. Sites of one kind within <see cref="MergeMetres"/> of a larger one are that one (a cluster of vents is one
	/// fumarole), and a kind keeps at most <see cref="MaxPerKind"/> (an ice moon's tiger stripes jet by the hundred), largest first.
	/// A jet is a geyser; a sea hall's black smoker is the hall's (never on the surface).
	/// </para>
	/// <para>
	/// <b>Again on every run.</b> The records are told from the planner's by their kind (<see cref="IsHotGroundRecord"/>: the four hot
	/// ground kinds, and a hot spring holding its pool's lake), taken out and made again from the plan each time, so a repaint never doubles them
	/// and a site the ground no longer has goes. Regenerate POIs, which plans afresh without the biome stage, carries the old ones over
	/// (<see cref="CarryOver"/>). They take no pad and no keep-out: the carve has shaped their ground.
	/// </para>
	/// </remarks>
	public static class GeothermalPointsOfInterest
	{
		/// <summary>How close two sites of one kind are one place, metres.</summary>
		public const float MergeMetres = 30f;
		/// <summary>The most sites of one kind a scene marks.</summary>
		public const int MaxPerKind = 12;
		/// <summary>The map tier the Great Spout is drawn at (a waterfall's, a peak's); the other sites take their kind's (3).</summary>
		public const int GreatSpoutTier = 2;
		/// <summary>The word the Great Spout's name is qualified with ("Vorish Spout" → "Great Vorish Spout").</summary>
		public const string GreatQualifier = "Great";

		/// <summary>The point-of-interest kind a geothermal site is marked as; false for one that is no surface place (a smoker).</summary>
		public static bool TryKindOf(GeothermalKind kind, out POIType poi)
		{
			switch (kind)
			{
				case GeothermalKind.Fumarole: poi = POIType.Fumarole; return true;
				case GeothermalKind.HotSpring: poi = POIType.HotSpring; return true;
				case GeothermalKind.Geyser: poi = POIType.Geyser; return true;
				case GeothermalKind.Jet: poi = POIType.Geyser; return true;
				case GeothermalKind.MudPot: poi = POIType.MudPot; return true;
				case GeothermalKind.SulphurPool: poi = POIType.SulphurPool; return true;
				default: poi = POIType.Any; return false;
			}
		}

		/// <summary>True for the kinds only the hot ground marks.</summary>
		public static bool IsHotGroundKind(POIType kind)
			=> kind == POIType.Geyser || kind == POIType.MudPot || kind == POIType.Fumarole || kind == POIType.SulphurPool;

		/// <summary>
		/// True for a record this made: a hot ground kind, or a hot spring holding a hot spring pool's lake id
		/// (GeothermalGroundCarve.IsPool: every spring the cut digs has its pool; the planner's hot springs rise on a river and hold none).
		/// </summary>
		public static bool IsHotGroundRecord(PointOfInterestRecord record)
			=> record != null && (IsHotGroundKind(record.Kind) || record.Kind == POIType.HotSpring && GeothermalGroundCarve.IsPool(record.LakeId));

		/// <summary>One site chosen to be marked: its index among the plan's sites, its kind, and whether it is the Great Spout.</summary>
		public struct Pick
		{
			public int Site;
			public POIType Kind;
			public bool Great;
		}

		/// <summary>
		/// Which of a scene's sites are marked (pure): of the <paramref name="dug"/> ones (indices into <paramref name="sites"/>), the
		/// <paramref name="great"/> site first (−1 for none), then the rest largest first (ties by index); one is left out when a site of
		/// its kind already taken stands within <see cref="MergeMetres"/>, or its kind has <see cref="MaxPerKind"/>. In site order.
		/// </summary>
		public static List<Pick> Choose(IReadOnlyList<GeothermalSite> sites, IReadOnlyList<int> dug, int great)
		{
			var picks = new List<Pick>();
			if (sites == null || dug == null)
			{
				return picks;
			}
			var order = new List<int>();
			var seen = new HashSet<int>();
			foreach (int i in dug)
			{
				if (i >= 0 && i < sites.Count && seen.Add(i) && TryKindOf(sites[i].Kind, out _))
				{
					order.Add(i);
				}
			}
			order.Sort((a, b) =>
			{
				if (a == great || b == great)
				{
					return a == great ? (b == great ? 0 : -1) : 1;
				}
				int c = sites[b].Size.CompareTo(sites[a].Size);
				return c != 0 ? c : a.CompareTo(b);
			});
			var counts = new Dictionary<POIType, int>();
			float merge2 = MergeMetres * MergeMetres;
			foreach (int i in order)
			{
				TryKindOf(sites[i].Kind, out POIType kind);
				counts.TryGetValue(kind, out int count);
				if (count >= MaxPerKind)
				{
					continue;
				}
				bool near = false;
				foreach (Pick taken in picks)
				{
					if (taken.Kind == kind && (sites[taken.Site].Position - sites[i].Position).sqrMagnitude < merge2)
					{
						near = true;
						break;
					}
				}
				if (near)
				{
					continue;
				}
				picks.Add(new Pick { Site = i, Kind = kind, Great = i == great });
				counts[kind] = count + 1;
			}
			picks.Sort((a, b) => a.Site.CompareTo(b.Site));
			return picks;
		}

		/// <summary>The size class a site is marked with: its size's third (0 small, 1 medium, 2 large), the Great Spout large.</summary>
		public static byte SizeClassOf(in GeothermalSite site, bool great) => (byte)(great ? 2 : site.Size < 0.34f ? 0 : site.Size < 0.67f ? 1 : 2);

		/// <summary>
		/// Replaces the hot ground's records in <paramref name="plan"/> with those of <paramref name="hotGround"/> (pure in its
		/// arguments): the old ones out (<see cref="IsHotGroundRecord"/>), one record a chosen site (<see cref="Choose"/>), its id the
		/// planner's stable id of its kind and place, its biome <paramref name="biomeIdAt"/>, its y <paramref name="groundAt"/>, named by
		/// <paramref name="namer"/> clear of every name in the plan (else its kind's name and a number), described by
		/// <paramref name="describe"/>. Returns how many were added.
		/// </summary>
		public static int Replace(PointOfInterestPlan plan, GeothermalGroundCarve.Plan hotGround, Func<float, float, int> biomeIdAt,
			Func<float, float, float> groundAt, PointOfInterestNamer namer, Func<PointOfInterestRecord, string> describe)
		{
			if (plan == null)
			{
				return 0;
			}
			var removed = new HashSet<int>();
			plan.Records.RemoveAll(r =>
			{
				if (!IsHotGroundRecord(r))
				{
					return false;
				}
				removed.Add(r.Id);
				return true;
			});
			plan.Pads.RemoveAll(p => removed.Contains(p.Id));
			plan.KeepOuts.RemoveAll(k => removed.Contains(k.Id));
			if (hotGround == null || !hotGround.Any)
			{
				return 0;
			}
			var dug = new List<int>(hotGround.Dug.Count);
			foreach (GeothermalGroundCarve.Dug d in hotGround.Dug)
			{
				dug.Add(d.Index);
			}
			int great = hotGround.GreatSpout >= 0 ? hotGround.GreatSpout : hotGround.GreatPlume;
			List<Pick> picks = Choose(hotGround.Sites, dug, great);

			var ids = new HashSet<int>();
			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (record != null)
				{
					ids.Add(record.Id);
					if (!string.IsNullOrEmpty(record.Name))
					{
						used.Add(record.Name);
					}
				}
			}
			var fallbacks = new Dictionary<POIType, int>();
			int added = 0;
			foreach (Pick pick in picks)
			{
				GeothermalSite site = hotGround.Sites[pick.Site];
				float x = site.Position.x, z = site.Position.y;
				int id = PointOfInterestPlanner.StableId(pick.Kind, x, z);
				while (!ids.Add(id))
				{
					id = id == int.MaxValue ? 1 : id + 1;
				}
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(pick.Kind);
				var record = new PointOfInterestRecord
				{
					Id = id,
					Kind = pick.Kind,
					Position = new Vector3(Round(x), Round(groundAt != null ? groundAt(x, z) : 0f), Round(z)),
					Yaw = 0f,
					Radius = Round(GeothermalGroundCarve.CarveRadius(site)),
					Outline = Array.Empty<float>(),
					SizeClass = SizeClassOf(site, pick.Great),
					BiomeID = biomeIdAt != null ? biomeIdAt(x, z) : 0,
					VariantIndex = 0,
					DetailTier = pick.Great ? Mathf.Min(GreatSpoutTier, info.DetailTier) : info.DetailTier,
					RequiresDiscovery = true,
					ParentId = -1,
					RiverId = -1,
					PlanetRiver = -1,
					// A spring's or sulphur pond's own pool, as the hydrology holds it.
					LakeId = GeothermalVents.IsPool(site.Kind) ? GeothermalGroundCarve.PoolLakeId(pick.Site) : -1,
					Template = string.Empty,
					Race = string.Empty,
					Name = string.Empty,
					Description = string.Empty,
					SiteSeed = PointOfInterestPlanner.SiteSeed(plan.Seed, id),
					UnlockIndex = -1,
				};
				record.Name = NameOf(record, pick.Great, namer, used, fallbacks);
				used.Add(record.Name);
				string about = describe != null ? describe(record) : null;
				record.Description = pick.Great ? $"The Great Spout, the largest geyser here. {about}".TrimEnd() : about ?? string.Empty;
				plan.Records.Add(record);
				added++;
			}
			return added;
		}

		/// <summary>
		/// A record's name: the namer's first draw no record holds (as PointOfInterestNaming.NameAll draws), the Great Spout's
		/// qualified "Great"; with none, the kind's name and a number ("Mud Pot 2"; "Great Geyser" for the spout).
		/// </summary>
		private static string NameOf(PointOfInterestRecord record, bool great, PointOfInterestNamer namer, HashSet<string> used, Dictionary<POIType, int> fallbacks)
		{
			for (int attempt = 0; attempt < PointOfInterestNaming.MaxAttempts && namer != null; attempt++)
			{
				string name;
				try
				{
					name = namer(record, attempt);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[Points of interest] Naming a {record.Kind} failed: {ex.Message}");
					break;
				}
				if (string.IsNullOrWhiteSpace(name))
				{
					break;
				}
				name = name.Trim();
				if (great && name.IndexOf(GreatQualifier, StringComparison.OrdinalIgnoreCase) < 0)
				{
					name = PointOfInterestNaming.Qualify(name, GreatQualifier);
				}
				if (!used.Contains(name))
				{
					return name;
				}
			}
			string display = PointOfInterestKinds.Info(record.Kind).DisplayName;
			if (great)
			{
				string spout = $"{GreatQualifier} {display}";
				if (!used.Contains(spout))
				{
					return spout;
				}
			}
			fallbacks.TryGetValue(record.Kind, out int n);
			string chosen;
			do
			{
				n++;
				chosen = n == 1 ? display : $"{display} {n}";
			}
			while (used.Contains(chosen));
			fallbacks[record.Kind] = n;
			return chosen;
		}

		/// <summary>
		/// Carries the hot ground's records of a scene's old plan into a new one that the biome stage will not visit (Regenerate POIs):
		/// the new plan's own of them out, the old ones in, each whose id and name no new record holds (pure). Returns how many.
		/// </summary>
		public static int CarryOver(PointOfInterestPlan plan, IEnumerable<PointOfInterestRecord> old)
		{
			if (plan == null || old == null)
			{
				return 0;
			}
			plan.Records.RemoveAll(IsHotGroundRecord);
			var ids = new HashSet<int>();
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (record != null)
				{
					ids.Add(record.Id);
					if (!string.IsNullOrEmpty(record.Name))
					{
						names.Add(record.Name);
					}
				}
			}
			int carried = 0;
			foreach (PointOfInterestRecord record in old)
			{
				if (IsHotGroundRecord(record) && ids.Add(record.Id) && (string.IsNullOrEmpty(record.Name) || names.Add(record.Name)))
				{
					plan.Records.Add(record);
					carried++;
				}
			}
			return carried;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// The biome stage's call, right after <see cref="GeothermalGroundCarve.Lay"/>: the stage's plan given the hot ground's records
		/// (<see cref="Replace"/>), named by the scene's name generator and described in their biome, with a note. Nothing without a
		/// stage or a plan.
		/// </summary>
		public static int AddTo(PointOfInterestStage poi, GeothermalGroundCarve.Plan hotGround, SceneBiomeField field, SceneGenerationRequest request,
			Terrain[,] terrains, TerrainTilePlan tiles, List<string> notes)
		{
			if (poi?.Plan == null || request == null)
			{
				return 0;
			}
			string bodySeed = request.Body != null ? request.Body.ResolvedTerrainSeed.ToString() : "1";
			PointOfInterestNamer namer = PointOfInterestNaming.Generator(request.SceneName, bodySeed, poi.Plan.Seed);
			int added = Replace(poi.Plan, hotGround,
				(east, north) =>
				{
					BiomeTemplate biome = field != null ? field.DominantAt(east, north) : null;
					return biome != null ? BiomeRegistry.IDOf(biome) : 0;
				},
				(east, north) => SceneGenerator.GroundAltitude(terrains, tiles, east, north),
				namer,
				record =>
				{
					BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome);
					return PointOfInterestNaming.Describe(record, biome != null ? biome.ResolvedDisplayName : null);
				});
			if (added > 0)
			{
				var counts = new Dictionary<POIType, int>();
				string great = null;
				foreach (PointOfInterestRecord record in poi.Plan.Records)
				{
					if (IsHotGroundRecord(record))
					{
						counts.TryGetValue(record.Kind, out int n);
						counts[record.Kind] = n + 1;
						if (record.DetailTier <= GreatSpoutTier && record.Kind == POIType.Geyser)
						{
							great = record.Name;
						}
					}
				}
				var parts = new List<string>();
				foreach (KeyValuePair<POIType, int> kind in counts)
				{
					parts.Add($"{kind.Value} {PointOfInterestKinds.Info(kind.Key).DisplayName.ToLowerInvariant()}");
				}
				parts.Sort(StringComparer.Ordinal);
				notes?.Add($"Hot ground: {added} point(s) of interest marked ({string.Join(", ", parts)})" +
					(great != null ? $"; the Great Spout is '{great}'." : "."));
			}
			return added;
		}

		/// <summary>Regenerate POIs' call: the scene's old hot ground records carried into the new plan (<see cref="CarryOver"/>).</summary>
		public static int KeepFrom(PointOfInterestStage poi, Scene scene, List<string> notes)
		{
			ScenePointsOfInterest old = PointOfInterestProbe.Asset(scene);
			if (poi?.Plan == null || old == null || old.Points == null)
			{
				return 0;
			}
			int kept = CarryOver(poi.Plan, old.Points);
			if (kept > 0)
			{
				notes?.Add($"Hot ground: {kept} point(s) of interest kept from the last plan (a repaint marks them again from the ground).");
			}
			return kept;
		}

		/// <summary>Centimetres, as the planner stores its records.</summary>
		private static float Round(float v) => Mathf.Round(v * 100f) / 100f;
	}
}
#endif
