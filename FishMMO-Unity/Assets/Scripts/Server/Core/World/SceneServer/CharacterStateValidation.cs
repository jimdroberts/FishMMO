using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Shared character state validation for broadcast handlers.
	/// Ensures characters cannot perform actions while dead, teleporting, frozen, unloaded,
	/// or in combat (for movement-restricted operations).
	/// Called at the start of every server-side broadcast handler that mutates game state.
	/// </summary>
	public static class CharacterStateValidation
	{
		/// <summary>
		/// Returns true if the character is in a valid state to perform actions.
		/// Logs and rejects dead, teleporting, frozen, and unloaded characters.
		/// </summary>
		public static bool CanAct(IPlayerCharacter character)
		{
			if (character == null) return false;
			if (character.IsFlagged(CharacterFlags.IsDead)) return false;
			if (character.IsTeleporting) return false;
			if (character.IsFlagged(CharacterFlags.IsFrozen)) return false;
			if (!character.IsFlagged(CharacterFlags.IsLoaded)) return false;
			return true;
		}

		/// <summary>
		/// Returns true if the character is in a valid state to perform movement.
		/// Same as <see cref="CanAct"/> but also rejects characters in combat
		/// (prevents combat-escape via teleport or scene change).
		/// </summary>
		public static bool CanActOrMove(IPlayerCharacter character)
		{
			if (!CanAct(character)) return false;
			if (character.IsFlagged(CharacterFlags.IsInCombat)) return false;
			return true;
		}

		/// <summary>
		/// Resolves the <see cref="IPlayerCharacter"/> from <paramref name="conn"/> and
		/// validates it via <see cref="CanAct"/>. This is the canonical pattern for
		/// server-side broadcast handlers: every state-mutating handler must call this
		/// (or <see cref="CanActOrMove"/> for movement-gated operations) before processing.
		/// <para>
		/// Usage:
		/// <code>
		/// if (!CharacterStateValidation.TryGetPlayerAndValidate(conn, out IPlayerCharacter player))
		///     return;
		/// </code>
		/// </para>
		/// </summary>
		/// <param name="conn">The network connection to resolve the player from.</param>
		/// <param name="player">The resolved and validated player character, or null.</param>
		/// <returns>True if the player was resolved and is in a valid state to act.</returns>
		public static bool TryGetPlayerAndValidate(NetworkConnection conn, out IPlayerCharacter player)
		{
			player = null;
			if (conn == null || conn.FirstObject == null)
				return false;
			player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			return CanAct(player);
		}
	}
}