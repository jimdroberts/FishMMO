using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Reads and moves a character's currency.
	/// </summary>
	/// <remarks>
	/// Currency is a <see cref="CharacterAttribute"/> rather than a field of its own, so every
	/// system that touches it — looting, crafting, merchants, mail, and housing — has to resolve
	/// the template, find the attribute, check sufficiency, write the balance and persist it. Done
	/// by hand at each site that is five chances to get it wrong, and it has already gone wrong:
	/// the crafting path tested <c>FinalValue</c> while writing <c>Value</c>, which let a
	/// character wearing any currency-boosting buff spend money it did not have, and that was
	/// fixed in the merchant and ability-learning paths before anyone noticed crafting had the
	/// same defect.
	///
	/// <para>This puts the sequence in one place so a new caller inherits the corrected behaviour
	/// instead of reimplementing it.</para>
	/// </remarks>
	public static class CharacterCurrency
	{
		/// <summary>
		/// Reads a character's currency balance.
		/// </summary>
		/// <remarks>
		/// Deliberately the BASE value, not <see cref="CharacterAttribute.FinalValue"/>.
		/// <see cref="CharacterAttribute.AddValue"/> writes the base, and FinalValue is the base
		/// plus every modifier in force — so testing one while writing the other lets a buff be
		/// spent as though it were money and drives the balance negative by exactly the size of
		/// the buff.
		/// <para>
		/// The balance includes any credit a settling trade is still holding
		/// (<see cref="CharacterAttribute.HeldValue"/>). That is right for everything this is used for
		/// beyond spending — the headroom left in an <c>int</c>, what a report shows — and
		/// <see cref="TrySpend"/> is where the hold is enforced.
		/// </para>
		/// </remarks>
		/// <param name="character">The character to read.</param>
		/// <param name="template">The currency attribute template.</param>
		/// <param name="balance">The balance, or zero when it could not be read.</param>
		/// <returns>True when the character has the attribute.</returns>
		public static bool TryGetBalance(ICharacter character, CharacterAttributeTemplate template, out long balance)
		{
			balance = 0;

			if (character == null || template == null)
			{
				return false;
			}

			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(template, out CharacterAttribute currency))
			{
				return false;
			}

			balance = currency.Value;
			return true;
		}

		/// <summary>
		/// What of a currency attribute's balance may be spent: the base value less any credit a
		/// settling transaction is holding. Never negative. Pure.
		/// </summary>
		/// <remarks>
		/// A held credit is in the value so that every save carries it, and out of reach so that a
		/// refused transaction can always take it back exactly; see
		/// <see cref="CharacterAttribute.HeldValue"/>.
		/// </remarks>
		/// <param name="value">The attribute's base value.</param>
		/// <param name="held">Its held credit.</param>
		/// <returns>The spendable amount.</returns>
		public static long Spendable(int value, int held)
		{
			long spendable = (long)value - Math.Max(0, held);
			return spendable > 0 ? spendable : 0;
		}

		/// <summary>
		/// True when the character holds at least <paramref name="amount"/>.
		/// </summary>
		/// <remarks>
		/// A non-positive amount is affordable by definition; callers that treat zero as a free
		/// transaction do not need to special-case it.
		/// </remarks>
		public static bool CanAfford(ICharacter character, CharacterAttributeTemplate template, long amount)
		{
			if (amount <= 0)
			{
				return true;
			}
			return TryGetBalance(character, template, out long balance) && balance >= amount;
		}

		/// <summary>
		/// Grants currency to a character.
		/// </summary>
		/// <param name="amount">Amount to grant. Non-positive amounts are rejected rather than
		/// silently deducting, so a sign error cannot quietly take money.</param>
		/// <returns>True when the balance was changed.</returns>
		public static bool TryAdd(ICharacter character, CharacterAttributeTemplate template, long amount)
		{
			if (amount <= 0)
			{
				return false;
			}

			if (character == null || template == null)
			{
				return false;
			}

			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(template, out CharacterAttribute currency))
			{
				return false;
			}

			currency.AddValue((int)Math.Min(amount, int.MaxValue));
			return true;
		}

		/// <summary>
		/// Deducts currency, persists the change, and refunds it if persistence is refused.
		/// </summary>
		/// <remarks>
		/// The ordering is deliberate and matches the merchant, ability-learning and crafting
		/// paths: deduct, then persist, then let the caller grant whatever was bought. Persisting
		/// snapshots the in-memory values as they stand, so it has to run after the deduction
		/// rather than before it — and if the write is refused the deduction has to be undone,
		/// otherwise the player is charged for something they never received.
		///
		/// <para>Persistence is supplied by the caller because it differs by context: the scene
		/// server has its own attribute-write path, and shared code has no business knowing about
		/// it. Passing null skips persistence for callers that batch their own writes.</para>
		/// </remarks>
		/// <param name="character">The character to charge.</param>
		/// <param name="template">The currency attribute template.</param>
		/// <param name="amount">Amount to deduct. Non-positive amounts are rejected.</param>
		/// <param name="persist">Persistence callback; return false to refuse and trigger a refund.</param>
		/// <returns>True when the character was charged and the change persisted.</returns>
		public static bool TrySpend(ICharacter character, CharacterAttributeTemplate template, long amount, Func<bool> persist = null)
		{
			if (amount <= 0)
			{
				return false;
			}

			if (character == null || template == null)
			{
				return false;
			}

			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(template, out CharacterAttribute currency))
			{
				return false;
			}

			int price = (int)Math.Min(amount, int.MaxValue);
			/* Against what is spendable, not the whole balance: a credit a settling trade is holding
			 * must still be there if that trade is refused. The hold lasts only while the trade's
			 * transaction is in flight, so a spend that needs it is refused for those milliseconds and
			 * nothing else. CanAfford deliberately does not subtract it — callers use it to tell "too
			 * poor" from "could not be done now", and a hold is the second. */
			if (Spendable(currency.Value, currency.HeldValue) < price)
			{
				return false;
			}

			currency.AddValue(-price);

			if (persist != null && !persist())
			{
				currency.AddValue(price);
				Log.Warning("CharacterCurrency",
					$"Spend of {price} refused by persistence for CharID={character.ID}; refunded.");
				return false;
			}

			return true;
		}
	}
}
