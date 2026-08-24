using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Works out what an ability does by reading the ECA action graph the designer already built,
	/// so archetypes never have to name specific abilities.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What this replaces.</b> A healer archetype used to carry a list of heal ability template
	/// IDs, and a defender a list of taunt IDs. That list is a second, hand-maintained statement of
	/// something the ability asset already says: an ability with an <see cref="ApplyHealAction"/>
	/// in its on-hit event <em>is</em> a heal. Two statements of one fact drift — a designer adds a
	/// third heal and the archetype silently keeps casting only the two it knows, with no error
	/// anywhere. It also does not scale: every archetype needs the list, every new ability needs
	/// adding to every archetype that should use it, and a shared archetype cannot be reused across
	/// creatures with different spellbooks.
	/// </para>
	/// <para>
	/// <b>How it works.</b> An <see cref="AbilityTemplate"/>'s five event lists are
	/// <see cref="Trigger"/>s, and a trigger's actions are ordinary serialized objects. Walking
	/// them and looking at the action types present is enough to know whether an ability heals,
	/// damages, controls, dispels or taunts — no naming convention, no ID list, no extra field for
	/// a designer to forget.
	/// </para>
	/// <para>
	/// <b>Where it stops.</b> A buff that expresses its effect through ECA tick events is read the
	/// same way as everything else — an <see cref="ApplyDamageAction"/> on a tick is a
	/// damage-over-time effect and says so outright. The simple buff templates that express their
	/// effect as serialized numbers instead carry no "this is a debuff" flag, so those are separated
	/// by the sign of the attributes they modify and by whether the state flags they set are
	/// incapacitating. That is a heuristic, and it is the one place this class can be wrong. It is also the place a designer can correct it, via the manual
	/// intent override on the ability template.
	/// </para>
	/// <para>
	/// Results are cached per template. Templates are immutable ScriptableObjects at runtime, and
	/// the walk is deep enough that repeating it inside an ability picker would be wasteful.
	/// </para>
	/// </remarks>
	public static class AIAbilityClassifier
	{
		/// <summary>
		/// Classification cache keyed by the template's Unity instance ID.
		/// </summary>
		/// <remarks>
		/// Not <see cref="CachedScriptableObject{T}.ID"/>, which is a hash of the type and asset
		/// names. That is the right key for looking a template up by identity, but it is derived
		/// from the asset name, so an unnamed in-memory template — every one a test constructs —
		/// hashes to the same value as every other, and they would silently share a cache entry.
		/// The instance ID is unique per object by construction.
		/// </remarks>
		private static readonly Dictionary<int, AIAbilityIntent> cache = new Dictionary<int, AIAbilityIntent>();

		/// <summary>
		/// Clears the cache. Call when ability templates are reloaded in the editor.
		/// </summary>
		public static void ClearCache()
		{
			cache.Clear();
		}

		/// <summary>
		/// Returns what an ability instance does.
		/// </summary>
		/// <param name="ability">The ability to classify.</param>
		/// <returns>The ability's intent flags, or <see cref="AIAbilityIntent.None"/>.</returns>
		public static AIAbilityIntent Classify(Ability ability)
		{
			return ability == null ? AIAbilityIntent.None : Classify(ability.Template);
		}

		/// <summary>
		/// Returns what an ability template does, using the cache.
		/// </summary>
		/// <param name="template">The template to classify.</param>
		/// <returns>The template's intent flags, or <see cref="AIAbilityIntent.None"/>.</returns>
		public static AIAbilityIntent Classify(AbilityTemplate template)
		{
			if (template == null)
			{
				return AIAbilityIntent.None;
			}

			int key = template.GetInstanceID();

			if (cache.TryGetValue(key, out AIAbilityIntent cached))
			{
				return cached;
			}

			AIAbilityIntent intent = Analyze(template);
			cache[key] = intent;
			return intent;
		}

		/// <summary>
		/// True when an ability carries every one of the given intent flags.
		/// </summary>
		/// <param name="ability">The ability to test.</param>
		/// <param name="flags">Flags that must all be present.</param>
		/// <returns>True if all flags are present.</returns>
		public static bool Has(Ability ability, AIAbilityIntent flags)
		{
			return (Classify(ability) & flags) == flags;
		}

		/// <summary>
		/// True when an ability carries any of the given intent flags.
		/// </summary>
		/// <param name="ability">The ability to test.</param>
		/// <param name="flags">Flags to look for.</param>
		/// <returns>True if at least one flag is present.</returns>
		public static bool HasAny(Ability ability, AIAbilityIntent flags)
		{
			return Classify(ability).HasAny(flags);
		}

		/// <summary>
		/// Walks a template's whole ECA graph and accumulates intent.
		/// </summary>
		/// <param name="template">The template to analyse.</param>
		/// <returns>The accumulated intent flags.</returns>
		private static AIAbilityIntent Analyze(AbilityTemplate template)
		{
			// An explicit override on the template always wins. This is the escape hatch for a
			// custom action the classifier has never heard of, and for the buff-sign heuristic
			// getting a particular effect wrong.
			if (template.IntentOverride != AIAbilityIntent.None)
			{
				return template.IntentOverride;
			}

			AIAbilityIntent intent = AIAbilityIntent.None;

			// A pet ability brings something into the world whatever else it does.
			if (template is PetAbilityTemplate)
			{
				intent |= AIAbilityIntent.Summon;
			}

			AccumulateEvents(template.OnTickEvents, ref intent);
			AccumulateEvents(template.OnHitEvents, ref intent);
			AccumulateEvents(template.OnPreSpawnEvents, ref intent);
			AccumulateEvents(template.OnSpawnEvents, ref intent);
			AccumulateEvents(template.OnDestroyEvents, ref intent);

			return intent;
		}

		/// <summary>
		/// Accumulates intent from a list of ability events.
		/// </summary>
		/// <typeparam name="T">The event type.</typeparam>
		/// <param name="events">The events to walk.</param>
		/// <param name="intent">Accumulated intent flags.</param>
		private static void AccumulateEvents<T>(List<T> events, ref AIAbilityIntent intent) where T : AbilityEvent
		{
			if (events == null)
			{
				return;
			}

			for (int i = 0; i < events.Count; ++i)
			{
				AbilityEvent abilityEvent = events[i];
				if (abilityEvent == null)
				{
					continue;
				}

				/* Both branches. An ability whose heal is on the conditions-met branch and whose
				 * consolation damage is on the not-met branch is still both, and an NPC that only
				 * saw one of them would misjudge who to point it at. */
				AccumulateActions(abilityEvent.OnConditionsMetActions, ref intent);
				AccumulateActions(abilityEvent.OnConditionsNotMetActions, ref intent);
			}
		}

		/// <summary>
		/// Accumulates intent from a list of ECA actions.
		/// </summary>
		/// <param name="actions">The actions to inspect.</param>
		/// <param name="intent">Accumulated intent flags.</param>
		private static void AccumulateActions(List<BaseAction> actions, ref AIAbilityIntent intent)
		{
			if (actions == null)
			{
				return;
			}

			for (int i = 0; i < actions.Count; ++i)
			{
				intent |= ClassifyAction(actions[i]);
			}
		}

		/// <summary>
		/// Returns the intent a single ECA action contributes.
		/// </summary>
		/// <param name="action">The action to classify. Null contributes nothing.</param>
		/// <returns>The intent flags for this action.</returns>
		public static AIAbilityIntent ClassifyAction(BaseAction action)
		{
			switch (action)
			{
				case null:
					return AIAbilityIntent.None;

				case ApplyDamageAction _:
					return AIAbilityIntent.Damage;

				case ApplyHealAction _:
					return AIAbilityIntent.Heal;

				case ApplyReviveAction _:
					return AIAbilityIntent.Revive;

				case ApplyTauntAction _:
				case ApplyThreatAction _:
					return AIAbilityIntent.Threat;

				case ApplyBuffAction buff:
					return ClassifyBuff(buff.BuffTemplate);

				case ApplyDispelAction dispel:
					return ClassifyDispel(dispel);

				// Interrupting a cast and physically displacing a target are both denial of
				// the target's agency, which is what Control means here.
				case InterruptAction _:
				case KnockbackHitAction _:
					return AIAbilityIntent.Control;

				case TeleportAction _:
					return AIAbilityIntent.Utility;

				default:
					/* Deliberately None rather than Utility. Most actions in the project are
					 * quest, inventory, dialogue and region plumbing that an ability's combat role
					 * does not depend on, and marking them Utility would give every ability that
					 * happens to increment an achievement a spurious flag. */
					return AIAbilityIntent.None;
			}
		}

		/// <summary>
		/// Decides whether a buff template is beneficial, detrimental, or crowd control.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="BaseBuffTemplate"/> has no "harmful" flag, so this reads the effect itself:
		/// a state flag that <see cref="CharacterIncapacitation"/> recognises is control, a net
		/// negative attribute change is a debuff, a net positive one is a buff, and a resource tick
		/// is a heal or damage over time by its sign.
		/// </para>
		/// <para>
		/// The sum, not the count. A buff that grants +100 armour and −5 movement speed is a buff;
		/// counting entries would call it ambiguous and counting the first entry would depend on
		/// list order.
		/// </para>
		/// </remarks>
		/// <param name="template">The buff template to classify.</param>
		/// <returns>The intent flags this buff contributes.</returns>
		public static AIAbilityIntent ClassifyBuff(BaseBuffTemplate template)
		{
			if (template == null)
			{
				return AIAbilityIntent.None;
			}

			AIAbilityIntent intent = AIAbilityIntent.None;

			/* Tick events first. A buff's periodic effect is authored as ECA actions on
			 * BuffTickEvent, so a damage-over-time buff carries an ApplyDamageAction and says
			 * outright what it does — no inference needed, and the same reading the ability's own
			 * events get. The serialized-field cases below cover the simple templates that express
			 * their effect as numbers instead. */
			if (template.OnTickEvents != null)
			{
				for (int i = 0; i < template.OnTickEvents.Count; ++i)
				{
					BuffTickEvent tickEvent = template.OnTickEvents[i];
					if (tickEvent == null)
					{
						continue;
					}

					AccumulateActions(tickEvent.OnConditionsMetActions, ref intent);
					AccumulateActions(tickEvent.OnConditionsNotMetActions, ref intent);
				}
			}

			switch (template)
			{
				case StateBuffTemplate state:
					intent |= ClassifyStateFlag(state.Flag);
					break;

				case AttributeBuffTemplate attribute:
					intent |= ClassifyAttributeSum(attribute.BonusAttributes);
					break;

				case AttributeTickBuffTemplate attributeTick:
					intent |= ClassifyAttributeSum(attributeTick.TickAttributes);
					break;

				case ResourceTickBuffTemplate resourceTick:
					// A resource tick moves health up or down over time: a HoT or a DoT.
					intent |= ClassifyResourceSum(resourceTick.TickAttributes);
					break;

				case CompositeBuffTemplate composite:
					intent |= ClassifyAttributeSum(composite.BonusAttributes);
					intent |= ClassifyResourceSum(composite.TickAttributes);
					if (composite.Flags != null)
					{
						for (int i = 0; i < composite.Flags.Count; ++i)
						{
							intent |= ClassifyStateFlag(composite.Flags[i]);
						}
					}
					break;

				default:
					/* An unrecognised buff subclass with nothing else to go on is assumed
					 * beneficial: applying a buff to an ally is the safe default, and the override
					 * exists for the exceptions. Skipped when the tick events already said what the
					 * buff does, or a pure-ECA damage-over-time buff would be read as a blessing. */
					if (intent == AIAbilityIntent.None)
					{
						intent |= AIAbilityIntent.Buff;
					}
					break;
			}

			return intent;
		}

		/// <summary>
		/// Returns Control for a state flag that prevents a character from acting, Buff otherwise.
		/// </summary>
		/// <param name="flag">The character flag the buff sets.</param>
		/// <returns>The intent flags for this state.</returns>
		private static AIAbilityIntent ClassifyStateFlag(CharacterFlags flag)
		{
			/* Kept in step with CharacterIncapacitation by construction: that class is the single
			 * definition of "cannot act", and this asks it the same question about a flag rather
			 * than repeating its list. */
			if (flag == CharacterFlags.IsFrozen ||
				flag == CharacterFlags.IsStunned ||
				flag == CharacterFlags.IsMesmerized)
			{
				return AIAbilityIntent.Control;
			}

			return AIAbilityIntent.Buff;
		}

		/// <summary>
		/// Sums attribute modifiers and returns Buff for a net gain, Debuff for a net loss.
		/// </summary>
		/// <param name="attributes">The attribute modifiers to sum.</param>
		/// <returns>The intent flags implied by the net change.</returns>
		private static AIAbilityIntent ClassifyAttributeSum(List<BuffAttributeTemplate> attributes)
		{
			if (attributes == null || attributes.Count == 0)
			{
				return AIAbilityIntent.None;
			}

			long sum = 0;
			for (int i = 0; i < attributes.Count; ++i)
			{
				if (attributes[i] != null)
				{
					sum += attributes[i].Value;
				}
			}

			if (sum < 0) return AIAbilityIntent.Debuff;
			if (sum > 0) return AIAbilityIntent.Buff;

			// A net-zero attribute change is a wash; contribute nothing rather than guessing.
			return AIAbilityIntent.None;
		}

		/// <summary>
		/// Sums resource-tick modifiers and returns Heal for a net gain, Damage for a net loss.
		/// </summary>
		/// <param name="attributes">The resource modifiers to sum.</param>
		/// <returns>The intent flags implied by the net change.</returns>
		private static AIAbilityIntent ClassifyResourceSum(List<BuffAttributeTemplate> attributes)
		{
			if (attributes == null || attributes.Count == 0)
			{
				return AIAbilityIntent.None;
			}

			long sum = 0;
			for (int i = 0; i < attributes.Count; ++i)
			{
				if (attributes[i] != null)
				{
					sum += attributes[i].Value;
				}
			}

			if (sum < 0) return AIAbilityIntent.Damage;
			if (sum > 0) return AIAbilityIntent.Heal;

			return AIAbilityIntent.None;
		}

		/// <summary>
		/// Decides which way a dispel points.
		/// </summary>
		/// <remarks>
		/// Stripping debuffs helps a friend; stripping buffs hurts an enemy. The action says which
		/// it does, so the classifier can set an accompanying Buff or Debuff flag and let the
		/// offensive/supportive tests below work without a special case for dispels.
		/// </remarks>
		/// <param name="dispel">The dispel action.</param>
		/// <returns>The intent flags for this dispel.</returns>
		private static AIAbilityIntent ClassifyDispel(ApplyDispelAction dispel)
		{
			AIAbilityIntent intent = AIAbilityIntent.Dispel;

			// Removing a target's debuffs is a favour: point it at an ally.
			if (dispel.IncludeDebuffs)
			{
				intent |= AIAbilityIntent.Buff;
			}

			// Removing a target's buffs is an attack: point it at an enemy.
			if (dispel.IncludeBuffs)
			{
				intent |= AIAbilityIntent.Debuff;
			}

			return intent;
		}
	}
}
