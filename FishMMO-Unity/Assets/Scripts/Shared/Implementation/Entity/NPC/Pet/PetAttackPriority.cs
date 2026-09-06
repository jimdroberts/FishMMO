namespace FishMMO.Shared
{
	/// <summary>
	/// One way a pet attack command can choose its victim.
	/// </summary>
	public enum PetAttackTarget : byte
	{
		/// <summary>The owner's pinned target.</summary>
		Pinned = 0,

		/// <summary>The owner's current (hovered) target.</summary>
		Current = 1,

		/// <summary>The NPC holding the most threat against the owner.</summary>
		HighestThreat = 2,
	}

	/// <summary>
	/// The order a pet attack command tries its three ways of choosing a victim, packed into one
	/// int so it can ride a broadcast and a settings file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three steps, each used exactly once, tried first to last until one resolves a valid
	/// target. The shipped default is pinned, then current, then highest threat — what a player
	/// most plausibly means by "attack my target" — and the panel lets a player reorder it.
	/// </para>
	/// <para>
	/// Layout: bit 0 is a set marker so that 0, the value an old client or an unset field
	/// carries, is never a valid order and decodes to the default instead; bits 2-3, 4-5 and 6-7
	/// hold the first, second and third step. Pure, so both peers and the tests agree on it.
	/// </para>
	/// </remarks>
	public static class PetAttackPriority
	{
		/// <summary>How many steps an order has.</summary>
		public const int StepCount = 3;

		private const int SetMarker = 1;
		private const int StepBits = 2;
		private const int StepMask = 3;
		private const int FirstStepShift = 2;

		/// <summary>Pinned, then current, then highest threat.</summary>
		public static readonly int Default = Encode(PetAttackTarget.Pinned, PetAttackTarget.Current, PetAttackTarget.HighestThreat);

		/// <summary>Packs an order. The caller is trusted to pass a permutation; see <see cref="IsValid"/>.</summary>
		public static int Encode(PetAttackTarget first, PetAttackTarget second, PetAttackTarget third)
		{
			return SetMarker |
				((int)first << FirstStepShift) |
				((int)second << (FirstStepShift + StepBits)) |
				((int)third << (FirstStepShift + StepBits * 2));
		}

		/// <summary>
		/// Unpacks an order into <paramref name="order"/> (first step at index 0).
		/// </summary>
		/// <returns>False when the value is not a permutation of the three steps.</returns>
		public static bool TryDecode(int packed, PetAttackTarget[] order)
		{
			if (order == null || order.Length < StepCount || (packed & SetMarker) == 0)
			{
				return false;
			}

			int seen = 0;
			for (int i = 0; i < StepCount; ++i)
			{
				int value = (packed >> (FirstStepShift + StepBits * i)) & StepMask;
				if (value >= StepCount || (seen & (1 << value)) != 0)
				{
					return false;
				}
				seen |= 1 << value;
				order[i] = (PetAttackTarget)value;
			}
			return true;
		}

		/// <summary>True when <paramref name="packed"/> decodes to a permutation of the three steps.</summary>
		public static bool IsValid(int packed)
		{
			return TryDecode(packed, new PetAttackTarget[StepCount]);
		}

		/// <summary>The value itself when valid, else <see cref="Default"/>.</summary>
		public static int Normalize(int packed)
		{
			return IsValid(packed) ? packed : Default;
		}

		/// <summary>
		/// Swaps the step at <paramref name="slot"/> with the one before it, so it is tried one
		/// place earlier. Slot 0 and an invalid slot leave the order as it is.
		/// </summary>
		public static int MoveUp(int packed, int slot)
		{
			PetAttackTarget[] order = new PetAttackTarget[StepCount];
			if (!TryDecode(packed, order))
			{
				TryDecode(Default, order);
			}
			if (slot <= 0 || slot >= StepCount)
			{
				return Encode(order[0], order[1], order[2]);
			}

			PetAttackTarget moved = order[slot];
			order[slot] = order[slot - 1];
			order[slot - 1] = moved;
			return Encode(order[0], order[1], order[2]);
		}

		/// <summary>The player-facing name of a step.</summary>
		public static string Describe(PetAttackTarget target)
		{
			switch (target)
			{
				case PetAttackTarget.Pinned:
					return "Pinned target";
				case PetAttackTarget.Current:
					return "Current target";
				case PetAttackTarget.HighestThreat:
					return "Highest threat";
				default:
					return target.ToString();
			}
		}
	}
}
