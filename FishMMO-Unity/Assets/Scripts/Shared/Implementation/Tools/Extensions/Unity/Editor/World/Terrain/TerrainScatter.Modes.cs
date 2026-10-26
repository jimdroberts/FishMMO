#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The spawn rules' SCATTER MODES in the cut's scatter (2026-10-26, phase SCATTER-MODES, decision 34; PrefabSpawnRule.scatterMode,
	/// BiomeScatterMode). A rule with every mode off gets no <see cref="ScatterModeRun"/> and takes none of the paths below: the same
	/// cells, the same draws, the same instances as before the modes existed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Placement</b> (anchors, the weighting patterns) multiplies a candidate's chance after its other bands, in the instance and the carpet
	/// path alike; <b>Even</b> places one instance per jittered lattice point instead of drawing a chance (instance path only). <b>Facing</b> and
	/// <b>power-law sizes</b> rewrite a tree instance's turn and scale from its own hashes (the turn draw is the spread's); a detail's ride the
	/// terrain's record (<see cref="TerrainScatterModes"/>) to the GPU detail scatter, as does every <b>lean</b> (trees: the client's tree
	/// renderer). Visual only: a tree's collider stands as the instance does, upright.
	/// </para>
	/// <para>
	/// <b>Anchored rules run last</b> on each tile (a stable partition, so every other rule keeps its order, budgets and draws): a lee, a
	/// nurse or a shelter stands by what the tile's other rules placed — their trees (footprint radius × width) and, for named shrubs, the
	/// detail cells their prototypes cover. A tile sees only its own instances (a few metres at a seam), and a lee also stands behind the
	/// scene's structures and ways (the trees' keep-out). Anchored carpets see every scattered rule's.
	/// </para>
	/// </remarks>
	public static partial class TerrainScatter
	{
		/// <summary>One rule's modes as a run holds them: the mode, what it reads, and its per-tile obstacles.</summary>
		private sealed class ScatterModeRun
		{
			public BiomeScatterMode Mode;
			public BiomeScatterCompass Compass;
			public BiomeLandformZones Zones;
			/// <summary>The rule's biome's template id (landform sites and seep lines are keyed by it).</summary>
			public int BiomeID;
			public uint PatternSeed, EvenSeed;
			public float Reach, SeepPhase;
			public IReadOnlyList<BiomeScatterSite> Sites;
			/// <summary>The scene's structures and ways (the trees' keep-out): a lee stands behind them too.</summary>
			public Func<float, float, bool> KeepOut;
			/// <summary>What the anchor stands by on the current tile (set before the rule runs on it); null: nothing.</summary>
			public BiomeScatterObstacles Obstacles;
			public bool Even, Weighs, Anchored, Faces, Sizes;
		}

		/// <summary>The run's view of a rule's modes; null when they are all off (the rule then takes no mode path at all).</summary>
		private static ScatterModeRun ModesFor(PrefabSpawnRule rule, SceneTerrainPalette.Entry entry, TerrainScatterOptions options, uint sceneSeed,
			uint ruleSeed, TerrainScatterReport report)
		{
			BiomeScatterMode mode = rule.scatterMode;
			if (mode.IsOff)
			{
				return null;
			}
			bool tree = rule.spawnChannel == PrefabSpawnChannel.TreeInstance;
			int biome = entry.Biome != null ? entry.Biome.ID : 0;
			var run = new ScatterModeRun
			{
				Mode = mode,
				Compass = options != null ? options.Compass : default,
				Zones = options?.LandformZones,
				BiomeID = biome,
				// Every rain-cell rule of a scene blooms in the same cells; every other pattern is the rule's own.
				PatternSeed = mode.Pattern == BiomeScatterPattern.RainCells ? Mix(sceneSeed ^ 0x2A1CE115u) : Mix(ruleSeed ^ 0xF1E1D5EEu),
				EvenSeed = Mix(ruleSeed ^ 0xE7E1A77Eu),
				Reach = mode.AnchorMetres > 0f ? mode.AnchorMetres : DefaultReach(mode.Anchor),
				Sites = options?.Sites,
				KeepOut = options?.Excluded,
				Even = mode.Pattern == BiomeScatterPattern.Even && !rule.IsCarpet,
				Weighs = mode.Weighs,
				Anchored = mode.NeedsObstacles,
				Faces = tree && mode.Facing != BiomeScatterFacing.Random,
				Sizes = tree && mode.SizePower > 0f,
			};
			if (mode.Anchor == BiomeScatterAnchor.SeepLine)
			{
				run.SeepPhase = BiomeScatterModes.SeepPhase(biome, run.Reach);
			}
			string who = $"{entry.Biome?.ResolvedDisplayName} / {entry.Slot} / '{rule.ruleName}'";
			bool windless = run.Compass.WindTo.sqrMagnitude < 1e-8f;
			if (windless && (mode.Anchor == BiomeScatterAnchor.Lee || mode.Facing == BiomeScatterFacing.Wind || mode.Lean == BiomeScatterLean.Wind))
			{
				report.Warnings.Add($"{who} reads the wind (scatter mode {mode.Signature()}), but the scene has none (no air, or none given): a lee grows only its floor, and the wind turns and leans nothing.");
			}
			if (!tree && (mode.Facing == BiomeScatterFacing.AlongLine || mode.Facing == BiomeScatterFacing.Strike))
			{
				report.Warnings.Add($"{who} is a detail rule facing {mode.Facing}, which varies by place and has no GPU form: its details turn at random (make it a tree-channel rule to align it).");
			}
			if (mode.Pattern == BiomeScatterPattern.Even && rule.IsCarpet)
			{
				report.Warnings.Add($"{who} is a carpet: Even spacing places instances, and a carpet has none (ignored).");
			}
			return run;
		}

		private static float DefaultReach(BiomeScatterAnchor anchor)
		{
			switch (anchor)
			{
				case BiomeScatterAnchor.Lee: return BiomeScatterModes.DefaultLeeTail;
				case BiomeScatterAnchor.Nurse: return BiomeScatterModes.DefaultNurseReach;
				case BiomeScatterAnchor.Shelter: return BiomeScatterModes.DefaultShelterReach;
				case BiomeScatterAnchor.CliffFoot: return BiomeScatterModes.DefaultCliffReach;
				case BiomeScatterAnchor.Wall: return BiomeScatterModes.DefaultWallSlope;
				case BiomeScatterAnchor.SeepLine: return BiomeScatterModes.DefaultSeepSpacing;
				case BiomeScatterAnchor.NearSite: return BiomeScatterModes.DefaultSiteReach;
				default: return 0f;
			}
		}

		// ── Placement ───────────────────────────────────────────────

		/// <summary>
		/// What a rule's anchor and weighting pattern make of its chance at a candidate (both paths), 0 … (the pattern's mean is 1): pure reads
		/// of the tile and of the run's own data, so safe from the scatter's workers.
		/// </summary>
		private static float ModeWeight(ScatterModeRun m, TileFrame frame, float[] heights, float[] slopes, int heightRes, float u, float v,
			float worldX, float worldY, float worldZ)
		{
			BiomeScatterMode mode = m.Mode;
			float weight = 1f;
			switch (mode.Anchor)
			{
				case BiomeScatterAnchor.Lee:
				{
					float lee = m.Obstacles != null ? m.Obstacles.Lee(worldX, worldZ, m.Compass.WindTo, m.Reach, null) : 0f;
					weight = BiomeScatterModes.Anchored(mode.AnchorFloor, Mathf.Max(lee, StructureLee(m, worldX, worldZ)));
					break;
				}
				case BiomeScatterAnchor.Nurse:
				case BiomeScatterAnchor.Shelter:
					weight = BiomeScatterModes.Anchored(mode.AnchorFloor, m.Obstacles != null ? m.Obstacles.Near(worldX, worldZ, m.Reach, mode.AnchorNames) : 0f);
					break;
				case BiomeScatterAnchor.CliffFoot:
				{
					float own = Bilinear(slopes, heightRes, u, v);
					Vector2 down = Downhill(frame, heights, heightRes, u, v);
					float foot = 0f;
					if (down.sqrMagnitude > 0f)
					{
						float[] at = BiomeScatterModes.CliffSamples;
						foot = BiomeScatterModes.CliffFoot(own, Uphill(frame, slopes, heightRes, worldX, worldZ, down, at[0] * m.Reach),
							Uphill(frame, slopes, heightRes, worldX, worldZ, down, at[1] * m.Reach), Uphill(frame, slopes, heightRes, worldX, worldZ, down, at[2] * m.Reach),
							Uphill(frame, slopes, heightRes, worldX, worldZ, down, at[3] * m.Reach));
					}
					weight = BiomeScatterModes.Anchored(mode.AnchorFloor, foot);
					break;
				}
				case BiomeScatterAnchor.Wall:
					weight = BiomeScatterModes.Anchored(mode.AnchorFloor, BiomeScatterModes.Wall(Bilinear(slopes, heightRes, u, v), m.Reach));
					break;
				case BiomeScatterAnchor.SeepLine:
					weight = BiomeScatterModes.Anchored(mode.AnchorFloor, BiomeScatterModes.SeepLine(worldY, Bilinear(slopes, heightRes, u, v), m.Reach, m.SeepPhase));
					break;
				case BiomeScatterAnchor.NearSite:
					weight = BiomeScatterModes.Anchored(mode.AnchorFloor, BiomeScatterModes.NearSite(m.Sites, mode.AnchorNames, worldX, worldZ, m.Reach));
					break;
			}
			if (weight > 0f && mode.Pattern != BiomeScatterPattern.None && mode.Pattern != BiomeScatterPattern.Even)
			{
				weight *= BiomeScatterModes.Pattern(mode, m.PatternSeed, worldX, worldZ);
			}
			return weight;
		}

		/// <summary>A lee behind the scene's structures and ways: the first keep-out met walking upwind, in 1.5 m steps, within the tail.</summary>
		private static float StructureLee(ScatterModeRun m, float x, float z)
		{
			Vector2 wind = m.Compass.WindTo;
			if (m.KeepOut == null || wind.sqrMagnitude < 1e-8f)
			{
				return 0f;
			}
			int steps = Mathf.Min(24, Mathf.CeilToInt(m.Reach / 1.5f));
			for (int k = 1; k <= steps; k++)
			{
				float d = k * 1.5f;
				if (m.KeepOut(x - wind.x * d, z - wind.y * d))
				{
					return Mathf.Clamp01(1f - d / (m.Reach + 1.5f));
				}
			}
			return 0f;
		}

		/// <summary>The fall line's direction at (u, v) (world xz, unit), zero on flat ground.</summary>
		private static Vector2 Downhill(TileFrame frame, float[] heights, int heightRes, float u, float v)
		{
			float e = 1f / Mathf.Max(1, heightRes - 1);
			float u0 = Mathf.Clamp01(u - e), u1 = Mathf.Clamp01(u + e), v0 = Mathf.Clamp01(v - e), v1 = Mathf.Clamp01(v + e);
			float dx = (Bilinear(heights, heightRes, u1, v) - Bilinear(heights, heightRes, u0, v)) * frame.Size.y / Mathf.Max(1e-4f, (u1 - u0) * frame.Size.x);
			float dz = (Bilinear(heights, heightRes, u, v1) - Bilinear(heights, heightRes, u, v0)) * frame.Size.y / Mathf.Max(1e-4f, (v1 - v0) * frame.Size.z);
			var down = new Vector2(-dx, -dz);
			float m = down.magnitude;
			return m > 1e-4f ? down / m : Vector2.zero;
		}

		/// <summary>The slope (degrees) <paramref name="metres"/> uphill of (x, z), read on the tile (clamped to its edge).</summary>
		private static float Uphill(TileFrame frame, float[] slopes, int heightRes, float x, float z, Vector2 down, float metres)
		{
			float u = Mathf.Clamp01((x - down.x * metres - frame.Origin.x) / frame.Size.x);
			float v = Mathf.Clamp01((z - down.y * metres - frame.Origin.z) / frame.Size.z);
			return Bilinear(slopes, heightRes, u, v);
		}

		/// <summary>
		/// EVEN spacing: whether cell (<paramref name="i"/>, <paramref name="j"/>) holds the rule's lattice point, and for a tree its place
		/// there (a detail cell is a cell). The pitch is the mode's, else the rule's density's, never under a cell.
		/// </summary>
		private static bool EvenCell(ScatterModeRun m, PrefabSpawnRule rule, TileFrame frame, int i, int j, int cellsX, int cellsZ, bool tree, ref float u, ref float v)
		{
			float cellX = frame.Size.x / cellsX, cellZ = frame.Size.z / cellsZ;
			float x0 = frame.Origin.x + i * cellX, z0 = frame.Origin.z + j * cellZ;
			float pitch = BiomeScatterModes.EvenPitch(m.Mode, rule.densityPer100m2, Mathf.Max(cellX, cellZ));
			if (!BiomeScatterModes.EvenPoint(m.EvenSeed, pitch, m.Mode.PatternShare, x0, z0, x0 + cellX, z0 + cellZ, out Vector2 point))
			{
				return false;
			}
			if (tree)
			{
				u = Mathf.Clamp01((point.x - frame.Origin.x) / frame.Size.x);
				v = Mathf.Clamp01((point.y - frame.Origin.z) / frame.Size.z);
			}
			return true;
		}

		// ── A tree's turn and size ──────────────────────────────────

		/// <summary>
		/// A tree instance's power-law size (from its own size draw, Mix(h + 4): both scales at the same share, the instance's proportions
		/// kept) and its facing (the heading plus the offset, strayed by the spread from its own turn draw, Mix(h + 6)); a facing that finds no
		/// heading here (flat ground, no line or landform near, no wind) keeps the turn it drew.
		/// </summary>
		private static void OrientTree(ScatterModeRun m, PrefabSpawnRule rule, ref TreeInstance instance, uint h, TileFrame frame, float[] heights,
			int heightRes, float u, float v, float worldX, float worldZ)
		{
			if (m.Sizes)
			{
				float t = Unit(Mix(h + 4u));
				float alpha = m.Mode.SizePower;
				if (rule.useNonUniformScaling)
				{
					instance.widthScale = Mathf.Max(0.05f, Mathf.Lerp(rule.widthScaleRange.x, rule.widthScaleRange.y,
						BiomeScatterModes.PowerLawShare(t, alpha, rule.widthScaleRange.x, rule.widthScaleRange.y)));
					instance.heightScale = Mathf.Max(0.05f, Mathf.Lerp(rule.heightScaleRange.x, rule.heightScaleRange.y,
						BiomeScatterModes.PowerLawShare(t, alpha, rule.heightScaleRange.x, rule.heightScaleRange.y)));
				}
				else
				{
					instance.widthScale = Mathf.Max(0.05f, Mathf.Lerp(rule.uniformScaleRange.x, rule.uniformScaleRange.y,
						BiomeScatterModes.PowerLawShare(t, alpha, rule.uniformScaleRange.x, rule.uniformScaleRange.y)));
					instance.heightScale = instance.widthScale;
				}
			}
			if (m.Faces && HeadingAt(m, frame, heights, heightRes, u, v, worldX, worldZ, out float heading))
			{
				instance.rotation = BiomeScatterModes.Facing(m.Mode, heading, Unit(Mix(h + 6u)));
			}
		}

		/// <summary>The heading a tree's facing finds at a place (Unity's turn, radians); false where it finds none.</summary>
		private static bool HeadingAt(ScatterModeRun m, TileFrame frame, float[] heights, int heightRes, float u, float v, float worldX, float worldZ, out float heading)
		{
			heading = 0f;
			switch (m.Mode.Facing)
			{
				case BiomeScatterFacing.Downslope:
				{
					Vector2 down = Downhill(frame, heights, heightRes, u, v);
					if (down.sqrMagnitude <= 0f)
					{
						return false;
					}
					heading = BiomeScatterModes.YawOf(down);
					return true;
				}
				case BiomeScatterFacing.AlongLine:
					return m.Zones != null && m.Zones.LineHeading(m.BiomeID, worldX, worldZ, BiomeScatterModes.LineReach, out heading);
				case BiomeScatterFacing.Strike:
					return m.Zones != null && m.Zones.StrikeHeading(m.BiomeID, worldX, worldZ, out heading);
				default:
					if (BiomeScatterModes.CompassDirection(m.Mode.Facing, m.Compass, out Vector2 direction))
					{
						heading = BiomeScatterModes.YawOf(direction);
						return true;
					}
					return false;
			}
		}

		// ── Anchored rules: run last, by what the tile placed ───────

		/// <summary>The rules in run order with the anchored (lee, nurse, shelter) scattered rules moved after the rest; the list itself when none is.</summary>
		private static List<ScatterRule> AnchoredLast(List<ScatterRule> rules)
		{
			bool any = false;
			foreach (ScatterRule rule in rules)
			{
				any |= IsAnchored(rule) && !rule.Carpet;
			}
			if (!any)
			{
				return rules;
			}
			var ordered = new List<ScatterRule>(rules.Count);
			foreach (ScatterRule rule in rules)
			{
				if (!(IsAnchored(rule) && !rule.Carpet))
				{
					ordered.Add(rule);
				}
			}
			foreach (ScatterRule rule in rules)
			{
				if (IsAnchored(rule) && !rule.Carpet)
				{
					ordered.Add(rule);
				}
			}
			return ordered;
		}

		private static bool IsAnchored(ScatterRule rule) => rule.Modes != null && rule.Modes.Anchored;

		/// <summary>
		/// What the anchored rules of a tile stand by, gathered once (before the first anchored rule runs, or before the anchored carpets):
		/// the tile's trees so far and, for the words an anchored rule names, the detail cells of the matching prototypes the scattered rules
		/// wrote; handed to every anchored rule of the kind (<paramref name="carpets"/>: the carpets, else the scattered ones).
		/// </summary>
		private static void PrepareAnchors(TerrainData data, List<ScatterRule> rules, TileFrame frame, Workspace work, bool carpets)
		{
			var obstacles = new BiomeScatterObstacles();
			TreePrototype[] treePrototypes = data.treePrototypes;
			foreach (TreeInstance t in work.Trees)
			{
				int p = t.prototypeIndex;
				string name = p >= 0 && p < treePrototypes.Length && treePrototypes[p].prefab != null ? treePrototypes[p].prefab.name : string.Empty;
				float radius = (p >= 0 && p < work.TreeFootprints.Length ? work.TreeFootprints[p] : 0.5f) * t.widthScale;
				obstacles.Add(frame.Origin.x + t.position.x * frame.Size.x, frame.Origin.z + t.position.z * frame.Size.z, radius, name);
			}

			// The words the anchored rules name: only prototypes they name are read back as cells (a named shrub may be a detail).
			var words = new List<string>();
			foreach (ScatterRule rule in rules)
			{
				if (IsAnchored(rule) && rule.Carpet == carpets && !string.IsNullOrWhiteSpace(rule.Modes.Mode.AnchorNames))
				{
					words.Add(rule.Modes.Mode.AnchorNames);
				}
			}
			if (words.Count > 0)
			{
				DetailPrototype[] details = data.detailPrototypes;
				int res = data.detailResolution;
				float cellX = frame.Size.x / res, cellZ = frame.Size.z / res;
				var read = new HashSet<int>();
				foreach (ScatterRule rule in rules)
				{
					if (!rule.Runnable || rule.Carpet || rule.Rule.spawnChannel != PrefabSpawnChannel.DetailLayer)
					{
						continue;
					}
					foreach (int p in rule.Prototypes)
					{
						string name = p >= 0 && p < details.Length && details[p].prototype != null ? details[p].prototype.name : null;
						if (name == null || !read.Add(p) || !words.Exists(w => BiomeScatterModes.Names(w, name)))
						{
							continue;
						}
						int[,] layer = data.GetDetailLayer(0, 0, res, res, p);
						for (int j = 0; j < res; j++)
						{
							for (int i = 0; i < res; i++)
							{
								// Unity's detail maps are [y, x].
								if (layer[j, i] > 0)
								{
									obstacles.Add(frame.Origin.x + (i + 0.5f) * cellX, frame.Origin.z + (j + 0.5f) * cellZ, 0.5f * Mathf.Max(cellX, cellZ), name);
								}
							}
						}
					}
				}
			}
			foreach (ScatterRule rule in rules)
			{
				if (IsAnchored(rule) && rule.Carpet == carpets)
				{
					rule.Modes.Obstacles = obstacles;
				}
			}
		}

		/// <summary>True when an anchored carpet reaches the tile's run (its obstacles are gathered before the carpets).</summary>
		private static bool AnyAnchoredCarpet(List<ScatterRule> rules)
		{
			foreach (ScatterRule rule in rules)
			{
				if (rule.Runnable && rule.Carpet && IsAnchored(rule))
				{
					return true;
				}
			}
			return false;
		}

		// ── The record the client reads ─────────────────────────────

		/// <summary>
		/// The terrain record of every rule's modes the client draws (<see cref="TerrainScatterModes"/>): a detail prototype's facing, lean and
		/// power-law size for the GPU detail scatter; a tree prototype's lean. Empty lists when no rule names one.
		/// </summary>
		private static void ModeRecord(List<ScatterRule> rules, DetailPrototype[] details, TreePrototype[] trees, TerrainScatterOptions options,
			TerrainScatterReport report, List<TerrainScatterModes.Detail> detailEntries, List<TerrainScatterModes.Tree> treeEntries)
		{
			BiomeScatterCompass compass = options != null ? options.Compass : default;
			foreach (ScatterRule rule in rules)
			{
				if (!rule.Runnable || rule.Modes == null)
				{
					continue;
				}
				bool tree = rule.Rule.spawnChannel == PrefabSpawnChannel.TreeInstance;
				BiomeScatterModeWord.Pack(rule.Modes.Mode, compass, !tree, out uint word, out uint angles, out _);
				if (tree)
				{
					// A tree's turn and size are in its instance; only the lean is the renderer's.
					word &= (0x7u << BiomeScatterModeWord.LeanShift) | (0x7Fu << BiomeScatterModeWord.LeanDegreesShift);
					angles &= 0xFFFF0000u;
				}
				if (word == 0u)
				{
					continue;
				}
				foreach (int p in rule.Prototypes)
				{
					if (tree)
					{
						if (p >= 0 && p < trees.Length)
						{
							treeEntries.Add(new TerrainScatterModes.Tree { Prototype = p, Word = word, Angles = angles });
						}
					}
					else if (p >= 0 && p < details.Length && details[p]?.prototype != null)
					{
						detailEntries.Add(new TerrainScatterModes.Detail { Prefab = details[p].prototype.name, NoiseSeed = details[p].noiseSeed, Word = word, Angles = angles });
					}
				}
			}
		}

		/// <summary>Writes a tile's <see cref="TerrainScatterModes"/> when the scene's rules name a mode; removes an earlier cut's when none does.</summary>
		private static void RecordModes(Terrain terrain, List<TerrainScatterModes.Detail> details, List<TerrainScatterModes.Tree> trees)
		{
			TerrainScatterModes record = terrain.GetComponent<TerrainScatterModes>();
			if (details.Count == 0 && trees.Count == 0)
			{
				if (record != null)
				{
					Object.DestroyImmediate(record);
				}
				return;
			}
			if (record == null)
			{
				record = terrain.gameObject.AddComponent<TerrainScatterModes>();
			}
			record.Details = new List<TerrainScatterModes.Detail>(details);
			record.Trees = new List<TerrainScatterModes.Tree>(trees);
			EditorUtility.SetDirty(record);
		}

		// ── What the scene generator hands the modes ────────────────

		/// <summary>
		/// The scene's compass for the modes, in scene axes as the footprint lays them: the prevailing wind (SceneGround.AeolianFor; none on an
		/// airless world), the local east at the scene's centre (a moon's prograde way: the corotating plasma's) and the way to the equator.
		/// </summary>
		public static BiomeScatterCompass CompassFor(SceneGenerationRequest request, SolarSystemProfile system)
		{
			var compass = new BiomeScatterCompass { East = Vector2.right, Equator = request != null && request.Latitude < 0.0 ? Vector2.up : Vector2.down };
			if (request == null)
			{
				return compass;
			}
			AeolianSettings wind = SceneGround.AeolianFor(request, system);
			if (wind != null)
			{
				compass.WindTo = new Vector2(wind.WindX, wind.WindZ);
			}
			AtlasFootprint footprint = request.Footprint;
			double radius = request.ResolvedRadiusKm;
			Vector3 centre = AtlasGeometry.SceneToUnit(footprint, 0.0, 0.0, radius).ToVector3();
			Vector3 alongX = (AtlasGeometry.SceneToUnit(footprint, 1.0, 0.0, radius).ToVector3() - centre).normalized;
			Vector3 alongZ = (AtlasGeometry.SceneToUnit(footprint, 0.0, 1.0, radius).ToVector3() - centre).normalized;
			Vector3 east = AtlasGeometry.ToUnit(request.Latitude, request.Longitude + 0.01).ToVector3() - AtlasGeometry.ToUnit(request.Latitude, request.Longitude).ToVector3();
			Vector3 north = AtlasGeometry.ToUnit(request.Latitude + 0.01, request.Longitude).ToVector3() - AtlasGeometry.ToUnit(request.Latitude, request.Longitude).ToVector3();
			var e = new Vector2(Vector3.Dot(east, alongX), Vector3.Dot(east, alongZ));
			var n = new Vector2(Vector3.Dot(north, alongX), Vector3.Dot(north, alongZ));
			if (e.sqrMagnitude > 1e-12f)
			{
				compass.East = e.normalized;
			}
			if (n.sqrMagnitude > 1e-12f)
			{
				compass.Equator = request.Latitude < 0.0 ? n.normalized : -n.normalized;
			}
			return compass;
		}

		/// <summary>The planned points of interest as the NearSite mode reads them; null with none.</summary>
		public static List<BiomeScatterSite> SitesOf(PointOfInterestPlan plan)
		{
			if (plan == null || plan.Records.Count == 0)
			{
				return null;
			}
			var sites = new List<BiomeScatterSite>(plan.Records.Count);
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (record != null)
				{
					sites.Add(new BiomeScatterSite { Centre = new Vector2(record.Position.x, record.Position.z), Radius = record.Radius, Kind = record.Kind.ToString() });
				}
			}
			return sites;
		}
	}
}
#endif
