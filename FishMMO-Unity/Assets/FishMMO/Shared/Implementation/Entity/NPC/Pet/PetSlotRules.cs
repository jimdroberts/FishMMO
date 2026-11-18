using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// How many pets a character may keep out, and which one a new summon replaces. Pure.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two limits apply. A character has the pet slots its Pet Slots character attribute gives it
	/// (<see cref="Core.IPetController.PetSlots"/>: one unless an archetype, an item or a buff raises it), and each summon
	/// spell carries its own maximum
	/// (<see cref="PetAbilityTemplate.MaxPets"/>): a spell that may raise three wolves still raises one for a character
	/// with one slot.
	/// </para>
	/// <para>
	/// A summon past either limit replaces the oldest pet in its way rather than failing: past the spell's limit, the
	/// oldest pet of that spell; past the slots, the oldest pet of any kind. That is what a summon did when a character
	/// could only have one pet.
	/// </para>
	/// <para>
	/// A pet keeps the slot it was given for as long as it is out, and the database stores each pet under its slot. A
	/// slot is not a list position: when the first of two pets dies the second stays in slot 1.
	/// </para>
	/// </remarks>
	public static class PetSlotRules
	{
		/// <summary>Pet slots a character has unless an archetype grants more.</summary>
		public const int DefaultSlots = 1;

		/// <summary>The most slots anything may grant. Bounds what the server accepts and the database stores.</summary>
		public const int MaxSlots = 8;

		/// <summary>
		/// The character attribute that holds a character's pet slots. Its asset starts at <see cref="DefaultSlots"/>, so an
		/// archetype, an item or a buff grants slots the way it moves any other attribute.
		/// </summary>
		public const string AttributeName = "Pet Slots";

		/// <summary>The template ID of the <see cref="AttributeName"/> asset, which a pet controller uses unless its prefab names another.</summary>
		public static readonly int DefaultAttributeID = (nameof(CharacterAttributeTemplate) + AttributeName).GetDeterministicHashCode();

		/// <summary>
		/// A character's pet slots from its Pet Slots attribute: the attribute's final value, clamped, or
		/// <see cref="DefaultSlots"/> when the character has no such attribute.
		/// </summary>
		public static int SlotsFrom(bool hasAttribute, int finalValue)
		{
			return hasAttribute ? ClampSlots(finalValue) : DefaultSlots;
		}

		/// <summary>True when a pet holds a slot at or past <paramref name="slots"/>: the slots fell under it and it must go.</summary>
		/// <param name="usedSlots">The slot of each pet out.</param>
		/// <param name="slots">The character's pet slots now.</param>
		public static bool AnyPastSlots(IReadOnlyList<int> usedSlots, int slots)
		{
			int count = ClampSlots(slots);
			for (int i = 0; usedSlots != null && i < usedSlots.Count; ++i)
			{
				if (usedSlots[i] >= count)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The character's slot count, clamped to 1..<see cref="MaxSlots"/>.</summary>
		public static int ClampSlots(int slots)
		{
			return Math.Max(1, Math.Min(MaxSlots, slots));
		}

		/// <summary>
		/// The most pets of one spell a character can have out: the smaller of the spell's own maximum and the character's
		/// slots, never less than one.
		/// </summary>
		public static int SpellCap(int spellMax, int slots)
		{
			return Math.Min(Math.Max(1, spellMax), ClampSlots(slots));
		}

		/// <summary>
		/// Which pet a new summon must replace, or -1 when it fits.
		/// </summary>
		/// <param name="petTemplateIDs">The template of each pet out, oldest first.</param>
		/// <param name="templateID">The template being summoned.</param>
		/// <param name="spellMax">The summon spell's own maximum.</param>
		/// <param name="slots">The character's pet slots.</param>
		/// <returns>The index into <paramref name="petTemplateIDs"/> of the pet to dismiss, or -1.</returns>
		public static int IndexToReplace(IReadOnlyList<int> petTemplateIDs, int templateID, int spellMax, int slots)
		{
			if (petTemplateIDs == null || petTemplateIDs.Count == 0)
			{
				return -1;
			}

			int ofThisSpell = 0;
			int oldestOfThisSpell = -1;
			for (int i = 0; i < petTemplateIDs.Count; ++i)
			{
				if (petTemplateIDs[i] == templateID)
				{
					if (oldestOfThisSpell < 0)
					{
						oldestOfThisSpell = i;
					}
					++ofThisSpell;
				}
			}

			if (ofThisSpell >= SpellCap(spellMax, slots))
			{
				return oldestOfThisSpell;
			}
			if (petTemplateIDs.Count >= ClampSlots(slots))
			{
				return 0;
			}
			return -1;
		}

		/// <summary>
		/// The lowest slot no pet holds, or -1 when all <paramref name="slots"/> are taken.
		/// </summary>
		/// <param name="usedSlots">The slot of each pet out.</param>
		/// <param name="slots">The character's pet slots.</param>
		public static int LowestFreeSlot(IReadOnlyList<int> usedSlots, int slots)
		{
			int count = ClampSlots(slots);
			for (int slot = 0; slot < count; ++slot)
			{
				bool taken = false;
				for (int i = 0; usedSlots != null && i < usedSlots.Count; ++i)
				{
					if (usedSlots[i] == slot)
					{
						taken = true;
						break;
					}
				}
				if (!taken)
				{
					return slot;
				}
			}
			return -1;
		}
	}
}
