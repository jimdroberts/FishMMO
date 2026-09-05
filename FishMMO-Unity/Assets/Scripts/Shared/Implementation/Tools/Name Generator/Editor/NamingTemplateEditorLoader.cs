using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration.Editor
{
	/// <summary>
	/// Fills the naming registries from the project's assets when nothing else
	/// has. At runtime Addressables load every template and each registers itself
	/// as it enters the cache; in the editor outside play mode nothing loads, so
	/// the test window and EditMode tests would find empty registries. This finds
	/// the same assets through <see cref="AssetDatabase"/> and registers them.
	/// </summary>
	public static class NamingTemplateEditorLoader
	{
		/// <summary>Counts of what the last load registered.</summary>
		public struct LoadReport
		{
			public int Races;
			public int Biomes;
			public int Modifiers;
			public int Grammars;

			public override string ToString() =>
				$"{Races} race(s), {Biomes} biome(s), {Modifiers} modifier(s), {Grammars} grammar(s)";
		}

		[InitializeOnLoadMethod]
		private static void RegisterOnEditorLoad()
		{
			// Deferred so the AssetDatabase is warm on a fresh project open.
			EditorApplication.delayCall += () => EnsureLoaded();
		}

		/// <summary>Registers the project's naming assets unless the registries are already populated.</summary>
		public static LoadReport EnsureLoaded()
		{
			if (NameGenerator.IsReady)
			{
				return new LoadReport
				{
					Races = RaceRegistry.Count,
					Biomes = BiomeRegistry.Count,
					Modifiers = ModifierRegistry.Count,
					Grammars = NameGrammar.IsLoaded ? 1 : 0,
				};
			}
			return Reload();
		}

		/// <summary>Clears the registries and registers every naming asset in the project.</summary>
		public static LoadReport Reload()
		{
			RaceRegistry.Clear();
			BiomeRegistry.Clear();
			ModifierRegistry.Clear();
			NameGrammar.Clear();

			var report = new LoadReport();

			foreach (RaceTemplate race in FindAll<RaceTemplate>())
			{
				if (race.Naming != null && race.Naming.IsUsable)
				{
					RaceRegistry.Register(race);
					report.Races++;
				}
			}
			foreach (BiomeNamingTemplate biome in FindAll<BiomeNamingTemplate>())
			{
				BiomeRegistry.Register(biome);
				report.Biomes++;
			}
			foreach (NameModifierTemplate modifier in FindAll<NameModifierTemplate>())
			{
				ModifierRegistry.Register(modifier);
				report.Modifiers++;
			}
			foreach (NameGrammarTemplate grammar in FindAll<NameGrammarTemplate>())
			{
				// Several grammars would be a mistake; the first by path wins and the rest are reported.
				if (report.Grammars == 0)
				{
					NameGrammar.Register(grammar);
				}
				else
				{
					Debug.LogWarning($"[NamingTemplateEditorLoader] Ignoring extra NameGrammarTemplate '{grammar.name}'; '{NameGrammar.Current.name}' is active.");
				}
				report.Grammars++;
			}

			return report;
		}

		/// <summary>Every asset of a type, in path order so registration is deterministic.</summary>
		private static List<T> FindAll<T>() where T : ScriptableObject
		{
			string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
			var paths = new List<string>(guids.Length);
			for (int i = 0; i < guids.Length; i++)
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
			}
			paths.Sort(System.StringComparer.Ordinal);

			var assets = new List<T>(paths.Count);
			for (int i = 0; i < paths.Count; i++)
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(paths[i]);
				if (asset != null)
				{
					assets.Add(asset);
				}
			}
			return assets;
		}
	}
}
