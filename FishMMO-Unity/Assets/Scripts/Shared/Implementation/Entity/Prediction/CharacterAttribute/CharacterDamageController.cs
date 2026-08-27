using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls damage, healing, kill, and resurrection logic. Handles resistance
	/// calculation, ECA trigger dispatch, immortal state, combat state transitions,
	/// and combat-escape prevention via the <see cref="CharacterFlags.IsInCombat"/> flag.
	/// </summary>
	public class CharacterDamageController : CharacterBehaviour, ICharacterDamageController
	{
		// ───── ECA Trigger Lists ─────────────────────────────────────────────

		[Header("ECA - Damage")]
		[Tooltip("Triggers invoked when this character deals damage to another.")]
		/// <summary>Triggers invoked when this character deals damage to another.</summary>
		[SerializeField]
		private List<Trigger> onDamageTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character receives damage from another.")]
		/// <summary>Triggers invoked when this character receives damage from another.</summary>
		[SerializeField]
		private List<Trigger> onDamagedTriggers = new List<Trigger>();

		[Header("ECA - Healing")]
		[Tooltip("Triggers invoked when this character heals another.")]
		/// <summary>Triggers invoked when this character heals another.</summary>
		[SerializeField]
		private List<Trigger> onHealTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character is healed by another.")]
		/// <summary>Triggers invoked when this character is healed by another.</summary>
		[SerializeField]
		private List<Trigger> onHealedTriggers = new List<Trigger>();

		[Header("ECA - Kill")]
		[Tooltip("Triggers invoked when this character kills another.")]
		/// <summary>Triggers invoked when this character kills another.</summary>
		[SerializeField]
		private List<Trigger> onKillTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character is killed by another.")]
		/// <summary>Triggers invoked when this character is killed by another.</summary>
		[SerializeField]
		private List<Trigger> onKilledTriggers = new List<Trigger>();

		[Header("ECA - Resurrect")]
		[Tooltip("Triggers invoked when this character resurrects another.")]
		/// <summary>Triggers invoked when this character resurrects another.</summary>
		[SerializeField]
		private List<Trigger> onResurrectTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character is resurrected by another.")]
		/// <summary>Triggers invoked when this character is resurrected by another.</summary>
		[SerializeField]
		private List<Trigger> onResurrectedTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnDamageTriggers => onDamageTriggers;
		/// <inheritdoc />
		public List<Trigger> OnDamagedTriggers => onDamagedTriggers;
		/// <inheritdoc />
		public List<Trigger> OnHealTriggers => onHealTriggers;
		/// <inheritdoc />
		public List<Trigger> OnHealedTriggers => onHealedTriggers;
		/// <inheritdoc />
		public List<Trigger> OnKillTriggers => onKillTriggers;
		/// <inheritdoc />
		public List<Trigger> OnKilledTriggers => onKilledTriggers;
		/// <inheritdoc />
		public List<Trigger> OnResurrectTriggers => onResurrectTriggers;
		/// <inheritdoc />
		public List<Trigger> OnResurrectedTriggers => onResurrectedTriggers;

		/// <summary>
		/// If true, this character cannot be damaged or killed.
		/// </summary>
		[SerializeField]
		private bool immortal = false;
		/// <summary>
		/// Gets or sets whether the character is immortal (cannot be damaged or killed).
		/// </summary>
		public bool Immortal { get { return this.immortal; } set { this.immortal = value; } }

		// ───── Combat State ─────────────────────────────────────────────────

		[Header("Combat")]
		[Tooltip("Duration in ticks before combat ends after the last combat action. Default 600 = 20s at 30 tick/s.")]
		/// <summary>Duration in ticks before combat ends after the last combat action.</summary>
		[SerializeField]
		private uint combatDurationTicks = 600;

		/// <summary>
		/// The replicate-domain tick of the last combat action (damage dealt/received, or healing an in-combat ally).
		/// Used with <see cref="combatDurationTicks"/> to manage the <see cref="CharacterFlags.IsInCombat"/> flag.
		/// </summary>
		private uint lastCombatTick = 0;

		/// <summary>
		/// True while the character has seen at least one combat action and the timer has not expired.
		/// </summary>
		private bool combatTimerActive = false;

		/// <summary>
		/// Cached reference to the prediction controller for replicate-domain tick resolution.
		/// </summary>
		private CharacterPredictionController predictionController;

		// ───── Loot Contribution State ──────────────────────────────────────

		/// <summary>
		/// One entry per character credited with a share of this character's death.
		/// </summary>
		private struct ContributorEntry
		{
			/// <summary>The contributor's own damage controller, held so the link can be undone from either end.</summary>
			public CharacterDamageController Controller;
			/// <summary>How the credit was first earned.</summary>
			public CombatContributionKind Kind;
		}

		/// <summary>
		/// Characters credited with a share of THIS character's death, keyed by character ID.
		/// Server-only; null until the first contribution is recorded.
		/// </summary>
		private Dictionary<long, ContributorEntry> contributors;

		/// <summary>
		/// The characters THIS character is credited against — the reverse of
		/// <see cref="contributors"/>, kept so a heal can find everything the healed character is
		/// fighting without a global index, and so either end can undo the link.
		/// </summary>
		private HashSet<CharacterDamageController> contributionTargets;

		/// <summary>
		/// Scratch buffer for iterating <see cref="contributionTargets"/> while recording into it.
		/// </summary>
		private static readonly List<CharacterDamageController> contributionIterationBuffer = new List<CharacterDamageController>();

		/// <summary>
		/// Gets whether this character is currently in combat (within the combat duration window).
		/// </summary>
		public bool IsInCombat => combatTimerActive;

		/// <summary>
		/// Gets the tick of the last combat action.
		/// </summary>
		public uint LastCombatTick => lastCombatTick;

		/// <summary>
		/// Gets the configured combat duration in ticks.
		/// </summary>
		public uint CombatDurationTicks => combatDurationTicks;

		/// <summary>
		/// Returns true if the character is alive (resource attribute's current value is above zero).
		/// </summary>
		public bool IsAlive
		{
			get
			{
				if (ResourceInstance == null)
				{
					return false;
				}
				return ResourceInstance.CurrentValue > 0;
			}
		}

		//public List<Character> Attackers; // Uncomment and implement if tracking attackers is needed.

		/// <summary>
		/// Cached reference to the character's health resource attribute.
		/// Lazily initialized on first access; throws if missing.
		/// </summary>
		private CharacterResourceAttribute resourceInstance;
		/// <summary>
		/// Gets the cached health resource attribute for this character.
		/// Returns null and logs an error if the attribute controller or health attribute is missing.
		/// </summary>
		public CharacterResourceAttribute ResourceInstance
		{
			get
			{
				if (resourceInstance == null)
				{
					if (!Character.TryGet(out ICharacterAttributeController attributeController) ||
						!attributeController.TryGetHealthAttribute(out resourceInstance))
					{
						Log.Error("CharacterDamageController", $"{gameObject.name} is missing ICharacterAttributeController or Health Resource Attribute.");
					}
				}
				return resourceInstance;
			}
		}

		// ───── Network Lifecycle ────────────────────────────────────────────

		/// <summary>
		/// Caches the prediction controller reference and subscribes to tick events for combat timer management.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			predictionController = GetComponent<CharacterPredictionController>();

			/* Register the shared death-state handler the first time any character starts on this
			 * client. Never unregistered: ClientManager does not clear handlers on stop, so a
			 * per-character unregister would have to be reference counted or the first despawn
			 * would leave every remaining character unable to show a death. */
			if (base.IsClientStarted)
			{
				RegisterDeathStateBroadcast(base.NetworkManager);
			}

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Unsubscribes from tick events and clears cached references.
		/// </summary>
		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}

			/* A character leaving the world must not stay on other characters' contributor lists.
			 * A logged-out player holds no loot rights worth preserving, and the stale entry would
			 * otherwise survive as a reference to a despawned controller. */
			ClearCombatContributions();

			predictionController = null;

			base.OnStopNetwork();
		}

		// ───── Combat Timer ─────────────────────────────────────────────────

		/// <summary>
		/// Tick-aligned combat timer. Called every tick on both client and server.
		/// Clears the <see cref="CharacterFlags.IsInCombat"/> flag when the combat duration
		/// has elapsed since the last combat action.
		/// </summary>
		private void TimeManager_OnTick()
		{
			if (!combatTimerActive || Character == null)
			{
				return;
			}

			uint currentTick = ResolveCurrentCombatTick();
			if (currentTick == TimeManager.UNSET_TICK)
			{
				return;
			}

			if (EvaluateCombatTimer(currentTick, combatDurationTicks, ref lastCombatTick) == CombatTimerStep.Expired)
			{
				combatTimerActive = false;
				Character.DisableFlags(CharacterFlags.IsInCombat);

				/* Leaving combat expires loot rights in both directions. Without this, tagging a
				 * creature for one point of damage and walking away would still entitle the
				 * tagger to loot it half an hour later when somebody else finally killed it. The
				 * combat window is already the game's definition of "engaged", so reusing it
				 * keeps tag expiry and combat state from ever disagreeing. */
				ClearCombatContributions();
			}
		}

		/// <summary>
		/// Outcome of one evaluation of the combat timer.
		/// </summary>
		public enum CombatTimerStep
		{
			/// <summary>Still in combat; the window has not elapsed.</summary>
			Continue = 0,
			/// <summary>
			/// The reference tick moved backwards, so the window was re-measured from the new
			/// value. The character stays in combat.
			/// </summary>
			Rebaselined = 1,
			/// <summary>The window elapsed; the character should leave combat.</summary>
			Expired = 2,
		}

		/// <summary>
		/// Decides whether the combat window has elapsed, tolerating a reference tick that moves
		/// backwards.
		/// </summary>
		/// <remarks>
		/// Pure and static so the arithmetic can be proven in isolation — the surrounding
		/// behaviour needs a live FishNet TimeManager, which would otherwise make this logic
		/// untestable.
		/// <para>
		/// The subtraction is unsigned, so a regression of even one tick becomes a value near
		/// <see cref="uint.MaxValue"/>, trivially satisfies the expiry test, and silently drops
		/// the character out of combat. That is the state the teleport gate and the
		/// combat-logout hold both key off, so it reads as a combat-escape exploit rather than a
		/// clock glitch.
		/// </para>
		/// <para>
		/// The regression is not hypothetical. <c>ResolveCurrentCombatTick</c> prefers the
		/// owner's replicate tick, which in client-side prediction runs AHEAD of the server's
		/// local tick; the moment ownership is removed — which is exactly what starting a
		/// combat-logout linger does — <c>IsController</c> flips true on the server and the
		/// resolver falls back to that slower local tick. A client hitch or a reconnect moves it
		/// backwards the same way.
		/// </para>
		/// <para>
		/// Re-baselining rather than expiring means the character stays in combat and the window
		/// is measured afresh from the new domain, so an ownership handover costs the player a
		/// fresh combat window instead of instantly clearing their combat state.
		/// </para>
		/// </remarks>
		/// <param name="currentTick">The tick to evaluate against.</param>
		/// <param name="combatDurationTicks">Ticks of inactivity before combat ends.</param>
		/// <param name="lastCombatTick">Tick of the last combat action; re-baselined on regression.</param>
		/// <returns>What the caller should do about this tick.</returns>
		public static CombatTimerStep EvaluateCombatTimer(uint currentTick, uint combatDurationTicks, ref uint lastCombatTick)
		{
			if (currentTick < lastCombatTick)
			{
				lastCombatTick = currentTick;
				return CombatTimerStep.Rebaselined;
			}

			return currentTick - lastCombatTick >= combatDurationTicks
				? CombatTimerStep.Expired
				: CombatTimerStep.Continue;
		}

		/// <summary>
		/// Resolves the best available tick for combat timer evaluation.
		/// Prefers the prediction controller's replicate-domain tick when available,
		/// falling back to the raw TimeManager LocalTick.
		/// </summary>
		private uint ResolveCurrentCombatTick()
		{
			if (predictionController != null)
			{
				if (predictionController.CurrentReplicateTickSnapshot != TimeManager.UNSET_TICK)
				{
					return predictionController.CurrentReplicateTickSnapshot;
				}
				if (predictionController.CurrentLocalTickSnapshot != TimeManager.UNSET_TICK)
				{
					return predictionController.CurrentLocalTickSnapshot;
				}
			}
			if (base.TimeManager != null)
			{
				return base.TimeManager.LocalTick;
			}
			return TimeManager.UNSET_TICK;
		}

		/// <summary>
		/// Enters combat state, refreshing the timer. Sets <see cref="CharacterFlags.IsInCombat"/>
		/// and records the current tick. Safe to call every combat action — repeated calls
		/// within the combat window simply refresh the expiry.
		/// </summary>
		public void EnterCombat()
		{
			if (Character == null)
			{
				return;
			}

			uint currentTick = ResolveCurrentCombatTick();
			if (currentTick == TimeManager.UNSET_TICK)
			{
				// No tick source available (TimeManager not yet wired, prediction
				// controller not present). Defer combat entry until the first tick
				// where ResolveCurrentCombatTick returns a valid value.
				return;
			}

			lastCombatTick = currentTick;

			if (!combatTimerActive)
			{
				combatTimerActive = true;
				Character.EnableFlags(CharacterFlags.IsInCombat);
			}
		}

		// ───── Loot Contribution ────────────────────────────────────────────

		/// <summary>
		/// Resolves the character that should actually be credited for an action.
		/// </summary>
		/// <remarks>
		/// A pet's kills belong to its owner, and nothing that is not ultimately a player can be
		/// credited at all — loot rights exist to decide who may open a window, and an NPC has no
		/// window to open. Returning null is the normal answer for NPC-on-NPC combat.
		/// </remarks>
		/// <param name="contributor">The character that performed the action.</param>
		/// <returns>The player to credit, or null when no player is responsible.</returns>
		private static IPlayerCharacter ResolveContributionCredit(ICharacter contributor)
		{
			if (contributor == null)
			{
				return null;
			}

			if (contributor is IPlayerCharacter player)
			{
				return player;
			}

			/* A pet is an NPC, so without this it would be discarded by the test above and a
			 * hunter who killed something entirely through their pet would earn no loot rights on
			 * their own kill. */
			if (contributor is Pet pet)
			{
				return pet.PetOwner as IPlayerCharacter;
			}

			return null;
		}

		/// <summary>
		/// Resolves the character a kill should be attributed to.
		/// </summary>
		/// <remarks>
		/// The mirror of <see cref="ResolveContributionCredit"/> for the kill itself. It differs
		/// in one way: this one falls back to the killer rather than to null, because an NPC
		/// killing another NPC still legitimately runs its own kill triggers — there is simply
		/// no loot window involved.
		/// </remarks>
		/// <param name="killer">The character that landed the killing blow.</param>
		/// <returns>The character to credit, or null when there was no killer.</returns>
		private static ICharacter ResolveKillCredit(ICharacter killer)
		{
			if (killer is Pet pet && pet.PetOwner != null)
			{
				return pet.PetOwner;
			}

			return killer;
		}

		/// <inheritdoc />
		public void RecordCombatContribution(ICharacter contributor, CombatContributionKind kind)
		{
			// Contribution decides loot rights, so it is authoritative state and is tracked only
			// where loot is granted. A client tracking it would just be building a list nothing reads.
			if (!base.IsServerStarted)
			{
				return;
			}

			IPlayerCharacter credit = ResolveContributionCredit(contributor);
			if (credit == null || ReferenceEquals(credit, Character))
			{
				return;
			}

			if (!credit.TryGet(out ICharacterDamageController creditDamageController))
			{
				return;
			}
			CharacterDamageController creditController = creditDamageController as CharacterDamageController;

			contributors ??= new Dictionary<long, ContributorEntry>();

			/* First credit wins. Overwriting would let a stray debuff late in a fight relabel a
			 * contributor who had been hitting the target since the pull — which matters only for
			 * diagnostics today, but silently rewriting recorded history is not worth the saving
			 * of a dictionary probe. */
			if (!contributors.ContainsKey(credit.ID))
			{
				contributors[credit.ID] = new ContributorEntry()
				{
					Controller = creditController,
					Kind = kind,
				};
			}

			if (creditController != null)
			{
				creditController.contributionTargets ??= new HashSet<CharacterDamageController>();
				creditController.contributionTargets.Add(this);
			}
		}

		/// <inheritdoc />
		public void PropagateCombatContribution(ICharacter supporter)
		{
			if (!base.IsServerStarted ||
				contributionTargets == null ||
				contributionTargets.Count < 1)
			{
				return;
			}

			IPlayerCharacter credit = ResolveContributionCredit(supporter);
			if (credit == null || ReferenceEquals(credit, Character))
			{
				// Self-healing earns nothing new: the healer is already on every list this
				// character is on, which is precisely the set being iterated below.
				return;
			}

			/* Snapshot before iterating. RecordCombatContribution writes into the SUPPORTER's
			 * contributionTargets rather than this one, so the collection being walked is not the
			 * one being mutated — but that is a property of the current call graph rather than of
			 * the data structure, and a buffered walk costs nothing to make it unconditional. */
			contributionIterationBuffer.Clear();
			contributionIterationBuffer.AddRange(contributionTargets);

			for (int i = 0; i < contributionIterationBuffer.Count; ++i)
			{
				CharacterDamageController victim = contributionIterationBuffer[i];
				if (victim == null)
				{
					continue;
				}
				victim.RecordCombatContribution(supporter, CombatContributionKind.Healing);
			}

			contributionIterationBuffer.Clear();
		}

		/// <inheritdoc />
		public bool TryConsumeContributors(out List<long> contributorIDs)
		{
			if (contributors == null || contributors.Count < 1)
			{
				contributorIDs = null;
				ClearCombatContributions();
				return false;
			}

			contributorIDs = new List<long>(contributors.Keys);
			ClearCombatContributions();
			return true;
		}

		/// <inheritdoc />
		public void ClearCombatContributions()
		{
			/* Both directions, and each from the side that owns the reference. Clearing only this
			 * character's own dictionary would leave every contributor still holding a link back
			 * to it, so a later heal would push credit onto a corpse whose rights had already been
			 * handed out — and, on a pooled NPC, onto whatever creature next occupied the slot. */
			if (contributors != null)
			{
				foreach (ContributorEntry entry in contributors.Values)
				{
					entry.Controller?.contributionTargets?.Remove(this);
				}
				contributors.Clear();
			}

			if (contributionTargets != null)
			{
				// Character can be null while the behaviour is being torn down; without an ID
				// there is nothing to remove and the far side's entry is cleared by its own reset.
				long selfID = Character != null ? Character.ID : 0;
				if (selfID != 0)
				{
					foreach (CharacterDamageController victim in contributionTargets)
					{
						victim?.contributors?.Remove(selfID);
					}
				}
				contributionTargets.Clear();
			}
		}

		// ───── Death Replication ────────────────────────────────────────────

		/// <summary>
		/// Tells observers this character has just died so they can pose it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Death was previously invisible to everyone except the dying player. <see cref="Kill"/>
		/// runs only on the server, and <c>TriggerDeath</c> compiles out of server builds, so the
		/// only thing that ever posed a corpse was <c>RestorePoseForCurrentState</c> reading
		/// <see cref="CharacterFlags.IsDead"/> out of a spawn payload — which covers a client that
		/// arrives after the death and nobody who was already watching. An NPC killed in front of
		/// a player simply stopped moving, upright.
		/// </para>
		/// <para>
		/// Deliberately not buffered. Late observers are served by the spawn payload, which
		/// carries the flag for both players and NPCs, and a buffered RPC on a pooled NPC is a
		/// message whose lifetime is tied to the pool slot rather than to the creature.
		/// </para>
		/// </remarks>
		/// <param name="dead">True on death, false when the character is revived.</param>
		private void BroadcastDeathState(bool dead)
		{
			ApplyDeathState(dead);

			if (base.NetworkManager == null || base.NetworkObject == null || !base.IsServerStarted)
			{
				return;
			}

			base.NetworkManager.ServerManager.Broadcast(base.NetworkObject, new CharacterDeathStateBroadcast
			{
				CharacterObjectID = base.NetworkObject.ObjectId,
				Dead = dead,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>Applies a death state locally, on the server or on a receiving client.</summary>
		/// <remarks>
		/// Applied locally by the sender before broadcasting, because a broadcast is never delivered
		/// back to its own sender — the previous <c>ObserversRpc</c> reached the server through
		/// FishNet's own dispatch, and without this the server would broadcast a death it never
		/// applied to itself.
		/// </remarks>
		private void ApplyDeathState(bool dead)
		{
			if (Character == null)
			{
				return;
			}

			if (dead)
			{
				Character.EnableFlags(CharacterFlags.IsDead);
			}
			else
			{
				Character.DisableFlags(CharacterFlags.IsDead);
			}

			if (Character.TryGet(out ICharacterAnimationController animationController))
			{
				if (dead)
				{
					animationController.TriggerDeath();
				}
				else
				{
					animationController.ResetDeath();
				}
			}
		}

		/// <summary>True once this client has registered the shared death-state handler.</summary>
		private static bool deathStateBroadcastRegistered;

		/// <summary>Registers the shared death-state handler for this client.</summary>
		/// <remarks>
		/// Registered once per client rather than per character, so one death costs one delegate
		/// call rather than one per character in the scene.
		/// </remarks>
		internal static void RegisterDeathStateBroadcast(FishNet.Managing.NetworkManager networkManager)
		{
			if (deathStateBroadcastRegistered || networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<CharacterDeathStateBroadcast>(OnDeathStateBroadcast);
			deathStateBroadcastRegistered = true;
		}

		/// <summary>Applies a death-state broadcast to whichever character it names.</summary>
		private static void OnDeathStateBroadcast(CharacterDeathStateBroadcast msg, FishNet.Transporting.Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CharacterObjectID, out FishNet.Object.NetworkObject nob) ||
				nob == null)
			{
				return;
			}

			CharacterDamageController controller = nob.GetComponent<CharacterDamageController>();
			controller?.ApplyDeathState(msg.Dead);
		}

		// ───── Damage / Resistance ──────────────────────────────────────────

		/// <summary>
		/// Applies resistance modifiers to the damage amount for the target character.
		/// Subtracts the target's resistance value from the incoming damage and clamps the result.
		/// <para>
		/// If <paramref name="target"/> has no <see cref="ICharacterAttributeController"/> or
		/// <paramref name="damageAttribute"/> is null, the resistance lookup is skipped and the
		/// original <paramref name="amount"/> is returned unchanged — absence of resistance
		/// metadata means untyped damage, not immunity.
		/// </para>
		/// </summary>
		/// <param name="target">The character receiving damage.</param>
		/// <param name="amount">The base damage amount.</param>
		/// <param name="damageAttribute">The damage type being applied.</param>
		/// <returns>The modified damage amount after resistance is applied.</returns>
		public int ApplyModifiers(ICharacter target, int amount, DamageAttributeTemplate damageAttribute)
		{
			const int MIN_DAMAGE = 0;
			const int MAX_DAMAGE = 999999;

			if (target == null || damageAttribute == null)
			{
				return amount;
			}

			// No attribute controller means no resistance stats — pass through at full value.
			// Returning 0 here would make the character silently invulnerable, which is wrong.
			if (!target.TryGet(out ICharacterAttributeController attributeController))
			{
				return amount;
			}

			// Resistance may be null for damage types that intentionally bypass all resistance
			// (environmental hazards, true damage, etc.). Guard before accessing .ID to prevent NPE.
			if (damageAttribute.Resistance != null &&
				attributeController.TryGetAttribute(damageAttribute.Resistance.ID, out CharacterAttribute resistance))
			{
				amount = (amount - resistance.FinalValue).Clamp(MIN_DAMAGE, MAX_DAMAGE);
			}
			return amount;
		}

		/// <summary>
		/// Applies damage to this character from an attacker. Handles resistance calculation,
		/// kill detection, combat state, and ECA trigger dispatch. Does nothing if the character
		/// is immortal or already dead. Resistance-reduced damage below 1 is silently discarded.
		/// </summary>
		/// <param name="attacker">The character dealing damage, or null for environmental damage.</param>
		/// <param name="amount">Base damage before resistance is applied.</param>
		/// <param name="damageAttribute">The damage type; determines which resistance stat is checked.</param>
		/// <param name="ignoreAchievements">If true, suppresses ECA trigger dispatch for this hit.</param>
		public void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false)
		{
			if (Immortal)
			{
				return;
			}

			if (ResourceInstance == null)
			{
				return;
			}

			// We are already dead.
			if (ResourceInstance.CurrentValue <= 0.0f)
			{
				return;
			}

			amount = ApplyModifiers(Character, amount, damageAttribute);

			if (amount < 1)
			{
				return;
			}
			ResourceInstance.Consume(amount);

			// Enter combat: both defender (self) and attacker.
			EnterCombat();
			if (attacker != null &&
				attacker.TryGet(out ICharacterDamageController attackerDamageController))
			{
				attackerDamageController.EnterCombat();
			}

			/* Recorded after the damage has actually landed, so the early returns above — immortal,
			 * already dead, fully resisted — do not hand out loot rights for a hit that did
			 * nothing. */
			RecordCombatContribution(attacker, CombatContributionKind.Damage);

			ICharacterDamageController.OnDamaged?.Invoke(attacker, Character, amount, damageAttribute);

			if (!ignoreAchievements)
			{
				// Invoke attacker's OnDamage triggers (e.g. achievements for dealing damage)
				if (attacker != null &&
					attacker.TryGet(out ICharacterDamageController attackerDamage))
				{
					attacker.Invoke(attackerDamage.OnDamageTriggers, new DamageEventData(attacker, Character, amount, damageAttribute));
				}

				// Invoke defender's OnDamaged triggers (e.g. achievements for receiving damage)
				Character.Invoke(OnDamagedTriggers, new DamageEventData(Character, attacker, amount, damageAttribute));
			}

			// Check if we died after taking damage.
			if (ResourceInstance.CurrentValue <= 0.0f)
			{
				Kill(attacker);
			}
		}

		/// <summary>
		/// Kills this character. Handles faction rewards, ECA triggers, ability cancellation,
		/// death animation, and the OnKilled event. Buff removal and pet despawning are handled
		/// by the server-side OnKilled subscriber (CharacterSystem.Connection.cs).
		/// </summary>
		/// <param name="killer">The character responsible for the kill, or null for non-player kills.</param>
		public void Kill(ICharacter killer)
		{
			if (Immortal) return;
			if (!base.IsServerStarted) return;

			// Already dead — prevent duplicate OnKilled events and ECA triggers.
			if (Character.IsFlagged(CharacterFlags.IsDead)) return;

			/* Set the flag the guard above reads, here, rather than trusting a subscriber to do
			 * it. CharacterSystem's OnKilled handler sets it for players and for players only, so
			 * an NPC never carried it: the guard was permanently false for every NPC in the game
			 * and a second Kill call ran the whole death path again — duplicate OnKilled, duplicate
			 * ECA triggers, and with corpse looting, a second roll of the loot table. It also has
			 * to be set before the subscribers run, because NPC.Despawn is one of them and the
			 * corpse it creates is only correct for something already marked dead. */
			Character.EnableFlags(CharacterFlags.IsDead);

			// Clear combat state on death.
			combatTimerActive = false;
			Character.DisableFlags(CharacterFlags.IsInCombat);

			/* Credit the owner, not the pet.
			 *
			 * Quest objectives, achievements and faction gains are all driven off the killer's
			 * OnKillTriggers, and a pet is an NPC whose prefab carries none — so a player who
			 * killed something entirely through their pet advanced no quest, earned no
			 * achievement and gained no standing. FactionController also refuses adjustments for
			 * anything that is an NPC, so even a pet with triggers configured could not have
			 * earned the standing. Loot rights already resolved through to the owner
			 * (ResolveContributionCredit); this is the other half of the same rule. */
			ICharacter creditedKiller = ResolveKillCredit(killer);
			if (creditedKiller != null)
			{
				if (creditedKiller.TryGet(out IFactionController fc) &&
					Character.TryGet(out IFactionController dfc))
					fc.AdjustFaction(dfc, 0.01f, 0.01f);

				if (creditedKiller.TryGet(out ICharacterDamageController kdc))
					creditedKiller.Invoke(kdc.OnKillTriggers, new EventData(creditedKiller, Character));
			}

			Character.Invoke(OnKilledTriggers, new EventData(Character, killer));

			if (base.IsServerStarted && Character.TryGet(out IAbilityController ac))
				ac.Cancel();

			if (Character.TryGet(out ICharacterAnimationController anim))
				anim.TriggerDeath();

			// Observers pose the corpse from here; the owner and late joiners are covered by the
			// death broadcast and the spawn payload respectively.
			BroadcastDeathState(true);

			InvokeKilledIsolated(killer, Character);
		}

		/// <summary>
		/// Raises <see cref="ICharacterDamageController.OnKilled"/>, invoking each subscriber
		/// independently so one failure cannot suppress the rest.
		/// </summary>
		/// <remarks>
		/// A plain multicast invoke abandons the remainder of the list at the first exception.
		/// That is unusually costly for this event: its subscribers are the scene server's
		/// <c>CharacterSystem</c> — which sets <see cref="CharacterFlags.IsDead"/> and sends the
		/// client its <c>DeathBroadcast</c> — plus one <c>AggressionState</c> per aggressive NPC,
		/// registered at runtime. A single throwing NPC handler could therefore stop a player
		/// ever being told they died, leaving them with no death dialog and no way to respawn.
		/// <para>
		/// It would also disarm this method's own re-entry guard, which tests the very flag that
		/// <c>CharacterSystem</c>'s handler sets: with that handler skipped, the character is
		/// never marked dead and a subsequent <see cref="Kill"/> would run the whole path again.
		/// </para>
		/// <para>
		/// Applied here and not to <c>OnDamaged</c>/<c>OnHealed</c> deliberately.
		/// <see cref="Delegate.GetInvocationList"/> allocates an array per call, which is
		/// acceptable for a death and not for something raised on every hit.
		/// </para>
		/// </remarks>
		private static void InvokeKilledIsolated(ICharacter killer, ICharacter victim)
		{
			Action<ICharacter, ICharacter> handler = ICharacterDamageController.OnKilled;
			if (handler == null)
			{
				return;
			}

			Delegate[] subscribers = handler.GetInvocationList();
			for (int i = 0; i < subscribers.Length; ++i)
			{
				try
				{
					((Action<ICharacter, ICharacter>)subscribers[i]).Invoke(killer, victim);
				}
				catch (Exception ex)
				{
					Log.Error("CharacterDamageController",
						$"An OnKilled subscriber threw while handling the death of {victim?.ID}: {ex}");
				}
			}
		}

		/// <summary>
		/// Heals this character by the specified amount. Events and ECA triggers are only
		/// fired when healing actually changes the resource value; healing a dead character,
		/// healing for zero, or attempting to heal a full-health character are all silent no-ops.
		/// If the target is in combat, the healer also enters combat.
		/// </summary>
		/// <param name="healer">The character providing the healing, or null.</param>
		/// <param name="amount">The amount to heal.</param>
		/// <param name="ignoreAchievements">If true, suppresses ECA trigger dispatch.</param>
		public void Heal(ICharacter healer, int amount, bool ignoreAchievements = false)
		{
			/* A character at zero health is dead and cannot be healed — only revived.
			 *
			 * The test is the health value rather than CharacterFlags.IsDead on purpose. This
			 * runs in the prediction path, and Flags travels only in the spawn payload and is
			 * never re-synced, so a client's copy is stale from the first death onward; gating
			 * on it here would make client and server disagree about every later heal. The
			 * health value is replicated each reconcile, so both sides agree.
			 *
			 * That equivalence is only sound because nothing else raises health off zero
			 * behind this guard: Revive is the single sanctioned route (and it clears the dead
			 * flag), CompleteHeal applies the same zero test, and regeneration is skipped
			 * entirely while health is depleted — see CharacterAttributeController.Regenerate. */
			if (ResourceInstance == null || ResourceInstance.CurrentValue <= 0.0f)
			{
				return;
			}

			float valueBefore = ResourceInstance.CurrentValue;
			ResourceInstance.Gain(amount);

			// Suppress events if nothing actually changed (amount == 0 or resource was already full).
			// Firing OnHealed/achievement triggers for 0-effective healing wastes ECA evaluation
			// and can cause false achievement awards.
			if (ResourceInstance.CurrentValue <= valueBefore)
			{
				return;
			}

			// Enter combat: the healed target always enters combat.
			// Capture combat state BEFORE EnterCombat so we know if the defender
			// was already fighting — the healer only joins an existing combat.
			bool defenderWasInCombat = combatTimerActive;
			EnterCombat();

			// If the healer is healing someone who is already in combat, the healer also enters combat.
			if (healer != null && defenderWasInCombat &&
				healer.TryGet(out ICharacterDamageController healerDamageController))
			{
				healerDamageController.EnterCombat();
			}

			/* Healing earns loot rights on everything the healed character is fighting. Gated on
			 * the target already being in combat for the same reason the healer's own combat entry
			 * is: topping someone up between pulls is not participation in a kill. */
			if (healer != null && defenderWasInCombat)
			{
				PropagateCombatContribution(healer);
			}

			ICharacterDamageController.OnHealed?.Invoke(healer, Character, amount);

			if (!ignoreAchievements)
			{
				// Invoke healer's OnHeal triggers (e.g. achievements for healing)
				if (healer != null &&
					healer.TryGet(out ICharacterDamageController healerDamage))
				{
					healer.Invoke(healerDamage.OnHealTriggers, new HealEventData(healer, Character, amount));
				}

				// Invoke healed character's OnHealed triggers (e.g. achievements for being healed)
				Character.Invoke(OnHealedTriggers, new HealEventData(Character, healer, amount));
			}
		}

		/// <summary>
		/// Fully restores this character's health resource to its maximum (final) value.
		/// Does nothing if the character is dead.
		/// </summary>
		public void CompleteHeal()
		{
			if (ResourceInstance != null && ResourceInstance.CurrentValue > 0.0f)
			{
				float toHeal = ResourceInstance.FinalValue - ResourceInstance.CurrentValue;
				ResourceInstance.Gain(toHeal);
			}
		}

		/// <inheritdoc />
		public void Revive(ICharacter resurrector, int amount)
		{
			if (ResourceInstance == null || amount <= 0) return;

			/* Clearing the flag is part of reviving, not a step callers are trusted to remember.
			 *
			 * It used to be done by the two CharacterSystem broadcast handlers and nowhere else,
			 * so any other caller — an ability's ApplyReviveAction, a future system revive —
			 * restored health while leaving CharacterFlags.IsDead set. That character is then
			 * alive to everything that tests health and dead to everything that tests the flag:
			 * Kill() early-returns on the flag, so it can never be killed again, while Heal()
			 * sees a non-zero value and starts working. Doing it here makes "has health" and
			 * "is not dead" impossible to disagree, whoever performs the revive. */
			Character.DisableFlags(CharacterFlags.IsDead);

			// Gain bypasses Heal() dead-character guard -- works on CurrentValue == 0.
			ResourceInstance.Gain(amount);

			// Reset death animation on the client.
			if (Character.TryGet(out ICharacterAnimationController animController))
			{
				animController.ResetDeath();
			}

			// Observers are holding the death pose from BroadcastDeathState(true); without the
			// matching clear, a resurrected character stands up on its own screen and stays a
			// corpse on everyone else's.
			if (base.IsServerStarted)
			{
				BroadcastDeathState(false);
			}

			// Fire ECA resurrect triggers.
			if (resurrector != null)
			{
				resurrector.Invoke(onResurrectTriggers, new EventData(resurrector, Character));
			}
			Character.Invoke(onResurrectedTriggers, new EventData(Character, resurrector));

			ICharacterDamageController.OnResurrected?.Invoke(resurrector, Character);
		}

		/// <summary>
		/// Clears the cached health resource attribute reference and combat state
		/// so it is re-resolved on the next access. Prevents a stale object reference
		/// if the <see cref="CharacterAttributeController"/> is re-initialized
		/// (character pooling, hot-reload, or any scenario where attribute instances
		/// are recreated).
		/// </summary>
		public override void ResetState(bool asServer)
		{
			/* Before base.ResetState, which is where Character is liable to be dropped — the
			 * cleanup needs the character ID to unhook this controller from the far side of every
			 * contribution link. */
			ClearCombatContributions();

			base.ResetState(asServer);
			resourceInstance = null;
			combatTimerActive = false;
			lastCombatTick = 0;
			Character?.DisableFlags(CharacterFlags.IsInCombat);
			immortal = false;
		}
	}
}