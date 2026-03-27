using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template representing a character archetype, including rewards, requirements, and metadata.
	/// </summary>
	[CreateAssetMenu(fileName = "New Archetype", menuName = "FishMMO/Character/Archetype/Archetype", order = 1)]
	public class ArchetypeTemplate : CachedScriptableObject<ArchetypeTemplate>, ICachedObject
	{
		/// <summary>
		/// The NPC guild associated with this archetype, if any.
		/// </summary>
		public NPCGuildTemplate NPCGuild;

		/// <summary>
		/// Addressable reference to the icon sprite for this archetype.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this archetype (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// The description of the archetype, shown to the player.
		/// </summary>
		public string Description;

		/// <summary>
		/// List of attribute templates rewarded for unlocking this archetype.
		/// </summary>
		public List<CharacterAttributeTemplate> AttributeRewards;

		/// <summary>
		/// List of ability templates rewarded for unlocking this archetype.
		/// </summary>
		public List<BaseAbilityTemplate> AbilityRewards;

		/// <summary>
		/// List of item templates rewarded for unlocking this archetype.
		/// </summary>
		public List<BaseItemTemplate> ItemRewards;

		/// <summary>
		/// List of buff templates rewarded for unlocking this archetype.
		/// </summary>
		public List<BaseBuffTemplate> BuffRewards;

		/// <summary>
		/// List of title strings rewarded for unlocking this archetype.
		/// </summary>
		public List<string> TitleRewards;

		/// <summary>
		/// The condition that must be met to unlock this archetype.
		/// </summary>
		public BaseCondition ArchetypeRequirements;

		/// <summary>
		/// The name of this archetype template (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the archetype template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(ArchetypeTemplate))
				return;

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the archetype template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(ArchetypeTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
		/// </summary>
		/// <param name="playerCharacter">The player character to evaluate.</param>
		/// <returns>True if requirements are met, or if no requirements are set; otherwise, false.</returns>
		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (ArchetypeRequirements == null)
			{
				// If no requirements are set, assume requirements are met.
				//Log.Warning($"ArchetypeTemplate: No Archetype Requirements assigned for {this.name}. Assuming requirements are met.");
				return true;
			}
			return ArchetypeRequirements.Evaluate(playerCharacter);
		}
	}
}