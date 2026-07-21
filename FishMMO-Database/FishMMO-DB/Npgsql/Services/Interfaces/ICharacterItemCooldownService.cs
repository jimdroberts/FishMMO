using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character item cooldowns.
	/// </summary>
	/// <remarks>
	/// Item cooldown persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterItemCooldownService :
		ICountByKeyAction<long>,
		IPersistAction<CharacterItemCooldownData, long>,
		IPersistManyAction<CharacterItemCooldownData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterItemCooldownData>
	{
	}
}
