using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base ScriptableObject for ability templates, providing common fields, activation conditions, and tooltip logic.
	/// Ability requirements (resources, faction, archetype, attributes) are defined as ECA conditions
	/// on the <see cref="ActivationConditions"/> list rather than as hardcoded fields.
	/// </summary>
	public abstract class BaseAbilityTemplate : CachedScriptableObject<BaseAbilityTemplate>, ICachedObject, ITooltip
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this ability.
		/// </summary>
		[SerializeField]
		private AssetReferenceSprite icon;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// Description of the ability.
		/// </summary>
		public string Description;

		/// <summary>
		/// Time required to activate the ability.
		/// </summary>
		/// <remarks>
		/// Clamped at zero in the inspector. An ability's activation window is
		/// <c>ceil(ActivationTime / TickDelta)</c> assigned to an unsigned tick counter, so a
		/// negative value authored here does not mean "instant" — it produces a negative ceiling
		/// that wraps to a cast of roughly four billion ticks that never finishes. Zero is a
		/// legitimate value and means instant; anything below it is only ever a typo.
		/// </remarks>
		[Min(0)]
		public float ActivationTime;

		/// <summary>
		/// Lifetime of the ability effect.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Speed of the ability effect.
		/// </summary>
		public float Speed;

		/// <summary>
		/// Cooldown time for the ability.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// Crafting price of the ability (in-game currency).
		/// </summary>
		public int Price;

		/// <summary>
		/// Conditions that must be met to activate this ability.
		/// Use ECA conditions such as <see cref="HasResourceCondition"/>, <see cref="HasRequiredAttribute"/>,
		/// <see cref="HasFactionCondition"/>, and <see cref="IsArchetypeCondition"/> to define activation requirements.
		/// Resource conditions implementing <see cref="IResourceCost"/> are aggregated for total cost validation.
		/// </summary>
		[Header("Activation Conditions")]
		[Tooltip("Conditions that must be met to activate this ability (resource costs, attribute requirements, faction, archetype, etc.).")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> ActivationConditions = new List<BaseCondition>();

		/// <summary>
		/// The name of the ability (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing the ability (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Called when the ability template is loaded into cache. Loads the icon sprite on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(BaseAbilityTemplate))
				return;

#if !UNITY_SERVER
			if (icon != null && icon.RuntimeKeyIsValid())
			{
				icon.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the ability template is unloaded from cache. Releases the icon sprite on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(BaseAbilityTemplate))
			{
#if !UNITY_SERVER
				if (icon != null && icon.IsValid())
				{
					icon.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}

		/// <summary>
		/// Writes this template's own description: what it is, what it costs, what it needs.
		/// </summary>
		/// <remarks>
		/// Only the template itself. A base ability that a player will craft effects onto is
		/// described by <see cref="AbilitySummary"/>, which knows about the effects bundled into
		/// the template as well as the ones the player chose — this override serves the plain
		/// templates that are not abilities, such as an ability-type override event.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		public virtual void BuildTooltip(TooltipContent content)
		{
			content.Icon = Icon;
			content.AddTitle(Name);

			if (!string.IsNullOrWhiteSpace(Description))
			{
				content.AddBody(Description);
			}

			if (ActivationTime > 0) content.AddStat("Cast Time", $"{ActivationTime:0.##}s", TooltipPriority.Stats);
			if (Cooldown > 0) content.AddStat("Cooldown", $"{Cooldown:0.##}s", TooltipPriority.Stats + 1);
			if (Speed > 0 && LifeTime > 0) content.AddStat("Range", $"{Speed * LifeTime:0.##}m", TooltipPriority.Stats + 2);
			if (Speed > 0) content.AddStat("Speed", $"{Speed:0.##}m/s", TooltipPriority.Stats + 3);
			if (LifeTime > 0) content.AddStat("Duration", $"{LifeTime:0.##}s", TooltipPriority.Stats + 4);

			AppendConditions(content, ActivationConditions);

			if (Price > 0)
			{
				content.AddStat("Craft Cost", Price.ToString(), TooltipPriority.Price);
			}
		}

		/// <summary>
		/// Writes a condition list as resource costs and requirements.
		/// </summary>
		/// <remarks>
		/// Shared by every template and event that carries conditions, so a resource cost reads
		/// the same wherever it is written. Conditions implementing <see cref="IResourceCost"/> are
		/// costs; everything else with wording is a requirement.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		/// <param name="conditions">The conditions to describe.</param>
		public static void AppendConditions(TooltipContent content, List<BaseCondition> conditions)
		{
			if (conditions == null)
			{
				return;
			}

			bool wroteCostHeader = false;
			bool wroteRequirementHeader = false;
			int costOrder = 1;
			int requirementOrder = 1;

			foreach (BaseCondition condition in conditions)
			{
				if (condition == null)
				{
					continue;
				}

				if (condition is IResourceCost resourceCost &&
					resourceCost.ResourceTemplate != null &&
					resourceCost.ResourceAmount > 0)
				{
					if (!wroteCostHeader)
					{
						content.AddHeader("Resource Cost", TooltipPriority.ResourceCost);
						wroteCostHeader = true;
					}
					content.AddStat(resourceCost.ResourceTemplate.Name, resourceCost.ResourceAmount.ToString(),
						TooltipPriority.ResourceCost + costOrder++);
					continue;
				}

				string contribution = condition.GetTooltipContribution();
				if (string.IsNullOrWhiteSpace(contribution))
				{
					continue;
				}

				if (!wroteRequirementHeader)
				{
					content.AddHeader("Requirements", TooltipPriority.Requirements);
					wroteRequirementHeader = true;
				}
				content.AddRequirement(contribution, TooltipPriority.Requirements + requirementOrder++);
			}
		}
	}
}