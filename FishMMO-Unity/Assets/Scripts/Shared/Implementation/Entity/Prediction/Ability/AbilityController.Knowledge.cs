using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class for AbilityController handling ability knowledge management,
	/// including tracking and learning base abilities, ability events, and ability instances.
	/// </summary>
	public partial class AbilityController
	{
		/// <summary>
		/// Event invoked when a new ability is added to the character.
		/// </summary>
		public event Action<Ability> OnAddAbility;

		/// <summary>
		/// Event invoked when a new base ability is learned.
		/// </summary>
		public event Action<BaseAbilityTemplate> OnAddKnownAbility;

		/// <summary>
		/// Event invoked when a new ability event is learned.
		/// </summary>
		public event Action<AbilityEvent> OnAddKnownAbilityEvent;

		/// <inheritdoc />
		public event Action<long> OnRemoveAbility;

		/// <summary>
		/// All known abilities for this character, indexed by ability ID.
		/// </summary>
		/// <remarks>
		/// SECURITY: This dictionary must only be populated via server-authoritative
		/// code paths (WritePayload / ReadPayload). All activation methods
		/// (<see cref="Activate"/>, <see cref="CanActivate"/>) gate on membership
		/// in this dictionary, so a desync or bug that adds an unearned ability
		/// would let the character activate it. Ensure no client-only code path
		/// can insert entries.
		/// </remarks>
		public SortedDictionary<long, Ability> KnownAbilities { get; private set; }

		/// <summary>
		/// Reverse index mapping ability template IDs to ability instance IDs.
		/// Enables O(1) <see cref="KnowsLearnedAbility"/> lookups instead of O(n) scans.
		/// </summary>
		private Dictionary<int, long> templateToAbilityID;

		/// <summary>
		/// All known base ability template IDs for this character.
		/// </summary>
		public HashSet<int> KnownBaseAbilities { get; private set; }

		/// <summary>
		/// All known ability event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityEvents { get; private set; }

		/// <summary>
		/// All known OnTick event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnTickEvents { get; private set; }

		/// <summary>
		/// All known OnHit event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnHitEvents { get; private set; }

		/// <summary>
		/// All known OnPreSpawn event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnPreSpawnEvents { get; private set; }

		/// <summary>
		/// All known OnSpawn event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnSpawnEvents { get; private set; }

		/// <summary>
		/// All known OnDestroy event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnDestroyEvents { get; private set; }

		/// <summary>
		/// Checks if the controller knows a base ability with the given template ID.
		/// </summary>
		/// <param name="templateID">The base ability template ID to check.</param>
		/// <returns>True if the base ability is known, false otherwise.</returns>
		public bool KnowsAbility(int templateID)
		{
			return KnownBaseAbilities?.Contains(templateID) ?? false;
		}

		/// <summary>
		/// Adds the provided base ability templates to the known base abilities set.
		/// </summary>
		/// <param name="abilityTemplates">List of base ability templates to learn.</param>
		/// <returns>True if any templates were learned, false if input is null.</returns>
		public bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null)
		{
			if (abilityTemplates == null)
			{
				return false;
			}

			for (int i = 0; i < abilityTemplates.Count; ++i)
			{
				BaseAbilityTemplate template = abilityTemplates[i];
				if (template != null)
				{
					if (!KnownBaseAbilities.Contains(template.ID))
					{
						KnownBaseAbilities.Add(template.ID);
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Adds a single base ability template to the known base abilities set without allocating a list.
		/// </summary>
		/// <param name="template">The base ability template to learn.</param>
		/// <returns>True if the template was learned, false if input is null.</returns>
		public bool LearnBaseAbility(BaseAbilityTemplate template)
		{
			if (template == null)
			{
				return false;
			}

			if (!KnownBaseAbilities.Contains(template.ID))
			{
				KnownBaseAbilities.Add(template.ID);
			}
			return true;
		}

		/// <summary>
		/// Checks if the controller knows the specified ability event by event ID.
		/// </summary>
		/// <param name="eventID">The event template ID to check.</param>
		/// <returns>True if the event is known, false otherwise.</returns>
		public bool KnowsAbilityEvent(int eventID)
		{
			return KnownAbilityEvents?.Contains(eventID) ?? false;
		}

		/// <summary>
		/// Adds the provided ability events to the known event sets, categorizing them by event type.
		/// </summary>
		/// <param name="abilityEvents">List of ability events to learn.</param>
		/// <returns>True if any events were learned, false if input is null.</returns>
		public bool LearnAbilityEvents(List<AbilityEvent> abilityEvents = null)
		{
			if (abilityEvents == null)
			{
				return false;
			}

			foreach (var abilityEvent in abilityEvents)
			{
				if (abilityEvent == null) continue;

				KnownAbilityEvents.Add(abilityEvent.ID);
				CategorizeAbilityEvent(abilityEvent);
			}
			return true;
		}

		/// <summary>
		/// Adds a single ability event to the known event sets without allocating a list.
		/// </summary>
		/// <param name="abilityEvent">The ability event to learn.</param>
		/// <returns>True if the event was learned, false if input is null.</returns>
		public bool LearnAbilityEvent(AbilityEvent abilityEvent)
		{
			if (abilityEvent == null)
			{
				return false;
			}

			KnownAbilityEvents.Add(abilityEvent.ID);
			CategorizeAbilityEvent(abilityEvent);
			return true;
		}

		/// <summary>
		/// Categorizes a single ability event into the appropriate known event type set for fast lookup.
		/// </summary>
		/// <param name="abilityEvent">The ability event to categorize. Must not be null.</param>
		private void CategorizeAbilityEvent(AbilityEvent abilityEvent)
		{
			switch (abilityEvent)
			{
				case AbilityOnTickEvent _:
					KnownAbilityOnTickEvents.Add(abilityEvent.ID);
					break;
				case AbilityOnHitEvent _:
					KnownAbilityOnHitEvents.Add(abilityEvent.ID);
					break;
				case AbilityOnPreSpawnEvent _:
					KnownAbilityOnPreSpawnEvents.Add(abilityEvent.ID);
					break;
				case AbilityOnSpawnEvent _:
					KnownAbilityOnSpawnEvents.Add(abilityEvent.ID);
					break;
				case AbilityOnDestroyEvent _:
					KnownAbilityOnDestroyEvents.Add(abilityEvent.ID);
					break;
			}
		}

		/// <summary>
		/// Checks if the controller knows an ability instance with the given template ID.
		/// Uses the <see cref="templateToAbilityID"/> reverse index for O(1) lookups.
		/// </summary>
		/// <param name="templateID">The template ID to check.</param>
		/// <returns>True if the ability is known, false otherwise.</returns>
		public bool KnowsLearnedAbility(int templateID)
		{
			return templateToAbilityID != null && templateToAbilityID.ContainsKey(templateID);
		}

		/// <summary>
		/// Adds the given ability to the known abilities set, and applies any remaining cooldown if specified.
		/// </summary>
		/// <param name="ability">The ability to learn.</param>
		/// <param name="remainingCooldown">Optional remaining cooldown to apply to the ability.</param>
		/// <remarks>
		/// SECURITY: This method is public because it implements
		/// <see cref="IAbilityKnowledgeController.LearnAbility"/>. All current call sites
		/// are server-authoritative:
		/// <list type="bullet">
		/// <item><description>AbilityController.Networking.cs — ReadPayload/WritePayload (server sync).</description></item>
		/// <item><description>NPC.cs — server-side NPC initialisation.</description></item>
		/// <item><description>CharacterSystem.Loading.cs — server-side character loading from DB.</description></item>
		/// <item><description>InteractableSystem.AbilityCraft.cs — server-side ability crafting.</description></item>
		/// </list>
		/// If a new call site is added that is reachable from client-controlled input
		/// (e.g., a broadcast handler), it MUST perform its own server-side validation
		/// before calling this method. An attacker with a crafted packet could otherwise
		/// grant themselves arbitrary abilities.
		/// </remarks>
		public void LearnAbility(Ability ability, float remainingCooldown = 0.0f)
		{
			if (ability == null)
			{
				return;
			}
			KnownAbilities[ability.ID] = ability;

			// Update reverse index for O(1) KnowsLearnedAbility lookups.
			if (ability.Template != null)
			{
				templateToAbilityID ??= new Dictionary<int, long>();
				templateToAbilityID[ability.Template.ID] = ability.ID;
			}

			// If a cooldown is specified, apply it to the cooldown controller.
			if (remainingCooldown > 0.0f &&
				Character.TryGet(out ICooldownController cooldownController))
			{
				float cooldownReduction = CalculateSpeedReduction(CooldownReductionTemplate);
				float cooldown = ability.Cooldown * cooldownReduction;
				uint currentTick = cooldownController.ResolveAuthoritativeTick(base.TimeManager.LocalTick);

				cooldownController.AddCooldown(ability.ID, CooldownInstance.FromRemainingSeconds(
					currentTick, cooldown, remainingCooldown, (float)base.TimeManager.TickDelta));
			}
		}

		/// <summary>
		/// Abilities this peer knows about only because it OBSERVES the caster, kept apart from
		/// <see cref="KnownAbilities"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An observer's copy of a peer's <see cref="KnownAbilities"/> is written once, by
		/// <c>ReadPayload</c>, at the moment it starts observing. Anything the caster learns after
		/// that is unknown to it, and every cast of such an ability was dropped by
		/// <c>OnAbilityActivatedBroadcast</c> — nothing was drawn, for the rest of the session.
		/// <c>AbilityLearnedObserverBroadcast</c> fills this dictionary instead.
		/// </para>
		/// <para>
		/// Deliberately NOT <see cref="KnownAbilities"/>. That dictionary is what
		/// <see cref="CanActivate"/> gates on, and its documented invariant is that only
		/// server-authoritative paths populate it. This one is read by the visual reproduction
		/// path and by nothing else, so a forged learn message buys an attacker a projectile
		/// drawn on their own screen and no activation of any kind.
		/// </para>
		/// <para>
		/// Instance state, so it dies with the character rather than needing eviction: an
		/// observer that loses sight of a caster despawns the object and the whole controller
		/// with it, and a later re-spawn refills <see cref="KnownAbilities"/> from the payload.
		/// </para>
		/// </remarks>
		private Dictionary<long, Ability> observedAbilities;

		/// <summary>
		/// Files an ability an observer was told about, for visual reproduction only.
		/// </summary>
		/// <param name="abilityID">The ability instance id used by activation broadcasts.</param>
		/// <param name="template">The template the ability was built from.</param>
		/// <param name="abilityEvents">Crafted event ids, or null.</param>
		public void RegisterObservedAbility(long abilityID, AbilityTemplate template, List<int> abilityEvents)
		{
			if (template == null || abilityID == NO_ABILITY)
			{
				return;
			}

			// Already authoritative here; the payload copy wins.
			if (KnownAbilities != null && KnownAbilities.ContainsKey(abilityID))
			{
				return;
			}

			observedAbilities ??= new Dictionary<long, Ability>();
			if (observedAbilities.ContainsKey(abilityID))
			{
				return;
			}

			observedAbilities[abilityID] = new Ability(abilityID, template, abilityEvents);
		}

		/// <summary>
		/// Resolves an ability for the visual reproduction paths (activation and destroy
		/// broadcasts): the authoritative set first, then anything learned while observing.
		/// </summary>
		/// <remarks>
		/// Never use this to decide whether a character may cast — that is
		/// <see cref="KnownAbilities"/>'s job, and only its.
		/// </remarks>
		public bool TryGetAbilityForVisuals(long abilityID, out Ability ability)
		{
			if (KnownAbilities != null && KnownAbilities.TryGetValue(abilityID, out ability))
			{
				return true;
			}
			if (observedAbilities != null && observedAbilities.TryGetValue(abilityID, out ability))
			{
				return true;
			}
			ability = null;
			return false;
		}

		/// <summary>
		/// Detaches and forgets every observer-only ability. Called from
		/// <see cref="AbilityController.ResetState"/> alongside the authoritative sets.
		/// </summary>
		private void ClearObservedAbilities()
		{
			if (observedAbilities == null)
			{
				return;
			}
			foreach (Ability ability in observedAbilities.Values)
			{
				ability.DetachAllAbilityObjects();
			}
			observedAbilities.Clear();
		}

		/// <summary>
		/// Removes the ability with the given reference ID from the known abilities set.
		/// </summary>
		/// <param name="referenceID">The ability reference ID to remove.</param>
		public void RemoveAbility(long referenceID)
		{
			// Update reverse index before removing.
			if (KnownAbilities.TryGetValue(referenceID, out Ability removedAbility) &&
				removedAbility.Template != null &&
				templateToAbilityID != null)
			{
				templateToAbilityID.Remove(removedAbility.Template.ID);
			}
			if (!KnownAbilities.Remove(referenceID))
			{
				// Nothing was bound to that ID; do not raise a removal nobody can act on.
				return;
			}

			OnRemoveAbility?.Invoke(referenceID);
		}
	}
}