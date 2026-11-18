using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration.Editor
{
	/// <summary>
	/// Writes the place-naming content into the project's assets: the built-in compositions, type words,
	/// fused endings, adjectives, nouns, kind weights and water phonology into the Name Grammar, and the
	/// plural and adjective forms into every race.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every step only ADDS: a grammar table that already has rows is left as authored, a type-word key that
	/// exists keeps its words, a race whose Plural or Adjective is set keeps it. So the tool is safe to run
	/// again after designers have edited the results, and re-running it after a kind is appended to
	/// <see cref="POIType"/> just adds that kind's rows.
	/// </para>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.Shared.NameGeneration.Editor.PlaceNamingContentTool.RunAll</c>,
	/// or the two steps separately (<c>WriteGrammarDefaults</c>, then <c>FillRaceForms</c>). The generator
	/// already falls back to the same built-in tables, so names are correct before this runs; running it
	/// is what hands the vocabulary to designers.
	/// </para>
	/// </remarks>
	public static class PlaceNamingContentTool
	{
		/// <summary>Both steps, for a batch run.</summary>
		public static void RunAll()
		{
			WriteGrammarDefaults();
			FillRaceForms();
		}

		/// <summary>Adds the built-in place-naming tables to the Name Grammar asset wherever it has none.</summary>
		public static void WriteGrammarDefaults()
		{
			string[] guids = AssetDatabase.FindAssets("t:" + nameof(NameGrammarTemplate));
			if (guids.Length == 0)
			{
				Debug.LogWarning("[Place naming] No NameGrammarTemplate asset exists; nothing written.");
				return;
			}
			var paths = new List<string>();
			foreach (string guid in guids)
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(StringComparer.Ordinal);
			// The loader registers the first grammar by path; write into the same one.
			NameGrammarTemplate grammar = AssetDatabase.LoadAssetAtPath<NameGrammarTemplate>(paths[0]);
			if (grammar == null)
			{
				return;
			}

			Undo.RecordObject(grammar, "Place naming defaults");
			var report = new List<string>();
			int added = AddMissingTypeWords(grammar);
			if (added > 0) report.Add($"{added} kind(s) of type words");
			if (FillTemplates(grammar)) report.Add($"{grammar.PlaceNameTemplates.Count} compositions");
			added = AddMissingFusedSuffixes(grammar);
			if (added > 0) report.Add($"{added} kind(s) of fused endings");
			if (grammar.PlaceAdjectives == null || grammar.PlaceAdjectives.Length == 0)
			{
				grammar.PlaceAdjectives = (string[])PlaceNameDefaults.Adjectives.Clone();
				report.Add("place adjectives");
			}
			if (grammar.PlaceNouns == null || grammar.PlaceNouns.Length == 0)
			{
				grammar.PlaceNouns = (string[])PlaceNameDefaults.Nouns.Clone();
				report.Add("place nouns");
			}
			added = AddMissingKindWeights(grammar);
			if (added > 0) report.Add($"{added} kind weight(s)");
			if (grammar.WaterPhonology == null || !grammar.WaterPhonology.IsUsable())
			{
				grammar.WaterPhonology = SerializableBiomePhonology.From(PlaceNameDefaults.WaterPhonology());
				report.Add("water phonology");
			}

			if (report.Count == 0)
			{
				Debug.Log($"[Place naming] '{grammar.name}' already has every place-naming table.");
				return;
			}
			EditorUtility.SetDirty(grammar);
			AssetDatabase.SaveAssets();
			if (NameGrammar.Current == grammar)
			{
				NameGrammar.Rebuild();
			}
			Debug.Log($"[Place naming] Added to '{grammar.name}': {string.Join(", ", report)}.");
		}

		/// <summary>Sets every race's empty Plural and Adjective from the English rules and the exception table in <see cref="RaceNaming"/>.</summary>
		public static void FillRaceForms()
		{
			string[] guids = AssetDatabase.FindAssets("t:" + nameof(RaceTemplate));
			var paths = new List<string>(guids.Length);
			foreach (string guid in guids)
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(StringComparer.Ordinal);

			int changed = 0;
			foreach (string path in paths)
			{
				RaceTemplate race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(path);
				if (race == null || race.Naming == null)
				{
					continue;
				}
				bool needsPlural = string.IsNullOrWhiteSpace(race.Naming.Plural);
				bool needsAdjective = string.IsNullOrWhiteSpace(race.Naming.Adjective);
				if (!needsPlural && !needsAdjective)
				{
					continue;
				}
				Undo.RecordObject(race, "Race name forms");
				if (needsPlural)
				{
					race.Naming.Plural = RaceNaming.DerivePlural(race.name);
				}
				if (needsAdjective)
				{
					race.Naming.Adjective = RaceNaming.DeriveAdjective(race.name);
				}
				EditorUtility.SetDirty(race);
				changed++;
			}
			if (changed > 0)
			{
				AssetDatabase.SaveAssets();
			}
			Debug.Log(changed > 0
				? $"[Place naming] Set plural/adjective forms on {changed} race(s)."
				: "[Place naming] Every race already has its plural and adjective forms.");
		}

		// ── Steps ──────────────────────────────────────────────────────

		private static int AddMissingTypeWords(NameGrammarTemplate grammar)
		{
			grammar.POITypeSuffixes ??= new List<StringListMapping>();
			var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (StringListMapping row in grammar.POITypeSuffixes)
			{
				if (row != null && !string.IsNullOrEmpty(row.Key) && row.Values != null && row.Values.Length > 0)
				{
					present.Add(row.Key);
				}
			}
			int added = 0;
			foreach (POIType kind in Enum.GetValues(typeof(POIType)))
			{
				string key = PlaceNameDefaults.KeyOf(kind);
				if (kind == POIType.Any || present.Contains(key))
				{
					continue;
				}
				// A keyless or empty row for the kind is replaced, not duplicated.
				grammar.POITypeSuffixes.RemoveAll(r => r != null && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
				grammar.POITypeSuffixes.Add(new StringListMapping(key, (string[])PlaceNameDefaults.TypeWordsFor(kind).Clone()));
				added++;
			}
			return added;
		}

		private static bool FillTemplates(NameGrammarTemplate grammar)
		{
			grammar.PlaceNameTemplates ??= new List<PlaceNameTemplate>();
			if (grammar.PlaceNameTemplates.Count > 0)
			{
				return false;
			}
			foreach (PlaceNameTemplate t in PlaceNameDefaults.Templates)
			{
				grammar.PlaceNameTemplates.Add(new PlaceNameTemplate
				{
					Pattern = t.Pattern,
					Weight = t.Weight,
					Kinds = new List<POIType>(t.Kinds),
					Groups = new List<PointOfInterestGroup>(t.Groups),
				});
			}
			return true;
		}

		private static int AddMissingFusedSuffixes(NameGrammarTemplate grammar)
		{
			grammar.PlaceFusedSuffixes ??= new List<StringListMapping>();
			var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (StringListMapping row in grammar.PlaceFusedSuffixes)
			{
				if (row != null && !string.IsNullOrEmpty(row.Key))
				{
					present.Add(row.Key);
				}
			}
			int added = 0;
			foreach (POIType kind in Enum.GetValues(typeof(POIType)))
			{
				string[] suffixes = PlaceNameDefaults.FusedSuffixesFor(kind);
				string key = PlaceNameDefaults.KeyOf(kind);
				if (suffixes == null || present.Contains(key))
				{
					continue;
				}
				grammar.PlaceFusedSuffixes.Add(new StringListMapping(key, (string[])suffixes.Clone()));
				added++;
			}
			return added;
		}

		private static int AddMissingKindWeights(NameGrammarTemplate grammar)
		{
			grammar.POIKindWeights ??= new List<POIKindWeight>();
			var present = new HashSet<POIType>();
			foreach (POIKindWeight row in grammar.POIKindWeights)
			{
				if (row != null)
				{
					present.Add(row.Kind);
				}
			}
			int added = 0;
			foreach (POIType kind in Enum.GetValues(typeof(POIType)))
			{
				if (kind == POIType.Any || present.Contains(kind))
				{
					continue;
				}
				grammar.POIKindWeights.Add(new POIKindWeight(kind, PlaceNameDefaults.KindWeight(kind)));
				added++;
			}
			return added;
		}
	}
}
