using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character hotkeys.
	/// </summary>
	/// <remarks>
	/// Hotkey persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterHotkeyService :
		ICountByKeyAction<long>,
		IPersistAction<CharacterHotkeyData, long>,
		IPersistManyAction<CharacterHotkeyData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterHotkeyData>
	{
	}
}