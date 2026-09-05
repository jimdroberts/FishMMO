using UnityEngine;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Phonology and vocabulary for one biome, used to name dungeons and points
	/// of interest and to flavour city names. Registers itself with
	/// <see cref="BiomeRegistry"/> when loaded.
	/// </summary>
	[CreateAssetMenu(fileName = "New Biome Naming", menuName = "FishMMO/Naming/Biome Naming", order = 2)]
	public class BiomeNamingTemplate : CachedScriptableObject<BiomeNamingTemplate>, ICachedObject
	{
		[Header("Identity")]
		[Tooltip("Lowercase key code uses to request this biome, e.g. 'volcanic'. Defaults to the asset name.")]
		public string BiomeKey;
		[Tooltip("Shown in tools and in generated entries, e.g. 'Volcanic Rim'.")]
		public string DisplayName;

		[Header("Phonology")]
		public SerializableBiomePhonology Phonology = new();

		private string key;
		private BiomePhonology runtimePhonology;

		/// <summary>Normalised registry key: lowercase letters only.</summary>
		public string Key
		{
			get
			{
				if (key == null)
				{
					key = GeneratorUtility.NormalizeRace(string.IsNullOrWhiteSpace(BiomeKey) ? name : BiomeKey);
				}
				return key;
			}
		}

		/// <summary>Display name, falling back to the key when unset.</summary>
		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;

		public BiomePhonology RuntimePhonology
		{
			get
			{
				if (runtimePhonology == null)
				{
					BuildRuntime();
				}
				return runtimePhonology;
			}
		}

		/// <summary>True when the phonology can produce a root at all.</summary>
		public bool IsUsable => Phonology != null && Phonology.IsUsable();

		/// <summary>Rebuilds the runtime view from the serialized fields.</summary>
		public void BuildRuntime()
		{
			key = null;
			runtimePhonology = (Phonology ?? new SerializableBiomePhonology()).ToRuntime();
		}

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			BiomeRegistry.Register(this);
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			BiomeRegistry.Unregister(this);
			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
