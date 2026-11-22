using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining an ability, including prefabs, triggers, event lists, and requirements.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability", menuName = "FishMMO/Character/Ability/Ability", order = 1)]
	public class AbilityTemplate : BaseAbilityTemplate, IAbilityTemplateInfo
	{
		/// <summary>
		/// The prefab for the ability object (visual or functional representation).
		/// </summary>
		public GameObject AbilityObjectPrefab;

		/// <summary>
		/// The spawn target type for the ability (where the ability effect appears).
		/// </summary>
		public AbilitySpawnTarget AbilitySpawnTarget;

		/// <summary>
		/// Whether the ability requires a target to be used.
		/// </summary>
		public bool RequiresTarget;

		/// <summary>
		/// Additional event slots for the ability (for extensibility).
		/// </summary>
		public byte AdditionalEventSlots;

		/// <summary>
		/// The number of times the ability can hit (e.g., for multi-hit abilities).
		/// </summary>
		public int HitCount;

		/// <summary>
		/// The ability object passes through scenery (ground, props, walls) and strikes only characters. For a volume that
		/// travels with its caster (a charge), whose stop and impact must answer to a body ahead, not to the ground it skims.
		/// </summary>
		[Tooltip("Pass through scenery: only characters are struck (a charge's volume skims the ground it rushes over).")]
		public bool StrikesCharactersOnly;

		/// <summary>
		/// The type of the ability (e.g., offensive, defensive, utility).
		/// </summary>
		public AbilityType Type;

		/// <summary>
		/// A charge-up the player chooses the length of: 0 or 1 for an ordinary cast, 2 or more for that many stages.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The hotkey is held (<see cref="AbilityController.RequiresHeld"/>) and the cast's <see cref="BaseAbilityTemplate.ActivationTime"/>
		/// is the full charge. Releasing early FIRES rather than cancels, at the stage reached: with three stages, before
		/// half the charge is stage 1, from half is stage 2, and the full charge (which fires by itself) is stage 3. Each
		/// stage evenly divides the time before the last. The stage reaches the ability's events through
		/// <see cref="Ability.ChargeStage"/> and <see cref="AbilityObject.ChargeStage"/>
		/// (<see cref="ChargeStageValue"/>, <see cref="CasterRushAction"/>).
		/// </para>
		/// </remarks>
		[Tooltip("0/1 = an ordinary cast. 2+ = hold to charge through this many stages; releasing early fires at the stage reached (ActivationTime is the full charge).")]
		public byte ChargeStages;

		/// <summary>
		/// What a <see cref="AbilityType.Block"/> ability's guard does while it is held. Ignored by every other type.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A Block ability is HELD (<see cref="AbilityController.RequiresHeld"/>): the guard is up from the first tick of the
		/// cast until the hotkey is released or <see cref="BaseAbilityTemplate.ActivationTime"/> (the longest hold) runs out,
		/// and then the cooldown starts. It spawns nothing and costs nothing to raise beyond its activation conditions; each
		/// hit it takes the edge off costs <see cref="AbilityGuard.CostResource"/> (see <see cref="AbilityGuard"/>).
		/// </para>
		/// <para>
		/// Read off the defender's cast state when a hit lands (<c>HeldGuardMitigation</c>), so there is no buff to keep in
		/// step with the hold: the guard is up exactly while the cast is, on every peer that knows the cast.
		/// </para>
		/// </remarks>
		[Tooltip("Block abilities only: what the held guard takes off frontal hits, and what each blocked hit costs.")]
		public AbilityGuard Guard = new AbilityGuard();

		/// <summary>
		/// Above zero: a HELD, PULSED ability — a beam. While the hotkey is held it fires every this many seconds (its object
		/// is spawned and its spawn chain runs, a hitscan for a beam) and pays its activation costs each time, from the press
		/// until release, <see cref="BaseAbilityTemplate.ActivationTime"/> (the longest hold) or a pulse it cannot pay for.
		/// Then the cooldown starts. Zero is an ordinary cast.
		/// </summary>
		/// <remarks>
		/// Unlike the channel marker (<see cref="AbilityController.ChanneledTemplate"/>), which spawns and charges every
		/// network tick, the pulse sets the rate. The pulses are counted from the cast's own countdown, which is reconciled,
		/// so the owner and the server fire on the same ticks.
		/// </remarks>
		[Tooltip("Above zero: held and pulsed (a beam). Fires and pays its costs every PulseSeconds while held; ActivationTime is the longest hold.")]
		[Min(0f)]
		public float PulseSeconds;

		/// <summary>
		/// Forces this ability's AI intent instead of deriving it from the action graph.
		/// </summary>
		/// <remarks>
		/// Leave at <see cref="AIAbilityIntent.None"/> for the normal case.
		/// <see cref="AIAbilityClassifier"/> reads the ECA actions the ability already carries and
		/// works out whether it heals, damages, controls or taunts, so archetypes never name
		/// specific abilities.
		/// <para>
		/// The override exists for the two cases classification cannot get right on its own: a
		/// custom action type it has never heard of, and a buff whose beneficial-or-detrimental
		/// nature the attribute-sign heuristic reads backwards. Setting it replaces the derived
		/// value entirely rather than adding to it.
		/// </para>
		/// </remarks>
		[Header("AI")]
		[Tooltip("Leave as None. Only set this when the AI misreads what the ability does — it replaces the derived intent entirely.")]
		public AIAbilityIntent IntentOverride = AIAbilityIntent.None;

		[Header("Event-Condition-Action (ECA) Triggers")]
		[Tooltip("Ability Events to execute when the ability object 'ticks' (e.g., moves, applies continuous effects).")]
		/// <summary>
		/// Events executed when the ability object ticks.
		/// </summary>
		public List<AbilityOnTickEvent> OnTickEvents = new List<AbilityOnTickEvent>();

		[Tooltip("Ability Events to execute when the ability object collides or hits a character.")]
		/// <summary>
		/// Events executed when the ability object collides or hits a character.
		/// </summary>
		public List<AbilityOnHitEvent> OnHitEvents = new List<AbilityOnHitEvent>();

		[Tooltip("Ability Events to execute before the primary ability object is spawned.")]
		/// <summary>
		/// Events executed before the primary ability object is spawned.
		/// </summary>
		public List<AbilityOnPreSpawnEvent> OnPreSpawnEvents = new List<AbilityOnPreSpawnEvent>();

		[Tooltip("Ability Events to execute when the primary ability object is spawned.")]
		/// <summary>
		/// Events executed when the primary ability object is spawned.
		/// </summary>
		public List<AbilityOnSpawnEvent> OnSpawnEvents = new List<AbilityOnSpawnEvent>();

		[Tooltip("Ability Events to execute when the ability object is destroyed.")]
		/// <summary>
		/// Events executed when the ability object is destroyed.
		/// </summary>
		public List<AbilityOnDestroyEvent> OnDestroyEvents = new List<AbilityOnDestroyEvent>();

		/// <summary>
		/// Cached list of all event IDs for this ability template.
		/// </summary>
		[System.NonSerialized]
		private List<int> cachedAllEventIDs;

		/// <summary>
		/// Returns a cached list of all unique event IDs from all event lists on this template.
		/// </summary>
		/// <returns>A list of all unique event IDs used by this ability template.</returns>
		public List<int> GetAllAbilityEventIDs()
		{
			if (cachedAllEventIDs != null)
			{
				return cachedAllEventIDs;
			}

			HashSet<int> eventIDs = new HashSet<int>();
			AppendEventIDs(OnTickEvents, eventIDs);
			AppendEventIDs(OnHitEvents, eventIDs);
			AppendEventIDs(OnPreSpawnEvents, eventIDs);
			AppendEventIDs(OnSpawnEvents, eventIDs);
			AppendEventIDs(OnDestroyEvents, eventIDs);
			if (this is PetAbilityTemplate petAbilityTemplate)
			{
				AppendEventIDs(petAbilityTemplate.OnSummonEvents, eventIDs);
			}

			cachedAllEventIDs = new List<int>(eventIDs);
			return cachedAllEventIDs;
		}

		/// <summary>
		/// Clears cached event ID data when the template is edited in the inspector.
		/// </summary>
		/// <remarks>Virtual so a subclass can add its own checks; an override must call this.</remarks>
		protected virtual void OnValidate()
		{
			cachedAllEventIDs = null;
		}

		/// <summary>
		/// Appends all non-null event IDs from the provided list into the target set without extra list allocations.
		/// </summary>
		/// <typeparam name="T">The concrete ability event type.</typeparam>
		/// <param name="source">The source list to scan.</param>
		/// <param name="eventIDs">The destination set receiving event IDs.</param>
		private static void AppendEventIDs<T>(List<T> source, HashSet<int> eventIDs) where T : AbilityEvent
		{
			if (source == null)
			{
				return;
			}

			for (int i = 0; i < source.Count; ++i)
			{
				T abilityEvent = source[i];
				if (abilityEvent != null)
				{
					eventIDs.Add(abilityEvent.ID);
				}
			}
		}

		/// <summary>
		/// Describes the ability as it would exist with only the effects this template ships with.
		/// </summary>
		/// <remarks>
		/// Composed rather than read off the fields. The template's own numbers are not the
		/// ability's numbers: the events bundled into it contribute cast time, cooldown, speed and
		/// lifetime too, and a tooltip that ignored them advertised a fireball with none of the
		/// speed its bundled movement effect supplies. <see cref="AbilitySummary"/> is the one
		/// place that composition happens.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content)
		{
			AbilitySummary.Compose(this).BuildTooltip(content);
		}
	}
}