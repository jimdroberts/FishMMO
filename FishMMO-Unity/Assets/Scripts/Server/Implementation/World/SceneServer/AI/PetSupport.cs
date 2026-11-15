using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// How a pet uses the heals and buffs it knows: on its owner first, always, then on itself and
	/// the friends around it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Driven by what the pet knows, not by its archetype.</b> A pet's spells can come from its
	/// summon spell (<see cref="AbilityOnSummonEvent"/>), so a melee pet may well know a heal. The
	/// heals and buffs are found the way <see cref="HealerAttackingState"/> finds its heals — from the
	/// ability's own ECA actions (<see cref="AIAbilityClassifier"/>) — and a pet that knows none
	/// finds nothing to do here. Run in combat by <see cref="BaseAttackingState"/> before the attack,
	/// and out of combat by <see cref="PetIdleState"/>, so a pet patches its owner up after a fight
	/// as well as during one.
	/// </para>
	/// <para>
	/// <b>The order is fixed.</b> The owner is looked after first, at any distance: an owner below
	/// the heal threshold is healed, and an owner missing a buff the pet can give is buffed, before
	/// anyone else is considered — the pet walks back to its owner to do it. Then the most wounded of
	/// the pet itself and its friends, then their buffs. <see cref="AIPetSupportSettings"/> tunes the
	/// threshold and the reach; it cannot reorder this.
	/// </para>
	/// <para>
	/// <b>A friend</b> is someone the pet's owner may not harm and is on the same side as: the
	/// owner's party, guild and other pets, and anyone the owner is allied with
	/// (<see cref="IsFriend"/>).
	/// </para>
	/// <para>
	/// Static and single-threaded, like the healer's ally sweep: every call runs to completion
	/// inside one synchronous AI tick, which is what lets the filters below share one scratch
	/// recipient instead of allocating a closure per pick.
	/// </para>
	/// </remarks>
	public static class PetSupport
	{
		/// <summary>What a support choice does.</summary>
		public enum Kind : byte
		{
			/// <summary>Restores health.</summary>
			Heal,

			/// <summary>Applies a beneficial buff the recipient is missing.</summary>
			Buff,
		}

		/// <summary>One support cast a pet has decided on.</summary>
		public struct Choice
		{
			/// <summary>The heal or buff to cast.</summary>
			public Ability Ability;

			/// <summary>Who it is for.</summary>
			public ICharacter Recipient;

			/// <summary>Whether it heals or buffs.</summary>
			public Kind Kind;
		}

		/// <summary>What <see cref="Plan"/> decided to do this tick.</summary>
		public enum Step : byte
		{
			/// <summary>Nobody needs anything the pet can give; carry on.</summary>
			None,

			/// <summary>In reach: the caller casts <see cref="Choice.Ability"/> now.</summary>
			Cast,

			/// <summary>Out of reach: the pet is walking toward the recipient.</summary>
			Moving,
		}

		/// <summary>Colliders from the friend sweep. Grown when a query comes back full.</summary>
		private static Collider[] allyHits = new Collider[TargetOrdering.QueryBufferSize(20)];

		/// <summary>Scratch for the buffs one ability applies.</summary>
		private static readonly List<BaseBuffTemplate> buffScratch = new List<BaseBuffTemplate>();

		/// <summary>The recipient the filters below are judging for. See the class remarks.</summary>
		private static ICharacter filterRecipient;

		/// <summary>Whether <see cref="filterRecipient"/> is the pet itself.</summary>
		private static bool filterRecipientIsSelf;

		/// <summary><see cref="IsHealForRecipient"/> as a delegate, built once.</summary>
		private static readonly System.Func<Ability, bool> HealFilter = IsHealForRecipient;

		/// <summary><see cref="IsMissingBuffForRecipient"/> as a delegate, built once.</summary>
		private static readonly System.Func<Ability, bool> BuffFilter = IsMissingBuffForRecipient;

		/// <summary>
		/// Decides the pet's support action for this tick and, when the recipient is out of reach and
		/// <paramref name="mayMove"/> allows, starts walking to them.
		/// </summary>
		/// <param name="controller">The pet's AI controller.</param>
		/// <param name="fallbackLayers">Layers to sweep for friends when the settings name none.</param>
		/// <param name="mayMove">False while the pet has been told to stay where it is.</param>
		/// <param name="choice">The decided cast, when the step is not <see cref="Step.None"/>.</param>
		/// <returns>What the caller should do.</returns>
		public static Step Plan(AIController controller, LayerMask fallbackLayers, bool mayMove, out Choice choice)
		{
			AIPetSupportSettings settings = controller != null && controller.Archetype != null ? controller.Archetype.PetSupport : null;
			if (!TryChoose(controller, settings, fallbackLayers, out choice))
			{
				return Step.None;
			}

			if (ReferenceEquals(choice.Recipient, controller.Character))
			{
				return Step.Cast;
			}

			float reach = controller.ResolveAbilityReach(choice.Ability);
			float distance = Vector3.Distance(controller.Character.Transform.position, choice.Recipient.Transform.position);
			if (distance <= reach)
			{
				return Step.Cast;
			}

			if (mayMove && MoveToward(controller, choice.Recipient, reach))
			{
				AimAt(controller, choice.Recipient);
				return Step.Moving;
			}
			return Step.None;
		}

		/// <summary>
		/// Turns the pet, and its aim, to the recipient and stops it, ready for the cast.
		/// </summary>
		/// <param name="controller">The pet's AI controller.</param>
		/// <param name="choice">The decided cast.</param>
		public static void BeginCast(AIController controller, in Choice choice)
		{
			controller.Agent.isStopped = true;
			if (!ReferenceEquals(choice.Recipient, controller.Character))
			{
				AimAt(controller, choice.Recipient);
			}
		}

		/// <summary>
		/// Queues a support cast for an NPC with no attack pacing of its own (the pet's idle state).
		/// </summary>
		/// <param name="controller">The pet's AI controller.</param>
		/// <param name="abilityController">The pet's ability controller.</param>
		/// <param name="ability">The ability to cast.</param>
		/// <returns>True when the cast was queued.</returns>
		public static bool Activate(AIController controller, IAbilityController abilityController, Ability ability)
		{
			if (ability == null)
			{
				return false;
			}
			bool held = abilityController.RequiresHeld(ability.ID);
			if (!abilityController.Activate(ability.ID, held))
			{
				return false;
			}
			AIAimProfile aimProfile = controller.Archetype != null ? controller.Archetype.AimProfile : null;
			controller.RollAimForCast(aimProfile, ability.Speed);
			return true;
		}

		/// <summary>
		/// Hands the aim and the facing back to the enemy (or to nobody, out of combat) once nothing
		/// is being cast at a friend.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public static void EndSupportAim(AIController controller)
		{
			if (controller == null || controller.SupportTarget == null)
			{
				return;
			}
			controller.SupportTarget = null;
			controller.LookTarget = controller.Target;
		}

		/// <summary>True while a cast is being made or is queued.</summary>
		/// <param name="abilityController">The ability controller.</param>
		public static bool IsCasting(IAbilityController abilityController)
		{
			return abilityController != null && (abilityController.IsActivating || abilityController.AbilityQueued);
		}

		/// <summary>
		/// The support cast the pet should make, in the fixed order: owner first, then itself and its
		/// friends. Does not move or cast anything.
		/// </summary>
		/// <param name="controller">The pet's AI controller.</param>
		/// <param name="settings">The archetype's settings; null or disabled chooses nothing.</param>
		/// <param name="fallbackLayers">Layers to sweep for friends when the settings name none.</param>
		/// <param name="choice">The decided cast.</param>
		/// <returns>True when there is something to cast.</returns>
		public static bool TryChoose(AIController controller, AIPetSupportSettings settings, LayerMask fallbackLayers, out Choice choice)
		{
			choice = default;
			Pet pet = controller != null ? controller.OwningPet : null;
			if (pet == null || settings == null || !settings.Enabled)
			{
				return false;
			}

			ICharacter self = controller.Character;
			ICharacter owner = pet.PetOwner;

			// The owner first, at any distance: a heal if wounded, else any buff they are missing.
			if (owner != null && AITargetSelection.IsValidTarget(owner))
			{
				if (AITargetSelection.GetHealthPercent(owner) < settings.HealThreshold &&
					TryPick(controller, owner, false, Kind.Heal, out choice))
				{
					return true;
				}
				if (TryPick(controller, owner, false, Kind.Buff, out choice))
				{
					return true;
				}
			}

			RefreshAllies(controller, pet, settings, fallbackLayers);

			// Then whoever of the pet and its friends is most wounded.
			ICharacter wounded = MostWounded(controller, settings);
			if (wounded != null && TryPick(controller, wounded, ReferenceEquals(wounded, self), Kind.Heal, out choice))
			{
				return true;
			}

			// Then their buffs: the pet's own, then its friends' in a stable order.
			if (settings.SupportSelf && TryPick(controller, self, true, Kind.Buff, out choice))
			{
				return true;
			}
			if (settings.SupportAllies)
			{
				List<ICharacter> allies = controller.PetSupportAllies;
				for (int i = 0; i < allies.Count; ++i)
				{
					ICharacter ally = allies[i];
					if (IsStillNear(controller, ally, settings) && TryPick(controller, ally, false, Kind.Buff, out choice))
					{
						return true;
					}
				}
			}

			choice = default;
			return false;
		}

		/// <summary>
		/// Whether <paramref name="candidate"/> is someone the pet looks after: its owner's side, and
		/// never someone its owner may harm.
		/// </summary>
		/// <param name="pet">The pet.</param>
		/// <param name="candidate">The character to judge.</param>
		/// <returns>True for a friend.</returns>
		public static bool IsFriend(Pet pet, ICharacter candidate)
		{
			if (pet == null || candidate == null)
			{
				return false;
			}
			if (FactionHarm.AreCompanions(pet, candidate))
			{
				return true;
			}
			if (FactionHarm.MayHarm(pet, candidate, out _))
			{
				return false;
			}
			return pet.TryGet(out IFactionController petFaction) &&
				candidate.TryGet(out IFactionController candidateFaction) &&
				petFaction.GetAllianceLevel(candidateFaction) == FactionAllianceLevel.Ally;
		}

		/// <summary>Picks the best usable heal or buff for one recipient.</summary>
		private static bool TryPick(AIController controller, ICharacter recipient, bool recipientIsSelf, Kind kind, out Choice choice)
		{
			choice = default;
			if (recipient == null)
			{
				return false;
			}

			filterRecipient = recipient;
			filterRecipientIsSelf = recipientIsSelf;
			try
			{
				float sqrDistance = recipientIsSelf
					? 0f
					: (recipient.Transform.position - controller.Character.Transform.position).sqrMagnitude;
				Ability ability = controller.PickScoredAbility(sqrDistance, kind == Kind.Heal ? HealFilter : BuffFilter, 0f);
				if (ability == null)
				{
					return false;
				}
				choice = new Choice { Ability = ability, Recipient = recipient, Kind = kind };
				return true;
			}
			finally
			{
				filterRecipient = null;
				filterRecipientIsSelf = false;
			}
		}

		/// <summary>
		/// Whether an ability is a heal that can reach the current recipient: something that heals,
		/// harms nobody, and is aimed the right way — a self-cast only at the pet itself, anything
		/// else only at someone else (a projectile cannot strike its own caster).
		/// </summary>
		private static bool IsHealForRecipient(Ability ability)
		{
			if (ability == null || ability.Template == null)
			{
				return false;
			}
			AIAbilityIntent intent = AIAbilityClassifier.Classify(ability);
			return intent.HasAny(AIAbilityIntent.Heal) &&
				!intent.IsOffensive() &&
				AimsAtRecipient(ability.Template.AbilitySpawnTarget);
		}

		/// <summary>
		/// Whether an ability is a buff aimed the right way for the current recipient, and applies at
		/// least one beneficial buff the recipient does not already have.
		/// </summary>
		private static bool IsMissingBuffForRecipient(Ability ability)
		{
			if (ability == null || ability.Template == null)
			{
				return false;
			}
			AIAbilityIntent intent = AIAbilityClassifier.Classify(ability);
			if (!intent.HasAny(AIAbilityIntent.Buff) ||
				intent.IsOffensive() ||
				!AimsAtRecipient(ability.Template.AbilitySpawnTarget) ||
				filterRecipient == null ||
				!filterRecipient.TryGet(out IBuffController buffController))
			{
				return false;
			}

			buffScratch.Clear();
			AIAbilityClassifier.CollectBeneficialBuffs(ability, buffScratch);
			for (int i = 0; i < buffScratch.Count; ++i)
			{
				if (!buffController.Buffs.ContainsKey(buffScratch[i].ID))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Self-casts reach only the pet; everything else only someone else.</summary>
		private static bool AimsAtRecipient(AbilitySpawnTarget spawnTarget)
		{
			bool selfCast = spawnTarget == AbilitySpawnTarget.Self;
			return filterRecipientIsSelf ? selfCast || spawnTarget == AbilitySpawnTarget.PointBlank : !selfCast;
		}

		/// <summary>The most wounded of the pet and its friends, below the heal threshold, or null.</summary>
		private static ICharacter MostWounded(AIController controller, AIPetSupportSettings settings)
		{
			ICharacter best = null;
			float bestHealth = settings.HealThreshold;

			if (settings.SupportSelf)
			{
				float selfHealth = controller.GetHealthPercent();
				if (selfHealth < bestHealth)
				{
					bestHealth = selfHealth;
					best = controller.Character;
				}
			}

			if (!settings.SupportAllies)
			{
				return best;
			}

			List<ICharacter> allies = controller.PetSupportAllies;
			for (int i = 0; i < allies.Count; ++i)
			{
				ICharacter ally = allies[i];
				if (!IsStillNear(controller, ally, settings))
				{
					continue;
				}
				// Strictly lower: the list is in a stable order, so a tie keeps the earlier friend.
				float health = AITargetSelection.GetHealthPercent(ally);
				if (health < bestHealth)
				{
					bestHealth = health;
					best = ally;
				}
			}
			return best;
		}

		/// <summary>Whether a remembered friend is still alive and still within reach of the sweep.</summary>
		private static bool IsStillNear(AIController controller, ICharacter ally, AIPetSupportSettings settings)
		{
			if (!AITargetSelection.IsValidTarget(ally) || ally.Transform == null)
			{
				return false;
			}
			float radius = settings.AllyScanRadius;
			return (ally.Transform.position - controller.Character.Transform.position).sqrMagnitude <= radius * radius;
		}

		/// <summary>
		/// Refills <see cref="AIController.PetSupportAllies"/> when the scan interval has run out:
		/// every friend within the sweep, apart from the owner and the pet, in a stable order.
		/// </summary>
		private static void RefreshAllies(AIController controller, Pet pet, AIPetSupportSettings settings, LayerMask fallbackLayers)
		{
			controller.PetSupportScanTimer -= controller.StateDeltaTime;
			if (controller.PetSupportScanTimer > 0f)
			{
				return;
			}
			controller.PetSupportScanTimer = settings.AllyScanInterval;

			List<ICharacter> allies = controller.PetSupportAllies;
			allies.Clear();
			if (!settings.SupportAllies || settings.AllyScanRadius <= 0f)
			{
				return;
			}

			// Never a silent zero: an unset mask sweeps the layers the pet already hunts enemies on.
			LayerMask layers = settings.AllyLayers.value != 0 ? settings.AllyLayers : fallbackLayers;
			if (layers.value == 0)
			{
				return;
			}

			int count;
			while (true)
			{
				count = controller.PhysicsScene.OverlapSphere(
					controller.Character.Transform.position,
					settings.AllyScanRadius,
					allyHits,
					layers,
					QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref allyHits, count))
				{
					break;
				}
			}

			ICharacter owner = pet.PetOwner;
			for (int i = 0; i < count && i < allyHits.Length; ++i)
			{
				Collider hit = allyHits[i];
				if (hit == null)
				{
					continue;
				}
				GameObject key = TargetOrdering.ResolveHitKey(hit, out ICharacter candidate);
				if (key == null ||
					candidate == null ||
					ReferenceEquals(candidate, controller.Character) ||
					ReferenceEquals(candidate, owner) ||
					allies.Contains(candidate) ||
					!AITargetSelection.IsValidTarget(candidate) ||
					!IsFriend(pet, candidate))
				{
					continue;
				}
				allies.Add(candidate);
			}

			// Network identity, not broadphase order: the same fight picks the same friend every run.
			allies.Sort((a, b) => IdentityKey(a).CompareTo(IdentityKey(b)));
		}

		/// <summary>A character's stable sort key: its object id, or the maximum when it has none.</summary>
		private static int IdentityKey(ICharacter character)
		{
			FishNet.Object.NetworkObject networkObject = character?.NetworkObject;
			return networkObject != null ? networkObject.ObjectId : int.MaxValue;
		}

		/// <summary>Points the pet's aim and facing at a friend.</summary>
		private static void AimAt(AIController controller, ICharacter recipient)
		{
			controller.SupportTarget = recipient;
			controller.LookTarget = recipient.Transform;
		}

		/// <summary>
		/// Walks toward a friend until within <paramref name="reach"/>; false when there is no way there.
		/// </summary>
		private static bool MoveToward(AIController controller, ICharacter recipient, float reach)
		{
			controller.Resume();

			float sphereRadius = Mathf.Max(reach * AICombatDecision.RANGE_APPROACH_FACTOR, 0.1f);
			Vector3 approach = Vector3Extensions.GetNearestPositionOnSphere(
				controller.Character.Transform.position,
				recipient.Transform.position,
				sphereRadius);

			return controller.TryMoveTo(approach) != AIMovementResult.Failed && !controller.LastPathWasPartial;
		}
	}
}
