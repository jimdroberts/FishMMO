using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A complete, ready-to-use ability a merchant sells: a base <see cref="AbilityTemplate"/> with
	/// its events already chosen, at a price of its own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The ordinary route to a usable ability is two steps — buy the template and the effects from
	/// a merchant, then combine them at an Ability Crafter. This is the one-step alternative: the
	/// buyer receives the finished <see cref="Ability"/> straight into their usable set, exactly as
	/// if they had crafted it, and never needs to know the template. Content decides the trade-off;
	/// a premade combination will usually be weaker or plainer than what a player could build.
	/// </para>
	/// <para>
	/// A plain ScriptableObject rather than a cached template. Nothing ever looks one of these up
	/// by ID: the purchase names a merchant and an index into that merchant's list, the server
	/// reads the recipe from its own copy of the asset, and the client renders it from the copy
	/// that loaded with the merchant template. It therefore needs no addressable label and no
	/// place in the cache.
	/// </para>
	/// <para>
	/// The recipe is held to the crafting rules — at most <see cref="AbilityTemplate.AdditionalEventSlots"/>
	/// events and at most one type override — so a premade ability can never be something a
	/// player could not have built. <see cref="Validate"/> is the single statement of those rules;
	/// the server runs it before selling and the inspector runs it on edit.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Premade Ability", menuName = "FishMMO/Character/Merchant/Premade Ability", order = 2)]
	public class PremadeAbilityTemplate : ScriptableObject, ITooltip
	{
		/// <summary>The base ability the finished ability is built from.</summary>
		[Tooltip("The base ability. The buyer receives a finished ability built from it; they do not learn the template itself.")]
		public AbilityTemplate Ability;

		/// <summary>Events attached to the ability, as a crafter would attach them.</summary>
		[Tooltip("Events baked into the ability. Bound by the base ability's Additional Event Slots.")]
		public List<AbilityEvent> Events = new List<AbilityEvent>();

		/// <summary>Optional ability-type override, as a crafter would attach one.</summary>
		[Tooltip("Optional type override. Counts as one event slot, like it does at the crafter.")]
		public AbilityTypeOverrideEventType TypeOverride;

		/// <summary>
		/// What the merchant charges. Zero is free; the template and event prices are not summed.
		/// </summary>
		[Tooltip("The merchant's price for the finished ability. Zero is free. Component prices are not added on top.")]
		[Min(0)]
		public int Price;

		/// <inheritdoc />
		public string Name { get { return Ability != null ? Ability.Name : this.name; } }

		/// <inheritdoc />
		public Sprite Icon { get { return Ability != null ? Ability.Icon : null; } }

		/// <summary>Human-readable name of the offer, for logs and the inspector.</summary>
		public string AssetName { get { return this.name; } }

		/// <summary>
		/// The event IDs a crafted <see cref="Ability"/> is constructed with: the events, then the
		/// type override, in that order. The same shape the crafter sends and the database stores.
		/// </summary>
		public List<int> BuildEventIDs()
		{
			List<int> ids = new List<int>();
			if (Events != null)
			{
				for (int i = 0; i < Events.Count; ++i)
				{
					AbilityEvent abilityEvent = Events[i];
					if (abilityEvent != null && !ids.Contains(abilityEvent.ID))
					{
						ids.Add(abilityEvent.ID);
					}
				}
			}
			if (TypeOverride != null)
			{
				ids.Add(TypeOverride.ID);
			}
			return ids;
		}

		/// <summary>
		/// Checks the recipe against the crafting rules.
		/// </summary>
		/// <param name="reason">Why the recipe is not sellable, or null when it is.</param>
		/// <returns>True when a crafter would have accepted this exact combination.</returns>
		/// <remarks>
		/// Written once and used on both sides so an asset the inspector accepts is one the
		/// server will sell. Duplicate events are tolerated here and collapsed by
		/// <see cref="BuildEventIDs"/>, because a duplicated inspector row is a slip rather than
		/// an attempt to double an effect.
		/// </remarks>
		public bool Validate(out string reason)
		{
			if (Ability == null)
			{
				reason = "no base ability is assigned.";
				return false;
			}
			if (Price < 0)
			{
				reason = "the price is negative.";
				return false;
			}

			int slotsUsed = 0;
			if (Events != null)
			{
				HashSet<int> seen = new HashSet<int>();
				for (int i = 0; i < Events.Count; ++i)
				{
					AbilityEvent abilityEvent = Events[i];
					if (abilityEvent == null)
					{
						reason = $"event slot {i} is empty.";
						return false;
					}
					if (seen.Add(abilityEvent.ID))
					{
						++slotsUsed;
					}
				}
			}
			if (TypeOverride != null)
			{
				++slotsUsed;
			}

			if (slotsUsed > Ability.AdditionalEventSlots)
			{
				reason = $"{slotsUsed} events are attached but '{Ability.Name}' allows {Ability.AdditionalEventSlots}.";
				return false;
			}

			reason = null;
			return true;
		}

		/// <summary>
		/// Describes the finished ability this offer sells, at the price it sells for.
		/// </summary>
		/// <remarks>
		/// The composition is the one a crafter would produce from the same parts, so the offer
		/// cannot advertise numbers the bought ability will not have. The craft cost is suppressed:
		/// this is sold at its own price and two price lines would defeat the point of showing one.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		public void BuildTooltip(TooltipContent content)
		{
			if (Ability == null)
			{
				content.AddTitle(this.name);
				return;
			}

			List<ITooltip> components = new List<ITooltip>();
			if (Events != null)
			{
				for (int i = 0; i < Events.Count; ++i)
				{
					if (Events[i] != null)
					{
						components.Add(Events[i]);
					}
				}
			}
			if (TypeOverride != null)
			{
				components.Add(TypeOverride);
			}

			AbilitySummary.Compose(Ability, components).BuildTooltip(content, showDeltas: false, includePrice: false);

			content.AddBody("Premade ability. Ready to use as soon as it is bought; no crafting needed.",
				TooltipPriority.Description + 1, TooltipTone.Good);

			if (Price > 0)
			{
				content.AddStat("Price", Price.ToString(), TooltipPriority.Price);
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// Surfaces a recipe the server would refuse, at edit time, where it can be fixed.
		/// </summary>
		private void OnValidate()
		{
			if (!Validate(out string reason))
			{
				Debug.LogWarning($"Premade ability '{this.name}' will not be sold: {reason}", this);
			}
		}
#endif
	}
}
