using System.Runtime.CompilerServices;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Convenience accessors derived from <see cref="IPlayerCharacter"/>'s own state.
	/// </summary>
	public static class IPlayerCharacterExtensions
	{
		/// <summary>
		/// The name of the scene the character is physically standing in.
		/// </summary>
		/// <remarks>
		/// Not the same thing as <see cref="IPlayerCharacter.SceneName"/>. While a character is
		/// inside an instance, <c>SceneName</c> keeps naming the open-world scene it will return
		/// to — that is what makes the return trip possible — and the scene it is actually in is
		/// <see cref="IPlayerCharacter.InstanceSceneName"/>. Reading <c>SceneName</c> where this
		/// was meant has been the same bug several times over: teleporters inside a dungeon
		/// could never fire because the wrong scene's teleporter list was consulted, and every
		/// interactable handler's "is this a known scene?" check was answered about a scene the
		/// character had left.
		/// <para>
		/// This names a scene, so it still cannot distinguish one instance of a scene from
		/// another — scene stacking means several share a name. Anything that needs instance
		/// identity within this process must compare <c>GameObject.scene.handle</c>, and
		/// anything that needs it across processes must use the scene row id. See
		/// <see cref="IPlayerCharacter.SceneHandle"/>.
		/// </para>
		/// </remarks>
		/// <param name="character">Character to inspect.</param>
		/// <returns>The instance scene name when inside an instance; otherwise the world scene name.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string CurrentSceneName(this IPlayerCharacter character)
		{
			if (character == null)
			{
				return null;
			}

			return character.IsInInstance() && !string.IsNullOrEmpty(character.InstanceSceneName)
				? character.InstanceSceneName
				: character.SceneName;
		}
	}
}
