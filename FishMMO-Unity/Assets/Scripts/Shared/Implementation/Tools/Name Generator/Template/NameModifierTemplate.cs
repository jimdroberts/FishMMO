using System;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// A cross-race flavour — <c>ashen</c>, <c>bloodborn</c>, <c>tidebound</c> —
	/// layered on top of any race's phonology. Its syllables are merged into the
	/// base phonology so names lean toward the theme without losing the race's
	/// sound. Registers itself with <see cref="ModifierRegistry"/> when loaded.
	/// </summary>
	[CreateAssetMenu(fileName = "New Name Modifier", menuName = "FishMMO/Naming/Name Modifier", order = 3)]
	public class NameModifierTemplate : CachedScriptableObject<NameModifierTemplate>, ICachedObject
	{
		[Header("Identity")]
		[Tooltip("Lowercase key code uses to request this modifier, e.g. 'ashen'. Defaults to the asset name.")]
		public string ModifierKey;
		public string DisplayName;
		[TextArea]
		public string Description;

		[Header("Additions")]
		[Tooltip("How strongly picks lean toward this modifier's syllables (0 = never, 1 = always).")]
		[Range(0f, 1f)]
		public float Bias = 0.35f;
		public string[] OnsetAdditions;
		public string[] CodaAdditions;
		public string[] MiddleAdditions;
		public string[] Tags;

		private string key;

		/// <summary>Normalised registry key: lowercase letters only.</summary>
		public string Key
		{
			get
			{
				if (key == null)
				{
					key = GeneratorUtility.NormalizeRace(string.IsNullOrWhiteSpace(ModifierKey) ? name : ModifierKey);
				}
				return key;
			}
		}

		/// <summary>Display name, falling back to the key when unset.</summary>
		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;

		/// <summary>Returns a new phonology with this modifier's syllables merged in.</summary>
		public RacePhonology Apply(RacePhonology basePhonology)
		{
			if (basePhonology == null)
			{
				return null;
			}
			return new RacePhonology
			{
				Onsets = Merge(basePhonology.Onsets, OnsetAdditions),
				Nuclei = basePhonology.Nuclei,
				Codas = Merge(basePhonology.Codas, CodaAdditions),
				Middles = Merge(basePhonology.Middles, MiddleAdditions),
				SyllMin = basePhonology.SyllMin,
				SyllMax = basePhonology.SyllMax,
				FeminineSuffixes = basePhonology.FeminineSuffixes,
				MasculineSuffixes = basePhonology.MasculineSuffixes,
				Description = basePhonology.Description + " + " + ResolvedDisplayName,
				Tags = Merge(basePhonology.Tags, Tags),
				WeightedCodas = basePhonology.WeightedCodas,
			};
		}

		private static string[] Merge(string[] a, string[] b)
		{
			if (b == null || b.Length == 0)
			{
				return a ?? Array.Empty<string>();
			}
			if (a == null || a.Length == 0)
			{
				return b;
			}
			var merged = new string[a.Length + b.Length];
			Array.Copy(a, merged, a.Length);
			Array.Copy(b, 0, merged, a.Length, b.Length);
			return merged;
		}

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			key = null;
			ModifierRegistry.Register(this);
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			ModifierRegistry.Unregister(this);
			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
