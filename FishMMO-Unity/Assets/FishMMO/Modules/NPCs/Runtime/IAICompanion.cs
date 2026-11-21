using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// What the NPCs module's AI reads of a companion it drives (2026-11-20: pets are the Pets module's, which depends on
	/// this one): whom it follows and fights for, its stance and its movement order. The Pets module's <c>Pet</c>
	/// implements it.
	/// </summary>
	public interface IAICompanion : ICharacter
	{
		/// <summary>The character it belongs to, or null.</summary>
		ICharacter PetOwner { get; }

		/// <summary>When it fights.</summary>
		PetStance Stance { get; }

		/// <summary>Where it goes when not fighting.</summary>
		PetMovementOrder MovementOrder { get; }
	}

	/// <summary>Companion notices the AI listens to.</summary>
	public static class AICompanions
	{
		/// <summary>
		/// Raised whenever a companion's owner changes (null when the link is cleared). The server's threat dispatcher
		/// listens, so threat against either member of the pair is threat against both.
		/// </summary>
		public static event Action<IAICompanion, ICharacter> OnOwnerChanged;

		/// <summary>Raises <see cref="OnOwnerChanged"/>; the companion calls it.</summary>
		public static void RaiseOwnerChanged(IAICompanion companion, ICharacter owner) => OnOwnerChanged?.Invoke(companion, owner);
	}
}
