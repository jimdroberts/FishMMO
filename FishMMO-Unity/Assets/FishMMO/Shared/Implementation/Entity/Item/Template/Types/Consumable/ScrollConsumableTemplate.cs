using System.Collections.Generic;
using FishNet.Broadcast;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for scroll consumable templates, which grant abilities when consumed.
	/// Inherits from ConsumableTemplate and adds ability learning logic.
	/// </summary>
	public abstract class ScrollConsumableTemplate : ConsumableTemplate
	{
		/// <summary>
		/// The list of ability templates that will be learned when the scroll is consumed.
		/// </summary>
		public List<BaseAbilityTemplate> AbilityTemplates;

		/// <summary>
		/// The ability EVENTS learned when the scroll is consumed.
		/// </summary>
		/// <remarks>
		/// Knowledge comes in two kinds and crafting needs both: a base ability is the core, an
		/// effect is what configures it. A scroll could only ever teach the first, so half the
		/// crafting vocabulary had no way into a player's hands but a merchant.
		/// </remarks>
		public List<AbilityEvent> AbilityEvents;

		/// <summary>Describes the scroll, and names what reading it teaches.</summary>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);

			bool teachesAbilities = AbilityTemplates != null && AbilityTemplates.Count > 0;
			bool teachesEvents = AbilityEvents != null && AbilityEvents.Count > 0;
			if (!teachesAbilities && !teachesEvents)
			{
				return;
			}

			content.AddHeader("Teaches", TooltipPriority.Effects);
			int order = 1;

			if (teachesAbilities)
			{
				foreach (BaseAbilityTemplate template in AbilityTemplates)
				{
					if (template != null)
					{
						content.AddStat(template.Name, "base ability", TooltipPriority.Effects + order++, tone: TooltipTone.Good);
					}
				}
			}

			if (teachesEvents)
			{
				foreach (AbilityEvent abilityEvent in AbilityEvents)
				{
					if (abilityEvent != null)
					{
						content.AddStat(abilityEvent.Name, "effect", TooltipPriority.Effects + order++, tone: TooltipTone.Good);
					}
				}
			}

			content.AddHint("Reading it adds the knowledge to your Knowledge tab; craft with it at an Ability Crafter.");
		}

		/// <summary>
		/// Invokes the scroll consumption logic, granting abilities to the character if successful.
		/// Calls base Invoke to handle cooldown and charge logic.
		/// </summary>
		/// <param name="character">The player character consuming the scroll.</param>
		/// <param name="item">The scroll item being consumed.</param>
		/// <returns>True if the scroll was successfully consumed and abilities granted, false otherwise.</returns>
		public override bool Invoke(IPlayerCharacter character, Item item, uint currentTick)
		{
			if (!base.Invoke(character, item, currentTick))
			{
				return false;
			}

			/* SERVER ONLY, and then told to the owner.
			 *
			 * Invoke runs on the owning client as well — the consumable pipeline predicts the use —
			 * but knowledge is not a thing to predict. A predicted grant the server then denies
			 * leaves the client believing it knows something it does not, and nothing corrects it
			 * short of a relog; the same grant arriving by broadcast is authoritative and costs one
			 * round trip nobody is waiting on. Every other grant in the game works this way.
			 *
			 * The learn methods raise the controller's knowledge events, which is what puts the
			 * entry in the abilities panel and marks the character for the next save. */
			if (character.NetworkObject == null ||
				!character.NetworkObject.IsServerStarted ||
				!character.TryGet(out IAbilityController abilityController))
			{
				return true;
			}

			GrantBaseAbilities(character, abilityController);
			GrantAbilityEvents(character, abilityController);
			return true;
		}

		/// <summary>Learns the scroll's base abilities and tells the owner about each new one.</summary>
		private void GrantBaseAbilities(IPlayerCharacter character, IAbilityController abilityController)
		{
			if (AbilityTemplates == null)
			{
				return;
			}

			foreach (BaseAbilityTemplate template in AbilityTemplates)
			{
				// Idempotent: a template already known is neither re-learned nor re-announced.
				if (template == null || abilityController.KnowsAbility(template.ID))
				{
					continue;
				}

				abilityController.LearnBaseAbility(template);
				SendToOwner(character, new KnownAbilityAddBroadcast() { TemplateID = template.ID });
			}
		}

		/// <summary>Learns the scroll's effects and tells the owner about each new one.</summary>
		private void GrantAbilityEvents(IPlayerCharacter character, IAbilityController abilityController)
		{
			if (AbilityEvents == null)
			{
				return;
			}

			foreach (AbilityEvent abilityEvent in AbilityEvents)
			{
				if (abilityEvent == null || abilityController.KnowsAbilityEvent(abilityEvent.ID))
				{
					continue;
				}

				abilityController.LearnAbilityEvent(abilityEvent);
				SendToOwner(character, new KnownAbilityEventAddBroadcast() { TemplateID = abilityEvent.ID });
			}
		}

		/// <summary>
		/// Sends a message to the character's owning connection, when it has one.
		/// </summary>
		/// <remarks>
		/// The same shape as <c>BaseAction.SendToOwner</c>, which the lore object's grant uses. It
		/// lives there as a protected helper on the ECA action base and this is an item template,
		/// so the four lines are repeated rather than made a dependency between the two.
		/// </remarks>
		private static void SendToOwner<T>(ICharacter character, T message) where T : struct, IBroadcast
		{
			FishNet.Connection.NetworkConnection owner = character?.Owner;
			if (owner == null || !owner.IsActive)
			{
				return;
			}
			owner.Broadcast(message);
		}
	}
}