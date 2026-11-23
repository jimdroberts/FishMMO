namespace FishMMO.Shared
{
	/// <summary>Why a travel spell did not take its caster anywhere.</summary>
	public enum TravelSpellRefusal : byte
	{
		None = 0,

		/// <summary>Dead, incapacitated, unloaded or already teleporting.</summary>
		CannotAct = 1,

		/// <summary>Standing in an arena match.</summary>
		InArena = 2,

		/// <summary>Standing in a <see cref="NoTravelZone"/>.</summary>
		NoTravelZone = 3,

		/// <summary>There is no destination to go to (no bind point, no mark).</summary>
		NoDestination = 4,

		/// <summary>The scene server could not move the character.</summary>
		TransferFailed = 5,

		/// <summary>Mark only: inside an instance, which is never a destination.</summary>
		InInstance = 6,

		/// <summary>Mark only: not a spot the caster could safely be put back down on (see <c>StandingSpot</c>).</summary>
		UnsafeSpot = 7,
	}

	/// <summary>
	/// The rules every travel spell (Portal to Bind, Recall, Gate) applies before it moves anyone (issue #241).
	/// </summary>
	/// <remarks>
	/// <para>Combat is deliberately not a rule: a travel spell is castable in combat, and is stopped only by interrupting
	/// it or by damaging the caster (<c>AbilityTemplate.InterruptedByDamage</c>). Instances are not one either: a spell
	/// may take its caster out of one, never into one.</para>
	/// <para>Pure, so the order is a table the tests pin.</para>
	/// </remarks>
	public static class TravelSpellRules
	{
		/// <summary>The first rule that refuses, in order, or <see cref="TravelSpellRefusal.None"/>.</summary>
		public static TravelSpellRefusal Decide(bool canAct, bool inArena, bool inNoTravelZone, bool hasDestination)
		{
			if (!canAct)
			{
				return TravelSpellRefusal.CannotAct;
			}
			if (inArena)
			{
				return TravelSpellRefusal.InArena;
			}
			if (inNoTravelZone)
			{
				return TravelSpellRefusal.NoTravelZone;
			}
			if (!hasDestination)
			{
				return TravelSpellRefusal.NoDestination;
			}
			return TravelSpellRefusal.None;
		}

		/// <summary>
		/// The Mark spell's rules, in order: the shared ones, then not inside an instance, then a spot the caster can be put
		/// back down on.
		/// </summary>
		public static TravelSpellRefusal DecideMark(bool canAct, bool inArena, bool inNoTravelZone, bool inInstance, bool safeSpot)
		{
			TravelSpellRefusal shared = Decide(canAct, inArena, inNoTravelZone, hasDestination: true);
			if (shared != TravelSpellRefusal.None)
			{
				return shared;
			}
			if (inInstance)
			{
				return TravelSpellRefusal.InInstance;
			}
			if (!safeSpot)
			{
				return TravelSpellRefusal.UnsafeSpot;
			}
			return TravelSpellRefusal.None;
		}

		/// <summary>
		/// The rules of the spells that open a way to the caster's side (Gate, Call to Arms), in order: the shared ones, then not
		/// inside an instance (nothing leads into one).
		/// </summary>
		public static TravelSpellRefusal DecideOpening(bool canAct, bool inArena, bool inNoTravelZone, bool inInstance)
		{
			TravelSpellRefusal shared = Decide(canAct, inArena, inNoTravelZone, hasDestination: true);
			if (shared != TravelSpellRefusal.None)
			{
				return shared;
			}
			return inInstance ? TravelSpellRefusal.InInstance : TravelSpellRefusal.None;
		}

		/// <summary>What the caster is told, or null for a refusal that says nothing (one the caster cannot act on).</summary>
		/// <param name="refusal">The refusal.</param>
		/// <param name="noDestinationText">The line for <see cref="TravelSpellRefusal.NoDestination"/>, which differs per spell.</param>
		/// <param name="inInstanceText">The line for <see cref="TravelSpellRefusal.InInstance"/>; Mark's when null.</param>
		public static string Describe(TravelSpellRefusal refusal, string noDestinationText, string inInstanceText = null)
		{
			switch (refusal)
			{
				case TravelSpellRefusal.InArena:
					return "Travel magic does not work in an arena.";
				case TravelSpellRefusal.NoTravelZone:
					return "Something here stops travel magic.";
				case TravelSpellRefusal.NoDestination:
					return noDestinationText;
				case TravelSpellRefusal.TransferFailed:
					return "The portal fails to open.";
				case TravelSpellRefusal.InInstance:
					return inInstanceText ?? "You cannot mark a location inside an instance.";
				case TravelSpellRefusal.UnsafeSpot:
					return "You cannot mark this spot. Find somewhere clearer to stand.";
				default:
					return null;
			}
		}
	}
}
