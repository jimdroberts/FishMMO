using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The four numbers every ability carries, and the range they imply.
	/// </summary>
	public struct AbilityStatBlock
	{
		/// <summary>Seconds of casting before the ability goes off.</summary>
		public float ActivationTime;

		/// <summary>Seconds the spawned ability object lives.</summary>
		public float LifeTime;

		/// <summary>Metres per second the ability object travels.</summary>
		public float Speed;

		/// <summary>Seconds before the ability can be used again.</summary>
		public float Cooldown;

		/// <summary>How many times the ability object can hit before it is done.</summary>
		public int HitCount;

		/// <summary>
		/// How far the ability reaches: the distance its object covers before its lifetime ends.
		/// </summary>
		public readonly float Range => Speed * LifeTime;

		/// <summary>Adds another block's contributions to this one.</summary>
		public void Add(float activationTime, float lifeTime, float speed, float cooldown)
		{
			ActivationTime += activationTime;
			LifeTime += lifeTime;
			Speed += speed;
			Cooldown += cooldown;
		}
	}

	/// <summary>
	/// Everything the UI needs to describe an ability, composed once from one set of rules.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> An ability's numbers were worked out in three places that disagreed.
	/// <see cref="Ability"/> sums the template's stats, the events baked into the template, and any
	/// crafted events. The template's tooltip summed only the template and whatever the crafting
	/// panel passed it, ignoring the template's own events entirely — so a fireball whose bundled
	/// movement event supplies its speed advertised numbers the crafted ability would never have.
	/// The crafting panel then added its own third rule for the price. A player comparing the
	/// preview against the ability they got was comparing two different calculations.
	/// </para>
	/// <para>
	/// One composition now serves the template tooltip, the learned ability's tooltip, the
	/// knowledge panel and the crafting preview. It also keeps <see cref="BaseStats"/> beside
	/// <see cref="Stats"/>, which is what lets the crafting UI show what each chosen effect
	/// actually changed rather than only the total.
	/// </para>
	/// </remarks>
	public sealed class AbilitySummary
	{
		/// <summary>The base ability this was composed from.</summary>
		public AbilityTemplate Template { get; private set; }

		/// <summary>The ability's name.</summary>
		public string Name { get; private set; }

		/// <summary>The ability's icon.</summary>
		public Sprite Icon { get; private set; }

		/// <summary>The ability's description.</summary>
		public string Description { get; private set; }

		/// <summary>The effective ability type, after any override event.</summary>
		public AbilityType Type { get; private set; }

		/// <summary>The override event that replaced the type, when one was chosen.</summary>
		public AbilityTypeOverrideEventType TypeOverride { get; private set; }

		/// <summary>How many effects may be crafted onto this base ability.</summary>
		public int EventSlots { get; private set; }

		/// <summary>The base ability with only the effects its template ships with.</summary>
		public AbilityStatBlock BaseStats;

		/// <summary>The composed ability, including every crafted effect.</summary>
		public AbilityStatBlock Stats;

		/// <summary>Effects the template ships with. The player did not choose these and cannot remove them.</summary>
		public readonly List<AbilityEvent> TemplateEvents = new List<AbilityEvent>();

		/// <summary>Effects crafted onto the base ability.</summary>
		public readonly List<AbilityEvent> CraftedEvents = new List<AbilityEvent>();

		/// <summary>Resource cost of the base ability alone.</summary>
		public readonly Dictionary<CharacterAttributeTemplate, int> BaseResourceCosts = new Dictionary<CharacterAttributeTemplate, int>();

		/// <summary>Resource cost of the composed ability.</summary>
		public readonly Dictionary<CharacterAttributeTemplate, int> ResourceCosts = new Dictionary<CharacterAttributeTemplate, int>();

		/// <summary>What the character must have or be, in the order the conditions were authored.</summary>
		public readonly List<string> Requirements = new List<string>();

		/// <summary>How the composed ability picks what it affects.</summary>
		public readonly List<string> Targeting = new List<string>();

		/// <summary>What the composed ability does when it connects.</summary>
		public readonly List<string> Effects = new List<string>();

		/// <summary>What crafting this composition costs: the base ability plus every chosen effect.</summary>
		public int CraftPrice { get; private set; }

		/// <summary>True when at least one effect was crafted on, so deltas are worth drawing.</summary>
		public bool HasCraftedEvents => CraftedEvents.Count > 0 || TypeOverride != null;

		/// <summary>
		/// Composes a base ability with the effects a player has chosen for it.
		/// </summary>
		/// <param name="template">The base ability.</param>
		/// <param name="chosen">
		/// Effects and type overrides being crafted onto it. Entries that are neither are ignored,
		/// which is what lets a UI pass its slot contents straight in.
		/// </param>
		public static AbilitySummary Compose(AbilityTemplate template, IReadOnlyList<ITooltip> chosen = null)
		{
			AbilitySummary summary = new AbilitySummary();
			if (template == null)
			{
				return summary;
			}

			summary.Template = template;
			summary.Name = template.Name;
			summary.Icon = template.Icon;
			summary.Description = template.Description;
			summary.Type = template.Type;
			summary.EventSlots = template.AdditionalEventSlots;
			summary.CraftPrice = template.Price;

			/* The base is the template AND the events it ships with, because that is what
			 * Ability sums when the crafted ability is actually built. A base block that left
			 * them out would make every delta below wrong by the size of the template's own
			 * bundled effects. */
			summary.BaseStats.Add(template.ActivationTime, template.LifeTime, template.Speed, template.Cooldown);
			summary.BaseStats.HitCount = template.HitCount;

			CollectTemplateEvents(template, summary.TemplateEvents);
			foreach (AbilityEvent templateEvent in summary.TemplateEvents)
			{
				summary.BaseStats.Add(templateEvent.ActivationTime, templateEvent.LifeTime, templateEvent.Speed, templateEvent.Cooldown);
			}

			CollectResourceCosts(template.ActivationConditions, summary.BaseResourceCosts);
			foreach (AbilityEvent templateEvent in summary.TemplateEvents)
			{
				CollectResourceCosts(templateEvent.Conditions, summary.BaseResourceCosts);
			}

			summary.Stats = summary.BaseStats;
			foreach (KeyValuePair<CharacterAttributeTemplate, int> cost in summary.BaseResourceCosts)
			{
				summary.ResourceCosts[cost.Key] = cost.Value;
			}

			if (chosen != null)
			{
				for (int i = 0; i < chosen.Count; ++i)
				{
					switch (chosen[i])
					{
						case AbilityEvent abilityEvent:
							/* An effect the template already ships with is already counted in the
							 * base, and Ability.AddEvent refuses the duplicate outright — so
							 * counting it again here would preview a cooldown the crafted ability
							 * will not have. It stays listed as built in rather than as a choice. */
							if (Contains(summary.TemplateEvents, abilityEvent) ||
								Contains(summary.CraftedEvents, abilityEvent))
							{
								continue;
							}
							summary.CraftedEvents.Add(abilityEvent);
							summary.Stats.Add(abilityEvent.ActivationTime, abilityEvent.LifeTime, abilityEvent.Speed, abilityEvent.Cooldown);
							summary.CraftPrice += abilityEvent.Price;
							CollectResourceCosts(abilityEvent.Conditions, summary.ResourceCosts);
							break;

						/* An ability-type override extends BaseAbilityTemplate rather than
						 * AbilityEvent, so it is a different branch everywhere it is handled —
						 * including the server's craft validation. It replaces the type outright;
						 * it does not add to it. */
						case AbilityTypeOverrideEventType typeOverride:
							summary.TypeOverride = typeOverride;
							summary.Type = typeOverride.OverrideAbilityType;
							summary.Stats.Add(typeOverride.ActivationTime, typeOverride.LifeTime, typeOverride.Speed, typeOverride.Cooldown);
							summary.CraftPrice += typeOverride.Price;
							CollectResourceCosts(typeOverride.ActivationConditions, summary.ResourceCosts);
							break;
					}
				}
			}

			summary.CollectDescriptions();
			return summary;
		}

		/// <summary>
		/// Describes an ability the character has already learned.
		/// </summary>
		/// <remarks>
		/// The crafted effects are recovered by subtracting the template's own: an ability holds
		/// one flat dictionary of events and does not record where each came from, but the
		/// template does, and a player looking at a crafted ability wants to see the parts they
		/// chose separately from the ones that came in the box.
		/// </remarks>
		public static AbilitySummary FromAbility(Ability ability)
		{
			if (ability?.Template == null)
			{
				return new AbilitySummary();
			}

			List<ITooltip> crafted = new List<ITooltip>();
			HashSet<int> fromTemplate = new HashSet<int>();
			List<AbilityEvent> templateEvents = new List<AbilityEvent>();
			CollectTemplateEvents(ability.Template, templateEvents);
			foreach (AbilityEvent templateEvent in templateEvents)
			{
				fromTemplate.Add(templateEvent.ID);
			}

			foreach (KeyValuePair<int, AbilityEvent> pair in ability.AbilityEvents)
			{
				if (pair.Value != null && !fromTemplate.Contains(pair.Key))
				{
					crafted.Add(pair.Value);
				}
			}

			if (ability.TypeOverride != null)
			{
				crafted.Add(ability.TypeOverride);
			}

			AbilitySummary summary = Compose(ability.Template, crafted);
			summary.Name = ability.Name;
			return summary;
		}

		/// <summary>The change a stat underwent, or null when nothing changed it.</summary>
		/// <param name="baseValue">The base ability's value.</param>
		/// <param name="composed">The composed value.</param>
		/// <param name="suffix">Unit suffix, e.g. "s" or "m".</param>
		public static string FormatDelta(float baseValue, float composed, string suffix)
		{
			float delta = composed - baseValue;
			if (Mathf.Abs(delta) < 0.0005f)
			{
				return null;
			}
			return delta > 0.0f ? $"+{delta:0.##}{suffix}" : $"{delta:0.##}{suffix}";
		}

		/// <summary>
		/// Whether a change to a stat is an improvement.
		/// </summary>
		/// <remarks>
		/// Direction is per-stat and is not guessable from the sign: more range is good, more
		/// cooldown is not. Getting this wrong colours a penalty green, so it is stated once here
		/// rather than at each call site.
		/// </remarks>
		public static TooltipTone ToneForDelta(float delta, bool moreIsBetter)
		{
			if (Mathf.Abs(delta) < 0.0005f)
			{
				return TooltipTone.Neutral;
			}
			bool improvement = delta > 0.0f == moreIsBetter;
			return improvement ? TooltipTone.Good : TooltipTone.Bad;
		}

		/// <summary>
		/// Whether a list already holds this event.
		/// </summary>
		/// <remarks>
		/// By reference first, and by id only when the id is set. An <see cref="AbilityEvent"/>
		/// gets its id from <c>AddToCache</c> when the addressables loader registers it, so an
		/// instance that has not been through that carries zero — and matching on zero alone would
		/// treat two unrelated unregistered events as the same one.
		/// </remarks>
		private static bool Contains(List<AbilityEvent> events, AbilityEvent candidate)
		{
			for (int i = 0; i < events.Count; ++i)
			{
				AbilityEvent existing = events[i];
				if (ReferenceEquals(existing, candidate))
				{
					return true;
				}
				if (candidate.ID != 0 && existing.ID == candidate.ID)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Gathers every event a template ships with, in execution order.</summary>
		private static void CollectTemplateEvents(AbilityTemplate template, List<AbilityEvent> into)
		{
			AppendEvents(template.OnPreSpawnEvents, into);
			AppendEvents(template.OnSpawnEvents, into);
			AppendEvents(template.OnTickEvents, into);
			AppendEvents(template.OnHitEvents, into);
			AppendEvents(template.OnDestroyEvents, into);
		}

		/// <summary>Appends the non-null events of one list, skipping duplicates.</summary>
		private static void AppendEvents<T>(List<T> source, List<AbilityEvent> into) where T : AbilityEvent
		{
			if (source == null)
			{
				return;
			}

			for (int i = 0; i < source.Count; ++i)
			{
				T abilityEvent = source[i];
				if (abilityEvent == null)
				{
					continue;
				}

				bool seen = false;
				for (int j = 0; j < into.Count; ++j)
				{
					if (into[j].ID == abilityEvent.ID)
					{
						seen = true;
						break;
					}
				}
				if (!seen)
				{
					into.Add(abilityEvent);
				}
			}
		}

		/// <summary>Sums the resource costs a condition list declares.</summary>
		private static void CollectResourceCosts(List<BaseCondition> conditions, Dictionary<CharacterAttributeTemplate, int> into)
		{
			if (conditions == null)
			{
				return;
			}

			foreach (BaseCondition condition in conditions)
			{
				if (condition is IResourceCost cost &&
					cost.ResourceTemplate != null &&
					cost.ResourceAmount > 0)
				{
					into.TryGetValue(cost.ResourceTemplate, out int existing);
					into[cost.ResourceTemplate] = existing + cost.ResourceAmount;
				}
			}
		}

		/// <summary>Gathers the requirement, targeting and effect wording from every part.</summary>
		private void CollectDescriptions()
		{
			AppendRequirements(Template.ActivationConditions);

			foreach (AbilityEvent abilityEvent in TemplateEvents)
			{
				AppendEventDescriptions(abilityEvent);
			}
			foreach (AbilityEvent abilityEvent in CraftedEvents)
			{
				AppendEventDescriptions(abilityEvent);
			}
			if (TypeOverride != null)
			{
				AppendRequirements(TypeOverride.ActivationConditions);
			}
		}

		/// <summary>Gathers one event's requirements, targeting and effects.</summary>
		private void AppendEventDescriptions(AbilityEvent abilityEvent)
		{
			AppendRequirements(abilityEvent.Conditions);

			string targeting = abilityEvent.TargetSelector?.GetTooltipContribution();
			if (!string.IsNullOrWhiteSpace(targeting) && !Targeting.Contains(targeting))
			{
				Targeting.Add(targeting);
			}

			if (abilityEvent.OnConditionsMetActions == null)
			{
				return;
			}
			foreach (BaseAction action in abilityEvent.OnConditionsMetActions)
			{
				string effect = action?.GetTooltipContribution();
				if (!string.IsNullOrWhiteSpace(effect) && !Effects.Contains(effect))
				{
					Effects.Add(effect);
				}
			}
		}

		/// <summary>Gathers requirement wording, skipping the resource costs counted separately.</summary>
		private void AppendRequirements(List<BaseCondition> conditions)
		{
			if (conditions == null)
			{
				return;
			}

			foreach (BaseCondition condition in conditions)
			{
				if (condition == null || condition is IResourceCost)
				{
					continue;
				}

				string requirement = condition.GetTooltipContribution();
				if (!string.IsNullOrWhiteSpace(requirement) && !Requirements.Contains(requirement))
				{
					Requirements.Add(requirement);
				}
			}
		}

		/// <summary>
		/// Writes the whole composition into a tooltip.
		/// </summary>
		/// <param name="content">The content being assembled.</param>
		/// <param name="showDeltas">
		/// When true, each stat carries what the crafted effects changed it by. The crafting panel
		/// wants that; a finished ability's tooltip does not, because there is nothing to compare
		/// against once the ability exists.
		/// </param>
		/// <param name="includePrice">Whether to write the crafting price. A seller writes its own.</param>
		public void BuildTooltip(TooltipContent content, bool showDeltas = false, bool includePrice = true)
		{
			if (Template == null)
			{
				return;
			}

			content.Icon = Icon;
			content.AddTitle(Name);
			content.AddSubtitle(Type != AbilityType.None ? $"{Type} Ability" : "Ability");

			if (!string.IsNullOrWhiteSpace(Description))
			{
				content.AddBody(Description);
			}

			AddStat(content, "Cast Time", Stats.ActivationTime, BaseStats.ActivationTime, "s", moreIsBetter: false, showDeltas, TooltipPriority.Stats);
			AddStat(content, "Cooldown", Stats.Cooldown, BaseStats.Cooldown, "s", moreIsBetter: false, showDeltas, TooltipPriority.Stats + 1);
			AddStat(content, "Range", Stats.Range, BaseStats.Range, "m", moreIsBetter: true, showDeltas, TooltipPriority.Stats + 2);
			AddStat(content, "Speed", Stats.Speed, BaseStats.Speed, "m/s", moreIsBetter: true, showDeltas, TooltipPriority.Stats + 3);
			AddStat(content, "Duration", Stats.LifeTime, BaseStats.LifeTime, "s", moreIsBetter: true, showDeltas, TooltipPriority.Stats + 4);

			if (Stats.HitCount > 0)
			{
				content.AddStat("Hits", Stats.HitCount.ToString(), TooltipPriority.Stats + 5);
			}

			if (ResourceCosts.Count > 0)
			{
				content.AddHeader("Resource Cost", TooltipPriority.ResourceCost);
				int order = 1;
				foreach (KeyValuePair<CharacterAttributeTemplate, int> cost in ResourceCosts)
				{
					BaseResourceCosts.TryGetValue(cost.Key, out int baseCost);
					string delta = showDeltas ? FormatDelta(baseCost, cost.Value, string.Empty) : null;
					content.AddStat(cost.Key.Name, cost.Value.ToString(), TooltipPriority.ResourceCost + order++,
						delta, ToneForDelta(cost.Value - baseCost, moreIsBetter: false));
				}
			}

			AddList(content, Targeting, "Targeting", TooltipPriority.Targeting, TooltipRowKind.Effect);
			AddList(content, Requirements, "Requirements", TooltipPriority.Requirements, TooltipRowKind.Requirement);
			AddList(content, Effects, "Effects", TooltipPriority.Effects, TooltipRowKind.Effect);

			if (TemplateEvents.Count > 0 || CraftedEvents.Count > 0)
			{
				content.AddHeader("Composition", TooltipPriority.Composition);
				int order = 1;
				foreach (AbilityEvent templateEvent in TemplateEvents)
				{
					content.AddStat(templateEvent.Name, "built in", TooltipPriority.Composition + order++, tone: TooltipTone.Muted);
				}
				foreach (AbilityEvent craftedEvent in CraftedEvents)
				{
					content.AddStat(craftedEvent.Name, EventCategory(craftedEvent), TooltipPriority.Composition + order++, tone: TooltipTone.Accent);
				}
				if (TypeOverride != null)
				{
					content.AddStat(TypeOverride.Name, "type override", TooltipPriority.Composition + order, tone: TooltipTone.Accent);
				}
			}

			if (includePrice && CraftPrice > 0)
			{
				content.AddStat("Craft Cost", CraftPrice.ToString(), TooltipPriority.Price);
			}
		}

		/// <summary>Writes one stat row, dropping it when the ability does not use that stat.</summary>
		private static void AddStat(TooltipContent content, string label, float value, float baseValue, string suffix, bool moreIsBetter, bool showDeltas, int priority)
		{
			/* A zero stat is not a stat of zero — an instant ability has no cast time and a
			 * stationary one has no speed, and printing "Speed: 0m/s" on every melee strike is
			 * noise. It IS printed when crafting took it to zero, because that is a change the
			 * player made and needs to see. */
			bool changed = Mathf.Abs(value - baseValue) >= 0.0005f;
			if (value <= 0.0f && !changed)
			{
				return;
			}

			string delta = showDeltas ? FormatDelta(baseValue, value, suffix) : null;
			content.AddStat(label, $"{value:0.##}{suffix}", priority, delta, ToneForDelta(value - baseValue, moreIsBetter));
		}

		/// <summary>Writes a header and its rows, or nothing when the list is empty.</summary>
		private static void AddList(TooltipContent content, List<string> values, string header, int priority, TooltipRowKind kind)
		{
			if (values.Count == 0)
			{
				return;
			}

			content.AddHeader(header, priority);
			for (int i = 0; i < values.Count; ++i)
			{
				content.Add(new TooltipRow
				{
					Kind = kind,
					Label = values[i],
					Priority = priority + 1 + i,
				});
			}
		}

		/// <summary>
		/// When an event runs, in words a player can act on.
		/// </summary>
		public static string EventCategory(AbilityEvent abilityEvent)
		{
			switch (abilityEvent)
			{
				case AbilityOnPreSpawnEvent _: return "before cast";
				case AbilityOnSpawnEvent _: return "on cast";
				case AbilityOnTickEvent _: return "each tick";
				case AbilityOnHitEvent _: return "on hit";
				case AbilityOnDestroyEvent _: return "on expire";
				default: return "effect";
			}
		}
	}
}
