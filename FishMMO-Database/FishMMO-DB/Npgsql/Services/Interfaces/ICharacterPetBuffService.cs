using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character pet buffs.
	/// </summary>
	/// <remarks>
	/// Pet buff persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterPetBuffService :
		ICountByKeyAction<long>,
		IPersistAction<CharacterPetBuffData, long>,
		IPersistManyAction<CharacterPetBuffData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterPetBuffData>
	{
	}
}
