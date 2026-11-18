using System;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.Biomes.Editor
{
	/// <summary>
	/// The biome inspector: the template as Unity draws it, then each layer slot's LOCAL art override,
	/// with "Assign LOCAL…" and "Clear".
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every write goes to the biome's <see cref="BiomeLocalArt"/> sidecar</b> under
	/// <c>Assets/LOCAL/Biomes/Overrides</c>, created on first use — never to the template, which is
	/// committed and must not point at a file other clones do not have. The override reaches the
	/// ground the next time the scene's terrain arrays are baked, which happens by itself: the
	/// sidecar changing changes what the scene's layers resolve to.
	/// </para>
	/// <para>
	/// A whole TerrainLayer replaces the slot's art outright (textures, tiling, remaps); the three
	/// texture fields replace single maps over the committed layer when no layer is set.
	/// </para>
	/// <para>
	/// <b>Spawn-rule prefabs are references, so they reach only LOCAL scenes.</b> Each rule of each
	/// slot gets "Assign LOCAL prefab…" and "Clear", written to the same sidecar keyed by the rule's
	/// StableGuid. Textures reach every scene through the gitignored arrays; a prefab can only reach
	/// the ground as a prototype in a scene's terrain data, which is committed for every scene outside
	/// Assets/LOCAL — so the scene painter (LocalArtScope) uses rule overrides, and a whole-layer
	/// override as the terrain's own layer, only for a scene copied under Assets/LOCAL/SceneCopies.
	/// </para>
	/// </remarks>
	[CustomEditor(typeof(BiomeTemplate))]
	public sealed class BiomeTemplateInspector : UnityEditor.Editor
	{
		private const string ExpandedKey = "FishMMO.BiomeTemplateInspector.LocalArtExpanded";

		// One picker per inspector; the slot (or rule) it was opened for is remembered until it closes.
		private int pickerControlId;
		private int rulePickerControlId;
		private string pickingSlot;
		private string pickingRuleSlot;
		private PrefabSpawnRule pickingRule;

		private void OnEnable()
		{
			pickerControlId = GetHashCode();
			rulePickerControlId = pickerControlId ^ 0x5A17;
		}

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			var biome = (BiomeTemplate)target;
			if (biome == null || targets.Length > 1)
			{
				return;
			}

			EditorGUILayout.Space(12f);
			bool expanded = SessionState.GetBool(ExpandedKey, true);
			expanded = EditorGUILayout.Foldout(expanded, "LOCAL art overrides (this machine only)", true, EditorStyles.foldoutHeader);
			SessionState.SetBool(ExpandedKey, expanded);
			if (!expanded)
			{
				return;
			}
			DrawLocalArt(biome);
			HandlePicker(biome);
		}

		private void DrawLocalArt(BiomeTemplate biome)
		{
			EditorGUILayout.HelpBox(
				"Licensed or hand-made art for this biome lives in Assets/LOCAL, which is never committed. " +
				"Overrides are written to a sidecar asset in " + BiomeLocalArt.Folder + ", not to this template, " +
				"and reach the ground when the scene's terrain arrays rebake (automatically, on the next scene open or save).",
				MessageType.None);

			BiomeLocalArt sidecar = BiomeLocalArtIndex.For(biome);
			int sidecars = BiomeLocalArtIndex.AllFor(biome).Count;
			if (sidecars > 1)
			{
				EditorGUILayout.HelpBox($"{sidecars} sidecars dress this biome; per slot, the first by path that has anything wins. Merge them into one.", MessageType.Warning);
			}
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField("Sidecar", sidecar, typeof(BiomeLocalArt), false);
			}

			foreach (string slot in BiomeLocalArt.SlotsOf(biome))
			{
				DrawSlot(biome, sidecar, slot);
			}

			DrawRules(biome, sidecar);
		}

		private void DrawRules(BiomeTemplate biome, BiomeLocalArt sidecar)
		{
			EditorGUILayout.Space(10f);
			EditorGUILayout.LabelField("Spawn rule prefabs (LOCAL scenes only)", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"A rule's LOCAL prefabs replace its own only when a scene under Assets/LOCAL is painted (copy a scene there with " +
				"\"Copy open scene to LOCAL for real art\"). Committed scenes always scatter the rule's own prefabs. " +
				"A prefab in " + BiomeLocalArt.PrefabsFolder + " named like a committed one also stands in for it there.",
				MessageType.None);

			bool any = false;
			foreach (string slot in BiomeLocalArt.SlotsOf(biome))
			{
				TerrainTextureLayer layer = BiomeLocalArt.CommittedLayer(biome, slot);
				if (layer == null || layer.prefabSpawnRules == null)
				{
					continue;
				}
				foreach (PrefabSpawnRule rule in layer.prefabSpawnRules)
				{
					if (rule == null)
					{
						continue;
					}
					any = true;
					DrawRule(biome, sidecar, slot, rule);
				}
			}
			if (!any)
			{
				EditorGUILayout.LabelField("(this biome has no spawn rules)", EditorStyles.miniLabel);
			}
		}

		private void DrawRule(BiomeTemplate biome, BiomeLocalArt sidecar, string slot, PrefabSpawnRule rule)
		{
			BiomeLocalArt.RuleOverride current = sidecar != null ? sidecar.FindRule(slot, rule) : null;
			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUILayout.LabelField(new GUIContent($"{slot} / {rule.ruleName}", $"Rule GUID {rule.StableGuid}"), GUILayout.Width(200f));
				EditorGUILayout.LabelField(CommittedPrefabNames(rule), EditorStyles.miniLabel);
				if (GUILayout.Button(new GUIContent("Assign LOCAL prefab…", "Pick a prefab this rule scatters instead of its own, in LOCAL scenes on this machine."), EditorStyles.miniButtonLeft, GUILayout.Width(132f)))
				{
					pickingRuleSlot = slot;
					pickingRule = rule;
					GameObject first = current != null && current.Prefabs != null && current.Prefabs.Length > 0 ? current.Prefabs[0] : null;
					EditorGUIUtility.ShowObjectPicker<GameObject>(first, false, string.Empty, rulePickerControlId);
				}
				using (new EditorGUI.DisabledScope(current == null))
				{
					if (GUILayout.Button(new GUIContent("Clear", "Remove this rule's LOCAL prefabs; LOCAL scenes scatter its own again."), EditorStyles.miniButtonRight, GUILayout.Width(48f)))
					{
						ClearRule(biome, slot, rule);
						GUIUtility.ExitGUI();
					}
				}
			}
			if (current == null)
			{
				return;
			}
			using (new EditorGUI.IndentLevelScope())
			{
				// The first prefab is editable here; more variants are edited on the sidecar asset itself.
				EditorGUI.BeginChangeCheck();
				GameObject edited = (GameObject)EditorGUILayout.ObjectField("LOCAL prefab", current.Prefabs[0], typeof(GameObject), false);
				if (EditorGUI.EndChangeCheck())
				{
					WriteRule(biome, slot, rule, entry =>
					{
						if (entry.Prefabs == null || entry.Prefabs.Length == 0)
						{
							entry.Prefabs = new GameObject[1];
						}
						entry.Prefabs[0] = edited;
					});
					GUIUtility.ExitGUI();
				}
				if (current.Prefabs.Length > 1)
				{
					EditorGUILayout.LabelField($"+{current.Prefabs.Length - 1} more variant(s) on the sidecar", EditorStyles.miniLabel);
				}
				if (!AllLocal(current.Prefabs))
				{
					EditorGUILayout.HelpBox("Part of this override is a committed prefab. That is allowed, but a committed prefab could simply be assigned on the rule.", MessageType.Info);
				}
			}
		}

		private void DrawSlot(BiomeTemplate biome, BiomeLocalArt sidecar, string slot)
		{
			BiomeLocalArt.SlotOverride current = sidecar != null ? sidecar.Find(slot) : null;
			TerrainTextureLayer committed = BiomeLocalArt.CommittedLayer(biome, slot);

			EditorGUILayout.Space(4f);
			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUILayout.LabelField(new GUIContent(slot, "The palette slot key; scenes record their layers by it."), EditorStyles.boldLabel, GUILayout.Width(90f));
				EditorGUILayout.LabelField(CommittedArtName(committed), EditorStyles.miniLabel);
				if (GUILayout.Button(new GUIContent("Assign LOCAL…", "Pick a TerrainLayer to replace this slot's art on this machine."), EditorStyles.miniButtonLeft, GUILayout.Width(96f)))
				{
					pickingSlot = slot;
					EditorGUIUtility.ShowObjectPicker<TerrainLayer>(current != null ? current.TerrainLayer : null, false, string.Empty, pickerControlId);
				}
				using (new EditorGUI.DisabledScope(current == null))
				{
					if (GUILayout.Button(new GUIContent("Clear", "Remove this slot's LOCAL override; the committed art draws again."), EditorStyles.miniButtonRight, GUILayout.Width(48f)))
					{
						Clear(biome, slot);
						GUIUtility.ExitGUI();
					}
				}
			}

			using (new EditorGUI.IndentLevelScope())
			{
				// Object fields take a drop or a pick too; whatever lands in one is written to the sidecar.
				EditorGUI.BeginChangeCheck();
				var layer = (TerrainLayer)EditorGUILayout.ObjectField("Layer", current?.TerrainLayer, typeof(TerrainLayer), false);
				Texture2D albedo, normal, mask;
				using (new EditorGUI.DisabledScope(layer != null))
				{
					albedo = (Texture2D)EditorGUILayout.ObjectField("Albedo", current?.Albedo, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
					normal = (Texture2D)EditorGUILayout.ObjectField("Normal", current?.Normal, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
					mask = (Texture2D)EditorGUILayout.ObjectField("Mask", current?.Mask, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
				}
				if (EditorGUI.EndChangeCheck())
				{
					Write(biome, slot, entry =>
					{
						entry.TerrainLayer = layer;
						entry.Albedo = albedo;
						entry.Normal = normal;
						entry.Mask = mask;
					});
					GUIUtility.ExitGUI();
				}
				if (current != null && !IsAllLocal(current))
				{
					EditorGUILayout.HelpBox("Part of this override is a committed asset. That is allowed, but committed art could simply be assigned on the template.", MessageType.Info);
				}
			}
		}

		private void HandlePicker(BiomeTemplate biome)
		{
			Event e = Event.current;
			if (e.type != EventType.ExecuteCommand || e.commandName != "ObjectSelectorClosed")
			{
				return;
			}
			if (pickingRule != null && EditorGUIUtility.GetObjectPickerControlID() == rulePickerControlId)
			{
				string ruleSlot = pickingRuleSlot;
				PrefabSpawnRule rule = pickingRule;
				pickingRule = null;
				pickingRuleSlot = null;
				if (EditorGUIUtility.GetObjectPickerObject() is GameObject prefab && PrefabUtility.IsPartOfPrefabAsset(prefab))
				{
					WriteRule(biome, ruleSlot, rule, entry => entry.Prefabs = new[] { prefab });
				}
				e.Use();
				return;
			}
			if (pickingSlot == null || EditorGUIUtility.GetObjectPickerControlID() != pickerControlId)
			{
				return;
			}
			string slot = pickingSlot;
			pickingSlot = null;
			if (EditorGUIUtility.GetObjectPickerObject() is TerrainLayer picked)
			{
				Write(biome, slot, entry => entry.TerrainLayer = picked);
			}
			e.Use();
		}

		/// <summary>Writes one slot of the biome's sidecar, creating the sidecar first if this is its first override.</summary>
		private static void Write(BiomeTemplate biome, string slot, Action<BiomeLocalArt.SlotOverride> change)
		{
			BiomeLocalArt art = BiomeLocalArtIndex.GetOrCreate(biome);
			if (art == null)
			{
				return;
			}
			Undo.RecordObject(art, "LOCAL art override");
			BiomeLocalArt.SlotOverride entry = art.GetOrAdd(slot);
			change(entry);
			if (entry.IsEmpty)
			{
				art.Remove(slot);
			}
			EditorUtility.SetDirty(art);
			AssetDatabase.SaveAssetIfDirty(art);
			BiomeLocalArtIndex.Invalidate();
		}

		/// <summary>Writes one rule's entry of the biome's sidecar, creating the sidecar first if needed; an emptied entry is removed.</summary>
		private static void WriteRule(BiomeTemplate biome, string slot, PrefabSpawnRule rule, Action<BiomeLocalArt.RuleOverride> change)
		{
			BiomeLocalArt art = BiomeLocalArtIndex.GetOrCreate(biome);
			if (art == null || rule == null)
			{
				return;
			}
			Undo.RecordObject(art, "LOCAL prefab override");
			BiomeLocalArt.RuleOverride entry = art.GetOrAddRule(slot, rule);
			change(entry);
			if (entry.IsEmpty)
			{
				art.RemoveRule(slot, rule);
			}
			EditorUtility.SetDirty(art);
			AssetDatabase.SaveAssetIfDirty(art);
			BiomeLocalArtIndex.Invalidate();
		}

		private static void ClearRule(BiomeTemplate biome, string slot, PrefabSpawnRule rule)
		{
			foreach (BiomeLocalArt art in BiomeLocalArtIndex.AllFor(biome))
			{
				if (art.FindRule(slot, rule) == null)
				{
					continue;
				}
				Undo.RecordObject(art, "Clear LOCAL prefab override");
				art.RemoveRule(slot, rule);
				EditorUtility.SetDirty(art);
				AssetDatabase.SaveAssetIfDirty(art);
			}
			BiomeLocalArtIndex.Invalidate();
		}

		private static bool AllLocal(GameObject[] prefabs)
		{
			foreach (GameObject prefab in prefabs)
			{
				if (prefab != null && !BiomeLocalArtIndex.IsLocal(prefab))
				{
					return false;
				}
			}
			return true;
		}

		private static string CommittedPrefabNames(PrefabSpawnRule rule)
		{
			if (rule.prefabs == null || rule.prefabs.Length == 0)
			{
				return "(no prefabs)";
			}
			var names = new System.Collections.Generic.List<string>();
			foreach (GameObject prefab in rule.prefabs)
			{
				if (prefab != null)
				{
					names.Add(prefab.name);
				}
			}
			return names.Count > 0 ? string.Join(", ", names) : "(no prefabs)";
		}

		private static void Clear(BiomeTemplate biome, string slot)
		{
			foreach (BiomeLocalArt art in BiomeLocalArtIndex.AllFor(biome))
			{
				if (art.Find(slot) == null)
				{
					continue;
				}
				Undo.RecordObject(art, "Clear LOCAL art override");
				art.Remove(slot);
				EditorUtility.SetDirty(art);
				AssetDatabase.SaveAssetIfDirty(art);
			}
			BiomeLocalArtIndex.Invalidate();
		}

		private static bool IsAllLocal(BiomeLocalArt.SlotOverride entry)
		{
			return (entry.TerrainLayer == null || BiomeLocalArtIndex.IsLocal(entry.TerrainLayer))
				&& (entry.Albedo == null || BiomeLocalArtIndex.IsLocal(entry.Albedo))
				&& (entry.Normal == null || BiomeLocalArtIndex.IsLocal(entry.Normal))
				&& (entry.Mask == null || BiomeLocalArtIndex.IsLocal(entry.Mask));
		}

		private static string CommittedArtName(TerrainTextureLayer committed)
		{
			if (committed == null)
			{
				return "(no slot)";
			}
			if (committed.terrainLayer != null)
			{
				return committed.terrainLayer.name;
			}
			return committed.albedoTexture != null ? committed.albedoTexture.name : "(no committed art)";
		}
	}
}
