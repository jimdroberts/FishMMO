using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining NPC guilds, their icon, description, archetypes, and requirements.
	/// </summary>
	[CreateAssetMenu(fileName = "New NPC Guild", menuName = "FishMMO/Character/NPC/NPC Guild", order = 1)]
	public class NPCGuildTemplate : CachedScriptableObject<NPCGuildTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this guild.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this guild (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Description of the guild and its purpose.
		/// </summary>
		public string Description;

		/// <summary>
		/// List of archetypes associated with this guild.
		/// </summary>
		public List<ArchetypeTemplate> Archetypes = new List<ArchetypeTemplate>();

		/// <summary>
		/// Requirements that a player must meet to join or interact with this guild.
		/// </summary>
		public BaseCondition GuildRequirements;

		/// <summary>
		/// The name of the guild, derived from the asset name.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the NPC guild template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(NPCGuildTemplate))
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
		/// Called when the NPC guild template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(NPCGuildTemplate))
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
		/// Returns true if requirements are met or if no requirements are set.
		/// </summary>
		/// <param name="playerCharacter">The player character to evaluate.</param>
		/// <returns>True if requirements are met, false otherwise.</returns>
		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (GuildRequirements == null)
			{
				// If no requirements are set, assume requirements are met.
				//Log.Warning($"NPCGuildTemplate: No Guild Requirements assigned for {this.name}. Assuming requirements are met.");
				return true;
			}
			// Evaluate the requirements condition for the player character.
			return GuildRequirements.Evaluate(playerCharacter);
		}
	}
}