using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Shared character state validation for broadcast handlers.
	/// Ensures characters cannot perform actions while dead, teleporting, frozen, or unloaded.
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
	}
}
