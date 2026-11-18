using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for hotkey handling on scene servers.
	/// Implementations should process hotkey set requests coming from clients and
	/// update the player's hotkey state accordingly. Connection and channel types
	/// are intentionally typed as <c>object</c> to avoid tying this contract to a
	/// specific networking library.
	/// </summary>
	public interface IHotkeySystem : IServerBehaviour
	{
		/// <summary>
		/// Clears every ability binding that names <paramref name="abilityID"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Called when an ability is forgotten. Without it the bar keeps naming an id nothing
		/// resolves any more, and nothing revalidates in between: bindings are checked when they are
		/// MADE and once at login, so the dead slot survives until the character relogs — and the
		/// client can only hide it locally, leaving the server's bar and the database row still
		/// naming it.
		/// </para>
		/// <para>
		/// Staging the write and echoing the whole bar happen here rather than at the call site, so
		/// the correction reaches the database and the client in the same session. There is no
		/// hotkey-removal message and none is needed — a clear is an ordinary
		/// <c>HotkeySetBroadcast</c> carrying <c>Type = 0</c> and
		/// <c>ReferenceID = HotkeyData.UnsetReferenceID</c>.
		/// </para>
		/// </remarks>
		/// <param name="playerCharacter">The character whose bar is corrected.</param>
		/// <param name="abilityID">The forgotten ability's instance ID.</param>
		/// <returns>True when at least one binding was cleared.</returns>
		bool ForgetAbilityBindings(IPlayerCharacter playerCharacter, long abilityID);
	}
}