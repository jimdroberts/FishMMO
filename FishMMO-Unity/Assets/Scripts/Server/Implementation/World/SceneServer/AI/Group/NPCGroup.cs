using System.Collections.Generic;
using UnityEngine;
using FishMMO.Server.Core;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// A pack of NPCs that fight together: a shared focus target, group alerts, roles, and a
	/// tactic that arranges members around the enemy while they orbit it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A plain runtime object, owned by whatever founded it.</b> A spawner whose pack is enabled
	/// (<see cref="NPCPackSettings.Enabled"/>) founds one when its first NPC spawns and adds every
	/// NPC it spawns after that, with the role its entry names; a boss founds one for the adds it
	/// calls in. It used to be a scene <c>MonoBehaviour</c> with inspector-assigned members and its
	/// own <c>Update</c>, and nothing ever created one: NPC brains are added to pooled instances at
	/// runtime on the server, so there was never a brain in a scene for an inspector to point at,
	/// and <see cref="AIController.Group"/> was never set.
	/// </para>
	/// <para>
	/// <b>Membership is the living members.</b> A brain leaves on death
	/// (<see cref="AIController.SuspendForCorpse"/>), on despawn and pool reset
	/// (<see cref="AIController.ResetForPool"/>) and when it is destroyed with its scene. A pack whose
	/// last member leaves is <see cref="Released"/>: it stops ticking, refuses members, and its
	/// founder makes a new one for the next spawn. So a respawn rejoins its spawner's pack while any
	/// member of it still stands, and founds the next one after a wipe.
	/// </para>
	/// <para>
	/// <b>Ticked by the brain host, inside the AI contract.</b> <see cref="AIBrainHost"/> calls
	/// <see cref="Tick"/> on every network tick; the pack thinks only on AI ticks (the same whole
	/// divisor of the network tick the brains use), only while a member is in a tier that fights
	/// (<see cref="TierEvaluates"/>), and every <see cref="EVALUATE_INTERVAL"/> seconds scaled by
	/// that member's LOD interval. A pack with nobody near it costs a tier read per member per AI
	/// tick and does nothing else.
	/// </para>
	/// </remarks>
	public sealed class NPCGroup
	{
		/// <summary>
		/// Seconds of AI time between evaluations for a pack whose most active member is in the
		/// Active tier. Other tiers multiply it by their LOD interval.
		/// </summary>
		public const float EVALUATE_INTERVAL = 0.5f;

		/// <summary>
		/// Total arc, in radians, a <see cref="PackTactic.FocusFire"/> cluster is spread over (about 30°).
		/// </summary>
		public const float CLUSTER_SPREAD = 0.52f;

		/// <summary>
		/// Default <see cref="TacticOrbitRadius"/> for a pack founded without authored settings.
		/// </summary>
		public const float DEFAULT_ORBIT_RADIUS = 5f;

		/// <summary>
		/// Default <see cref="KiteRotationSpeed"/> for a pack founded without authored settings.
		/// </summary>
		public const float DEFAULT_KITE_ROTATION_SPEED = 30f;

		/// <summary>
		/// Counts packs founded, so each gets its own stagger phase.
		/// </summary>
		private static uint nextSequence;

		/// <summary>
		/// The members, in joining order.
		/// </summary>
		private readonly List<NPCGroupMember> members = new List<NPCGroupMember>();

		/// <summary>
		/// How the pack arranges itself around the enemy it focuses.
		/// </summary>
		public PackTactic Tactic { get; }

		/// <summary>
		/// When true, the pack's focus follows the target of its living, fighting tank.
		/// </summary>
		public bool FocusTargeting { get; }

		/// <summary>
		/// Radius of the ring an orbiting member takes its tactic slot on, in metres.
		/// </summary>
		public float TacticOrbitRadius { get; }

		/// <summary>
		/// Degrees per second the <see cref="PackTactic.Kite"/> ring turns.
		/// </summary>
		public float KiteRotationSpeed { get; }

		/// <summary>
		/// The character the pack is focusing, as recorded.
		/// </summary>
		private ICharacter groupTarget;

		/// <summary>
		/// <see cref="groupTarget"/>'s ID when it was recorded. See <see cref="GroupTargetCharacter"/>.
		/// </summary>
		private long groupTargetID;

		/// <summary>
		/// The enemy the pack is focusing, or null.
		/// </summary>
		/// <remarks>
		/// Set by <see cref="AlertGroup"/> and, with <see cref="FocusTargeting"/>, by the tank's target
		/// at each evaluation; dropped when the pack is no longer fighting. Held with the ID it had,
		/// for the reason <see cref="AIController.TargetCharacter"/> is: a pooled character's object
		/// is re-issued to somebody else after a despawn, and a bare reference would hand the pack's
		/// DPS the pool's next occupant as their focus.
		/// </remarks>
		public ICharacter GroupTargetCharacter
		{
			get
			{
				if (groupTarget == null || groupTarget.Transform == null || groupTarget.ID != groupTargetID)
				{
					return null;
				}
				return groupTarget;
			}
		}

		/// <summary>
		/// The focused enemy's transform, or null. See <see cref="GroupTargetCharacter"/>.
		/// </summary>
		public Transform GroupTarget
		{
			get
			{
				ICharacter focus = GroupTargetCharacter;
				return focus != null ? focus.Transform : null;
			}
		}

		/// <summary>
		/// The most wounded living member, or null when every living member is at full health.
		/// </summary>
		/// <remarks>
		/// Null rather than "whoever happened to be first" when nobody is hurt: a defender reads this
		/// to decide whom to body-block for, and shielding a healthy packmate took it out of the
		/// fight for nothing.
		/// </remarks>
		public AIController LowestHealthMember { get; private set; }

		/// <summary>
		/// Health fraction (0-1) of <see cref="LowestHealthMember"/>; 1 when nobody is wounded.
		/// </summary>
		public float LowestHealthPercent { get; private set; } = 1f;

		/// <summary>
		/// Living members at the last evaluation.
		/// </summary>
		public int AliveMemberCount { get; private set; }

		/// <summary>
		/// True when a member was fighting (in any combat state) at the last evaluation.
		/// </summary>
		public bool IsInCombat { get; private set; }

		/// <summary>
		/// Members, living or not yet pruned.
		/// </summary>
		public int MemberCount => members.Count;

		/// <summary>
		/// The members, in joining order.
		/// </summary>
		public IReadOnlyList<NPCGroupMember> Members => members;

		/// <summary>
		/// True once the last member has left. A released pack never takes a member again.
		/// </summary>
		public bool Released { get; private set; }

		/// <summary>
		/// The brain host ticking this pack, or null. Owned by <see cref="AIBrainHost"/>.
		/// </summary>
		internal AIBrainHost Host { get; set; }

		/// <summary>
		/// This pack's index in <see cref="Host"/>'s pack list, for swap-removal. Owned by the host.
		/// </summary>
		internal int HostSlot = -1;

		/// <summary>
		/// Fault log for this pack's ticks, created by the host on the first failure.
		/// </summary>
		internal RepeatingFaultLog Faults;

		/// <summary>
		/// A well-mixed identity, the source of this pack's stagger phases.
		/// </summary>
		private readonly uint key;

		/// <summary>
		/// True once the AI-tick phase has been drawn; it needs the host's divisor.
		/// </summary>
		private bool phased;

		/// <summary>
		/// Network ticks since the pack's last AI tick.
		/// </summary>
		private int aiTickCounter;

		/// <summary>
		/// Schedules evaluations and measures the time each covers.
		/// </summary>
		private AIStateClock clock;

		/// <summary>
		/// True while some member holds a tactic slot, so a stand-down knows there is one to clear.
		/// </summary>
		private bool holdsSlots;

		/// <summary>
		/// True while <see cref="AlertGroup"/> is walking the members.
		/// </summary>
		private bool isAlerting;

		// Scratch for the formation pass; a pack is a handful of NPCs, and this runs twice a second.
		private readonly List<AIController> formationMembers = new List<AIController>(8);
		private readonly List<NPCGroupRole> formationRoles = new List<NPCGroupRole>(8);
		private readonly List<float> formationBearings = new List<float>(8);
		private readonly List<float> formationAngles = new List<float>(8);
		private readonly List<int> formationOrder = new List<int>(8);

		/// <summary>
		/// Founds a pack with authored tuning.
		/// </summary>
		/// <param name="settings">The spawner's pack block; null founds an untuned pack.</param>
		public NPCGroup(NPCPackSettings settings)
			: this(settings != null ? settings.Tactic : PackTactic.None,
				settings == null || settings.FocusTargeting,
				settings != null ? settings.TacticOrbitRadius : DEFAULT_ORBIT_RADIUS,
				settings != null ? settings.KiteRotationSpeed : DEFAULT_KITE_ROTATION_SPEED)
		{
		}

		/// <summary>
		/// Founds a pack.
		/// </summary>
		/// <param name="tactic">How members arrange themselves while orbiting the focus.</param>
		/// <param name="focusTargeting">Whether the focus follows the tank's target.</param>
		/// <param name="tacticOrbitRadius">The tactic ring's radius, in metres.</param>
		/// <param name="kiteRotationSpeed">Degrees per second the Kite ring turns.</param>
		public NPCGroup(PackTactic tactic, bool focusTargeting, float tacticOrbitRadius, float kiteRotationSpeed)
		{
			Tactic = tactic;
			FocusTargeting = focusTargeting;
			TacticOrbitRadius = Mathf.Max(0f, tacticOrbitRadius);
			KiteRotationSpeed = kiteRotationSpeed;

			key = AIController.MixIdentity(unchecked((int)++nextSequence));

			/* The first evaluation lands at a point in the interval drawn from the pack's identity,
			 * as a brain's periodic checks do (AIController.SeedTimerPhases): a scene's packs are
			 * founded together, and would otherwise evaluate on the same AI tick for life. */
			clock.Rearm(EVALUATE_INTERVAL * AIController.PhaseFraction(key, 6));
		}

		// --- Membership --------------------------------------------------------------------------

		/// <summary>
		/// Adds a brain with a role, taking it out of any other pack first. Adding a member again
		/// updates its role.
		/// </summary>
		/// <remarks>
		/// The first member hands the pack to its brain's host, which ticks it from then on.
		/// </remarks>
		/// <param name="controller">The brain joining.</param>
		/// <param name="role">The role it plays here.</param>
		/// <returns>False when the brain is null or this pack has been released.</returns>
		public bool AddMember(AIController controller, NPCGroupRole role)
		{
			if (controller == null || Released)
			{
				return false;
			}

			int index = IndexOf(controller);
			if (index >= 0)
			{
				members[index] = new NPCGroupMember(controller, role);
				controller.GroupRole = role;
				return true;
			}

			// One pack at a time: a brain can only answer to one focus and one formation.
			controller.LeavePack();

			members.Add(new NPCGroupMember(controller, role));
			controller.Group = this;
			controller.GroupRole = role;
			controller.ClearPackSlot();

			if (Host == null && controller.Host != null)
			{
				controller.Host.AddGroup(this);
			}
			return true;
		}

		/// <summary>
		/// Removes a brain. Removing the last member releases the pack.
		/// </summary>
		/// <param name="controller">The brain leaving; compared by reference, so a destroyed brain can still leave.</param>
		public void RemoveMember(AIController controller)
		{
			int index = IndexOf(controller);
			if (index < 0)
			{
				return;
			}

			/* RemoveAt, not a swap: the formation walks members in joining order, and keeping it
			 * means a death re-spreads the ring rather than reshuffling who stands where. */
			members.RemoveAt(index);
			Detach(controller);

			if (members.Count == 0)
			{
				Release();
			}
		}

		/// <summary>
		/// Removes every member and releases the pack. Called when its founder stops — a spawner
		/// whose scene unloads.
		/// </summary>
		public void Dissolve()
		{
			for (int i = members.Count - 1; i >= 0; --i)
			{
				AIController controller = members[i].Controller;
				members.RemoveAt(i);
				Detach(controller);
			}
			Release();
		}

		/// <summary>
		/// The first living member with a role, or null.
		/// </summary>
		/// <param name="role">The role to look for.</param>
		/// <returns>The member's brain, or null.</returns>
		public AIController GetMemberByRole(NPCGroupRole role)
		{
			for (int i = 0; i < members.Count; i++)
			{
				AIController controller = members[i].Controller;
				if (controller != null && members[i].Role == role && TryGetHealthFraction(controller, out _))
				{
					return controller;
				}
			}
			return null;
		}

		/// <summary>
		/// Index of <paramref name="controller"/> in <see cref="members"/>, by reference; -1 when absent.
		/// </summary>
		private int IndexOf(AIController controller)
		{
			if (ReferenceEquals(controller, null))
			{
				return -1;
			}
			for (int i = 0; i < members.Count; ++i)
			{
				if (ReferenceEquals(members[i].Controller, controller))
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>
		/// Clears what membership wrote on a brain that has just been taken out of the list.
		/// </summary>
		private void Detach(AIController controller)
		{
			if (ReferenceEquals(controller, null))
			{
				return;
			}

			if (ReferenceEquals(controller.Group, this))
			{
				controller.Group = null;
				controller.GroupRole = NPCGroupRole.None;
			}
			controller.ClearPackSlot();

			if (ReferenceEquals(LowestHealthMember, controller))
			{
				LowestHealthMember = null;
				LowestHealthPercent = 1f;
			}
		}

		/// <summary>
		/// Ends the pack: no more ticks, no more members, no focus.
		/// </summary>
		private void Release()
		{
			if (Released)
			{
				return;
			}
			Released = true;

			ClearCombatState();
			AliveMemberCount = 0;
			Host?.RemoveGroup(this);
		}

		// --- Alerts ------------------------------------------------------------------------------

		/// <summary>
		/// Tells the pack that one of its members has engaged <paramref name="enemy"/>: the pack
		/// focuses it, and every member free to fight joins in.
		/// </summary>
		/// <remarks>
		/// Called by <see cref="AIController.ChangeState"/> whenever a member enters an attacking
		/// state with a target. Who answers is decided by the member, with the same rule a hit is
		/// asked (<see cref="AIController.MayAnswerPackAlert"/>): not a member already fighting,
		/// evading on a leash, immortal, dead or without a brain that runs.
		/// </remarks>
		/// <param name="enemy">The character a member has engaged.</param>
		public void AlertGroup(ICharacter enemy)
		{
			if (Released || !AITargetSelection.IsValidTarget(enemy))
			{
				return;
			}

			/* Re-entrancy guard. Each member that answers enters its attacking state, and entering
			 * one calls back into AlertGroup — without this the first alert recursed once per member
			 * as it woke each one up. */
			if (isAlerting)
			{
				return;
			}

			isAlerting = true;
			try
			{
				SetGroupTarget(enemy);

				for (int i = 0; i < members.Count; i++)
				{
					AIController controller = members[i].Controller;
					if (controller != null)
					{
						controller.AnswerPackAlert(enemy);
					}
				}
			}
			finally
			{
				isAlerting = false;
			}
		}

		/// <summary>
		/// Records the pack's focus, with its ID for the identity check.
		/// </summary>
		private void SetGroupTarget(ICharacter enemy)
		{
			groupTarget = enemy;
			groupTargetID = enemy != null ? enemy.ID : 0;
		}

		// --- Ticking -----------------------------------------------------------------------------

		/// <summary>
		/// One network tick of the pack, from its brain host.
		/// </summary>
		/// <remarks>
		/// The pack thinks on its own AI tick — every <paramref name="ticksPerAiUpdate"/>th network
		/// tick, at a phase drawn from its identity — and each AI tick asks its members' LOD tiers
		/// what that tick may do (<see cref="Advance"/>).
		/// </remarks>
		/// <param name="networkTickDelta">Seconds per network tick.</param>
		/// <param name="ticksPerAiUpdate">Network ticks per AI tick, as the brains resolve it.</param>
		/// <returns>True when the pack evaluated on this tick.</returns>
		internal bool Tick(float networkTickDelta, int ticksPerAiUpdate)
		{
			if (Released)
			{
				return false;
			}

			ticksPerAiUpdate = Mathf.Max(1, ticksPerAiUpdate);
			if (!phased)
			{
				phased = true;
				aiTickCounter = (int)(key % (uint)ticksPerAiUpdate);
			}

			if (++aiTickCounter < ticksPerAiUpdate)
			{
				return false;
			}
			aiTickCounter = 0;

			AILodTier tier = ResolveTier(out int lodInterval);
			return Advance(tier, lodInterval, networkTickDelta * ticksPerAiUpdate);
		}

		/// <summary>
		/// One AI tick of the pack at a given tier: stand down if nobody near it fights, otherwise
		/// evaluate once the interval for that tier has passed.
		/// </summary>
		/// <remarks>
		/// Split from <see cref="Tick"/> so the LOD half can be driven directly.
		/// </remarks>
		/// <param name="tier">The most active tier among the pack's running members.</param>
		/// <param name="lodInterval">That member's LOD interval for the tier, in AI ticks.</param>
		/// <param name="aiTickDelta">Seconds in one AI tick.</param>
		/// <returns>True when the pack evaluated.</returns>
		internal bool Advance(AILodTier tier, int lodInterval, float aiTickDelta)
		{
			if (Released)
			{
				return false;
			}

			/* Far and Dormant disengage every member (AIController.UpdateFar drops a fight; a Dormant
			 * brain runs nothing), so nothing is left for a pack to coordinate and nobody reads what
			 * it would compute. Its combat state goes with the fight. */
			if (!TierEvaluates(tier))
			{
				StandDown();
				return false;
			}

			if (!clock.Advance(aiTickDelta, out float elapsed))
			{
				return false;
			}

			Evaluate(elapsed);
			clock.Rearm(ResolveEvaluateInterval(lodInterval), aiTickDelta);
			return true;
		}

		/// <summary>
		/// The most active LOD tier among the running members, and that member's interval for it.
		/// </summary>
		private AILodTier ResolveTier(out int lodInterval)
		{
			AILodTier best = AILodTier.Dormant;
			lodInterval = 1;
			bool any = false;

			for (int i = 0; i < members.Count; ++i)
			{
				AIController controller = members[i].Controller;
				if (controller == null || !controller.IsRunning)
				{
					continue;
				}

				AILodTier tier = controller.CurrentLodTier;
				if (!any || tier < best)
				{
					any = true;
					best = tier;
					AILodSettings settings = controller.LodSettings;
					lodInterval = settings != null ? settings.GetTickInterval(tier) : 1;
				}
			}

			return best;
		}

		/// <summary>
		/// The more active of two tiers. Pure, so the pack's tier rule can be pinned.
		/// </summary>
		/// <param name="a">One tier.</param>
		/// <param name="b">The other.</param>
		/// <returns>Whichever does more work.</returns>
		public static AILodTier MostActiveTier(AILodTier a, AILodTier b)
		{
			return a <= b ? a : b;
		}

		/// <summary>
		/// Whether a pack whose most active member is in <paramref name="tier"/> evaluates at all.
		/// </summary>
		/// <remarks>
		/// Active and Nearby, the tiers whose brains still fight. Pure, so it can be pinned.
		/// </remarks>
		/// <param name="tier">The tier.</param>
		/// <returns>True when the pack evaluates.</returns>
		public static bool TierEvaluates(AILodTier tier)
		{
			return tier == AILodTier.Active || tier == AILodTier.Nearby;
		}

		/// <summary>
		/// Seconds between evaluations for a member tier's LOD interval.
		/// </summary>
		/// <remarks>
		/// The pack slows with its members: a Nearby brain thinking every third AI tick is read by
		/// a pack that evaluates a third as often. Pure, so it can be pinned.
		/// </remarks>
		/// <param name="lodInterval">The tier's interval, in AI ticks.</param>
		/// <returns>The evaluation interval, in seconds.</returns>
		public static float ResolveEvaluateInterval(int lodInterval)
		{
			return EVALUATE_INTERVAL * Mathf.Max(1, lodInterval);
		}

		/// <summary>
		/// Drops the pack's combat state once nobody near it fights.
		/// </summary>
		private void StandDown()
		{
			if (!IsInCombat && groupTarget == null && !holdsSlots && LowestHealthMember == null)
			{
				return;
			}
			ClearCombatState();
		}

		/// <summary>
		/// Clears the focus, the combat flag, the wounded member and every tactic slot.
		/// </summary>
		private void ClearCombatState()
		{
			IsInCombat = false;
			SetGroupTarget(null);
			LowestHealthMember = null;
			LowestHealthPercent = 1f;
			ClearSlots();
		}

		/// <summary>
		/// Takes every member off its tactic slot.
		/// </summary>
		private void ClearSlots()
		{
			if (!holdsSlots)
			{
				return;
			}
			for (int i = 0; i < members.Count; ++i)
			{
				AIController controller = members[i].Controller;
				if (!ReferenceEquals(controller, null))
				{
					controller.ClearPackSlot();
				}
			}
			holdsSlots = false;
		}

		// --- Evaluation --------------------------------------------------------------------------

		/// <summary>
		/// Re-reads the pack: living count, most wounded member, combat flag, the tank's focus, and
		/// each fighting member's tactic slot.
		/// </summary>
		/// <param name="elapsed">Seconds of AI time since the previous evaluation.</param>
		internal void Evaluate(float elapsed)
		{
			if (Released)
			{
				return;
			}

			/* A brain destroyed outright leaves through its OnDestroy; one that somehow did not is
			 * pruned here rather than read. */
			for (int i = members.Count - 1; i >= 0; --i)
			{
				AIController controller = members[i].Controller;
				if (controller == null)
				{
					members.RemoveAt(i);
					Detach(controller);
				}
			}
			if (members.Count == 0)
			{
				Release();
				return;
			}

			AliveMemberCount = 0;
			LowestHealthMember = null;
			LowestHealthPercent = 1f;
			IsInCombat = false;
			ICharacter tankTarget = null;

			for (int i = 0; i < members.Count; i++)
			{
				NPCGroupMember member = members[i];
				AIController controller = member.Controller;
				if (!TryGetHealthFraction(controller, out float health))
				{
					continue;
				}

				AliveMemberCount++;

				if (IsWounded(health) && health < LowestHealthPercent)
				{
					LowestHealthPercent = health;
					LowestHealthMember = controller;
				}

				/* Any state that is part of a fight, not the attacking state alone: a member mid-orbit
				 * or mid-flee is fighting, and reading only the attacking state had the pack stand
				 * down whenever its members manoeuvred. */
				bool fighting = controller.IsInCombatState;
				if (fighting)
				{
					IsInCombat = true;
				}

				if (member.Role == NPCGroupRole.Tank && fighting && tankTarget == null)
				{
					ICharacter target = controller.TargetCharacter;
					if (AITargetSelection.IsValidTarget(target))
					{
						tankTarget = target;
					}
				}
			}

			if (FocusTargeting && tankTarget != null)
			{
				SetGroupTarget(tankTarget);
			}

			// The fight is over: nothing to focus, nobody to arrange.
			if (!IsInCombat)
			{
				SetGroupTarget(null);
				ClearSlots();
				return;
			}

			AssignTacticalPositions(elapsed);
		}

		/// <summary>
		/// Health fraction of a living member. False for a dead, destroyed or healthless one.
		/// </summary>
		private static bool TryGetHealthFraction(AIController controller, out float health)
		{
			health = 0f;
			if (controller == null || controller.Character == null)
			{
				return false;
			}
			if (!controller.Character.TryGet(out ICharacterDamageController damage) || !damage.IsAlive)
			{
				return false;
			}

			health = damage.ResourceInstance != null && damage.ResourceInstance.FinalValue > 0
				? damage.ResourceInstance.CurrentValue / damage.ResourceInstance.FinalValue
				: 1f;
			return true;
		}

		/// <summary>
		/// Whether a member at this health fraction needs help. Pure, so the rule can be pinned.
		/// </summary>
		/// <param name="healthFraction">Current over maximum health.</param>
		/// <returns>True below full health.</returns>
		public static bool IsWounded(float healthFraction)
		{
			return healthFraction < 1f;
		}

		/// <summary>
		/// Gives every member fighting the focus its slot on the tactic's ring.
		/// </summary>
		/// <remarks>
		/// Only members fighting the pack's focus take part; one fighting somebody else orbits on
		/// its own. The slots are consumed by <see cref="OrbitState"/>.
		/// </remarks>
		private void AssignTacticalPositions(float elapsed)
		{
			ICharacter focus = GroupTargetCharacter;
			if (Tactic == PackTactic.None || focus == null)
			{
				ClearSlots();
				return;
			}

			Vector3 focusPosition = focus.Transform.position;
			formationMembers.Clear();
			formationRoles.Clear();
			formationBearings.Clear();

			float flankFront = BearingOf(focus.Transform.forward);
			bool hasTank = false;

			for (int i = 0; i < members.Count; ++i)
			{
				NPCGroupMember member = members[i];
				AIController controller = member.Controller;
				ICharacter target = controller != null && controller.IsInCombatState ? controller.TargetCharacter : null;
				if (target == null || target.ID != focus.ID || !TryGetHealthFraction(controller, out _))
				{
					if (controller != null)
					{
						controller.ClearPackSlot();
					}
					continue;
				}

				float bearing = BearingOf(controller.Character.Transform.position - focusPosition);
				formationMembers.Add(controller);
				formationRoles.Add(member.Role);
				formationBearings.Add(bearing);

				// A flank's front is where the tank stands; without one, it is where the enemy faces.
				if (member.Role == NPCGroupRole.Tank && !hasTank)
				{
					hasTank = true;
					flankFront = bearing;
				}
			}

			if (formationMembers.Count == 0)
			{
				holdsSlots = false;
				return;
			}

			float kiteAdvance = KiteRotationSpeed * Mathf.Deg2Rad * Mathf.Max(0f, elapsed);
			AssignFormation(Tactic, formationRoles, formationBearings, flankFront, kiteAdvance, formationOrder, formationAngles);

			for (int i = 0; i < formationMembers.Count; ++i)
			{
				formationMembers[i].SetPackSlot(formationAngles[i]);
			}
			holdsSlots = true;
		}

		/// <summary>
		/// The bearing of a horizontal offset, in the convention <see cref="OrbitState"/> places by:
		/// <c>(cos a, 0, sin a)</c>.
		/// </summary>
		/// <param name="offset">The offset.</param>
		/// <returns>The bearing in radians; 0 for an offset with no horizontal length.</returns>
		public static float BearingOf(Vector3 offset)
		{
			if (offset.x * offset.x + offset.z * offset.z < 1e-8f)
			{
				return 0f;
			}
			return Mathf.Atan2(offset.z, offset.x);
		}

		/// <summary>
		/// Assigns each formation member a bearing around the focus for a tactic.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Fitted to where the members already stand, never to the world axes.</b> The slots
		/// used to be absolute angles from world +X, so a Flank's "front" was east whatever the
		/// enemy faced and a FocusFire cluster formed on the east side even when the pack had come
		/// from the west. Now:
		/// </para>
		/// <list type="bullet">
		///   <item><b>Surround</b> — an even ring, rotated to the angle that moves the members least
		///   (the circular mean of each member's bearing less its ring step), members kept in the
		///   order they stand around the enemy.</item>
		///   <item><b>Kite</b> — the same ring advanced by <paramref name="kiteAdvance"/>, so the
		///   ring turns from wherever the members are rather than from a stored angle they have
		///   drifted away from.</item>
		///   <item><b>FocusFire</b> — a <see cref="CLUSTER_SPREAD"/> arc centred on the members' mean
		///   bearing: they converge from the side they are already on.</item>
		///   <item><b>Flank</b> — tanks at <paramref name="flankFront"/>, everyone else across the
		///   rear half-circle in the order they stand; a single flanker directly behind. (It used to
		///   put a lone flanker at the side, 90° from the front.)</item>
		/// </list>
		/// <para>Pure, so every tactic's geometry can be pinned without a scene.</para>
		/// </remarks>
		/// <param name="tactic">The tactic.</param>
		/// <param name="roles">Each formation member's role.</param>
		/// <param name="bearings">Each member's current bearing from the focus, in radians.</param>
		/// <param name="flankFront">The Flank tactic's front bearing: the tank's, or the direction the enemy faces.</param>
		/// <param name="kiteAdvance">Radians the Kite ring turns this evaluation.</param>
		/// <param name="order">Scratch list, overwritten.</param>
		/// <param name="angles">Receives each member's assigned bearing, index for index; a member's current bearing when the tactic is None.</param>
		public static void AssignFormation(PackTactic tactic, IReadOnlyList<NPCGroupRole> roles, IReadOnlyList<float> bearings,
			float flankFront, float kiteAdvance, List<int> order, List<float> angles)
		{
			angles.Clear();
			order.Clear();
			int count = bearings.Count;
			for (int i = 0; i < count; ++i)
			{
				angles.Add(bearings[i]);
			}
			if (count == 0)
			{
				return;
			}

			switch (tactic)
			{
				case PackTactic.Surround:
					AssignRing(bearings, 0f, order, angles);
					break;
				case PackTactic.Kite:
					AssignRing(bearings, kiteAdvance, order, angles);
					break;
				case PackTactic.FocusFire:
					AssignCluster(bearings, order, angles);
					break;
				case PackTactic.Flank:
					AssignFlank(roles, bearings, flankFront, order, angles);
					break;
			}
		}

		/// <summary>
		/// An even ring rotated to fit the members, then advanced.
		/// </summary>
		private static void AssignRing(IReadOnlyList<float> bearings, float advance, List<int> order, List<float> angles)
		{
			int count = bearings.Count;
			SortByBearing(bearings, 0f, false, null, order);

			float step = (Mathf.PI * 2f) / count;
			float sumX = 0f;
			float sumY = 0f;
			for (int k = 0; k < count; ++k)
			{
				float offset = bearings[order[k]] - k * step;
				sumX += Mathf.Cos(offset);
				sumY += Mathf.Sin(offset);
			}
			float rotation = (sumX * sumX + sumY * sumY) > 1e-8f
				? Mathf.Atan2(sumY, sumX)
				: bearings[order[0]];
			rotation += advance;

			for (int k = 0; k < count; ++k)
			{
				angles[order[k]] = Wrap(rotation + k * step);
			}
		}

		/// <summary>
		/// A tight arc centred on the members' mean bearing.
		/// </summary>
		private static void AssignCluster(IReadOnlyList<float> bearings, List<int> order, List<float> angles)
		{
			int count = bearings.Count;
			float sumX = 0f;
			float sumY = 0f;
			for (int i = 0; i < count; ++i)
			{
				sumX += Mathf.Cos(bearings[i]);
				sumY += Mathf.Sin(bearings[i]);
			}
			float centre = (sumX * sumX + sumY * sumY) > 1e-8f ? Mathf.Atan2(sumY, sumX) : bearings[0];

			SortByBearing(bearings, centre, true, null, order);

			float step = count > 1 ? CLUSTER_SPREAD / (count - 1) : 0f;
			float start = count > 1 ? -CLUSTER_SPREAD * 0.5f : 0f;
			for (int k = 0; k < count; ++k)
			{
				angles[order[k]] = Wrap(centre + start + k * step);
			}
		}

		/// <summary>
		/// Tanks at the front; everyone else spread over the rear half-circle.
		/// </summary>
		private static void AssignFlank(IReadOnlyList<NPCGroupRole> roles, IReadOnlyList<float> bearings, float front, List<int> order, List<float> angles)
		{
			for (int i = 0; i < bearings.Count; ++i)
			{
				if (roles != null && i < roles.Count && roles[i] == NPCGroupRole.Tank)
				{
					angles[i] = Wrap(front);
				}
			}

			SortByBearing(bearings, front, false, roles, order);
			int flankers = order.Count;
			for (int k = 0; k < flankers; ++k)
			{
				float slot = flankers > 1
					? Mathf.PI * 0.5f + Mathf.PI * k / (flankers - 1)
					: Mathf.PI;
				angles[order[k]] = Wrap(front + slot);
			}
		}

		/// <summary>
		/// Fills <paramref name="order"/> with member indices sorted by bearing relative to
		/// <paramref name="origin"/>, skipping tanks when <paramref name="skipTanksBy"/> is given.
		/// </summary>
		/// <param name="bearings">Member bearings.</param>
		/// <param name="origin">The bearing the sort measures from.</param>
		/// <param name="signed">True to measure in [-π, π), false in [0, 2π).</param>
		/// <param name="skipTanksBy">Roles to skip tanks by, or null to keep everyone.</param>
		/// <param name="order">Receives the sorted indices.</param>
		private static void SortByBearing(IReadOnlyList<float> bearings, float origin, bool signed, IReadOnlyList<NPCGroupRole> skipTanksBy, List<int> order)
		{
			order.Clear();
			for (int i = 0; i < bearings.Count; ++i)
			{
				if (skipTanksBy != null && i < skipTanksBy.Count && skipTanksBy[i] == NPCGroupRole.Tank)
				{
					continue;
				}

				// Insertion sort: a pack is a handful of members, and this allocates nothing.
				float relative = Relative(bearings[i], origin, signed);
				int at = order.Count;
				while (at > 0 && Relative(bearings[order[at - 1]], origin, signed) > relative)
				{
					--at;
				}
				order.Insert(at, i);
			}
		}

		/// <summary>
		/// A bearing relative to an origin, wrapped to [0, 2π) or [-π, π).
		/// </summary>
		private static float Relative(float bearing, float origin, bool signed)
		{
			float relative = Wrap(bearing - origin);
			if (signed && relative >= Mathf.PI)
			{
				relative -= Mathf.PI * 2f;
			}
			return relative;
		}

		/// <summary>
		/// An angle wrapped to [0, 2π).
		/// </summary>
		/// <param name="angle">Any angle, in radians.</param>
		/// <returns>The same direction in [0, 2π).</returns>
		public static float Wrap(float angle)
		{
			float twoPi = Mathf.PI * 2f;
			angle %= twoPi;
			if (angle < 0f)
			{
				angle += twoPi;
			}
			// A value a hair under zero wraps to exactly 2π in float; that is the same direction as 0.
			return angle >= twoPi ? 0f : angle;
		}
	}
}
