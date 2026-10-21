#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
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
	/// terrain layer, and no albedo or only a placeholder swatch (see <see cref="IsPlaceholderTexture"/>) —
	/// or still holds one of the first biomes' colour-only photos (<see cref="IsLegacyPhoto"/>), whose reference
	/// is then dropped.
	/// Filling sets the slot's <see cref="TerrainTextureLayer.terrainLayer"/>, which takes precedence
	/// over the loose texture fields, so even a swatch that was in the slot stays where it was: the
	/// tool adds, it does not erase. The bands (noise, slope, height, cliff angles) are written
	/// only into a slot the tool is filling, because they describe the art it is putting there.
	/// Running it twice changes nothing the second time.
	/// </para>
	/// <para>
	/// <b>Rule lists.</b> Spec rules are added to a layer whose rule list holds no live rule. A rule
	/// is dead when none of its prefabs exists (a missing reference, or none at all) or every one
	/// that does is a placeholder cube (<see cref="IsPlaceholderPrefab"/>). Dead rules are removed,
	/// as are rules the spec has moved out of a slot (when nobody tuned them) and the rules an earlier
	/// version switched off and kept (<see cref="DisabledMark"/>): kept, they were only dead weight in
	/// every template (2026-10-11). A rule someone switched off themselves, with real prefabs, is
	/// authored — its list is left alone.
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
	/// <b>Everything the scene cut reads is on the template (2026-10-15).</b> Besides the ground layers and their rules,
	/// the tool fills the way grounds (Small Path, Road, Cobbles) and the river bars (Bar Sand, Bar Gravel) as it fills any
	/// slot, from the spec's columns (<see cref="BiomeArtSpec.Entry.PathGround"/> …); and it writes the biome's rock (its own,
	/// the geology rocks it accepts, each cliff layer's rock type) and what its points of interest grow (a herb grove's
	/// flowers, a cave mouth's bushes, ferns, creeper, boulders and deadwood). Each of those values is marked with what the
	/// tool wrote (<see cref="BiomeTemplate.SetAuthoredFingerprint"/>; a cliff layer's own <see cref="CliffTextureLayer.rockTypeAuthored"/>):
	/// it is written again only while it still holds that, or while it is empty and was never written; a value somebody
	/// changed, or a list somebody emptied, is kept. The spec's columns default to what the cut decided in code before, so
	/// the first run changes no scene; the cut (<see cref="BiomeTemplateArt"/>) reads only the template, falling back to
	/// that old code with a warning where a template has not been authored.
	/// </para>
	/// <para>
	/// <b>Lists are marked entry by entry (2026-10-16, I5b).</b> A list on the template (the rocks it accepts, its cave shells,
	/// sea life, vine species, gathering resources, point-of-interest growth, site kinds, variants and styles, crops, palette
	/// entries and street lights) carries a mark per entry, keyed by the entry's identity (<see cref="BiomeEntryMarks"/>,
	/// <see cref="AuthorEntries{T}"/>): the spec's new entries reach a list somebody has edited, the tool's untouched entries
	/// follow the spec, and an entry edited, made or deleted by hand stays as it was left (a deletion is remembered). A list
	/// still holding one mark for the whole of it is converted on the next run. Single values keep one mark each.
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
		/// <summary>The prefix an earlier version of this tool gave a rule it switched off; such rules are now removed.</summary>
		public const string DisabledMark = "[disabled by Biome Art: ";

		/// <summary>Where the first biomes' colour-only photo textures live (<see cref="IsLegacyPhoto"/>).</summary>
		public const string LegacyPhotoFolder = "Assets/Prefabs/Shared/Biomes/Textures/";

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

		/// <summary>
		/// True for a slot still drawing one of the first biomes' photo textures: a loose albedo from
		/// <see cref="LegacyPhotoFolder"/> with no normal and no mask (Grass, Woodland, Forest, Glacier, Snow, Ocean). Not
		/// art somebody chose for a biome so much as the stand-in it was born with, a step below the generated ground in
		/// every way (no relief of its own, no mask, 1024 px), so the authoring pass replaces it as it does a swatch.
		/// </summary>
		public static bool IsLegacyPhoto(TerrainTextureLayer layer)
		{
			if (layer == null || layer.terrainLayer != null || layer.albedoTexture == null || layer.normalTexture != null || layer.maskTexture != null
				|| IsPlaceholderTexture(layer.albedoTexture))
			{
				return false;
			}
			string path = AssetDatabase.GetAssetPath(layer.albedoTexture);
			return !string.IsNullOrEmpty(path) && path.StartsWith(LegacyPhotoFolder, StringComparison.Ordinal);
		}

		/// <summary>True for an asset under the generated folders: build output, never authored.</summary>
		public static bool IsGenerated(Object asset)
		{
			return asset != null && ProceduralArtPayload.IsGenerated(AssetDatabase.GetAssetPath(asset));
		}

		/// <summary>
		/// True when the authoring pass may put <paramref name="layer"/> in the slot: it is empty, it holds
		/// one of the first biomes' photos (<see cref="IsLegacyPhoto"/>), or it holds a different GENERATED layer
		/// (procedural art the spec has since changed); never art somebody assigned.
		/// </summary>
		public static bool IsFillable(TerrainTextureLayer slot, TerrainLayer layer, Func<Object, bool> isGenerated)
		{
			if (slot == null)
			{
				return false;
			}
			if (IsEmptySlot(slot) || IsLegacyPhoto(slot))
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
			/// <summary>Dead rules, rules the spec retired from a slot, and rules an earlier run had switched off: removed.</summary>
			public int RulesDisabled;
			/// <summary>Template values written from the spec (rocks, cliff rock types, point-of-interest growth), or marked as the tool's.</summary>
			public int ValuesFilled;
			/// <summary>Template values the spec would change but somebody edited, so they were kept.</summary>
			public int ValuesKept;
			/// <summary>Grass Types entries the tool wrote, untouched since, for blade prefabs the biome no longer grows: removed.</summary>
			public int GrassTypesPruned;
			public int BiomesChanged;
			public readonly List<string> Missing = new List<string>();
			public readonly List<string> NoSpec = new List<string>();

			public override string ToString()
			{
				var sb = new StringBuilder();
				sb.AppendLine($"Biome art authoring: {BiomesChanged} biomes changed, {LocalReferencesMoved} LOCAL references moved to sidecars, {SlotsFilled} slots filled, {RulesAdded} rules added, updated {RulesUpdated} rules, {RulesKept} hand-tuned rules kept, {RulesDisabled} dead or retired rules removed, {ValuesFilled} values filled, {ValuesKept} hand-edited values kept, {GrassTypesPruned} grass types pruned.");
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
			Tooltip = "Generates any missing procedural art, then fills empty texture slots (ground, ways, river bars) and empty rule lists on every biome from the spec table, writes each biome's rocks, point-of-interest growth, wildlife, vines and gathering resources, prunes its own grass entries for blades no longer grown, and brings spec rules and values nobody has tuned up to date. Never overwrites authored or hand-tuned values; replaces the first biomes' colour-only photos; removes dead and retired rules.",
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
			List<PointOfInterestTemplateSpec> handMade = HandMadeTemplates(ProjectPointOfInterestTemplates());
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
				bool authored = Author(biome, entry, source, report, apply, handMade);
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

			// A ground layer's own ways (2026-10-16) hold terrain layers of their own, under keys of their own.
			foreach ((string key, TerrainTextureLayer ground, string way) in WaysOf(biome))
			{
				TerrainLayer layer = BiomeLocalArt.WayGround(ground.ways, way);
				if (!IsLocal(layer, BiomeLocalArtIndex.IsLocal))
				{
					continue;
				}
				any = true;
				report.LocalReferencesMoved++;
				if (!apply)
				{
					report.Lines.Add($"{biome.name} {key}: references art under Assets/LOCAL — would move it to the biome's LOCAL sidecar and clear the way (the biome's own until the template names one there)");
					continue;
				}
				BiomeLocalArt art = BiomeLocalArtIndex.GetOrCreate(biome);
				if (art == null)
				{
					continue;
				}
				Undo.RecordObject(art, "Move LOCAL art to sidecar");
				var dropped = new List<string>();
				BiomeLocalArt.SlotOverride target = art.GetOrAdd(key);
				target.TerrainLayer = Merge(target.TerrainLayer, layer, dropped);
				BiomeLocalArt.SetWayGround(ref ground.ways, way, null);
				EditorUtility.SetDirty(art);
				AssetDatabase.SaveAssetIfDirty(art);
				BiomeLocalArtIndex.Invalidate();
				report.Lines.Add($"{biome.name} {key}: art under Assets/LOCAL moved to '{art.name}' and cleared on the template; the sidecar dresses this way only "
					+ $"while the template names a ground there{(dropped.Count > 0 ? " — the sidecar keeps its own " + string.Join(", ", dropped) : string.Empty)}");
			}
			return any;
		}

		/// <summary>
		/// Every way of every ground layer that may carry ways of its own (the main layer, each detail layer), with its key
		/// (<see cref="BiomeLocalArt.WaySlot"/>), the layer and the way's slot, set or not.
		/// </summary>
		public static IEnumerable<(string Key, TerrainTextureLayer Ground, string Way)> WaysOf(BiomeTemplate biome)
		{
			if (biome == null)
			{
				yield break;
			}
			var grounds = new List<(string, TerrainTextureLayer)> { (BiomeLocalArt.SlotMain, biome.MainTextureLayer) };
			if (biome.DetailTextureLayers != null)
			{
				for (int i = 0; i < biome.DetailTextureLayers.Count; i++)
				{
					grounds.Add((BiomeLocalArt.DetailSlot(i), biome.DetailTextureLayers[i]));
				}
			}
			foreach ((string slot, TerrainTextureLayer ground) in grounds)
			{
				if (ground == null)
				{
					continue;
				}
				foreach (string way in BiomeLocalArt.WaySlots)
				{
					yield return (BiomeLocalArt.WaySlot(slot, way), ground, way);
				}
			}
		}

		// ── One biome ─────────────────────────────────────────────────

		/// <summary>
		/// Applies one spec entry to one biome. Pure apart from the source, so a test can run it on
		/// an in-memory template. Returns true when anything changed (or would, when not applying).
		/// </summary>
		/// <param name="handMadeTemplates">
		/// The project's point-of-interest templates the catalogue does not write, as plain data (<see cref="HandMadeTemplates"/>):
		/// a variant of theirs whose own biome list names this biome is added to its Site Variants (2026-10-16, I5b). Null for none.
		/// </param>
		public static bool Author(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply,
			IReadOnlyList<PointOfInterestTemplateSpec> handMadeTemplates = null)
		{
			string who = biome.name;
			int before = report.SlotsFilled + report.RulesAdded + report.RulesDisabled + report.RulesUpdated + report.ValuesFilled + report.GrassTypesPruned;

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

			// Rules an earlier run switched off, and dead rules, are removed first, so the lists they sit in count as empty below.
			foreach (string key in BiomeLocalArt.SlotsOf(biome))
			{
				RemoveSwitchedOff(who, key, BiomeLocalArt.CommittedLayer(biome, key), report, apply);
			}
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
			foreach (string key in BiomeLocalArt.SlotsOf(biome))
			{
				if (key.StartsWith(BiomeLocalArt.SlotVariant, StringComparison.Ordinal))
				{
					DisableDeadRules(who, key, BiomeLocalArt.CommittedLayer(biome, key), report, apply);
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

			// Under the water: the lakebed is what the sea floor is painted with, so what grows there rides on it.
			DisableDeadRules(who, "lakebed", biome.LakebedTextureLayer, report, apply);
			if (entry.Lakebed != null)
			{
				Fill(who, "lakebed", biome.LakebedTextureLayer, entry.Lakebed, null, source, report, apply);
			}
			if (entry.Bed != null)
			{
				AddRules(who, "lakebed", biome.LakebedTextureLayer, entry.Bed, source, report, apply);
			}

			// Rules the spec has moved or dropped: removed where the tool wrote them and nobody has tuned them since.
			foreach ((string slotName, string rule) in entry.Retired)
			{
				Retire(who, slotName, SlotNamed(biome, slotName), rule, report, apply);
			}
			if (entry.Riverbed != null)
			{
				Fill(who, "riverbed", biome.RiverbedTextureLayer, entry.Riverbed, null, source, report, apply);
			}
			/* What lives on a river's bed (decision 35, 2026-10-18): rules on the riverbed, painted under rivers since then (the lakebed
			 * took every river before), grown by their depth below the river's own surface and by its current. And the extra beds by
			 * depth (and current) under lakes, rivers and the sea. None for any biome until its round adds them. */
			DisableDeadRules(who, "riverbed", biome.RiverbedTextureLayer, report, apply);
			if (entry.RiverBed != null)
			{
				AddRules(who, "riverbed", biome.RiverbedTextureLayer, entry.RiverBed, source, report, apply);
			}
			AuthorBedLayers(biome, entry, source, report, apply);

			// The ways' grounds and the river bars': the generated grounds the spec names, for the cut to read off the template.
			Fill(who, BiomeLocalArt.SlotPath, biome.SmallPathTextureLayer, entry.PathGround, null, source, report, apply);
			Fill(who, BiomeLocalArt.SlotRoad, biome.RoadTextureLayer, entry.RoadGround, null, source, report, apply);
			Fill(who, BiomeLocalArt.SlotCobbles, biome.CobbleTextureLayer, entry.CobbleGround, null, source, report, apply);
			Fill(who, BiomeLocalArt.SlotBarSand, biome.BarSandTextureLayer, entry.BarSand, null, source, report, apply);
			Fill(who, BiomeLocalArt.SlotBarGravel, biome.BarGravelTextureLayer, entry.BarGravel, null, source, report, apply);
			AuthorLayerWays(biome, entry, source, report, apply);
			AuthorClimateVariants(biome, entry, source, report, apply);
			AuthorLayerClimate(biome, entry, source, report, apply);
			AuthorDescription(biome, entry, report, apply);
			AuthorNaming(biome, entry, report, apply);
			AuthorEmission(biome, entry, report, apply);
			AuthorGeothermal(biome, entry, report, apply);

			AuthorRock(biome, entry, report, apply);
			AuthorCaveShells(biome, entry, report, apply);
			AuthorCaveCrystals(biome, entry, report, apply);
			AuthorFloatingPlants(biome, entry, report, apply);
			AuthorWaterRole(biome, entry, report, apply);
			AuthorWaterLook(biome, entry, report, apply);
			AuthorSeaLook(biome, entry, report, apply);
			AuthorGrowth(biome, entry, source, report, apply);
			AuthorLooks(biome, entry, report, apply);
			AuthorGrass(biome, entry, source, report, apply);
			AuthorMakeup(biome, source, report, apply);
			AuthorWildlife(biome, entry, report, apply);
			AuthorVines(biome, entry, report, apply);
			AuthorGathering(biome, entry, report, apply);
			AuthorSites(biome, entry, source, report, apply, handMadeTemplates);
			AuthorLinearFeatures(biome, entry, source, report, apply);
			AuthorFieldKinds(biome, entry, source, report, apply);
			AuthorLandforms(biome, entry, report, apply);

			bool changed =report.SlotsFilled + report.RulesAdded + report.RulesDisabled + report.RulesUpdated + report.ValuesFilled + report.GrassTypesPruned != before;
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
			bool photo = IsLegacyPhoto(slot);
			string replacing = slot.terrainLayer != null ? $" (replacing the generated '{slot.terrainLayer.name}': the spec changed)"
				: photo ? $" (replacing the colour-only photo '{slot.albedoTexture.name}')" : string.Empty;
			report.SlotsFilled++;
			report.Lines.Add($"{who} {slotName}: {(apply ? "filled with" : "would fill with")} Ground_{family}{replacing}{(!photo && slot.albedoTexture != null && slot.terrainLayer == null ? $" (placeholder '{slot.albedoTexture.name}' left in place, now unused)" : string.Empty)}");
			if (!apply)
			{
				return true;
			}
			if (photo)
			{
				// The layer draws instead of the loose textures; the photo is dropped so nothing reads it again.
				slot.albedoTexture = null;
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
				if (reason == null)
				{
					continue;
				}
				report.RulesDisabled++;
				report.Lines.Add($"{who} {slotName} rule {r} '{rule?.ruleName}': {reason} — {(apply ? "removed" : "would be removed")}");
				if (apply)
				{
					slot.prefabSpawnRules.RemoveAt(r);
					r--;
				}
			}
		}

		/// <summary>
		/// Removes the rules an earlier version of this tool switched off and kept (<see cref="DisabledMark"/>): retired
		/// kelp and seagrass, rules the spec moved to another slot, rules whose prefabs were gone. Kept, they were dead
		/// weight in every template and in every look at one.
		/// </summary>
		private static void RemoveSwitchedOff(string who, string slotName, TerrainTextureLayer slot, Report report, bool apply)
		{
			if (slot?.prefabSpawnRules == null)
			{
				return;
			}
			for (int r = 0; r < slot.prefabSpawnRules.Count; r++)
			{
				PrefabSpawnRule rule = slot.prefabSpawnRules[r];
				if (rule == null || rule.enableSpawning || rule.ruleName == null || !rule.ruleName.StartsWith(DisabledMark, StringComparison.Ordinal))
				{
					continue;
				}
				report.RulesDisabled++;
				report.Lines.Add($"{who} {slotName}: rule '{rule.ruleName}' was switched off by an earlier run — {(apply ? "removed" : "would be removed")}");
				if (apply)
				{
					slot.prefabSpawnRules.RemoveAt(r);
					r--;
				}
			}
		}

		/// <summary>A biome's texture layer by its slot name (<c>main</c>, <c>detail/N</c>, <c>lakebed</c>, <c>riverbed</c>); null for none.</summary>
		private static TerrainTextureLayer SlotNamed(BiomeTemplate biome, string slotName)
		{
			if (slotName == "main") return biome.MainTextureLayer;
			if (slotName == "lakebed") return biome.LakebedTextureLayer;
			if (slotName == "riverbed") return biome.RiverbedTextureLayer;
			int bed = BiomeLocalArt.BedIndexOf(slotName);
			if (bed >= 0)
			{
				return biome.BedTextureLayers != null && bed < biome.BedTextureLayers.Count ? biome.BedTextureLayers[bed] : null;
			}
			if (slotName.StartsWith("detail/", System.StringComparison.Ordinal) && int.TryParse(slotName.Substring(7), out int index)
				&& biome.DetailTextureLayers != null && index >= 0 && index < biome.DetailTextureLayers.Count)
			{
				return biome.DetailTextureLayers[index];
			}
			return null;
		}

		/// <summary>
		/// Switches off a rule the spec no longer writes on a slot — kelp the sea-floor spec moved from the
		/// main layer, which is never painted under water, to the lakebed — when the tool wrote it (its
		/// fingerprint still matches, or it has none: an earlier run's) and nobody has tuned it.
		/// </summary>
		private static void Retire(string who, string slotName, TerrainTextureLayer slot, string name, Report report, bool apply)
		{
			PrefabSpawnRule rule = RuleNamed(slot?.prefabSpawnRules, name);
			if (rule == null || !rule.enableSpawning)
			{
				return;
			}
			if (!string.IsNullOrEmpty(rule.SpecFingerprint) && rule.SpecFingerprint != Fingerprint(rule))
			{
				report.RulesKept++;
				report.Lines.Add($"{who} {slotName}: rule '{name}' is no longer in the spec here but was tuned by hand — kept as it is");
				return;
			}
			report.RulesDisabled++;
			report.Lines.Add($"{who} {slotName}: rule '{name}' moved out of this slot by the spec — {(apply ? "removed" : "would be removed")}");
			if (apply)
			{
				slot.prefabSpawnRules.Remove(rule);
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
			// Dead rules count as nothing here — they were (or would be) removed above — and nor do
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
			if (rule.detailPlacement != DetailPlacement.Scattered || rule.sinkRange != Vector2.zero || rule.sinkSlopeFactor != 0f || rule.clusterMetres != 0f || rule.forestMetres != 0f
				|| rule.depthRange != Vector2.zero || rule.useClimateBand || rule.waters != BiomeBedWaters.Default || rule.currentRange != Vector2.zero
				|| !rule.worldGate.IsOpen || rule.useHeatBand || (rule.scaleSkew > 0f && rule.scaleSkew != 1f))
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

		// ── Template values ───────────────────────────────────────────

		/// <summary>
		/// The rock the scene cut stands the biome in, onto the template: its own rock (<see cref="CliffRocks.OwnRock"/>), the
		/// geology rocks it accepts (<see cref="BiomeArtSpec.Entry.Rocks"/>), and each cliff layer's rock
		/// (<see cref="CliffRocks.RockTypeFor"/> of the spec's family for that slot; <see cref="CliffTextureLayer.NoRock"/>
		/// where the face stays terrain). Exactly what the cut computed from the spec before the template carried them.
		/// </summary>
		private static void AuthorRock(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			string who = biome.name;
			string own = CliffRocks.OwnRock(entry);
			AuthorValue(who, biome, BiomeTemplate.Authored.OwnRock, Mark(biome.OwnRock), Mark(own), false, () => biome.OwnRock = own, own, report, apply);

			var rocks = new List<string>(entry.Rocks ?? Array.Empty<string>());
			AuthorEntries(who, biome, BiomeTemplate.Authored.AcceptedRocks, biome.AcceptedRocks, rocks, NameId, r => r ?? string.Empty,
				l => Mark(l), report, apply);

			if (biome.CliffTextureLayers == null)
			{
				return;
			}
			for (int i = 0; i < entry.Cliffs.Length && i < biome.CliffTextureLayers.Count; i++)
			{
				CliffTextureLayer slot = biome.CliffTextureLayers[i];
				if (slot == null)
				{
					continue;
				}
				string rock = CliffRocks.RockTypeFor(entry.Cliffs[i].Family, entry) ?? CliffTextureLayer.NoRock;
				if (slot.rockType == rock)
				{
					if (slot.rockTypeAuthored != rock)
					{
						report.ValuesFilled++;
						report.Lines.Add($"{who} cliff/{i}: rock type '{rock}' {(apply ? "marked" : "would be marked")} as the tool's");
						if (apply)
						{
							slot.rockTypeAuthored = rock;
						}
					}
					continue;
				}
				if (!string.IsNullOrEmpty(slot.rockType) && slot.rockType != slot.rockTypeAuthored)
				{
					report.ValuesKept++;
					report.Lines.Add($"{who} cliff/{i}: rock type '{slot.rockType}' was set by hand — kept (the spec says '{rock}')");
					continue;
				}
				report.ValuesFilled++;
				report.Lines.Add($"{who} cliff/{i}: rock type {(apply ? "set to" : "would be set to")} '{rock}'");
				if (apply)
				{
					slot.rockType = rock;
					slot.rockTypeAuthored = rock;
				}
			}
		}

		/// <summary>
		/// The rock each form of cave carved in the biome is cut in, its formations included (<see cref="BiomeTemplate.CaveShells"/>),
		/// from the spec (<see cref="BiomeArtSpec.Entry.CaveShells"/>: every form with the shell the cave shaper gave it by form
		/// alone before, Ground where that was the ground's own rock, unless the biome's file names its own). One mark for the
		/// One mark per form (2026-10-16): a form's shell edited by hand is kept, one deleted by hand stays deleted (the form then
		/// keeps its old shell), and the others still follow the spec.
		/// </summary>
		private static void AuthorCaveShells(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			List<BiomeCaveShell> shells = entry.CaveShells();
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.CaveShells, biome.CaveShells, shells, s => s.Form.ToString(), s => s.ToString(),
				l => Mark(AsList(l)), report, apply);
		}

		/// <summary>
		/// The part the biome plays for the scene's water and ice (<see cref="BiomeTemplate.WaterRole"/>) from the spec
		/// (<see cref="BiomeArtSpec.Entry.WaterRole"/>: the role each biome the cut found by name played). None is written and
		/// marked too, so a role cleared by hand after the authoring wrote one is kept cleared.
		/// </summary>
		private static void AuthorWaterRole(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			BiomeWaterRole target = entry.WaterRole;
			AuthorValue(biome.name, biome, BiomeTemplate.Authored.WaterRole, Mark(biome.WaterRole), Mark(target), true,
				() => biome.WaterRole = target, target.ToString(), report, apply, biome.WaterRole == BiomeWaterRole.None);
		}

		/// <summary>A water role's mark: its name, empty for None (no role).</summary>
		public static string Mark(BiomeWaterRole role) => role == BiomeWaterRole.None ? string.Empty : role.ToString();

		/// <summary>
		/// The colour and clarity of the biome's fresh water (<see cref="BiomeTemplate.WaterLook"/>, 2026-10-16, I2e) from the
		/// spec (<see cref="BiomeArtSpec.Entry.WaterLook"/>: the inland water's own look, <see cref="BiomeWaterLooks.Today"/>,
		/// unless the biome's file gives one). One value, marked by its signature; a look edited by hand is kept.
		/// </summary>
		private static void AuthorWaterLook(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			BiomeWaterLook target = entry.WaterLook();
			string current = biome.IsAuthored(BiomeTemplate.Authored.WaterLook) ? biome.WaterLook.Signature() : string.Empty;
			AuthorValue(biome.name, biome, BiomeTemplate.Authored.WaterLook, current, target.Signature(), false,
				() => biome.WaterLook = target, $"water {target}", report, apply);
		}

		/// <summary>
		/// The sea off the biome's coast as a diver sees it (<see cref="BiomeTemplate.SeaLook"/>, 2026-10-18, I2h) from the spec
		/// (<see cref="BiomeArtSpec.Entry.SeaLook"/>: the underwater look every sea had, <see cref="BiomeSeaLooks.Today"/>, unless
		/// the biome's file gives one). One value, marked by its signature; a look edited by hand is kept.
		/// </summary>
		private static void AuthorSeaLook(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			BiomeSeaLook target = entry.SeaLook();
			string current = biome.IsAuthored(BiomeTemplate.Authored.SeaLook) ? biome.SeaLook.Signature() : string.Empty;
			AuthorValue(biome.name, biome, BiomeTemplate.Authored.SeaLook, current, target.Signature(), false,
				() => biome.SeaLook = target, $"{target}", report, apply);
		}

		/// <summary>A cave shell list's mark: a hash of its "Form=Rock" entries in order.</summary>
		public static string Mark(List<BiomeCaveShell> rocks)
		{
			var entries = new List<string>();
			if (rocks != null)
			{
				foreach (BiomeCaveShell rock in rocks)
				{
					entries.Add(rock.ToString());
				}
			}
			return Mark((IReadOnlyList<string>)entries);
		}

		/// <summary>
		/// What a point of interest grows from the biome, onto the template: a herb grove's flowers
		/// (<see cref="BiomeArtSpec.Entry.GroveFlowers"/>) and a cave mouth's bushes, ferns, creeper, boulders and deadwood
		/// (<see cref="BiomeArtSpec.Entry.CaveMouth"/>), with the defaults the sites used where the spec has none.
		/// </summary>
		private static void AuthorGrowth(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			string who = biome.name;
			AuthorPrefabs(who, biome, BiomeTemplate.Authored.HerbGroveFlowers, biome.HerbGroveFlowers, entry.GroveFlowers(), source, report, apply);
			AuthorPrefabs(who, biome, BiomeTemplate.Authored.HerbGroveHerbs, biome.HerbGroveHerbs, entry.GroveHerbs(), source, report, apply);
			BiomeArtSpec.CaveMouthGrowth mouth = entry.CaveMouth();
			AuthorPrefabs(who, biome, BiomeTemplate.Authored.CaveMouthBushes, biome.CaveMouthBushes, mouth.Bushes, source, report, apply);
			AuthorPrefabs(who, biome, BiomeTemplate.Authored.CaveMouthFerns, biome.CaveMouthFerns, mouth.Ferns, source, report, apply);
			AuthorPrefabs(who, biome, BiomeTemplate.Authored.CaveMouthBoulders, biome.CaveMouthBoulders, mouth.Boulders, source, report, apply);
			AuthorPrefabs(who, biome, BiomeTemplate.Authored.CaveMouthLogs, biome.CaveMouthLogs, mouth.Logs, source, report, apply);

			GameObject creeper = null;
			if (mouth.Creeper != null)
			{
				creeper = source.Prefab(mouth.Creeper);
				if (creeper == null)
				{
					report.Missing.Add($"{who} {BiomeTemplate.Authored.CaveMouthCreeper}: prefab {mouth.Creeper}");
				}
			}
			AuthorValue(who, biome, BiomeTemplate.Authored.CaveMouthCreeper, Mark(biome.CaveMouthCreeper), Mark(creeper), true,
				() => biome.CaveMouthCreeper = creeper, creeper != null ? creeper.name : "none", report, apply, biome.CaveMouthCreeper == null);
		}

		/// <summary>
		/// The biome's look class (<see cref="BiomeTemplate.LookClass"/>) from the spec (<see cref="BiomeArtSpec.Entry.LookClass"/>),
		/// which names for every biome the class its name gave before (2026-10-15). Unset on the template is a gap and is filled.
		/// </summary>
		private static void AuthorLooks(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			if (entry.LookClass == BiomeLookClass.Unset)
			{
				return;
			}
			BiomeLookClass target = entry.LookClass;
			string current = biome.LookClass != BiomeLookClass.Unset ? biome.LookClass.ToString() : string.Empty;
			AuthorValue(biome.name, biome, BiomeTemplate.Authored.LookClass, current, target.ToString(), false, () => biome.LookClass = target,
				target.ToString(), report, apply);
		}

		/// <summary>
		/// How each blade prefab the biome grows looks there (<see cref="BiomeTemplate.GrassTypes"/>): every blade prefab its
		/// template's rules name (whoever wrote them) and its spec's, each with the spec's look of it
		/// (<see cref="BiomeArtSpec.Entry.LookOf"/>: the old look every biome had, <see cref="BiomeGrassDefaults"/>, ground
		/// tint 0.85 included, then the biome's own tunings). One entry per prefab, each marked on its own: an entry somebody
		/// edited is kept; a missing one is a gap and is added. An entry for a prefab the biome no longer grows is removed when
		/// the tool wrote it and nobody has changed it since (2026-10-16); one made or edited by hand is always kept
		/// (<see cref="PrunableGrassTypes"/>).
		/// </summary>
		private static void AuthorGrass(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			string who = biome.name;
			var prefabs = new List<GameObject>();
			foreach (string key in BiomeLocalArt.SlotsOf(biome))
			{
				TerrainTextureLayer slot = BiomeLocalArt.CommittedLayer(biome, key);
				if (slot?.prefabSpawnRules == null)
				{
					continue;
				}
				foreach (PrefabSpawnRule rule in slot.prefabSpawnRules)
				{
					if (rule?.prefabs == null || rule.spawnChannel != PrefabSpawnChannel.DetailLayer)
					{
						continue;
					}
					foreach (GameObject prefab in rule.prefabs)
					{
						if (prefab != null && BiomeGrassDefaults.IsBlade(prefab.name) && !prefabs.Contains(prefab))
						{
							prefabs.Add(prefab);
						}
					}
				}
			}
			foreach ((string name, BiomeGrassType _) in entry.GrassTypes())
			{
				GameObject prefab = source.Prefab(name);
				if (prefab == null)
				{
					report.Missing.Add($"{who} grass type: prefab {name}");
					continue;
				}
				if (!prefabs.Contains(prefab))
				{
					prefabs.Add(prefab);
				}
			}
			foreach (GameObject prefab in prefabs)
			{
				BiomeGrassType target = entry.LookOf(prefab.name);
				if (target == null)
				{
					continue;
				}
				target.Prefab = prefab;
				BiomeGrassType current = biome.GrassTypeFor(prefab);
				AuthorValue(who, biome, BiomeTemplate.Authored.GrassType(prefab.name), current != null ? Mark(current) : string.Empty, Mark(target), false, () =>
				{
					int at = current != null ? biome.GrassTypes.IndexOf(current) : -1;
					if (at >= 0)
					{
						biome.GrassTypes[at] = target;
					}
					else
					{
						biome.GrassTypes.Add(target);
					}
				}, $"the look of {prefab.name} (ground tint {target.GroundTint:0.##})", report, apply);
			}

			// Entries the tool wrote for blades the biome no longer grows, untouched since: pruned (their mark goes with them, so
			// the prefab is a gap again should the biome grow it later).
			var entries = new List<(string Prefab, string Mark)>(biome.GrassTypes.Count);
			var grown = new HashSet<string>(StringComparer.Ordinal);
			foreach (GameObject prefab in prefabs)
			{
				grown.Add(prefab.name);
			}
			foreach (BiomeGrassType look in biome.GrassTypes)
			{
				entries.Add((look != null && look.Prefab != null ? look.Prefab.name : null, look != null ? Mark(look) : null));
			}
			List<int> prune = PrunableGrassTypes(entries, name => biome.AuthoredFingerprint(BiomeTemplate.Authored.GrassType(name)), grown);
			for (int i = prune.Count - 1; i >= 0; i--)
			{
				string prefab = entries[prune[i]].Prefab;
				report.GrassTypesPruned++;
				report.Lines.Add($"{who} {BiomeTemplate.Authored.GrassType(prefab)}: {(apply ? "removed" : "would be removed")} (the tool's entry, untouched, for a blade the biome no longer grows)");
				if (apply)
				{
					biome.GrassTypes.RemoveAt(prune[i]);
					biome.ClearAuthoredFingerprint(BiomeTemplate.Authored.GrassType(prefab));
				}
			}
		}

		/// <summary>
		/// Which of a template's Grass Types entries (each its prefab's name, or null where it has none, and its mark,
		/// <see cref="Mark(BiomeGrassType)"/>) the authoring prunes, by index in order: those whose prefab the biome no longer
		/// grows (<paramref name="grown"/>), whose mark the tool wrote (<paramref name="authoredMark"/> of the prefab's name)
		/// and still holds. An entry with no prefab, never authored, or changed since is kept; of two entries for one prefab
		/// only the first can hold the mark.
		/// </summary>
		public static List<int> PrunableGrassTypes(IReadOnlyList<(string Prefab, string Mark)> entries, Func<string, string> authoredMark, ISet<string> grown)
		{
			var prune = new List<int>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < entries.Count; i++)
			{
				(string prefab, string mark) = entries[i];
				if (string.IsNullOrEmpty(prefab) || !seen.Add(prefab) || grown.Contains(prefab))
				{
					continue;
				}
				string written = authoredMark(prefab);
				if (!string.IsNullOrEmpty(written) && written == mark)
				{
					prune.Add(i);
				}
			}
			return prune;
		}

		/// <summary>
		/// The country the biome offers ambient creatures (<see cref="BiomeTemplate.Habitat"/>) and the sea creatures at home in
		/// its water with their weights (<see cref="BiomeTemplate.SeaLife"/>), from the spec (<see cref="BiomeArtSpec.Entry.Habitat"/>,
		/// <see cref="BiomeArtSpec.Entry.SeaLife"/>: what the biome's name gave the client before, 2026-10-16, and each kind at
		/// the weight it gave its favourites). None and an empty list are written and marked too, so one cleared by hand stays
		/// cleared.
		/// </summary>
		/// <remarks>
		/// Never the Alien bit (2026-10-16, I4b): the client reads alien ground live from the template's air. A template this
		/// authoring wrote before then holds it in its habitat and in the habitat's mark; both are read without it (the habitat
		/// masks it on every read), so a habitat the tool wrote and nobody touched stays the tool's to rewrite, and a hand edit
		/// stays a hand edit; the stored bit and the old mark are cleaned as it goes.
		/// </remarks>
		private static void AuthorWildlife(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			BiomeHabitat habitat = entry.Habitat & BiomeHabitats.Stored;
			string mark = HabitatMarkWithoutAlien(biome.AuthoredFingerprint(BiomeTemplate.Authored.Habitat));
			if (apply)
			{
				if (biome.DropStoredAlien())
				{
					report.ValuesFilled++;
					report.Lines.Add($"{biome.name} {BiomeTemplate.Authored.Habitat}: dropped its stored Alien bit (read live from the template's air since 2026-10-16)");
				}
				if (mark != null && mark != biome.AuthoredFingerprint(BiomeTemplate.Authored.Habitat))
				{
					biome.SetAuthoredFingerprint(BiomeTemplate.Authored.Habitat, mark);
				}
			}
			AuthorValue(biome.name, biome, BiomeTemplate.Authored.Habitat, mark, Mark(biome.Habitat), Mark(habitat), true,
				() => biome.Habitat = habitat, habitat.ToString(), report, apply, biome.Habitat == BiomeHabitat.None);
			List<BiomeSeaLife> seaLife = entry.SeaLife();
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.SeaLife, biome.SeaLife, seaLife, s => NameId(s.Kind), s => s.ToString(),
				l => Mark(AsList(l)), report, apply);
		}

		/// <summary>
		/// A habitat mark as it reads without the Alien bit (the marks of 2026-10-16's I4 authoring named it: "Ice, Alien",
		/// "Alien"); any other mark, and no mark (null), unchanged.
		/// </summary>
		public static string HabitatMarkWithoutAlien(string mark)
		{
			if (string.IsNullOrEmpty(mark) || !Enum.TryParse(mark, out BiomeHabitat habitat))
			{
				return mark;
			}
			return (habitat & BiomeHabitat.Alien) != 0 ? Mark(habitat & BiomeHabitats.Stored) : mark;
		}

		/// <summary>
		/// The vines the biome grows (<see cref="BiomeTemplate.Vines"/>) from the spec (<see cref="BiomeArtSpec.Entry.VineGrowth"/>:
		/// the vine table's row for its name, unless its file sets its own). No vines is written and marked too.
		/// </summary>
		/// <remarks>
		/// Since 2026-10-16 (I5b) the four densities are one value (<see cref="BiomeTemplate.Authored.VineDensities"/>) and each
		/// species an entry with its own mark (<see cref="BiomeEntryMarks"/>), so a species the spec adds reaches a biome whose
		/// densities or other species were tuned by hand. A template authored before then carries one mark for the whole (the
		/// vines' signature, which spells out what was written): its densities part becomes the densities' mark and its species
		/// the species' marks.
		/// </remarks>
		private static void AuthorVines(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			BiomeVines vines = entry.VineGrowth();
			if (apply && biome.Vines == null)
			{
				biome.Vines = new BiomeVines();
			}
			BiomeVines current = biome.Vines ?? new BiomeVines();
			current.Species ??= new List<BiomeVineSpecies>();
			string whole = biome.AuthoredFingerprint(BiomeTemplate.Authored.Vines);
			List<(string Id, string Print)> written = null;
			string densityMark = biome.AuthoredFingerprint(BiomeTemplate.Authored.VineDensities);
			if (whole != null && whole != BiomeEntryMarks.PerEntry)
			{
				written = VineSpeciesWritten(whole, out string densities);
				if (densityMark == null)
				{
					densityMark = densities;
					if (apply)
					{
						// Kept even where nothing is written below, so densities zeroed by hand stay a hand edit once the old mark is gone.
						biome.SetAuthoredFingerprint(BiomeTemplate.Authored.VineDensities, densities);
					}
				}
			}
			AuthorValue(biome.name, biome, BiomeTemplate.Authored.VineDensities, densityMark, VineDensities(current), VineDensities(vines), true,
				() =>
				{
					current.Caves = vines.Caves;
					current.Ruins = vines.Ruins;
					current.Cliffs = vines.Cliffs;
					current.Trees = vines.Trees;
				}, $"vine densities {VineDensities(vines)}", report, apply, current.Caves == 0f && current.Ruins == 0f && current.Cliffs == 0f && current.Trees == 0f);
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.Vines, current.Species, vines.Species ?? new List<BiomeVineSpecies>(),
				s => NameId(s.Species), VineSpeciesPrint, _ => current.Signature(), report, apply, written);
		}

		/// <summary>The vines' densities as their mark: caves, ruins, cliffs, trees to 1/10 000 (the head of <see cref="BiomeVines.Signature"/>).</summary>
		public static string VineDensities(BiomeVines vines)
		{
			if (vines == null)
			{
				return "0,0,0,0";
			}
			return new BiomeVines { Caves = vines.Caves, Ruins = vines.Ruins, Cliffs = vines.Cliffs, Trees = vines.Trees }.Signature();
		}

		/// <summary>One vine species' print, as the vines' signature spells it: "Species:weight to 1/10 000".</summary>
		public static string VineSpeciesPrint(BiomeVineSpecies species)
			=> new BiomeVines { Species = new List<BiomeVineSpecies> { species } }.Signature().Substring("0,0,0,0|".Length);

		/// <summary>
		/// What an authoring before 2026-10-16 wrote into the vines, read back from its whole-value mark (the vines' signature):
		/// the species, each by identity and print, and the densities' part.
		/// </summary>
		public static List<(string Id, string Print)> VineSpeciesWritten(string signature, out string densities)
		{
			var written = new List<(string, string)>();
			string[] parts = (signature ?? string.Empty).Split('|');
			densities = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "0,0,0,0";
			for (int i = 1; i < parts.Length; i++)
			{
				int colon = parts[i].LastIndexOf(':');
				string name = colon >= 0 ? parts[i].Substring(0, colon) : parts[i];
				written.Add((NameId(name == "<none>" ? null : name), parts[i]));
			}
			return written;
		}

		/// <summary>
		/// The gathering resources the biome's sites draw from (<see cref="BiomeTemplate.GatheringResources"/>) from the spec
		/// (<see cref="BiomeArtSpec.Entry.GatheringResources"/>: the catalogue's rows that name the biome, unless its file sets
		/// its own). An empty list is written and marked too.
		/// </summary>
		private static void AuthorGathering(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			List<string> nodes = entry.GatheringResources();
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.GatheringResources, biome.GatheringResources, nodes, NameId, n => n ?? string.Empty,
				l => Mark(l), report, apply);
		}

		/// <summary>
		/// The points of interest the biome hosts and how they are built (2026-10-16, phase I5), from the spec: the kinds sited on
		/// its ground (<see cref="BiomeArtSpec.Entry.SiteKinds"/>, from the project's catalogue rows by the biome's name), its
		/// template variants' and styles' weights (<see cref="BiomeArtSpec.Entry.SiteVariants"/>, <see cref="BiomeArtSpec.Entry.SiteStyles"/>),
		/// its weathering (<see cref="BiomeArtSpec.Entry.Weathering"/>), its fields' crops (<see cref="BiomeArtSpec.Entry.FieldCrops"/>)
		/// and its structure palette (<see cref="BiomeArtSpec.Entry.StructurePalette"/>). One mark each; empty lists are written
		/// and marked too, so a list cleared by hand stays cleared.
		/// </summary>
		private static void AuthorSites(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply,
			IReadOnlyList<PointOfInterestTemplateSpec> handMadeTemplates = null)
		{
			string who = biome.name;
			List<BiomeSiteKind> kinds = entry.SiteKinds(SiteRules);
			AuthorEntries(who, biome, BiomeTemplate.Authored.SiteKinds, biome.SiteKinds, kinds, SiteKindId, k => k.ToString(), l => Mark(AsList(l)), report, apply);

			List<BiomeSiteVariant> variants = entry.SiteVariants(handMadeTemplates);
			AuthorEntries(who, biome, BiomeTemplate.Authored.SiteVariants, biome.SiteVariants, variants, SiteVariantId, v => v.ToString(), l => Mark(AsList(l)), report, apply);

			List<BiomeSiteStyle> styles = entry.SiteStyles();
			AuthorEntries(who, biome, BiomeTemplate.Authored.SiteStyles, biome.SiteStyles, styles, s => s.Style.ToString(), s => s.ToString(), l => Mark(AsList(l)), report, apply);

			BiomeWeathering weathering = entry.Weathering();
			string currentWeathering = biome.IsAuthored(BiomeTemplate.Authored.Weathering) ? biome.Weathering.Signature() : string.Empty;
			AuthorValue(who, biome, BiomeTemplate.Authored.Weathering, currentWeathering, weathering.Signature(), false,
				() => biome.Weathering = weathering, weathering.ToString(), report, apply);

			var crops = new List<BiomeFieldCrop>();
			foreach ((string name, float weight) in entry.FieldCrops())
			{
				GameObject prefab = source.Prefab(name);
				if (prefab == null)
				{
					report.Missing.Add($"{who} {BiomeTemplate.Authored.FieldCrops}: prefab {name}");
					continue;
				}
				crops.Add(new BiomeFieldCrop(prefab, weight, entry.FieldSurface(name), entry.FieldRiser(name), entry.FieldRows(name)));
			}
			AuthorEntries(who, biome, BiomeTemplate.Authored.FieldCrops, biome.FieldCrops, crops, c => c.Prefab != null ? c.Prefab.name : null, c => c.ToString(),
				l => Mark(AsList(l)), report, apply);

			BiomeStructurePalette palette = entry.StructurePalette();
			if (apply && biome.StructurePalette.Surfaces == null)
			{
				biome.StructurePalette.Surfaces = new List<BiomeStructureSurface>();
			}
			List<BiomeStructureSurface> surfaces = biome.StructurePalette.Surfaces ?? new List<BiomeStructureSurface>();
			AuthorEntries(who, biome, BiomeTemplate.Authored.StructurePalette, surfaces, palette.Surfaces, SurfaceId, s => s.Signature(),
				l => Mark(new BiomeStructurePalette { Surfaces = new List<BiomeStructureSurface>(l) }), report, apply);

			// What stands in a stronghold's moat (2026-10-16, I5b): one value, written and marked like any other.
			BiomeMoatWater moat = entry.MoatWater;
			AuthorValue(who, biome, BiomeTemplate.Authored.MoatWater, biome.IsAuthored(BiomeTemplate.Authored.MoatWater) ? biome.MoatWater.ToString() : string.Empty,
				moat.ToString(), false, () => biome.MoatWater = moat, $"moat water {moat}", report, apply);

			// Which of its ways are raised boardwalks (2026-10-18, I2i): one value, None unless the biome's file raises some.
			BiomeBoardwalks boardwalks = entry.Boardwalks;
			AuthorValue(who, biome, BiomeTemplate.Authored.Boardwalks, biome.IsAuthored(BiomeTemplate.Authored.Boardwalks) ? biome.Boardwalks.ToString() : string.Empty,
				boardwalks.ToString(), false, () => biome.Boardwalks = boardwalks, $"boardwalks {boardwalks}", report, apply);

			/* No flame where no world it stands on has the air to burn one (decision 41, 2026-10-18): a biome whose template admits only
			 * thin air or none is lit with the cold Glow lights in the same places; every other biome as before. */
			bool glowOnly = BiomeAir.OnlyThinAir(biome.Atmosphere);

			// How its settlements light their streets (I5b): one entry per kind.
			AuthorEntries(who, biome, BiomeTemplate.Authored.WayLights, biome.WayLights, entry.WayLights(glowOnly), l => l.Kind.ToString(), l => l.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);

			// How its caves and mines are lit (2026-10-16, I6c): one entry per place, the cave dressing's old rule by default.
			AuthorEntries(who, biome, BiomeTemplate.Authored.CaveLights, biome.CaveLights, entry.CaveLights(glowOnly), CaveLightId, l => l.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);
		}

		/// <summary>A cave-light entry's identity: its place.</summary>
		public static string CaveLightId(BiomeCaveLight light) => light.Place.ToString();

		/// <summary>A cave-crystal entry's identity: its kind.</summary>
		public static string CaveCrystalId(BiomeCaveCrystal crystal) => crystal.Kind.ToString();

		/// <summary>
		/// The crystals the biome's caves grow (<see cref="BiomeTemplate.CaveCrystals"/>, 2026-10-16, I6c) from the spec
		/// (<see cref="BiomeArtSpec.Entry.CaveCrystals"/>: selenite, what every cave grew before, unless the biome's file names its
		/// own). One mark per kind: a kind's weight edited by hand is kept, a kind deleted by hand stays deleted.
		/// </summary>
		private static void AuthorCaveCrystals(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.CaveCrystals, biome.CaveCrystals, entry.CaveCrystals(), CaveCrystalId, c => c.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);
		}

		/// <summary>A floating-plant entry's identity: its kind.</summary>
		public static string FloatingPlantId(BiomeFloatingPlant plant) => plant.Kind.ToString();

		/// <summary>
		/// The plants the biome's still water carries (<see cref="BiomeTemplate.FloatingPlants"/>, 2026-10-18, I2i) from the spec
		/// (<see cref="BiomeArtSpec.Entry.FloatingPlants"/>: none, as no water carried anything before, unless the biome's file names
		/// its own). One mark per kind: a kind's weight edited by hand is kept, a kind deleted by hand stays deleted.
		/// </summary>
		private static void AuthorFloatingPlants(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.FloatingPlants, biome.FloatingPlants, entry.FloatingPlants(), FloatingPlantId, p => p.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);
		}

		/// <summary>A linear-feature entry's identity: its kind, form and place (<see cref="BiomeLinearFeatures.Id"/>).</summary>
		public static string LinearFeatureId(BiomeLinearStyle style) => BiomeLinearFeatures.Id(style);

		/// <summary>
		/// The biome's hedges, walls, fences, tree rows, channels and clearance rows (<see cref="BiomeTemplate.LinearFeatures"/>, 2026-10-18,
		/// I2f) from the spec (<see cref="BiomeArtSpec.Entry.LinearFeatures"/>: none, as nothing was laid before, unless the biome's file
		/// names its own), each species resolved to its generated prefab (one missing is reported and left out). One mark per kind, form
		/// and place: an entry edited by hand is kept, one deleted by hand stays deleted.
		/// </summary>
		private static void AuthorLinearFeatures(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			var missing = new List<string>();
			List<BiomeLinearStyle> styles = entry.LinearFeatures(name => source.Prefab(name), missing);
			foreach (string name in missing)
			{
				report.Missing.Add($"{biome.name} {BiomeTemplate.Authored.LinearFeatures}: prefab {name}");
			}
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.LinearFeatures, biome.LinearFeatures, styles, LinearFeatureId, s => s.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);
		}

		/// <summary>An orchard entry's identity: its tree's prefab name.</summary>
		public static string OrchardId(BiomeOrchard orchard) => orchard.Tree != null ? orchard.Tree.name : null;

		/// <summary>
		/// The biome's orchards, the layout of its fields, the old terraces round its settlements and the ground it paints along its
		/// channels (<see cref="BiomeTemplate.Orchards"/>, <see cref="BiomeTemplate.FieldLayout"/>, <see cref="BiomeTemplate.AbandonedTerraces"/>,
		/// <see cref="BiomeTemplate.ChannelMargin"/>, 2026-10-18, I2j-b) from the spec: none, anywhere, none and none unless the biome's
		/// file names its own, as fields were laid before. Orchards carry a mark per tree (an entry edited by hand is kept, one deleted
		/// stays deleted); the other three a mark for the whole value.
		/// </summary>
		private static void AuthorFieldKinds(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			string who = biome.name;
			var missing = new List<string>();
			List<BiomeOrchard> orchards = entry.FieldOrchards(name => source.Prefab(name), missing);
			foreach (string name in missing)
			{
				report.Missing.Add($"{who} {BiomeTemplate.Authored.Orchards}: prefab {name}");
			}
			AuthorEntries(who, biome, BiomeTemplate.Authored.Orchards, biome.Orchards, orchards, OrchardId, o => o.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);

			BiomeFieldLayout layout = entry.FieldLayout();
			AuthorValue(who, biome, BiomeTemplate.Authored.FieldLayout, biome.IsAuthored(BiomeTemplate.Authored.FieldLayout) ? biome.FieldLayout.Signature() : string.Empty,
				layout.Signature(), false, () => biome.FieldLayout = layout, layout.ToString(), report, apply);

			BiomeAbandonedTerraces terraces = entry.OldTerraces();
			AuthorValue(who, biome, BiomeTemplate.Authored.AbandonedTerraces,
				biome.IsAuthored(BiomeTemplate.Authored.AbandonedTerraces) ? biome.AbandonedTerraces.Signature() : string.Empty,
				terraces.Signature(), false, () => biome.AbandonedTerraces = terraces, $"old terraces {terraces}", report, apply);

			BiomeChannelMargin margin = entry.WetMargin();
			AuthorValue(who, biome, BiomeTemplate.Authored.ChannelMargin, biome.IsAuthored(BiomeTemplate.Authored.ChannelMargin) ? biome.ChannelMargin.Signature() : string.Empty,
				margin.Signature(), false, () => biome.ChannelMargin = margin, $"channel margin {margin}", report, apply);
		}

		/// <summary>A landform entry's identity: its name (<see cref="BiomeLandform.Kind"/>), trimmed, in lower case.</summary>
		public static string LandformId(BiomeLandform form) => NameId(form.Kind);

		/// <summary>
		/// The landforms the cut carves into the biome's ground (<see cref="BiomeTemplate.Landforms"/>, 2026-10-18, I2g) from the spec
		/// (<see cref="BiomeArtSpec.Entry.LandformList"/>: none, as no ground was sculpted before, unless the biome's file names its own).
		/// One mark per name: an entry edited by hand is kept, one deleted by hand stays deleted. A name no preset answers that carves
		/// nothing is reported as missing.
		/// </summary>
		private static void AuthorLandforms(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			List<BiomeLandform> forms = entry.LandformList();
			foreach (BiomeLandform form in forms)
			{
				if (!form.Carves && !BiomeLandformPresets.Knows(form.Kind))
				{
					report.Missing.Add($"{biome.name} {BiomeTemplate.Authored.Landforms}: no landform preset named '{form.Kind}'");
				}
			}
			AuthorEntries(biome.name, biome, BiomeTemplate.Authored.Landforms, biome.Landforms, forms, LandformId, f => f.ToString(),
				l => Mark(AsList(l).ConvertAll(e => e.ToString())), report, apply);
		}

		/// <summary>A site-kind entry's identity: its kind.</summary>
		public static string SiteKindId(BiomeSiteKind kind) => kind.Kind.ToString();

		/// <summary>A site-variant entry's identity: its kind and its variant's name, the name ignoring case and outer spaces.</summary>
		public static string SiteVariantId(BiomeSiteVariant variant) => variant.Kind + "/" + NameId(variant.Variant);

		/// <summary>A palette entry's identity: its style and part.</summary>
		public static string SurfaceId(BiomeStructureSurface surface) => surface.Style + "/" + surface.Role;

		/// <summary>A name's identity in a list: trimmed, in lower case; null for none (an entry with no name is never touched).</summary>
		public static string NameId(string name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToLowerInvariant();

		private static List<T> AsList<T>(IReadOnlyList<T> list) => list as List<T> ?? new List<T>(list ?? Array.Empty<T>());

		/// <summary>
		/// The project's point-of-interest template assets (sorted by path, as the cut's library loads them), for
		/// <see cref="HandMadeTemplates"/>.
		/// </summary>
		public static List<PointOfInterestTemplate> ProjectPointOfInterestTemplates()
		{
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(PointOfInterestTemplate)))
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(StringComparer.Ordinal);
			var templates = new List<PointOfInterestTemplate>();
			foreach (string path in paths)
			{
				var template = AssetDatabase.LoadAssetAtPath<PointOfInterestTemplate>(path);
				if (template != null)
				{
					templates.Add(template);
				}
			}
			return templates;
		}

		/// <summary>
		/// The templates among these the point-of-interest catalogue does not write (made by hand, or left over from an older
		/// catalogue), as plain data for <see cref="BiomeArtSpec.Entry.SiteVariants"/>: kind, variant (<see cref="PointOfInterestTemplate.VariantKey"/>),
		/// weight elsewhere and biome list. Only those whose biome list names a biome at all; of two of one variant, the first.
		/// </summary>
		public static List<PointOfInterestTemplateSpec> HandMadeTemplates(IEnumerable<PointOfInterestTemplate> templates)
		{
			var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				written.Add(spec.Kind + "/" + spec.Variant);
			}
			var handMade = new List<PointOfInterestTemplateSpec>();
			if (templates == null)
			{
				return handMade;
			}
			foreach (PointOfInterestTemplate template in templates)
			{
				if (template == null || template.BiomeWeights == null || template.BiomeWeights.Count == 0)
				{
					continue;
				}
				string variant = template.VariantKey;
				if (string.IsNullOrWhiteSpace(variant) || !written.Add(template.Kind + "/" + variant))
				{
					continue;
				}
				var own = new PointOfInterestTemplateSpec { Kind = template.Kind, Variant = variant, DefaultBiomeWeight = template.DefaultBiomeWeight };
				own.BiomeWeights.AddRange(template.BiomeWeights);
				handMade.Add(own);
			}
			return handMade;
		}

		/// <summary>
		/// The point-of-interest catalogue the cut sites kinds by (the code's rows with the project's catalogue asset laid over
		/// them): its rows' biome weights are what the authoring writes onto the templates.
		/// </summary>
		private static PointOfInterestRules SiteRules => PointOfInterestCatalogue.Resolve();

		/// <summary>A site-kind list's mark: a hash of its "Kind×weight" entries in order.</summary>
		public static string Mark(List<BiomeSiteKind> kinds) => Mark(kinds?.ConvertAll(k => k.ToString()));

		/// <summary>A site-variant list's mark: a hash of its "Kind/Variant×weight" entries in order.</summary>
		public static string Mark(List<BiomeSiteVariant> variants) => Mark(variants?.ConvertAll(v => v.ToString()));

		/// <summary>A site-style list's mark: a hash of its "Style×weight" entries in order.</summary>
		public static string Mark(List<BiomeSiteStyle> styles) => Mark(styles?.ConvertAll(v => v.ToString()));

		/// <summary>A crop list's mark: a hash of its "Prefab×weight" entries in order.</summary>
		public static string Mark(List<BiomeFieldCrop> crops) => Mark(crops?.ConvertAll(c => c.ToString()));

		/// <summary>A structure palette's mark: a hash of its entries' signatures in order; empty for none.</summary>
		public static string Mark(BiomeStructurePalette palette)
			=> palette == null || palette.IsEmpty ? string.Empty : Mark(palette.Surfaces.ConvertAll(e => e.Signature()));

		/// <summary>A habitat's mark: its members' names, empty for None.</summary>
		public static string Mark(BiomeHabitat habitat) => habitat == BiomeHabitat.None ? string.Empty : habitat.ToString();

		/// <summary>A sea life list's mark: a hash of its entries (kind and exact weight) in order.</summary>
		public static string Mark(List<BiomeSeaLife> seaLife)
		{
			var entries = new List<string>();
			if (seaLife != null)
			{
				foreach (BiomeSeaLife entry in seaLife)
				{
					entries.Add(entry.ToString());
				}
			}
			return Mark(entries);
		}

		/// <summary>Vines' mark: every value (<see cref="BiomeVines.Signature"/>), empty for none set at all.</summary>
		public static string Mark(BiomeVines vines) => vines == null || vines.IsEmpty ? string.Empty : vines.Signature();

		/// <summary>
		/// The ways across each ground layer (<see cref="TerrainTextureLayer.ways"/>, decision 19, 2026-10-16): the path, road
		/// and cobble grounds the spec gives the main layer and each detail layer (<see cref="BiomeArtSpec.Layer.Ways"/>), each
		/// way marked on its own (<see cref="BiomeTemplate.Authored.GroundWay"/>). Unset in the spec, a way stays empty — the
		/// biome's own Small Path, Road or Cobbles, as every ground was before — and nothing is marked.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A detail slot's ways are written only while it holds the spec's ground, as its rules are: a corduroy road meant for
		/// a bog layer does not belong on whatever hand-made ground sits in that slot. The main layer's are written whoever
		/// painted it, as its rules are. Empty is a decision (the biome's own way): a way the tool wrote and somebody cleared
		/// stays clear; one somebody set to other art is kept.
		/// </para>
		/// </remarks>
		private static void AuthorLayerWays(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			var layers = new List<(string slot, TerrainTextureLayer ground, BiomeArtSpec.Layer spec)>();
			if (entry.Main != null)
			{
				layers.Add((BiomeLocalArt.SlotMain, biome.MainTextureLayer, entry.Main));
			}
			for (int i = 0; i < entry.Details.Length; i++)
			{
				BiomeArtSpec.Layer spec = entry.Details[i];
				TerrainTextureLayer slot = biome.DetailTextureLayers != null && i < biome.DetailTextureLayers.Count ? biome.DetailTextureLayers[i] : null;
				if (spec == null || slot == null)
				{
					continue;
				}
				if (slot.terrainLayer == null || slot.terrainLayer != source.Layer(spec.Family))
				{
					if (spec.WayPath != null || spec.WayRoad != null || spec.WayCobbles != null)
					{
						report.Lines.Add($"{biome.name} {BiomeLocalArt.DetailSlot(i)}: holds authored art, so its spec ways were not written");
					}
					continue;
				}
				layers.Add((BiomeLocalArt.DetailSlot(i), slot, spec));
			}

			foreach ((string slotName, TerrainTextureLayer ground, BiomeArtSpec.Layer spec) in layers)
			{
				if (ground == null)
				{
					continue;
				}
				foreach (string way in BiomeLocalArt.WaySlots)
				{
					string key = BiomeLocalArt.WaySlot(slotName, way);
					string field = BiomeTemplate.Authored.GroundWay(key);
					string family = spec.WayGround(way);
					TerrainLayer target = family != null ? source.Layer(family) : null;
					if (family != null && target == null)
					{
						report.Missing.Add($"{biome.name} {key}: Ground_{family}");
						continue;
					}
					string current = WayMark(BiomeLocalArt.WayGround(ground.ways, way));
					string wanted = WayMark(target);
					if (current.Length == 0 && wanted.Length == 0 && biome.AuthoredFingerprint(field) == null)
					{
						// Nothing set, nothing wanted, nothing written before: the biome's own way, with no mark to record.
						continue;
					}
					TerrainTextureLayer on = ground;
					AuthorValue(biome.name, biome, field, current, wanted, true, () => BiomeLocalArt.SetWayGround(ref on.ways, way, target),
						target != null ? $"{key} Ground_{family}" : $"{key} the biome's own", report, apply, isEmpty: current.Length == 0);
				}
			}
		}

		/// <summary>
		/// The climate variants the spec declares (<see cref="BiomeArtSpec.Entry.ClimateVariant"/>) and the ground overrides it gives
		/// them (<see cref="BiomeArtSpec.Entry.ClimateGround"/>), decision 29, 2026-10-18. Nothing declared, nothing written: every
		/// template's variants stay exactly as they are.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Variants</b>, one mark each (<see cref="BiomeTemplate.Authored.ClimateVariant"/>, its envelope's print): one the
		/// template lacks is added (name, envelope, texture falloff; no names of its own, which a round gives by hand or in a later
		/// column) unless the tool added it once and somebody deleted it since; one still as the tool left it follows the spec's
		/// envelope; one made or edited by hand is kept. Never removed.
		/// </para>
		/// <para>
		/// <b>Overrides</b>, slot by slot (key BiomeLocalArt.VariantSlot): filled and given their rules exactly as a detail slot is
		/// (<see cref="Fill"/>: an empty slot or the tool's generated art; <see cref="AddRules"/> while it holds the spec's ground),
		/// the list padded with empty layers (which keep the biome's own ground) up to the slot.
		/// </para>
		/// </remarks>
		private static void AuthorClimateVariants(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			string who = biome.name;
			foreach (BiomeArtSpec.Entry.VariantSpec spec in entry.Variants)
			{
				string key = BiomeClimateVariant.Normalize(spec.Name);
				if (key.Length == 0)
				{
					report.Lines.Add($"{who}: a climate variant in the spec has no name — skipped");
					continue;
				}
				string field = BiomeTemplate.Authored.ClimateVariant(key);
				string mark = biome.AuthoredFingerprint(field);
				string target = VariantMark(spec.Name, spec.Temperature, spec.Humidity, spec.TextureFalloff);
				BiomeClimateVariant existing = biome.FindOwnVariant(key);
				if (existing == null)
				{
					if (mark != null)
					{
						report.ValuesKept++;
						report.Lines.Add($"{who} {field}: deleted by hand since the tool added it — stays deleted");
						continue;
					}
					report.ValuesFilled++;
					report.Lines.Add($"{who} {field}: {(apply ? "added" : "would add")} climate variant '{spec.Name}' (t {spec.Temperature.x:0.##} … {spec.Temperature.y:0.##}, h {spec.Humidity.x:0.##} … {spec.Humidity.y:0.##})");
					if (apply)
					{
						biome.ClimateVariants ??= new List<BiomeClimateVariant>();
						biome.ClimateVariants.Add(new BiomeClimateVariant
						{
							Name = spec.Name,
							MinTemperature = spec.Temperature.x,
							MaxTemperature = spec.Temperature.y,
							MinHumidity = spec.Humidity.x,
							MaxHumidity = spec.Humidity.y,
							TextureFalloff = spec.TextureFalloff,
						});
						biome.SetAuthoredFingerprint(field, target);
					}
					continue;
				}
				BiomeClimateVariant on = existing;
				AuthorValue(who, biome, field, VariantMark(existing), target, false, () =>
				{
					on.MinTemperature = spec.Temperature.x;
					on.MaxTemperature = spec.Temperature.y;
					on.MinHumidity = spec.Humidity.x;
					on.MaxHumidity = spec.Humidity.y;
					on.TextureFalloff = spec.TextureFalloff;
				}, $"climate variant '{spec.Name}' envelope", report, apply);
			}

			foreach (BiomeArtSpec.Entry.VariantGrounds grounds in entry.VariantLayers)
			{
				BiomeClimateVariant variant = biome.FindOwnVariant(grounds.Variant);
				int index = variant != null ? biome.ClimateVariants.IndexOf(variant) : -1;
				if (variant == null)
				{
					bool declared = entry.Variants.Exists(v => BiomeClimateVariant.Normalize(v.Name) == BiomeClimateVariant.Normalize(grounds.Variant));
					report.Lines.Add(declared && !apply
						? $"{who}: climate variant '{grounds.Variant}' would be added first; its grounds are filled when applied"
						: $"{who}: the spec gives climate variant '{grounds.Variant}' grounds, but the template has no such variant — skipped (declare it with ClimateVariant)");
					continue;
				}
				string id = BiomeLocalArt.VariantId(variant, index);
				for (int g = 0; g < grounds.Layers.Length; g++)
				{
					BiomeArtSpec.Layer spec = grounds.Layers[g];
					if (spec == null)
					{
						continue;
					}
					string key = BiomeLocalArt.VariantSlot(id, g);
					TerrainTextureLayer slot = variant.TextureOverrideFor(g);
					if (slot == null)
					{
						if (source.Layer(spec.Family) == null)
						{
							report.Missing.Add($"{who} {key}: Ground_{spec.Family}");
							continue;
						}
						slot = new TerrainTextureLayer();
						if (apply)
						{
							variant.TextureLayerOverrides ??= new List<TerrainTextureLayer>();
							while (variant.TextureLayerOverrides.Count <= g)
							{
								variant.TextureLayerOverrides.Add(new TerrainTextureLayer());
							}
							variant.TextureLayerOverrides[g] = slot;
						}
					}
					bool fillable = IsFillable(slot, source.Layer(spec.Family), IsGenerated);
					// The main layer's override is a ground slot like a detail: its noise and bands are the spec's.
					Fill(who, key, slot, spec.Family, spec, source, report, apply);
					if (slot.terrainLayer != null && slot.terrainLayer == source.Layer(spec.Family) || !apply && fillable)
					{
						AddRules(who, key, slot, spec, source, report, apply);
					}
					else if (spec.Scatter.Count > 0)
					{
						report.Lines.Add($"{who} {key}: holds authored art, so its spec rules ({spec.Scatter.Count}) were not added");
					}
				}
			}
		}

		/// <summary>A climate variant's mark: its name and envelope (and texture falloff) to 1/10 000.</summary>
		public static string VariantMark(BiomeClimateVariant variant)
		{
			return variant == null ? string.Empty
				: VariantMark(variant.Name, new Vector2(variant.MinTemperature, variant.MaxTemperature), new Vector2(variant.MinHumidity, variant.MaxHumidity), variant.TextureFalloff);
		}

		/// <summary>A climate variant's mark from its parts.</summary>
		public static string VariantMark(string name, Vector2 temperature, Vector2 humidity, float textureFalloff)
		{
			return $"{BiomeClimateVariant.Normalize(name)}|t{Q(temperature.x)},{Q(temperature.y)}|h{Q(humidity.x)},{Q(humidity.y)}|f{Q(textureFalloff)}";
		}

		/// <summary>
		/// Each ground layer's climate band (<see cref="TerrainTextureLayer.useClimateBand"/>) from the spec (<see cref="BiomeArtSpec.Layer.Temp"/>,
		/// <see cref="BiomeArtSpec.Layer.Humid"/>), decision 29, 2026-10-18: the main layer always, a detail slot or a climate variant's
		/// override while it holds the spec's ground (as their rules are). One mark per slot
		/// (<see cref="BiomeTemplate.Authored.GroundClimate"/>); no band is a decision too (a band the tool wrote and somebody switched
		/// off stays off). With no band in the spec and none on the layer nothing is written or marked: every layer paints as before.
		/// </summary>
		/// <summary>
		/// The biome's extra bed layers by depth (decision 35 (b), 2026-10-18; <see cref="BiomeArtSpec.Entry.BedLayer"/>): each spec bed
		/// fills slot <c>bed/i</c> (the template's list grown to it, at most <see cref="BiomeTemplate.MaximumBedLayers"/>) with its
		/// ground as an empty slot is filled, adds its rules as a detail layer's are (only while the slot holds the spec's art), and
		/// writes its bands (waters, depth, current) under one mark a slot (<see cref="BiomeTemplate.Authored.BedBands"/>): written
		/// while untouched or still at a new layer's defaults, a hand edit kept. A spec with no beds writes nothing.
		/// </summary>
		private static void AuthorBedLayers(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			string who = biome.name;
			if (biome.BedTextureLayers != null)
			{
				for (int i = 0; i < biome.BedTextureLayers.Count; i++)
				{
					DisableDeadRules(who, BiomeLocalArt.BedSlot(i), biome.BedTextureLayers[i], report, apply);
				}
			}
			int count = Mathf.Min(entry.BedLayers.Count, BiomeTemplate.MaximumBedLayers);
			for (int i = 0; i < count; i++)
			{
				BiomeArtSpec.Entry.BedSpec spec = entry.BedLayers[i];
				string slotName = BiomeLocalArt.BedSlot(i);
				BedTextureLayer slot = biome.BedTextureLayers != null && i < biome.BedTextureLayers.Count ? biome.BedTextureLayers[i] : null;
				if (slot == null)
				{
					if (source.Layer(spec.Layer.Family) == null)
					{
						report.Missing.Add($"{who} {slotName}: Ground_{spec.Layer.Family}");
						continue;
					}
					slot = new BedTextureLayer();
					if (apply)
					{
						while (biome.BedTextureLayers.Count < i)
						{
							biome.BedTextureLayers.Add(new BedTextureLayer());
						}
						if (biome.BedTextureLayers.Count == i)
						{
							biome.BedTextureLayers.Add(slot);
						}
						else
						{
							biome.BedTextureLayers[i] = slot;
						}
					}
				}
				bool fillable = IsFillable(slot, source.Layer(spec.Layer.Family), IsGenerated);
				Fill(who, slotName, slot, spec.Layer.Family, null, source, report, apply);
				if (slot.terrainLayer != null && slot.terrainLayer == source.Layer(spec.Layer.Family) || !apply && fillable)
				{
					AddRules(who, slotName, slot, spec.Layer, source, report, apply);
					BedTextureLayer on = slot;
					string current = BedMark(slot);
					string wanted = BedMark(spec);
					AuthorValue(who, biome, BiomeTemplate.Authored.BedBands(slotName), current, wanted, false, () => WriteBedBands(on, spec),
						$"{slotName} bands {wanted}", report, apply, isEmpty: current == BedMark(new BedTextureLayer()));
				}
				else if (spec.Layer.Scatter.Count > 0)
				{
					report.Lines.Add($"{who} {slotName}: holds authored art, so its spec rules ({spec.Layer.Scatter.Count}) and bands were not written");
				}
			}
		}

		/// <summary>Writes a spec bed's bands onto a template bed layer.</summary>
		public static void WriteBedBands(BedTextureLayer layer, BiomeArtSpec.Entry.BedSpec spec)
		{
			layer.waters = spec.Waters;
			layer.depthRange = new Vector2(Mathf.Max(0f, spec.Depth.x), Mathf.Max(0f, spec.Depth.y));
			layer.depthFalloff = Mathf.Max(0f, spec.DepthFalloff);
			layer.useCurrentBand = spec.Current.HasValue;
			layer.currentRange = spec.Current.HasValue ? new Vector2(Mathf.Max(0f, spec.Current.Value.x), Mathf.Max(0f, spec.Current.Value.y)) : Vector2.zero;
			layer.currentFalloff = Mathf.Max(0f, spec.CurrentFalloff);
		}

		/// <summary>A bed layer's bands' mark: its waters, depth band and current band to 1/10 000.</summary>
		public static string BedMark(BedTextureLayer layer)
		{
			if (layer == null)
			{
				return string.Empty;
			}
			return BedMark(layer.waters, layer.depthRange, layer.depthFalloff, layer.useCurrentBand ? layer.currentRange : (Vector2?)null, layer.currentFalloff);
		}

		/// <summary>A spec bed's bands' mark (<see cref="BedMark(BedTextureLayer)"/>'s form).</summary>
		public static string BedMark(BiomeArtSpec.Entry.BedSpec spec)
		{
			if (spec == null)
			{
				return string.Empty;
			}
			return BedMark(spec.Waters, new Vector2(Mathf.Max(0f, spec.Depth.x), Mathf.Max(0f, spec.Depth.y)), Mathf.Max(0f, spec.DepthFalloff),
				spec.Current.HasValue ? new Vector2(Mathf.Max(0f, spec.Current.Value.x), Mathf.Max(0f, spec.Current.Value.y)) : (Vector2?)null, Mathf.Max(0f, spec.CurrentFalloff));
		}

		private static string BedMark(BiomeBedWaters waters, Vector2 depth, float depthFalloff, Vector2? current, float currentFalloff)
		{
			// The current's falloff means nothing without a current band, so it is not in a mark that has none.
			string c = current.HasValue ? $"c{Q(current.Value.x)},{Q(current.Value.y)},{Q(currentFalloff)}" : "c-";
			return $"w{(int)waters}|d{Q(depth.x)},{Q(depth.y)},{Q(depthFalloff)}|{c}";
		}

		private static void AuthorLayerClimate(BiomeTemplate biome, BiomeArtSpec.Entry entry, IBiomeArtSource source, Report report, bool apply)
		{
			var layers = new List<(string slot, TerrainTextureLayer ground, BiomeArtSpec.Layer spec)>();
			if (entry.Main != null)
			{
				layers.Add((BiomeLocalArt.SlotMain, biome.MainTextureLayer, entry.Main));
			}
			void Spec(string slotName, TerrainTextureLayer slot, BiomeArtSpec.Layer spec)
			{
				if (spec == null || slot == null)
				{
					return;
				}
				if (slot.terrainLayer == null || slot.terrainLayer != source.Layer(spec.Family))
				{
					if (spec.HasClimateBand || spec.AirBand != BiomeAtmosphereRequirement.None || spec.HeatBand.HasValue)
					{
						report.Lines.Add($"{biome.name} {slotName}: holds authored art, so its spec climate/air band was not written");
					}
					return;
				}
				layers.Add((slotName, slot, spec));
			}
			for (int i = 0; i < entry.Details.Length; i++)
			{
				Spec(BiomeLocalArt.DetailSlot(i), biome.DetailTextureLayers != null && i < biome.DetailTextureLayers.Count ? biome.DetailTextureLayers[i] : null, entry.Details[i]);
			}
			/* Cliffs carry an air band only (decision 44, 2026-10-18: one biome's grey and rust scarps); their climate fields stay at
			 * the defaults the write leaves them at. A cliff spec without one, on a cliff without one, writes and marks nothing. */
			for (int i = 0; i < entry.Cliffs.Length; i++)
			{
				BiomeArtSpec.Cliff cliff = entry.Cliffs[i];
				Spec(BiomeLocalArt.SlotCliff + i, biome.CliffTextureLayers != null && i < biome.CliffTextureLayers.Count ? biome.CliffTextureLayers[i] : null,
					cliff != null ? new BiomeArtSpec.Layer { Family = cliff.Family, AirBand = cliff.AirBand } : null);
			}
			foreach (BiomeArtSpec.Entry.VariantGrounds grounds in entry.VariantLayers)
			{
				BiomeClimateVariant variant = biome.FindOwnVariant(grounds.Variant);
				if (variant == null)
				{
					continue;
				}
				string id = BiomeLocalArt.VariantId(variant, biome.ClimateVariants.IndexOf(variant));
				for (int g = 0; g < grounds.Layers.Length; g++)
				{
					Spec(BiomeLocalArt.VariantSlot(id, g), variant.TextureOverrideFor(g), grounds.Layers[g]);
				}
			}

			foreach ((string slotName, TerrainTextureLayer ground, BiomeArtSpec.Layer spec) in layers)
			{
				if (ground == null)
				{
					continue;
				}
				string field = BiomeTemplate.Authored.GroundClimate(slotName);
				string current = ClimateMark(ground);
				string wanted = ClimateMark(spec);
				if (current.Length == 0 && wanted.Length == 0 && biome.AuthoredFingerprint(field) == null)
				{
					// No band set, none wanted, none written before: the layer paints as it always did, with no mark to record.
					continue;
				}
				TerrainTextureLayer on = ground;
				AuthorValue(biome.name, biome, field, current, wanted, true, () => WriteClimateBand(on, spec),
					wanted.Length > 0 ? $"{slotName} climate band {wanted}" : $"{slotName} no climate band", report, apply, isEmpty: current.Length == 0);
			}
		}

		/// <summary>Writes a spec layer's climate band onto a template layer (off, ranges open, when the spec has none).</summary>
		public static void WriteClimateBand(TerrainTextureLayer layer, BiomeArtSpec.Layer spec)
		{
			// The air band rides with the climate's (decision 44): the same slot, the same mark.
			layer.airBand = spec != null ? spec.AirBand : BiomeAtmosphereRequirement.None;
			layer.useClimateBand = spec != null && spec.HasClimateBand;
			layer.temperatureRange = spec?.Temperature ?? new Vector2(-1f, 1f);
			layer.humidityRange = spec?.Humidity ?? new Vector2(-1f, 1f);
			layer.temperatureFalloff = spec != null ? spec.TemperatureFalloff : BiomeClimateBand.DefaultFalloff;
			layer.humidityFalloff = spec != null ? spec.HumidityFalloff : BiomeClimateBand.DefaultFalloff;
			// And the heat band (2026-10-18, I2q), riding the same mark.
			layer.useHeatBand = spec != null && spec.HeatBand.HasValue;
			layer.heatRange = spec?.HeatBand ?? new Vector2(0f, 1f);
			layer.heatFalloff = spec != null && spec.HeatBand.HasValue ? spec.HeatFalloff : BiomeHeatBand.DefaultFalloff;
		}

		/// <summary>A layer's climate band's mark: empty for none (off, or open at every end), else its ranges and falloffs to 1/10 000.</summary>
		public static string ClimateMark(TerrainTextureLayer layer)
		{
			if (layer == null)
			{
				return string.Empty;
			}
			string climate = layer.HasClimateBand ? ClimateMark(layer.temperatureRange, layer.temperatureFalloff, layer.humidityRange, layer.humidityFalloff) : string.Empty;
			return WithHeat(WithAir(climate, layer.airBand), layer.HasHeatBand ? layer.heatRange : (Vector2?)null, layer.heatFalloff);
		}

		/// <summary>
		/// A climate mark with a layer's air band after it ("|a" and the mask, decision 44), the climate part "t-|h-" when it has
		/// none; unchanged without an air band, so every mark written before air bands existed reads the same.
		/// </summary>
		private static string WithAir(string climate, BiomeAtmosphereRequirement air)
		{
			if (air == BiomeAtmosphereRequirement.None)
			{
				return climate;
			}
			return (climate.Length > 0 ? climate : "t-|h-") + "|a" + (int)air;
		}

		/// <summary>A spec layer's climate band's mark (<see cref="ClimateMark(TerrainTextureLayer)"/>'s form).</summary>
		public static string ClimateMark(BiomeArtSpec.Layer spec)
		{
			if (spec == null)
			{
				return string.Empty;
			}
			string climate = spec.HasClimateBand
				? ClimateMark(spec.Temperature ?? new Vector2(-1f, 1f), spec.TemperatureFalloff, spec.Humidity ?? new Vector2(-1f, 1f), spec.HumidityFalloff)
				: string.Empty;
			return WithHeat(WithAir(climate, spec.AirBand), spec.HeatBand, spec.HeatFalloff);
		}

		/// <summary>
		/// A climate mark with a layer's heat band after it ("|q" low, high, falloff, 2026-10-18, I2q), the climate part "t-|h-" when it
		/// has none; unchanged without a heat band, so every mark written before heat bands existed reads the same.
		/// </summary>
		private static string WithHeat(string mark, Vector2? heat, float falloff)
		{
			if (!heat.HasValue || !BiomeHeatBand.Closes(heat.Value))
			{
				return mark;
			}
			Vector2 h = heat.Value;
			return (mark.Length > 0 ? mark : "t-|h-") + $"|q{Q(Mathf.Min(h.x, h.y))},{Q(Mathf.Max(h.x, h.y))},{Q(falloff)}";
		}

		private static string ClimateMark(Vector2 temperature, float temperatureFalloff, Vector2 humidity, float humidityFalloff)
		{
			// An open axis reads the same whatever its falloff: the falloff of an axis that bands nothing changes nothing.
			string t = BiomeClimateBand.Closes(temperature) ? $"t{Q(Mathf.Min(temperature.x, temperature.y))},{Q(Mathf.Max(temperature.x, temperature.y))},{Q(temperatureFalloff)}" : "t-";
			string h = BiomeClimateBand.Closes(humidity) ? $"h{Q(Mathf.Min(humidity.x, humidity.y))},{Q(Mathf.Max(humidity.x, humidity.y))},{Q(humidityFalloff)}" : "h-";
			return t + "|" + h;
		}

		/// <summary>
		/// The template's Description from the spec's column (<see cref="BiomeArtSpec.Entry.Describe"/>, decision 23): one whole value,
		/// marked by its text. No column, nothing written. A description never authored is the one the template was made with, and
		/// a round that writes the column means to replace it, so it is written over once; after that, a hand edit is kept.
		/// </summary>
		private static void AuthorDescription(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			if (entry.Description == null)
			{
				return;
			}
			string target = entry.Description;
			AuthorText(biome, BiomeTemplate.Authored.Description, Mark(biome.Description), Mark(target), () => biome.Description = target,
				$"description \"{Shorten(target)}\"", report, apply);
		}

		/// <summary>
		/// What the ground gives the air (<see cref="BiomeTemplate.Emits"/>, <see cref="BiomeTemplate.EmissionRate"/>) from the spec's
		/// column (<see cref="BiomeArtSpec.Entry.Emits"/>, 2026-10-18, I2o): one whole value, marked "substance@rate" ("none" for
		/// nothing), under the description's rule (the weather content generator's first writing is written over once, a hand edit
		/// kept after). No column, nothing written. A substance the project lacks is reported missing and nothing is written.
		/// </summary>
		private static void AuthorEmission(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			if (!entry.Emission.HasValue)
			{
				return;
			}
			(string substance, float rate) = entry.Emission.Value;
			FishMMO.Shared.Weather.WeatherSubstance target = null;
			if (substance != null)
			{
				target = SubstanceNamed(substance);
				if (target == null && apply)
				{
					/* A shipped substance the project lacks is made now (2026-10-18, I2q): Steam was added to the weather content
					 * generator's table after the substances were first created, so no geyser basin had any sites. */
					target = WeatherContentGenerator.EnsureShipped(substance);
				}
				if (target == null)
				{
					report.Missing.Add($"{biome.name} {BiomeTemplate.Authored.Emission}: weather substance {substance}" +
						(apply ? string.Empty : " (Author creates it when the weather content generator ships one of that name)"));
					return;
				}
			}
			string current = EmissionMark(biome.Emits != null ? biome.Emits.name : null, biome.Emits != null ? biome.EmissionRate : 0f);
			string wanted = EmissionMark(substance, rate);
			AuthorText(biome, BiomeTemplate.Authored.Emission, current, wanted, () =>
			{
				biome.Emits = target;
				biome.EmissionRate = target != null ? rate : 0f;
			}, $"emits {wanted}", report, apply);
		}

		/// <summary>
		/// How the biome's hot ground divides among geysers, hot springs, fumaroles and mud pots (<see cref="BiomeTemplate.Geothermal"/>)
		/// from the spec's column (<see cref="BiomeArtSpec.Entry.Geothermal"/>, 2026-10-18, I2q): one whole value marked by its
		/// signature, under the emission's rule (the template's original, today's split, is written over once; a hand edit kept
		/// after). No column, nothing written: every template keeps today's split unmarked.
		/// </summary>
		private static void AuthorGeothermal(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			if (!entry.GeothermalShares.HasValue)
			{
				return;
			}
			BiomeGeothermalShares target = entry.GeothermalShares.Value;
			AuthorText(biome, BiomeTemplate.Authored.Geothermal, biome.Geothermal.Signature(), target.Signature(), () => biome.Geothermal = target,
				$"hot ground {target}", report, apply);
		}

		/// <summary>An emission's mark: "substance@rate" (the rate to 1/10 000), "none" for nothing.</summary>
		public static string EmissionMark(string substance, float rate)
		{
			return string.IsNullOrEmpty(substance) ? "none" : substance + "@" + Q(rate).ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>The project's weather substance of that asset name, or null.</summary>
		private static FishMMO.Shared.Weather.WeatherSubstance SubstanceNamed(string name)
		{
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FishMMO.Shared.Weather.WeatherSubstance)))
			{
				var substance = AssetDatabase.LoadAssetAtPath<FishMMO.Shared.Weather.WeatherSubstance>(AssetDatabase.GUIDToAssetPath(guid));
				if (substance != null && substance.name == name)
				{
					return substance;
				}
			}
			return null;
		}

		/// <summary>
		/// The parts of the naming block the spec's column rewrites (<see cref="BiomeArtSpec.Entry.Names"/>, decision 23): each part
		/// a whole value with its own mark (<see cref="BiomeTemplate.Authored.NamingPart"/>), under the description's rule (written
		/// over once, a hand edit kept after). A part left unset is not touched.
		/// </summary>
		private static void AuthorNaming(BiomeTemplate biome, BiomeArtSpec.Entry entry, Report report, bool apply)
		{
			BiomeArtSpec.NamingColumns naming = entry.Naming;
			if (naming == null || naming.IsEmpty)
			{
				return;
			}
			biome.Naming ??= new BiomeNamingData();
			biome.Naming.Phonology ??= new SerializableBiomePhonology();
			SerializableBiomePhonology ph = biome.Naming.Phonology;
			bool wrote = false;
			void Words(string part, string[] target, Func<string[]> read, Action<string[]> write)
			{
				if (target == null)
				{
					return;
				}
				string[] copy = (string[])target.Clone();
				wrote |= AuthorText(biome, BiomeTemplate.Authored.NamingPart(part), Mark(read()), Mark(copy), () => write(copy),
					$"naming {part} ({copy.Length}: {Shorten(string.Join(", ", copy))})", report, apply);
			}
			void Number(string part, int? target, Func<int> read, Action<int> write)
			{
				if (target == null)
				{
					return;
				}
				int value = target.Value;
				wrote |= AuthorText(biome, BiomeTemplate.Authored.NamingPart(part), read().ToString(CultureInfo.InvariantCulture),
					value.ToString(CultureInfo.InvariantCulture), () => write(value), $"naming {part} {value}", report, apply);
			}
			Words(nameof(ph.Onsets), naming.Onsets, () => ph.Onsets, v => ph.Onsets = v);
			Words(nameof(ph.Nuclei), naming.Nuclei, () => ph.Nuclei, v => ph.Nuclei = v);
			Words(nameof(ph.Codas), naming.Codas, () => ph.Codas, v => ph.Codas = v);
			Words(nameof(ph.Middles), naming.Middles, () => ph.Middles, v => ph.Middles = v);
			Number(nameof(ph.SyllMin), naming.SyllablesMin, () => ph.SyllMin, v => ph.SyllMin = v);
			Number(nameof(ph.SyllMax), naming.SyllablesMax, () => ph.SyllMax, v => ph.SyllMax = v);
			Words(nameof(ph.DungeonSuffixes), naming.DungeonSuffixes, () => ph.DungeonSuffixes, v => ph.DungeonSuffixes = v);
			Words(nameof(ph.DungeonPrefixes), naming.DungeonPrefixes, () => ph.DungeonPrefixes, v => ph.DungeonPrefixes = v);
			Words(nameof(ph.POISuffixes), naming.POISuffixes, () => ph.POISuffixes, v => ph.POISuffixes = v);
			Words(nameof(ph.Adjectives), naming.Adjectives, () => ph.Adjectives, v => ph.Adjectives = v);
			if (naming.POIWords != null)
			{
				List<StringListMapping> rows = PoiWordRows(naming.POIWords);
				wrote |= AuthorText(biome, BiomeTemplate.Authored.NamingPart(nameof(ph.POITypeWords)), PoiWordsMark(ph.POITypeWords), PoiWordsMark(rows),
					() => ph.POITypeWords = rows, $"naming {nameof(ph.POITypeWords)} ({rows.Count} kinds)", report, apply);
			}
			if (naming.About != null)
			{
				string about = naming.About;
				wrote |= AuthorText(biome, BiomeTemplate.Authored.NamingPart(nameof(ph.Description)), Mark(ph.Description), Mark(about),
					() => ph.Description = about, $"naming description \"{Shorten(about)}\"", report, apply);
			}
			if (wrote && apply)
			{
				// The runtime view is built once and cached: rebuilt so this session names with what was just written.
				biome.Naming.BuildRuntime();
			}
		}

		/// <summary>
		/// <see cref="AuthorValue(string, BiomeTemplate, string, string, string, bool, Action, string, Report, bool, bool)"/> for a
		/// text column (description, naming): a value never authored counts as untouched (the template's original text, which the
		/// round's column replaces); once written, the usual contract. True when it wrote (or would write) the value.
		/// </summary>
		private static bool AuthorText(BiomeTemplate biome, string field, string current, string target, Action write, string describe, Report report, bool apply)
		{
			string mark = biome.AuthoredFingerprint(field);
			bool writes = current != target && (mark == null || current == mark);
			// Never authored: the template's original text stands in for the mark, so it reads as untouched and is written over
			// (when it already equals the spec's, no stand-in, and it is only marked).
			string asMark = mark ?? (current == target ? null : current);
			AuthorValue(biome.name, biome, field, asMark, current, target, false, write, describe, report, apply);
			return writes;
		}

		/// <summary>A word list's mark (<see cref="Mark(IReadOnlyList{string})"/>): a hash of its words in order; null and empty alike.</summary>
		public static string Mark(string[] words) => Mark((IReadOnlyList<string>)(words ?? Array.Empty<string>()));

		/// <summary>The spec's point-of-interest words as the template's rows, in the spec's order, each list copied.</summary>
		public static List<StringListMapping> PoiWordRows((string Kind, string[] Words)[] words)
		{
			var rows = new List<StringListMapping>();
			if (words != null)
			{
				foreach ((string kind, string[] list) in words)
				{
					rows.Add(new StringListMapping(kind, list != null ? (string[])list.Clone() : Array.Empty<string>()));
				}
			}
			return rows;
		}

		/// <summary>A point-of-interest word table's mark: a hash of "kind=word|word" rows in order.</summary>
		public static string PoiWordsMark(List<StringListMapping> rows)
		{
			var lines = new List<string>();
			if (rows != null)
			{
				foreach (StringListMapping row in rows)
				{
					lines.Add(row == null ? "<none>" : (row.Key ?? string.Empty) + "=" + string.Join("|", row.Values ?? Array.Empty<string>()));
				}
			}
			return Mark((IReadOnlyList<string>)lines);
		}

		private static string Shorten(string text) => text == null ? string.Empty : text.Length <= 60 ? text : text.Substring(0, 57) + "...";

		/// <summary>A way ground's mark: the terrain layer's name (generated grounds have fixed, unique names), empty for the biome's own.</summary>
		public static string WayMark(TerrainLayer ground) => ground != null ? ground.name : string.Empty;

		/// <summary>
		/// What each slot's ground is made of (<see cref="TerrainTextureLayer.GroundMakeup"/>), for every slot holding a generated
		/// ground (the layer the art source gives for its name): the ground's own (<see cref="SurfaceCatalogue.TryMakeupOf"/>).
		/// Hand-made art is left for somebody to describe; a makeup somebody typed is kept.
		/// </summary>
		private static void AuthorMakeup(BiomeTemplate biome, IBiomeArtSource source, Report report, bool apply)
		{
			foreach (string key in BiomeLocalArt.SlotsOf(biome))
			{
				TerrainTextureLayer slot = BiomeLocalArt.CommittedLayer(biome, key);
				TerrainLayer layer = slot?.terrainLayer;
				if (layer == null || !layer.name.StartsWith(GroundLayerPrefix, StringComparison.Ordinal))
				{
					continue;
				}
				string ground = layer.name.Substring(GroundLayerPrefix.Length);
				if (source.Layer(ground) != layer || !SurfaceCatalogue.TryMakeupOf(ground, out Vector3 makeup))
				{
					continue;
				}
				AuthorValue(biome.name, biome, BiomeTemplate.Authored.GroundMakeup(key), slot.HasGroundMakeup ? Mark(slot.GroundMakeup) : string.Empty,
					Mark(makeup), false, () => slot.GroundMakeup = makeup, $"{key} ground makeup {Mark(makeup)} ({ground})", report, apply);
			}
		}

		/// <summary>The generated ground layers' name prefix (<c>Ground_&lt;Name&gt;</c>).</summary>
		private const string GroundLayerPrefix = "Ground_";

		/// <summary>A blade look's mark: its prefab's name and every value (<see cref="BiomeGrassType.Signature"/>).</summary>
		public static string Mark(BiomeGrassType look) => look == null ? string.Empty : (look.Prefab != null ? look.Prefab.name : "<none>") + "|" + look.Signature();

		/// <summary>A ground makeup's mark: living, lichen, mineral to 1/10 000.</summary>
		public static string Mark(Vector3 makeup) => $"{Q(makeup.x)},{Q(makeup.y)},{Q(makeup.z)}";

		private static void AuthorPrefabs(string who, BiomeTemplate biome, string field, List<GameObject> list, List<string> names,
			IBiomeArtSource source, Report report, bool apply)
		{
			var prefabs = new List<GameObject>();
			foreach (string name in names)
			{
				GameObject prefab = source.Prefab(name);
				if (prefab == null)
				{
					report.Missing.Add($"{who} {field}: prefab {name}");
					continue;
				}
				if (!prefabs.Contains(prefab))
				{
					prefabs.Add(prefab);
				}
			}
			AuthorEntries(who, biome, field, list, prefabs, p => p != null ? p.name : null, p => p != null ? p.name : string.Empty,
				l => Mark(AsList(l)), report, apply);
		}

		/// <summary>
		/// Writes one template LIST from the spec under the per-entry contract (<see cref="BiomeEntryMarks"/>, 2026-10-16, I5b):
		/// the spec's entries the list lacks are added (unless the tool wrote one and somebody deleted it), the tool's untouched
		/// entries are brought up to the spec's (or removed when the spec dropped them), and entries edited or made by hand are
		/// kept. A list still holding one mark for the whole of it (2026-10-15/16 authoring) is converted first. The field's own
		/// mark becomes <see cref="BiomeEntryMarks.PerEntry"/>: the list is authored, so empty is a decision.
		/// </summary>
		/// <param name="list">The template's list (written in place when <paramref name="apply"/>).</param>
		/// <param name="id">An entry's identity (null: no identity, never touched).</param>
		/// <param name="print">Everything about an entry the tool writes.</param>
		/// <param name="wholeMarkOf">How the whole-list mark was made before (for converting it).</param>
		/// <param name="written">What the whole-list mark says was written, where it spells it out (vines); else null.</param>
		private static void AuthorEntries<T>(string who, BiomeTemplate biome, string field, List<T> list, List<T> target, Func<T, string> id,
			Func<T, string> print, Func<IReadOnlyList<T>, string> wholeMarkOf, Report report, bool apply, IReadOnlyList<(string Id, string Print)> written = null)
		{
			string fieldMark = biome.AuthoredFingerprint(field);
			Dictionary<string, string> marks;
			bool migrated = false;
			if (fieldMark != null && fieldMark != BiomeEntryMarks.PerEntry)
			{
				marks = BiomeEntryMarks.Migrate(list, target, id, print, fieldMark, wholeMarkOf, written);
				migrated = true;
			}
			else
			{
				marks = biome.EntryMarks(field);
			}
			List<T> working = apply ? list : new List<T>(list);
			BiomeEntryMarks.Outcome outcome = BiomeEntryMarks.Merge(working, target, id, print, marks);
			bool wrote = migrated || fieldMark != BiomeEntryMarks.PerEntry || outcome.Wrote;
			string done(string did, string would) => apply ? did : would;
			if (migrated)
			{
				report.Lines.Add($"{who} {field}: its one mark for the whole list {done("became", "would become")} a mark per entry");
			}
			else if (fieldMark == null && outcome.Changes.Count == 0)
			{
				report.Lines.Add($"{who} {field}: '{(list.Count > 0 ? string.Join(", ", list.ConvertAll(e => print(e))) : "none")}' {done("marked", "would be marked")} as the tool's");
			}
			foreach (BiomeEntryMarks.Change change in outcome.Changes)
			{
				switch (change.Step)
				{
					case BiomeEntryMarks.Step.Added:
						report.Lines.Add($"{who} {field}: {done("added", "would add")} {change.Now}");
						break;
					case BiomeEntryMarks.Step.Updated:
						report.Lines.Add($"{who} {field}: {change.Was} {done("brought up to", "would be brought up to")} {change.Now}");
						break;
					case BiomeEntryMarks.Step.Pruned:
						report.Lines.Add($"{who} {field}: {change.Was} {done("removed", "would be removed")} (the tool's, untouched, and the spec no longer has it)");
						break;
					case BiomeEntryMarks.Step.Marked:
						report.Lines.Add($"{who} {field}: '{change.Now}' {done("marked", "would be marked")} as the tool's");
						break;
					case BiomeEntryMarks.Step.Tombstoned:
						report.Lines.Add($"{who} {field}: {change.Was} was deleted by hand — {done("remembered", "would be remembered")}, so it is not added again");
						break;
					case BiomeEntryMarks.Step.KeptEdited:
						report.Lines.Add($"{who} {field}: {change.Was} was edited or made by hand — kept (the spec says {change.Now})");
						break;
					case BiomeEntryMarks.Step.KeptDeleted:
						report.Lines.Add($"{who} {field}: {change.Now} was deleted by hand — stays deleted");
						break;
				}
			}
			if (wrote)
			{
				report.ValuesFilled++;
			}
			if (outcome.Kept)
			{
				report.ValuesKept++;
			}
			if (apply && wrote)
			{
				biome.SetEntryMarks(field, marks);
				biome.SetAuthoredFingerprint(field, BiomeEntryMarks.PerEntry);
			}
		}

		/// <summary>
		/// Writes one template value from the spec under the authoring contract: when the field is empty and was never
		/// authored, or still holds what the tool last wrote (its mark, <see cref="BiomeTemplate.AuthoredFingerprint"/>);
		/// a value somebody changed since is kept. A value already equal to the spec's is only marked as the tool's.
		/// </summary>
		/// <param name="current">The field's fingerprint now (<see cref="Mark(string)"/>).</param>
		/// <param name="target">The fingerprint of what the spec writes.</param>
		/// <param name="emptyIsADecision">
		/// For a list or an optional reference, where empty means "none" (no flowers, no creeper, no other rock): emptied by
		/// hand after the tool wrote it, it is kept empty. For a value that cannot be empty (a biome's own rock) an empty field
		/// is a gap and is filled.
		/// </param>
		private static void AuthorValue(string who, BiomeTemplate biome, string field, string current, string target, bool emptyIsADecision,
			Action write, string describe, Report report, bool apply, bool isEmpty = false)
		{
			AuthorValue(who, biome, field, biome.AuthoredFingerprint(field), current, target, emptyIsADecision, write, describe, report, apply, isEmpty);
		}

		/// <summary><see cref="AuthorValue(string, BiomeTemplate, string, string, string, bool, Action, string, Report, bool, bool)"/> with the field's mark as the caller reads it.</summary>
		private static void AuthorValue(string who, BiomeTemplate biome, string field, string mark, string current, string target, bool emptyIsADecision,
			Action write, string describe, Report report, bool apply, bool isEmpty = false)
		{
			bool empty = isEmpty || !emptyIsADecision && string.IsNullOrEmpty(current);
			if (current == target)
			{
				if (mark != target)
				{
					report.ValuesFilled++;
					report.Lines.Add($"{who} {field}: '{describe}' {(apply ? "marked" : "would be marked")} as the tool's");
					if (apply)
					{
						biome.SetAuthoredFingerprint(field, target);
					}
				}
				return;
			}
			bool untouched = mark != null && current == mark;
			if (!untouched && !(empty && (mark == null || !emptyIsADecision)))
			{
				report.ValuesKept++;
				report.Lines.Add($"{who} {field}: edited by hand since the tool wrote it — kept (the spec says {describe})");
				return;
			}
			report.ValuesFilled++;
			report.Lines.Add($"{who} {field}: {(apply ? "set to" : "would be set to")} {describe}");
			if (apply)
			{
				write();
				biome.SetAuthoredFingerprint(field, target);
			}
		}

		/// <summary>A string value's mark: the value itself, so a template's marks read plainly.</summary>
		public static string Mark(string value) => value ?? string.Empty;

		/// <summary>A list's mark: a hash of its entries in order.</summary>
		public static string Mark(IReadOnlyList<string> values)
		{
			var text = new StringBuilder(256);
			if (values != null)
			{
				foreach (string value in values)
				{
					text.Append(value ?? "<none>").Append('\n');
				}
			}
			return Hash64(text);
		}

		/// <summary>A prefab list's mark: a hash of its prefabs' names in order (generated prefabs have fixed, unique names).</summary>
		public static string Mark(List<GameObject> prefabs)
		{
			var names = new List<string>();
			if (prefabs != null)
			{
				foreach (GameObject prefab in prefabs)
				{
					names.Add(prefab != null ? prefab.name : "<none>");
				}
			}
			return Mark((IReadOnlyList<string>)names);
		}

		/// <summary>A single prefab's mark: its name, empty for none.</summary>
		public static string Mark(GameObject prefab) => prefab != null ? prefab.name : string.Empty;

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
			/* Stands the same way: only when a rule has them, so every rule marked before forests existed
			 * (none has a stand size) still hashes to its mark and is brought into them. */
			if (rule.forestMetres != 0f)
			{
				text.Append("|F").Append(Q(rule.forestMetres)).Append(',').Append(Q(rule.forestCover))
					.Append(',').Append(Q(rule.forestEdge)).Append(',').Append(Q(rule.forestOpen))
					.Append(',').Append(Q(rule.forestMix)).Append(',').Append(Q(rule.forestScaleBias));
			}
			// And depth bands, for the same reason: only a rule that has one hashes it.
			if (rule.depthRange != Vector2.zero)
			{
				text.Append("|D").Append(Q(rule.depthRange.x)).Append(',').Append(Q(rule.depthRange.y));
			}
			// A bed rule's waters and current (decision 35, 2026-10-18): only when set, so every rule marked before still hashes to its mark.
			if (rule.waters != BiomeBedWaters.Default)
			{
				text.Append("|W").Append((int)rule.waters);
			}
			if (rule.currentRange != Vector2.zero)
			{
				text.Append("|C").Append(Q(rule.currentRange.x)).Append(',').Append(Q(rule.currentRange.y));
			}
			/* Climate bands likewise, and keyed on the switch, not the ranges: a rule marked before bands
			 * existed (switch off) still hashes to its mark, and ranges edited while the switch is off are
			 * inert, as a group's settings are without a radius. Switching a band on, or moving one, is a
			 * tuning. */
			if (rule.useClimateBand)
			{
				text.Append("|K").Append(QBound(rule.temperatureRange.x)).Append(',').Append(QBound(rule.temperatureRange.y))
					.Append(',').Append(Q(rule.temperatureFalloff))
					.Append(',').Append(QBound(rule.humidityRange.x)).Append(',').Append(QBound(rule.humidityRange.y))
					.Append(',').Append(Q(rule.humidityFalloff));
			}
			// The world gate (decision 41, 2026-10-18) likewise: only a rule that has one hashes it, so every mark made before still holds.
			if (!rule.worldGate.IsOpen)
			{
				text.Append("|G").Append(rule.worldGate.Signature());
			}
			// The heat band (2026-10-18, I2q) likewise: keyed on its switch, so every mark made before still holds.
			if (rule.useHeatBand)
			{
				text.Append("|Q").Append(Q(rule.heatRange.x)).Append(',').Append(Q(rule.heatRange.y)).Append(',').Append(Q(rule.heatFalloff));
			}
			// The size skew (2026-10-20) likewise: only a rule leaned off even hashes it, so every mark made before still holds (0, an
			// asset saved before the field, is even too).
			if (rule.scaleSkew > 0f && rule.scaleSkew != 1f)
			{
				text.Append("|W").Append(Q(rule.scaleSkew));
			}
			return Hash64(text);
		}

		/// <summary>
		/// The hash of only the fields a rule had before fingerprints (and carpets and sinks) existed,
		/// for recognising a rule an older run of the tool wrote.
		/// </summary>
		/// <remarks>
		/// Deliberately without the climate band, as without every field added since (depth, groups,
		/// stands): a run before fingerprints could not have written one, so a rule that has a band is
		/// refused as legacy before this is compared (<see cref="IsLegacySpecRule"/>), and leaving it out
		/// is what lets an untouched legacy rule be recognised against a spec that has since gained one.
		/// </remarks>
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

		/// <summary>
		/// <see cref="Q"/> for a band's end, which may be infinite (open): clamped to ±10⁶ first, because
		/// an infinite or NaN double has no defined conversion to <c>long</c> and could hash differently
		/// from one machine to the next. NaN hashes as 0.
		/// </summary>
		private static long QBound(float value) => float.IsNaN(value) ? 0L : Q(Mathf.Clamp(value, -1e6f, 1e6f));

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
				scaleSkew = Mathf.Clamp(s.ScaleSkew, 0.25f, 4f),
				// Any heading (0–360) unless the spec aligns the rule (BiomeArtSpec.Aligned): the default hashes as before.
				yRotationRange = s.Yaw,
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
				forestMetres = Mathf.Clamp(s.ForestMetres, 0f, 4000f),
				forestCover = Mathf.Clamp(s.ForestCover, 0.01f, 1f),
				forestEdge = Mathf.Clamp01(s.ForestEdge),
				forestOpen = Mathf.Clamp01(s.ForestOpen),
				forestMix = Mathf.Clamp01(s.ForestMix),
				forestScaleBias = Mathf.Clamp01(s.ForestScaleBias),
				depthRange = new Vector2(Mathf.Max(0f, s.Depth.x), Mathf.Max(0f, s.Depth.y)),
				waters = s.Waters,
				currentRange = new Vector2(Mathf.Max(0f, s.Current.x), Mathf.Max(0f, s.Current.y)),
				// No band leaves every band field at its default, so a rule without one is exactly what it was.
				useClimateBand = s.Temperature.HasValue || s.Humidity.HasValue,
				temperatureRange = s.Temperature ?? new Vector2(-1f, 1f),
				temperatureFalloff = s.Temperature.HasValue || s.Humidity.HasValue ? Mathf.Clamp01(s.TemperatureFalloff) : 0.1f,
				humidityRange = s.Humidity ?? new Vector2(-1f, 1f),
				humidityFalloff = s.Temperature.HasValue || s.Humidity.HasValue ? Mathf.Clamp01(s.HumidityFalloff) : 0.1f,
				// Open (the default) for every rule without one: the rule is exactly what it was (decision 41, 2026-10-18).
				worldGate = s.World,
				// No heat band leaves the band's fields at their defaults (2026-10-18, I2q).
				useHeatBand = s.Heat.HasValue,
				heatRange = s.Heat ?? new Vector2(0f, 1f),
				heatFalloff = s.Heat.HasValue ? Mathf.Clamp01(s.HeatFalloff) : BiomeHeatBand.DefaultFalloff,
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
			to.scaleSkew = from.scaleSkew;
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
			to.forestMetres = from.forestMetres;
			to.forestCover = from.forestCover;
			to.forestEdge = from.forestEdge;
			to.forestOpen = from.forestOpen;
			to.forestMix = from.forestMix;
			to.forestScaleBias = from.forestScaleBias;
			to.depthRange = from.depthRange;
			to.waters = from.waters;
			to.currentRange = from.currentRange;
			to.useClimateBand = from.useClimateBand;
			to.temperatureRange = from.temperatureRange;
			to.temperatureFalloff = from.temperatureFalloff;
			to.humidityRange = from.humidityRange;
			to.humidityFalloff = from.humidityFalloff;
			to.worldGate = from.worldGate;
			to.useHeatBand = from.useHeatBand;
			to.heatRange = from.heatRange;
			to.heatFalloff = from.heatFalloff;
		}
	}
}
#endif
