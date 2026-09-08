using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The rules a player-to-player trade is held to, stated once so the server that enforces
	/// them and the client that pre-checks them cannot disagree.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure functions over plain values. The server is the only authority — every rule here is
	/// re-evaluated by <c>TradeSystem</c> on every request and again at the moment of
	/// completion — but the owning client asks the same questions first so a refusal costs no
	/// round trip, and so its window closes the instant the players separate rather than on
	/// the server's next range check.
	/// </para>
	/// <para>
	/// Kept free of session state on purpose. What is on the table, who has accepted, and
	/// which version is current live on the server in <c>TradeSession</c>; this file answers
	/// only "may these two trade at all" and "what does this request mean".
	/// </para>
	/// </remarks>
	public static class TradeRules
	{
		/// <summary>
		/// How far apart, in metres, two characters may be and still trade. Issue #144 asks for
		/// 15–20; the server asset can raise it, never lower it below <see cref="MinimumMaxDistance"/>.
		/// </summary>
		public const float DefaultMaxDistance = 15.0f;

		/// <summary>
		/// The floor for a configured range. A range shorter than a character's own collider
		/// would make every trade close on its first range check.
		/// </summary>
		public const float MinimumMaxDistance = 2.0f;

		/// <summary>
		/// How many distinct inventory slots one side may put on the table.
		/// </summary>
		/// <remarks>
		/// A bound on the wire payload and on the receiver's worst-case capacity check. Eight
		/// matches the window's 4×2 grid; the server asset can raise it and the window will
		/// build more slots.
		/// </remarks>
		public const int DefaultMaxOfferSlots = 8;

		/// <summary>
		/// The hard ceiling on offer slots, whatever the asset says. The table is drawn as a
		/// grid and the wire carries one entry per slot.
		/// </summary>
		public const int MaximumOfferSlots = 32;

		/// <summary>
		/// How long an invitation waits for an answer before it lapses.
		/// </summary>
		public const float DefaultInviteTtlSeconds = 30.0f;

		/// <summary>
		/// True when two positions are within <paramref name="maxDistance"/> of each other.
		/// </summary>
		/// <remarks>
		/// Squared comparison, so it costs no square root on the server's range tick. A
		/// non-positive range refuses everything rather than accepting everything: a
		/// misconfigured asset must fail closed.
		/// </remarks>
		public static bool IsWithinRange(Vector3 a, Vector3 b, float maxDistance)
		{
			if (maxDistance <= 0.0f)
			{
				return false;
			}
			return (a - b).sqrMagnitude <= maxDistance * maxDistance;
		}

		/// <summary>
		/// True when two characters are in the same loaded scene instance.
		/// </summary>
		/// <remarks>
		/// Compared by scene handle, not name. Scene stacking loads several instances of one
		/// scene at once and they share a name; two characters in different instances can have
		/// positions a metre apart and still be unable to see each other.
		/// </remarks>
		public static bool AreInSameScene(ICharacter a, ICharacter b)
		{
			if (a == null || b == null || a.GameObject == null || b.GameObject == null)
			{
				return false;
			}
			return a.GameObject.scene.handle == b.GameObject.scene.handle;
		}

		/// <summary>
		/// True when both characters may take part in a trade right now: both present, both
		/// able to act, in the same scene instance and within range.
		/// </summary>
		public static bool CanTradeTogether(ICharacter a, ICharacter b, float maxDistance)
		{
			if (a == null || b == null || ReferenceEquals(a, b) || a.ID == b.ID)
			{
				return false;
			}
			if (!CharacterStateValidation.CanAct(a) || !CharacterStateValidation.CanAct(b))
			{
				return false;
			}
			if (!AreInSameScene(a, b))
			{
				return false;
			}
			if (a.Transform == null || b.Transform == null)
			{
				return false;
			}
			return IsWithinRange(a.Transform.position, b.Transform.position, maxDistance);
		}

		/// <summary>
		/// Turns a requested quantity into the quantity that will actually be offered.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A non-stackable item is offered whole: the only acceptable requests are "whole"
		/// (zero) and one. A stackable item is offered whole for zero, and otherwise for the
		/// requested amount, which must fit inside the stack — asking for more than is there is
		/// refused rather than clamped, because a client that asks for 50 of a stack of 20 is
		/// either stale or lying and either way the server should not guess.
		/// </para>
		/// </remarks>
		/// <param name="item">The item in the slot.</param>
		/// <param name="requested">What the client asked for; zero means the whole stack.</param>
		/// <param name="amount">The quantity to put on the table.</param>
		/// <returns>True when the request resolves to a positive quantity the item can supply.</returns>
		public static bool TryResolveOfferAmount(Item item, uint requested, out uint amount)
		{
			amount = 0;
			if (item == null || item.Template == null)
			{
				return false;
			}

			if (!item.IsStackable)
			{
				if (requested > 1)
				{
					return false;
				}
				amount = 1;
				return true;
			}

			uint held = item.Stackable.Amount;
			if (held < 1)
			{
				return false;
			}

			if (requested == 0)
			{
				amount = held;
				return true;
			}

			if (requested > held)
			{
				return false;
			}

			amount = requested;
			return true;
		}

		/// <summary>
		/// True when the item in a slot is still the item an offer was made for, with at least
		/// the offered quantity.
		/// </summary>
		/// <remarks>
		/// The check the server makes at the moment of completion. Identity is the item's
		/// database id — a slot that now holds a different item of the same template fails —
		/// and quantity may only have grown: a stack that shrank below the offer cannot pay it.
		/// </remarks>
		public static bool OfferStillHolds(Item item, long itemID, int templateID, uint amount)
		{
			if (item == null || item.Template == null || item.ID != itemID || item.Template.ID != templateID)
			{
				return false;
			}
			uint held = item.IsStackable ? item.Stackable.Amount : 1u;
			return amount >= 1 && held >= amount;
		}

		/// <summary>
		/// Estimates whether a bag can take everything the other side is offering, once the
		/// bag's own whole-stack offers have left it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A pre-check, not the decision. The exchange itself (<c>TradeExchange</c>) is
		/// all-or-nothing and refuses for room on its own; this exists so the player is told
		/// "not enough bag space" when they press Accept rather than after both have accepted.
		/// It is deliberately CONSERVATIVE in one direction only — it may say there is room
		/// when a merge order the container chooses differently would find none, in which case
		/// the exchange still refuses safely — and it never says there is no room when there is.
		/// </para>
		/// <para>
		/// Stacks merge only into unlocked stacks of the same template with spare capacity,
		/// and only when the template does not generate per-item attributes (generated items
		/// carry a seed and do not stack across seeds). Anything left over needs
		/// ceil(remaining / MaxStackSize) empty slots. Empty slots counted are the unlocked
		/// empty ones plus the slots this side's whole-stack offers will vacate.
		/// </para>
		/// </remarks>
		/// <param name="bag">The receiving inventory.</param>
		/// <param name="incoming">What the other side is offering.</param>
		/// <param name="outgoing">What this side is offering out of <paramref name="bag"/>.</param>
		/// <param name="requiredSlots">Empty slots the incoming items need after merging.</param>
		/// <param name="availableSlots">Empty slots the bag will have.</param>
		/// <returns>True when <paramref name="requiredSlots"/> fits in <paramref name="availableSlots"/>.</returns>
		public static bool HasRoomFor(IItemContainer bag, TradeOfferEntry[] incoming, TradeOfferEntry[] outgoing,
			out int requiredSlots, out int availableSlots)
		{
			requiredSlots = 0;
			availableSlots = 0;

			if (bag == null)
			{
				return false;
			}

			// Slots this side's whole-stack offers will vacate. A partial offer leaves its stack.
			var vacated = new System.Collections.Generic.HashSet<int>();
			if (outgoing != null)
			{
				for (int i = 0; i < outgoing.Length; ++i)
				{
					TradeOfferEntry entry = outgoing[i];
					if (!bag.TryGetItem(entry.Slot, out Item leaving) || leaving == null)
					{
						continue;
					}
					uint held = leaving.IsStackable ? leaving.Stackable.Amount : 1u;
					if (entry.Amount >= held)
					{
						vacated.Add(entry.Slot);
					}
				}
			}

			for (int i = 0; i < bag.Items.Count; ++i)
			{
				if (bag.IsSlotEmpty(i) && !bag.IsSlotLocked(i))
				{
					availableSlots++;
				}
			}
			availableSlots += vacated.Count;

			if (incoming == null || incoming.Length == 0)
			{
				return true;
			}

			// Spare capacity per resident stack, consumed as incoming entries merge into them.
			var spare = new System.Collections.Generic.Dictionary<int, uint>();
			for (int i = 0; i < bag.Items.Count; ++i)
			{
				Item resident = bag.Items[i];
				if (resident == null || !resident.IsStackable || resident.Template == null ||
					resident.Template.Generate || vacated.Contains(i) ||
					bag.IsSlotLocked(i) || resident.Slot != i)
				{
					continue;
				}
				spare[i] = resident.Stackable.RemainingCapacity;
			}

			for (int i = 0; i < incoming.Length; ++i)
			{
				TradeOfferEntry entry = incoming[i];
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(entry.TemplateID);
				if (template == null || template.MaxStackSize <= 1 || template.Generate)
				{
					requiredSlots++;
					continue;
				}

				uint remaining = entry.Amount < 1 ? 1u : entry.Amount;
				for (int slot = 0; slot < bag.Items.Count && remaining > 0; ++slot)
				{
					Item resident = bag.Items[slot];
					if (resident == null || resident.Template == null || resident.Template.ID != template.ID ||
						!spare.TryGetValue(slot, out uint capacity) || capacity == 0)
					{
						continue;
					}
					uint take = remaining < capacity ? remaining : capacity;
					remaining -= take;
					spare[slot] = capacity - take;
				}

				if (remaining > 0)
				{
					requiredSlots += (int)((remaining + template.MaxStackSize - 1) / template.MaxStackSize);
				}
			}

			return requiredSlots <= availableSlots;
		}

		/// <summary>
		/// Clamps a configured maximum offer slot count into the supported band.
		/// </summary>
		public static int ClampMaxOfferSlots(int configured)
		{
			if (configured < 1)
			{
				return DefaultMaxOfferSlots;
			}
			return configured > MaximumOfferSlots ? MaximumOfferSlots : configured;
		}

		/// <summary>
		/// Clamps a configured range to the supported floor.
		/// </summary>
		public static float ClampMaxDistance(float configured)
		{
			return configured < MinimumMaxDistance ? MinimumMaxDistance : configured;
		}
	}
}
