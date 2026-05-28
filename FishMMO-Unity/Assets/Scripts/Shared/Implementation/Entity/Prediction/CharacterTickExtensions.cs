using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Helper extensions for character-related utilities.
	/// </summary>
	public static class CharacterTickExtensions
	{
		/// <summary>
		/// Returns the local TimeManager tick for the provided character, or 0 when unavailable.
		/// </summary>
		public static uint GetLocalTick(this ICharacter character)
		{
			if (character == null) return 0u;
			if (character is BaseCharacter bc)
			{
				return bc.LocalTick;
			}
			return 0u;
		}
	}
}