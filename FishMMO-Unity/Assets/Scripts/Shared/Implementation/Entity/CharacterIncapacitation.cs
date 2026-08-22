using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Single definition of "this character is incapacitated and may not act or move".
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="CharacterFlags.IsStunned"/> and <see cref="CharacterFlags.IsMesmerized"/> were
	/// SET by <see cref="StateBuffTemplate"/> and <see cref="CompositeBuffTemplate"/> and READ BY
	/// NOTHING. A repo-wide sweep found exactly one crowd-control flag with a reader —
	/// <see cref="CharacterFlags.IsFrozen"/>, tested by <c>KCCPlayer.OnReplicate</c> and
	/// <c>CharacterStateValidation.CanAct</c>. Every stun and every mesmerize in the game was
	/// therefore purely cosmetic: the target kept casting, kept moving and kept using items for
	/// the buff's whole duration.
	/// </para>
	/// <para>
	/// The three gates that need this test live in three different assemblies (the KCC movement
	/// replicate, the ability controller's manipulation check, and the server's broadcast-handler
	/// validation), so the definition lives here rather than being written out three times and
	/// drifting. Adding a new crowd-control flag now means editing one method.
	/// </para>
	/// <para>
	/// <b>Determinism:</b> these flags are written by buff apply/remove, which runs inside the
	/// prediction pipeline before ability activation (<c>BuffController.Order == 85</c>), and are
	/// reconciled with the rest of the buff state. Reading them from a replicate path is the same
	/// thing <c>IsFrozen</c> already does.
	/// </para>
	/// </remarks>
	public static class CharacterIncapacitation
	{
		/// <summary>
		/// Returns true when crowd control currently prevents the character from acting.
		/// </summary>
		/// <param name="character">The character to test.</param>
		/// <returns>True if the character is frozen, stunned or mesmerized.</returns>
		public static bool IsIncapacitated(ICharacter character)
		{
			if (character == null)
			{
				return false;
			}

			return character.IsFlagged(CharacterFlags.IsFrozen) ||
				character.IsFlagged(CharacterFlags.IsStunned) ||
				character.IsFlagged(CharacterFlags.IsMesmerized);
		}
	}
}
