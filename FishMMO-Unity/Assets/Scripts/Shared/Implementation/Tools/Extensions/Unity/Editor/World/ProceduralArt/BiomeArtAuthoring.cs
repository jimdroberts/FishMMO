#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Where the authoring tool finds generated art by name.</summary>
	public interface IBiomeArtSource
	{
		/// <summary>The terrain layer of a ground family, or null when it is missing.</summary>
		TerrainLayer Layer(string family);
		/// <summary>A prefab by catalogue name, or null when it is missing.</summary>
		GameObject Prefab(string name);
	}

	/// <summary>
	/// Assigns the generated art to biome templates from <see cref="BiomeArtSpec"/>: ground
	/// layers into their texture slots, and scatter rules onto those layers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Fills, never overwrites.</b> A texture slot is filled only when it is empty — no
	/// terrain layer, and no albedo or only a placeholder swatch (see <see cref="IsPlaceholderTexture"/>).
	/// Filling sets the slot's <see cref="TerrainTextureLayer.terrainLayer"/>, which takes precedence
	/// over the loose texture fields, so even a swatch that was in the slot stays where it was: the
	/// tool adds, it does not erase. The bands (noise, slope, height, cliff angles) are written
	/// only into a slot the tool is filling, because they describe the art it is putting there.
	/// Running it twice changes nothing the second time.
	/// </para>
	/// <para>
	/// <b>Rule lists.</b> Spec rules are added to a layer whose rule list holds no live rule. A rule
	/// is dead when none of its prefabs exists (a missing reference, or none at all) or every one
	/// that does is a placeholder cube (<see cref="IsPlaceholderPrefab"/>). Dead rules are never
	/// deleted: they are switched off (<see cref="PrefabSpawnRule.enableSpawning"/> = false) and
	/// their name marked, so the data stays for whoever authored it and a scatter does not plant
	/// cubes or nothing. A rule someone switched off themselves, with real prefabs, is authored —
	/// its list is left alone.
	/// </para>
	/// <para>
	/// <b>Spec rules stay the spec's until somebody tunes them.</b> Every rule the tool writes
	/// carries <see cref="PrefabSpawnRule.SpecFingerprint"/>, a hash of the values it wrote
	/// (<see cref="Fingerprint"/>). On a later run a rule named like a spec rule is brought up to date
	/// from the spec only while it still hashes to that fingerprint — nobody has changed a value
	/// since — and is then re-marked. A rule whose values differ is somebody's tuning and is kept
	/// as it is, for good (counted, and named when the spec would have changed it); a rule with no
	/// fingerprint was made by hand, or by a run before fingerprints existed. For the latter, a rule
	/// whose pre-fingerprint values (<see cref="LegacyFingerprint"/>) equal what the spec writes now
	/// or wrote in an earlier version (<see cref="BiomeArtSpec.Scatter.Earlier"/>), and whose newer
	/// fields are still at their defaults, is recognised as the tool's and adopted. The rule object
	/// is updated in place, so its stable GUID — and every seed drawn from it — stays.
	/// </para>
	/// <para>
	/// <b>Which layer a rule rides on.</b> Main-layer rules go on the biome's main layer whoever
	/// painted it: the main layer is the biome's base ground, and its vegetation belongs to the biome
	/// rather than to one picture of it. Detail-layer rules go only on the detail slot that holds
	/// the spec's family, because a meadow's flowers planted wherever a hand-authored sand layer
	/// happened to sit in the same slot would be wrong.
	/// </para>
	/// <para>
	/// <b>Generated art is never authored.</b> Everything under the generated folders
	/// (<see cref="ProceduralArtPayload.IsGenerated"/>) is build output the spec table produced, so it
	/// never counts as somebody's work: a slot holding a generated ground layer of another family than
	/// the spec's is re-filled (the spec changed); a slot pointing at a layer built from its own loose
	/// textures (<see cref="BiomeTerrainLayers.BuiltFolder"/>) has that reference dropped, since the
	/// loose textures resolve to it anyway; and a rule whose prefabs are all generated is a spec rule,
	/// which does not stop the spec's other rules being added. Art is authored when it lives outside
	/// the generated folders — a committed asset assigned by hand, or LOCAL art through a sidecar.
	/// </para>
	/// <para>
	/// <b>Nothing committed points into Assets/LOCAL.</b> Before a biome is filled, any of its slots
	/// that references an asset under <c>Assets/LOCAL</c> — a licensed texture or layer assigned on
	/// the template by hand — has that reference moved to the biome's <see cref="BiomeLocalArt"/>
	/// sidecar for the same slot and cleared on the template (<see cref="MoveLocalReferences"/>).
	/// The art still draws on this machine, through the sidecar; the template stops naming a file
	/// other clones do not have; and the emptied slot is then filled with procedural art like any
	/// other, which is what every other clone will draw.
	/// </para>
	/// </remarks>
	public static class BiomeArtAuthoring
	{
		/// <summary>The prefix marking a rule this tool switched off.</summary>
		public const string DisabledMark = "[disabled by Biome Art: ";

		// BiomeTexture_<Name>_<RRGGBB>: the flat swatches every biome was first given as a colour key.
		private static readonly Regex PlaceholderSwatch = new Regex(@"^BiomeTexture_.+_[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

		/// <summary>True for the flat-colour swatch textures (<c>BiomeTexture_&lt;Biome&gt;_&lt;RRGGBB&gt;</c>).</summary>
		public static bool IsPlaceholderTexture(Texture texture) => texture != null && PlaceholderSwatch.IsMatch(texture.name);

		/// <summary>True for stand-in prefabs: the primitive cube the first biome assets scattered.</summary>
		public static bool IsPlaceholderPrefab(GameObject prefab) => prefab != null && (prefab.name == "Cube" || prefab.name.StartsWith("Placeholder", System.StringComparison.Ordinal));

		/// <summary>True when the slot has no art of its own.</summary>
		public static bool IsEmptySlot(TerrainTextureLayer layer)
		{
			return layer != null && layer.terrainLayer == null && (layer.albedoTexture == null || IsPlaceholderTexture(layer.albedoTexture));
		}

		/// <summary>True for an asset under the generated folders: build output, never authored.</summary>
		public static bool IsGenerated(Object asset)
		{
			return asset != null && ProceduralArtPayload.IsGenerated(AssetDatabase.GetAssetPath(asset));
		}

		/// <summary>
		/// True when the authoring pass may put <paramref name="layer"/> in the slot: it is empty, or it
		/// holds a different GENERATED layer (procedural art the spec has since changed), never art
		/// somebody assigned.
		/// </summary>
		public static bool IsFillable(TerrainTextureLayer slot, TerrainLayer layer, Func<Object, bool> isGenerated)
		{
			if (slot == null)
			{
				return false;
			}
			if (IsEmptySlot(slot))
			{
				return true;
			}
			return slot.terrainLayer != null && slot.terrainLayer != layer && isGenerated != null && isGenerated(slot.terrainLayer)
				&& !IsBuiltLayer(slot.terrainLayer);
		}

		/// <summary>True for a layer <see cref="BiomeTerrainLayers"/> built from loose textures.</summary>
		public static bool IsBuiltLayer(Object asset)
		{
			if (asset == null)
			{
				return false;
			}
			string path = AssetDatabase.GetAssetPath(asset);
			return !string.IsNullOrEmpty(path) && path.StartsWith(BiomeTerrainLayers.BuiltFolder + "/", StringComparison.Ordinal);
		}

		/// <summary>
		/// True when a rule is the spec's (every prefab it has is generated) rather than authored. A
		/// rule with no prefabs at all is neither; it is dead.
		/// </summary>
		public static bool IsGeneratedRule(PrefabSpawnRule rule, Func<Object, bool> isGenerated)
		{
			if (rule?.prefabs == null || isGenerated == null)
			{
				return false;
			}
			bool any = false;
			foreach (GameObject prefab in rule.prefabs)
			{
				if (prefab == null)
				{
					continue;
				}
				if (!isGenerated(prefab))
				{
					return false;
				}
				any = true;
			}
			return any;
		}

		/// <summary>Why a rule is dead, or null when it is live.</summary>
		public static string DeadReason(PrefabSpawnRule rule)
		{
			if (rule == null)
			{
				return "empty entry";
			}
			if (rule.prefabs == null || rule.prefabs.Length == 0)
			{
				return "no prefabs";
			}
			bool anyReal = false, anyPresent = false;
			foreach (GameObject prefab in rule.prefabs)
			{
				if (prefab == null)
				{
					continue;
				}
				anyPresent = true;
				if (!IsPlaceholderPrefab(prefab))
				{
					anyReal = true;
				}
			}
			if (!anyPresent)
			{
				return "every prefab is missing";
			}
			return anyReal ? null : "placeholder prefab only";
		}

		/// <summary>True when the list holds no live rule.</summary>
		public static bool HasNoLiveRule(List<PrefabSpawnRule> rules) => HasNoAuthoredRule(rules, null);

		/// <summary>
		/// True when the list holds no live rule that somebody authored: dead rules and, given
		/// <paramref name="isGenerated"/>, rules made only of generated prefabs (the spec's own) do not count.
		/// </summary>
		public static bool HasNoAuthoredRule(List<PrefabSpawnRule> rules, Func<Object, bool> isGenerated)
		{
			if (rules == null)
			{
				return true;
			}
			foreach (PrefabSpawnRule rule in rules)
			{
				if (DeadReason(rule) == null && !IsGeneratedRule(rule, isGenerated) && !IsUntouchedSpecRule(rule))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True when the tool wrote the rule and nobody has changed a value since.</summary>
		public static bool IsUntouchedSpecRule(PrefabSpawnRule rule)
		{
			return rule != null && !string.IsNullOrEmpty(rule.SpecFingerprint) && rule.SpecFingerprint == Fingerprint(rule);
		}

		/// <summary>What a run did or would do.</summary>
		public sealed class Report
		{
			public readonly List<string> Lines = new List<string>();
			public int LocalReferencesMoved;
			public int SlotsFilled;
			public int RulesAdded;
			/// <summary>Spec rules nobody had tuned, brought up to date with the spec (or adopted and marked).</summary>
			public int RulesUpdated;
			/// <summary>Spec-named rules the spec would change but somebody tuned, so they were kept.</summary>
			public int RulesKept;
			public int RulesDisabled;
			public int BiomesChanged;
			public readonly List<string> Missing = new List<string>();
			public readonly List<string> NoSpec = new List<string>();

			public override string ToString()
			{
				var sb = new StringBuilder();
				sb.AppendLine($"Biome art authoring: {BiomesChanged} biomes changed, {LocalReferencesMoved} LOCAL references moved to sidecars, {SlotsFilled} slots filled, {RulesAdded} rules added, updated {RulesUpdated} rules, {RulesKept} hand-tuned rules kept, {RulesDisabled} dead rules switched off.");
				foreach (string m in NoSpec) sb.AppendLine("no spec entry: " + m);
				foreach (string m in Missing) sb.AppendLine("missing art: " + m);
				foreach (string l in Lines) sb.AppendLine(l);
				return sb.ToString();
			}
		}

		// ── Dashboard ─────────────────────────────────────────────────

		[DashboardTool(DashboardToolAttribute.Biomes, "Preview biome art authoring", Section = "Authoring", Order = 0,
			Tooltip = "Lists what Author Biome Art would fill and add on every biome. Changes nothing.")]
		public static void PreviewFromDashboard()
		{
			Debug.Log(Run(false).ToString());
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Author biome art", Section = "Authoring", Order = 1,
			Tooltip = "Generates any missing procedural art, then fills empty texture slots and empty rule lists on every biome from the spec table, and brings spec rules nobody has tuned up to date. Never overwrites authored or hand-tuned values; dead placeholder rules are switched off, not deleted.",
			Confirm = "Assign procedural art to every biome's empty slots and empty rule lists, and update untouched spec rules? Authored and hand-tuned values are not touched. Undoable per biome.")]
		public static void ApplyFromDashboard()
		{
			BiomeArtGenerator.Report generated = BiomeArtGenerator.Generate(true, ProceduralArtCatalogue.DefaultSeed);
			if (generated.Wrote.Count > 0)
			{
				Debug.Log(generated.ToString());
			}
			Debug.Log(Run(true).ToString());
		}

		/// <summary>Runs over every biome template in the project.</summary>
		/// <param name="apply">False reports what would change and changes nothing.</param>
		public static Report Run(bool apply)
		{
			var report = new Report();
			var source = new AssetSource();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome == null)
				{
					continue;
				}
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome.name);
				if (entry == null)
				{
					report.NoSpec.Add(biome.name);
					continue;
				}
				if (apply)
				{
					Undo.RecordObject(biome, "Author biome art");
				}
				// First, so a slot emptied of its LOCAL art is filled below like any empty slot.
				bool moved = MoveLocalReferencesToSidecar(biome, report, apply);
				bool authored = Author(biome, entry, source, report, apply);
				if ((moved || authored) && apply)
				{
					biome.InvalidateTextureLayerCache();
					EditorUtility.SetDirty(biome);
				}
			}
			if (apply)
			{
				AssetDatabase.SaveAssets();
			}
			return report;
		}

		/// <summary>The project's generated art, by catalogue path.</summary>
		public sealed class AssetSource : IBiomeArtSource
		{

			private readonly Dictionary<string, TerrainLayer> layers = new Dictionary<string, TerrainLayer>();
			private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

			public TerrainLayer Layer(string family)
			{
				if (!layers.TryGetValue(family, out TerrainLayer layer))
				{
					layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ProceduralArtCatalogue.GroundLayerPath(family));
					layers[family] = layer;
				}
				return layer;
			}

			public GameObject Prefab(string name)
			{
				if (!prefabs.TryGetValue(name, out GameObject prefab))
				{
					prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
					prefabs[name] = prefab;
				}
				return prefab;
			}
		}

		// ── LOCAL references on committed slots ──────────────────────

		/// <summary>True when any of a committed slot's art lives under Assets/LOCAL.</summary>
		public static bool HasLocalReference(TerrainTextureLayer slot, Func<Object, bool> isLocal)
		{
			return slot != null && (IsLocal(slot.terrainLayer, isLocal) || IsLocal(slot.albedoTexture, isLocal)
				|| IsLocal(slot.normalTexture, isLocal) || IsLocal(slot.maskTexture, isLocal));
		}

		private static bool IsLocal(Object asset, Func<Object, bool> isLocal) => asset != null && isLocal(asset);

		/// <summary>
		/// Moves a committed slot's LOCAL references into a sidecar entry and clears them on the slot.
		/// Pure apart from <paramref name="isLocal"/>, so a test can run it on in-memory objects.
		/// </summary>
		/// <remarks>
		/// The sidecar only gains what it does not already have: a field the sidecar already sets to
		/// something else keeps its value, because a sidecar is written on purpose and a template slot
		/// pointing into LOCAL is a mistake. The slot is cleared either way — a committed reference into
		/// LOCAL is never right — and the dropped reference is named in <paramref name="conflict"/>.
		/// With no <paramref name="target"/> (a preview) nothing is changed.
		/// </remarks>
		/// <returns>True when the slot had a LOCAL reference (and, when applying, it was moved).</returns>
		public static bool MoveLocalReferences(TerrainTextureLayer slot, BiomeLocalArt.SlotOverride target, Func<Object, bool> isLocal, out string conflict)
		{
			conflict = null;
			if (!HasLocalReference(slot, isLocal))
			{
				return false;
			}
			if (target == null)
			{
				return true;
			}
			var dropped = new List<string>();
			if (IsLocal(slot.terrainLayer, isLocal))
			{
				target.TerrainLayer = Merge(target.TerrainLayer, slot.terrainLayer, dropped);
				slot.terrainLayer = null;
			}
			if (IsLocal(slot.albedoTexture, isLocal))
			{
				target.Albedo = Merge(target.Albedo, slot.albedoTexture, dropped);
				slot.albedoTexture = null;
			}
			if (IsLocal(slot.normalTexture, isLocal))
			{
				target.Normal = Merge(target.Normal, slot.normalTexture, dropped);
				slot.normalTexture = null;
			}
			if (IsLocal(slot.maskTexture, isLocal))
			{
				target.Mask = Merge(target.Mask, slot.maskTexture, dropped);
				slot.maskTexture = null;
			}
			if (dropped.Count > 0)
			{
				conflict = "the sidecar already overrides this slot differently, so it keeps its own " + string.Join(", ", dropped);
			}
			return true;
		}

		private static T Merge<T>(T current, T moving, List<string> dropped) where T : Object
		{
			if (current == null || current == moving)
			{
				return moving;
			}
			dropped.Add($"'{current.name}' (template's '{moving.name}' dropped)");
			return current;
		}

		/// <summary>Moves every LOCAL reference on a biome's committed slots into its sidecar. True when any was found.</summary>
		private static bool MoveLocalReferencesToSidecar(BiomeTemplate biome, Report report, bool apply)
		{
			bool any = false;
			foreach (string key in BiomeLocalArt.SlotsOf(biome))
			{
				TerrainTextureLayer slot = BiomeLocalArt.CommittedLayer(biome, key);
				if (!HasLocalReference(slot, BiomeLocalArtIndex.IsLocal))
				{
					continue;
				}
				any = true;
				report.LocalReferencesMoved++;
				if (!apply)
				{
					report.Lines.Add($"{biome.name} {key}: references art under Assets/LOCAL — would move it to the biome's LOCAL sidecar and clear the slot");
					continue;
				}
				BiomeLocalArt art = BiomeLocalArtIndex.GetOrCreate(biome);
				if (art == null)
				{
					continue;
				}
				Undo.RecordObject(art, "Move LOCAL art to sidecar");
				MoveLocalReferences(slot, art.GetOrAdd(key), BiomeLocalArtIndex.IsLocal, out string conflict);
				EditorUtility.SetDirty(art);
				AssetDatabase.SaveAssetIfDirty(art);
				BiomeLocalArtIndex.Invalidate();
				report.Lines.Add($"{biome.name} {key}: art under Assets/LOCAL moved to '{art.name}' and cleared on the template{(conflict != null ? " — " + conflict : string.Empty)}");
			}
			return any;
		}

		// ── One biome ─────────────────────────────────────────────────

		/// <summary>
		/// Applies one spec entry to one biome. Pure apart from the source, so a test can run it on
		/// an in-memory template. Returns true when anything changed (or would, when not applying).
		/// </summary>
		public static bool Author(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			string who = biome.name;
			int before = report.SlotsFilled + report.RulesAdded + report.RulesDisabled + report.RulesUpdated;

			// A reference to a layer built from the slot's own loose textures is build output on a
			// committed template; the loose textures resolve to the same layer without it.
			foreach (string key in BiomeLocalArt.SlotsOf(biome))
			{
				TerrainTextureLayer slot = BiomeLocalArt.CommittedLayer(biome, key);
				if (slot != null && IsBuiltLayer(slot.terrainLayer))
				{
					report.SlotsFilled++;
					report.Lines.Add($"{who} {key}: {(apply ? "dropped" : "would drop")} its reference to the built layer '{slot.terrainLayer.name}' (build output; its loose textures resolve to it)");
					if (apply)
					{
						slot.terrainLayer = null;
					}
				}
			}

			// Dead rules first, so the lists they sit in count as empty below.
			DisableDeadRules(who, "main", biome.MainTextureLayer, report, apply);
			if (biome.DetailTextureLayers != null)
			{
				for (int i = 0; i < biome.DetailTextureLayers.Count; i++)
				{
					DisableDeadRules(who, "detail/" + i, biome.DetailTextureLayers[i], report, apply);
				}
			}
			if (biome.CliffTextureLayers != null)
			{
				for (int i = 0; i < biome.CliffTextureLayers.Count; i++)
				{
					DisableDeadRules(who, "cliff/" + i, biome.CliffTextureLayers[i], report, apply);
				}
			}

			// Main.
			if (entry.Main != null)
			{
				Fill(who, "main", biome.MainTextureLayer, entry.Main.Family, null, source, report, apply);
				AddRules(who, "main", biome.MainTextureLayer, entry.Main, source, report, apply);
			}

			// Details, slot by slot: an authored slot keeps its art and its index.
			for (int i = 0; i < entry.Details.Length; i++)
			{
				BiomeArtSpec.Layer spec = entry.Details[i];
				TerrainTextureLayer slot = biome.DetailTextureLayers != null && i < biome.DetailTextureLayers.Count ? biome.DetailTextureLayers[i] : null;
				if (slot == null)
				{
					if (source.Layer(spec.Family) == null)
					{
						report.Missing.Add($"{who} detail/{i}: Ground_{spec.Family}");
						continue;
					}
					slot = new TerrainTextureLayer();
					if (apply)
					{
						biome.DetailTextureLayers.Add(slot);
					}
				}
				bool fillable = IsFillable(slot, source.Layer(spec.Family), IsGenerated);
				Fill(who, "detail/" + i, slot, spec.Family, spec, source, report, apply);
				if (slot.terrainLayer != null && slot.terrainLayer == source.Layer(spec.Family) || !apply && fillable)
				{
					AddRules(who, "detail/" + i, slot, spec, source, report, apply);
				}
				else if (spec.Scatter.Count > 0)
				{
					report.Lines.Add($"{who} detail/{i}: holds authored art, so its spec rules ({spec.Scatter.Count}) were not added");
				}
			}

			// Cliffs.
			for (int i = 0; i < entry.Cliffs.Length; i++)
			{
				BiomeArtSpec.Cliff spec = entry.Cliffs[i];
				CliffTextureLayer slot = biome.CliffTextureLayers != null && i < biome.CliffTextureLayers.Count ? biome.CliffTextureLayers[i] : null;
				if (slot == null)
				{
					if (source.Layer(spec.Family) == null)
					{
						report.Missing.Add($"{who} cliff/{i}: Ground_{spec.Family}");
						continue;
					}
					slot = new CliffTextureLayer();
					if (apply)
					{
						biome.CliffTextureLayers.Add(slot);
					}
				}
				if (Fill(who, "cliff/" + i, slot, spec.Family, null, source, report, apply) && apply)
				{
					slot.minCliffAngle = spec.MinAngle;
					slot.maxCliffAngle = spec.MaxAngle;
					slot.useSlopeConstraint = true;
					slot.slopeRange = new MinMaxRange(spec.MinAngle, spec.MaxAngle);
				}
			}

			// Under the water.
			if (entry.Lakebed != null)
			{
				Fill(who, "lakebed", biome.LakebedTextureLayer, entry.Lakebed, null, source, report, apply);
			}
			if (entry.Riverbed != null)
			{
				Fill(who, "riverbed", biome.RiverbedTextureLayer, entry.Riverbed, null, source, report, apply);
			}

			bool changed = report.SlotsFilled + report.RulesAdded + report.RulesDisabled + report.RulesUpdated != before;
			if (changed)
			{
				report.BiomesChanged++;
			}
			return changed;
		}

		/// <summary>Fills an empty slot with a family's layer (and its bands); false when the slot was not empty.</summary>
		private static bool Fill(string who, string slotName, TerrainTextureLayer slot, string family, BiomeArtSpec.Layer bands,
			IBiomeArtSource source, Report report, bool apply)
		{
			if (slot == null)
			{
				return false;
			}
			TerrainLayer layer = source.Layer(family);
			if (!IsFillable(slot, layer, IsGenerated))
			{
				return false;
			}
			if (layer == null)
			{
				report.Missing.Add($"{who} {slotName}: Ground_{family}");
				return false;
			}
			string replacing = slot.terrainLayer != null ? $" (replacing the generated '{slot.terrainLayer.name}': the spec changed)" : string.Empty;
			report.SlotsFilled++;
			report.Lines.Add($"{who} {slotName}: {(apply ? "filled with" : "would fill with")} Ground_{family}{replacing}{(slot.albedoTexture != null && slot.terrainLayer == null ? $" (placeholder '{slot.albedoTexture.name}' left in place, now unused)" : string.Empty)}");
			if (!apply)
			{
				return true;
			}
			slot.terrainLayer = layer;
			if (bands != null)
			{
				slot.blendNoiseScale = Mathf.Clamp(bands.NoiseScale, 1f, 256f);
				slot.blendSharpness = Mathf.Clamp(bands.Sharpness, 0.1f, 10f);
				slot.useSlopeConstraint = bands.Slope.HasValue;
				if (bands.Slope.HasValue)
				{
					slot.slopeRange = new MinMaxRange(bands.Slope.Value.x, bands.Slope.Value.y);
					slot.slopeFalloff = 5f;
				}
				slot.useHeightConstraint = bands.Height.HasValue;
				if (bands.Height.HasValue)
				{
					slot.heightRange = new MinMaxRange(bands.Height.Value.x, bands.Height.Value.y);
					slot.heightFalloff = 0.05f;
				}
			}
			return true;
		}

		private static void DisableDeadRules(string who, string slotName, TerrainTextureLayer slot, Report report, bool apply)
		{
			if (slot?.prefabSpawnRules == null)
			{
				return;
			}
			for (int r = 0; r < slot.prefabSpawnRules.Count; r++)
			{
				PrefabSpawnRule rule = slot.prefabSpawnRules[r];
				string reason = DeadReason(rule);
				if (reason == null || rule == null || !rule.enableSpawning)
				{
					continue;
				}
				report.RulesDisabled++;
				report.Lines.Add($"{who} {slotName} rule {r} '{rule.ruleName}': {reason} — {(apply ? "switched off" : "would be switched off")} (kept, not deleted)");
				if (apply)
				{
					rule.enableSpawning = false;
					if (rule.ruleName == null || !rule.ruleName.StartsWith(DisabledMark, System.StringComparison.Ordinal))
					{
						rule.ruleName = $"{DisabledMark}{reason}] {rule.ruleName}";
					}
				}
			}
		}

		private static void AddRules(string who, string slotName, TerrainTextureLayer slot, BiomeArtSpec.Layer spec, IBiomeArtSource source, Report report, bool apply)
		{
			if (slot == null || spec.Scatter.Count == 0)
			{
				return;
			}
			// Rules already there by name: brought up to date when untouched, kept when tuned.
			var missing = new List<BiomeArtSpec.Scatter>();
			foreach (BiomeArtSpec.Scatter s in spec.Scatter)
			{
				PrefabSpawnRule existing = RuleNamed(slot.prefabSpawnRules, s.Name);
				if (existing != null)
				{
					Refresh(who, slotName, existing, s, source, report, apply);
				}
				else
				{
					missing.Add(s);
				}
			}
			if (missing.Count == 0)
			{
				return;
			}
			// Dead rules count as nothing here — they were (or would be) switched off above — and nor do
			// the spec's own (all-generated, or fingerprinted and untouched) rules: not somebody's work.
			if (!HasNoAuthoredRule(slot.prefabSpawnRules, IsGenerated))
			{
				report.Lines.Add($"{who} {slotName}: has authored rules, spec rules not added");
				return;
			}
			foreach (BiomeArtSpec.Scatter s in missing)
			{
				var prefabs = new List<GameObject>();
				foreach (string name in s.Prefabs)
				{
					GameObject prefab = source.Prefab(name);
					if (prefab == null)
					{
						report.Missing.Add($"{who} {slotName}: prefab {name}");
						continue;
					}
					prefabs.Add(prefab);
				}
				if (prefabs.Count == 0)
				{
					continue;
				}
				report.RulesAdded++;
				report.Lines.Add($"{who} {slotName}: {(apply ? "added" : "would add")} rule '{s.Name}' ({s.Channel}, {s.Density}/100 m²)");
				if (apply)
				{
					slot.prefabSpawnRules ??= new List<PrefabSpawnRule>();
					slot.prefabSpawnRules.Add(ToRule(who, s, prefabs.ToArray()));
				}
			}
		}

		private static PrefabSpawnRule RuleNamed(List<PrefabSpawnRule> rules, string name)
		{
			if (rules == null)
			{
				return null;
			}
			foreach (PrefabSpawnRule rule in rules)
			{
				if (rule != null && rule.ruleName == name)
				{
					return rule;
				}
			}
			return null;
		}

		/// <summary>
		/// Brings one existing spec-named rule up to date from the spec, when the tool wrote it and
		/// nobody has changed it since; otherwise leaves it alone. See the class remarks.
		/// </summary>
		private static void Refresh(string who, string slotName, PrefabSpawnRule rule, BiomeArtSpec.Scatter s, IBiomeArtSource source, Report report, bool apply)
		{
			GameObject[] prefabs = Resolve(s.Prefabs, source);
			if (prefabs.Length == 0)
			{
				return;
			}
			PrefabSpawnRule target = ToRule(who, s, prefabs);
			string current = Fingerprint(rule);
			if (current == target.SpecFingerprint && rule.SpecFingerprint == target.SpecFingerprint)
			{
				return; // Up to date.
			}

			string how;
			if (!string.IsNullOrEmpty(rule.SpecFingerprint))
			{
				if (rule.SpecFingerprint != current)
				{
					report.RulesKept++;
					report.Lines.Add($"{who} {slotName}: rule '{rule.ruleName}' was tuned by hand since the tool wrote it — kept as it is (the spec has changed)");
					return;
				}
				how = "brought up to date with the spec";
			}
			else if (IsLegacySpecRule(who, rule, s, prefabs, source))
			{
				how = current == target.SpecFingerprint ? "recognised as the tool's and marked" : "recognised as an earlier run's, untouched, and brought up to date with the spec";
			}
			else
			{
				if (current != target.SpecFingerprint)
				{
					report.RulesKept++;
					report.Lines.Add($"{who} {slotName}: rule '{rule.ruleName}' differs from anything the tool wrote (hand-made or hand-tuned) — kept as it is");
				}
				return;
			}

			report.RulesUpdated++;
			report.Lines.Add($"{who} {slotName}: rule '{rule.ruleName}' {(apply ? how : "would be " + how)}{(rule.IsCarpet != (s.Channel == PrefabSpawnChannel.DetailLayer && s.Placement == DetailPlacement.Carpet) ? $" (now {s.Placement})" : string.Empty)}");
			if (apply)
			{
				ApplySpec(rule, who, s, prefabs);
			}
		}

		/// <summary>
		/// True when a rule with no fingerprint holds exactly what this or an earlier version of the
		/// spec wrote before fingerprints existed, with every newer field still at its default.
		/// </summary>
		private static bool IsLegacySpecRule(string who, PrefabSpawnRule rule, BiomeArtSpec.Scatter s, GameObject[] prefabs, IBiomeArtSource source)
		{
			// Fields a pre-fingerprint run could not have set: anything but their defaults is somebody's.
			if (rule.detailPlacement != DetailPlacement.Scattered || rule.sinkRange != Vector2.zero || rule.sinkSlopeFactor != 0f || rule.clusterMetres != 0f)
			{
				return false;
			}
			string legacy = LegacyFingerprint(rule);
			if (legacy == LegacyFingerprint(ToRule(who, s, prefabs)))
			{
				return true;
			}
			foreach (BiomeArtSpec.Scatter earlier in s.Earlier)
			{
				GameObject[] earlierPrefabs = Resolve(earlier.Prefabs, source);
				if (earlierPrefabs.Length > 0 && legacy == LegacyFingerprint(ToRule(who, earlier, earlierPrefabs)))
				{
					return true;
				}
			}
			return false;
		}

		private static GameObject[] Resolve(string[] names, IBiomeArtSource source)
		{
			var prefabs = new List<GameObject>();
			if (names != null)
			{
				foreach (string name in names)
				{
					GameObject prefab = source.Prefab(name);
					if (prefab != null)
					{
						prefabs.Add(prefab);
					}
				}
			}
			return prefabs.ToArray();
		}

		// ── Fingerprints ──────────────────────────────────────────────

		/// <summary>
		/// A hash of every value of a rule the tool writes: what <see cref="PrefabSpawnRule.SpecFingerprint"/>
		/// holds, and what a rule must still hash to for the tool to treat it as untouched.
		/// </summary>
		/// <remarks>
		/// Not the stable GUID (the tool never writes it over) and not the fingerprint itself. Floats
		/// are rounded to 1/10 000 first, so a value that survives a trip through the asset's YAML
		/// as the nearest float still hashes the same. Prefabs by name: generated prefabs have fixed,
		/// unique names, and a test's in-memory stand-ins have no asset path to use instead.
		/// </remarks>
		public static string Fingerprint(PrefabSpawnRule rule)
		{
			var text = new StringBuilder(512);
			AppendLegacyFields(text, rule);
			text.Append('|').Append((int)rule.detailPlacement)
				.Append('|').Append(Q(rule.carpetCoverage)).Append(',').Append(Q(rule.carpetClumpMetres))
				.Append(',').Append(Q(rule.carpetClumpFloor)).Append(',').Append(Q(rule.carpetWeightRamp))
				.Append('|').Append(Q(rule.sinkRange.x)).Append(',').Append(Q(rule.sinkRange.y)).Append(',').Append(Q(rule.sinkSlopeFactor));
			/* Groups only when a rule has them, so a rule fingerprinted before they existed (no radius)
			 * still hashes to what it was marked with and is brought up to date, not taken for tuned. */
			if (rule.clusterMetres != 0f)
			{
				text.Append("|C").Append(Q(rule.clusterMetres)).Append(',').Append(Q(rule.clusterSpacing))
					.Append(',').Append(Q(rule.clusterBackground)).Append(',').Append(Q(rule.clusterScaleBias));
				// Likewise the group size, added after the first grouped rules were marked.
				if (rule.clusterSize != 0f)
				{
					text.Append(",N").Append(Q(rule.clusterSize));
				}
			}
			return Hash64(text);
		}

		/// <summary>
		/// The hash of only the fields a rule had before fingerprints (and carpets and sinks) existed,
		/// for recognising a rule an older run of the tool wrote.
		/// </summary>
		public static string LegacyFingerprint(PrefabSpawnRule rule)
		{
			var text = new StringBuilder(512);
			AppendLegacyFields(text, rule);
			return Hash64(text);
		}

		private static void AppendLegacyFields(StringBuilder text, PrefabSpawnRule rule)
		{
			text.Append(rule.enableSpawning ? '1' : '0').Append('|').Append(rule.ruleName).Append('|');
			if (rule.prefabs != null)
			{
				foreach (GameObject prefab in rule.prefabs)
				{
					text.Append(prefab != null ? prefab.name : "<none>").Append(';');
				}
			}
			text.Append('|').Append((int)rule.spawnChannel).Append('|').Append((int)rule.detailRenderMode)
				.Append('|').Append(Q(rule.densityPer100m2)).Append('|').Append(rule.maxPerChunk).Append('|').Append(Q(rule.minSpacing))
				.Append('|').Append(Q(rule.minTextureWeight)).Append('|').Append(Q(rule.spawnProbabilityMultiplier))
				.Append('|').Append(rule.useHeightConstraint ? '1' : '0').Append(Q(rule.heightRange.min)).Append(',').Append(Q(rule.heightRange.max)).Append(',').Append(Q(rule.heightFalloff))
				.Append('|').Append(rule.useSlopeConstraint ? '1' : '0').Append(Q(rule.slopeRange.min)).Append(',').Append(Q(rule.slopeRange.max)).Append(',').Append(Q(rule.slopeFalloff))
				.Append('|').Append(rule.useNonUniformScaling ? '1' : '0')
				.Append('|').Append(Q(rule.uniformScaleRange.x)).Append(',').Append(Q(rule.uniformScaleRange.y))
				.Append('|').Append(Q(rule.widthScaleRange.x)).Append(',').Append(Q(rule.widthScaleRange.y))
				.Append('|').Append(Q(rule.heightScaleRange.x)).Append(',').Append(Q(rule.heightScaleRange.y))
				.Append('|').Append(Q(rule.yRotationRange.x)).Append(',').Append(Q(rule.yRotationRange.y))
				.Append('|').Append(rule.alignToTerrainNormal ? '1' : '0').Append('|').Append(rule.seedOffset)
				.Append('|').Append(Q(rule.detailNoiseSpread))
				.Append('|').Append(Q(rule.detailHealthyColor.r)).Append(',').Append(Q(rule.detailHealthyColor.g)).Append(',').Append(Q(rule.detailHealthyColor.b)).Append(',').Append(Q(rule.detailHealthyColor.a))
				.Append('|').Append(Q(rule.detailDryColor.r)).Append(',').Append(Q(rule.detailDryColor.g)).Append(',').Append(Q(rule.detailDryColor.b)).Append(',').Append(Q(rule.detailDryColor.a))
				.Append('|').Append(rule.detailInstancesPerSpawn);
		}

		private static long Q(float value) => (long)Math.Round(value * 10000.0);

		/// <summary>FNV-1a, 64-bit, as hex: stable across runs and platforms, unlike GetHashCode.</summary>
		private static string Hash64(StringBuilder text)
		{
			unchecked
			{
				ulong h = 14695981039346656037UL;
				for (int i = 0; i < text.Length; i++)
				{
					h ^= text[i];
					h *= 1099511628211UL;
				}
				return h.ToString("x16");
			}
		}

		/// <summary>A spec rule as a new <see cref="PrefabSpawnRule"/>, marked with its fingerprint.</summary>
		public static PrefabSpawnRule ToRule(string biome, BiomeArtSpec.Scatter s, GameObject[] prefabs)
		{
			var rule = new PrefabSpawnRule();
			ApplySpec(rule, biome, s, prefabs);
			return rule;
		}

		/// <summary>
		/// Writes a spec rule's values into a rule, in place (its stable GUID kept), and marks it
		/// with the fingerprint of what was written.
		/// </summary>
		public static void ApplySpec(PrefabSpawnRule rule, string biome, BiomeArtSpec.Scatter s, GameObject[] prefabs)
		{
			Write(rule, new PrefabSpawnRule
			{
				enableSpawning = true,
				ruleName = s.Name,
				prefabs = prefabs,
				spawnChannel = s.Channel,
				detailRenderMode = DetailRenderMode.VertexLit,
				densityPer100m2 = Mathf.Clamp(s.Density, 0f, 50f),
				// Details: unlimited per rule (the tile budgets still apply); trees: the spec's cap.
				maxPerChunk = s.Channel == PrefabSpawnChannel.DetailLayer ? 0 : Mathf.Max(0, s.MaxPerChunk),
				minSpacing = Mathf.Clamp(s.Spacing, 0f, 50f),
				minTextureWeight = Mathf.Clamp01(s.MinWeight),
				spawnProbabilityMultiplier = 1f,
				useHeightConstraint = s.Height.HasValue,
				heightRange = s.Height.HasValue ? new MinMaxRange(s.Height.Value.x, s.Height.Value.y) : new MinMaxRange(0f, 1f),
				heightFalloff = 0.05f,
				useSlopeConstraint = s.Slope.HasValue,
				slopeRange = s.Slope.HasValue ? new MinMaxRange(s.Slope.Value.x, s.Slope.Value.y) : new MinMaxRange(0f, 90f),
				slopeFalloff = 5f,
				useNonUniformScaling = s.NonUniform,
				uniformScaleRange = s.Scale,
				widthScaleRange = s.WidthScale,
				heightScaleRange = s.HeightScale,
				yRotationRange = new Vector2(0f, 360f),
				alignToTerrainNormal = s.AlignToNormal,
				seedOffset = ProceduralNoise.SeedFor(biome + "/" + s.Name, 0) & 0xFFFF,
				detailNoiseSpread = Mathf.Clamp(s.NoiseSpread, 0.05f, 5f),
				detailHealthyColor = s.Healthy,
				detailDryColor = s.Dry,
				// Coverage of a chosen 1 m detail cell (255 = all of it), on the field's 64–255 scale.
				detailInstancesPerSpawn = Mathf.Clamp(s.Coverage, 64, 255),
				detailPlacement = s.Placement,
				carpetCoverage = Mathf.Clamp01(s.CarpetCoverage),
				carpetClumpMetres = Mathf.Clamp(s.ClumpMetres, 2f, 200f),
				carpetClumpFloor = Mathf.Clamp01(s.ClumpFloor),
				carpetWeightRamp = Mathf.Clamp(s.WeightRamp, 0.01f, 1f),
				sinkRange = new Vector2(Mathf.Max(0f, Mathf.Min(s.Sink.x, s.Sink.y)), Mathf.Max(0f, Mathf.Max(s.Sink.x, s.Sink.y))),
				sinkSlopeFactor = Mathf.Clamp01(s.SinkSlope),
				clusterMetres = Mathf.Clamp(s.ClusterMetres, 0f, 60f),
				clusterSpacing = Mathf.Clamp(s.ClusterSpacing, 2f, 10f),
				clusterSize = Mathf.Clamp(s.ClusterSize, 0f, 100f),
				clusterBackground = Mathf.Clamp01(s.ClusterBackground),
				clusterScaleBias = Mathf.Clamp01(s.ClusterScaleBias),
			});
			rule.SpecFingerprint = Fingerprint(rule);
		}

		/// <summary>Copies every value the tool writes from one rule to another; the GUID and fingerprint stay.</summary>
		private static void Write(PrefabSpawnRule to, PrefabSpawnRule from)
		{
			to.enableSpawning = from.enableSpawning;
			to.ruleName = from.ruleName;
			to.prefabs = from.prefabs;
			to.spawnChannel = from.spawnChannel;
			to.detailRenderMode = from.detailRenderMode;
			to.densityPer100m2 = from.densityPer100m2;
			to.maxPerChunk = from.maxPerChunk;
			to.minSpacing = from.minSpacing;
			to.minTextureWeight = from.minTextureWeight;
			to.spawnProbabilityMultiplier = from.spawnProbabilityMultiplier;
			to.useHeightConstraint = from.useHeightConstraint;
			to.heightRange = from.heightRange;
			to.heightFalloff = from.heightFalloff;
			to.useSlopeConstraint = from.useSlopeConstraint;
			to.slopeRange = from.slopeRange;
			to.slopeFalloff = from.slopeFalloff;
			to.useNonUniformScaling = from.useNonUniformScaling;
			to.uniformScaleRange = from.uniformScaleRange;
			to.widthScaleRange = from.widthScaleRange;
			to.heightScaleRange = from.heightScaleRange;
			to.yRotationRange = from.yRotationRange;
			to.alignToTerrainNormal = from.alignToTerrainNormal;
			to.seedOffset = from.seedOffset;
			to.detailNoiseSpread = from.detailNoiseSpread;
			to.detailHealthyColor = from.detailHealthyColor;
			to.detailDryColor = from.detailDryColor;
			to.detailInstancesPerSpawn = from.detailInstancesPerSpawn;
			to.detailPlacement = from.detailPlacement;
			to.carpetCoverage = from.carpetCoverage;
			to.carpetClumpMetres = from.carpetClumpMetres;
			to.carpetClumpFloor = from.carpetClumpFloor;
			to.carpetWeightRamp = from.carpetWeightRamp;
			to.sinkRange = from.sinkRange;
			to.sinkSlopeFactor = from.sinkSlopeFactor;
			to.clusterMetres = from.clusterMetres;
			to.clusterSpacing = from.clusterSpacing;
			to.clusterSize = from.clusterSize;
			to.clusterBackground = from.clusterBackground;
			to.clusterScaleBias = from.clusterScaleBias;
		}
	}
}
#endif
