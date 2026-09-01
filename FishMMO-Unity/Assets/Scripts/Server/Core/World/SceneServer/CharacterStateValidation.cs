using FishNet.Connection;
using FishMMO.Logging;
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
			/* Each refusal SAYS WHY, at Debug level. This gate fronts every state-mutating
			 * broadcast handler on the server, and it used to refuse in total silence — so a
			 * character with one stuck flag (dead, teleporting, a crowd-control flag whose buff
			 * removal misfired, or an IsLoaded that never got re-armed) presented to the player
			 * as "interaction/trading/crafting silently does nothing" with not one line in any
			 * log to bisect with. The messages carry the character id so a live report can be
			 * matched to a specific player. Debug level keeps them out of production noise
			 * unless the level is raised while chasing exactly this class of bug. */
			if (character == null)
			{
				Log.Debug("CharacterStateValidation", "CanAct refused: null character.");
				return false;
			}
			if (character.IsFlagged(CharacterFlags.IsDead))
			{
				Log.Debug("CharacterStateValidation", $"CanAct refused: character {character.ID} is dead.");
				return false;
			}
			if (character.IsTeleporting)
			{
				Log.Debug("CharacterStateValidation", $"CanAct refused: character {character.ID} is teleporting.");
				return false;
			}
			/* Frozen, stunned and mesmerized all mean "cannot act". Only IsFrozen was tested
			 * here; the other two were set by the crowd-control buff templates and read by no
			 * code anywhere, so a stunned player could still craft, trade, use hotkeys and
			 * activate abilities through every broadcast handler that funnels through CanAct. */
			if (CharacterIncapacitation.IsIncapacitated(character))
			{
				Log.Debug("CharacterStateValidation", $"CanAct refused: character {character.ID} is incapacitated (flags 0x{character.Flags:X}).");
				return false;
			}
			if (!character.IsFlagged(CharacterFlags.IsLoaded))
			{
				Log.Debug("CharacterStateValidation", $"CanAct refused: character {character.ID} is not loaded (flags 0x{character.Flags:X}).");
				return false;
			}
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