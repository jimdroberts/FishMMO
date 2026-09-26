using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for reading character buffs, and for tombstoning them on character deletion.
	/// </summary>
	/// <remarks>
	/// <b>There is deliberately no write here.</b> A character's buffs are written as one set with
	/// its character row — <see cref="FishMMO.Database.Data.CharacterData.Buffs"/>, applied by
	/// <c>ICharacterService.PersistOwnedAsync</c>, <c>PersistAsync</c> and <c>PersistManyAsync</c> —
	/// because the row's version is the only per-character ordering that can make "delete what the
	/// set no longer names" safe against saves landing out of order. The per-row upsert that used to
	/// be here could only add and overwrite, so an expired or dismissed buff came back at the next
	/// login; a second writer beside the set would reopen exactly that.
	/// </remarks>
	public interface ICharacterBuffService :
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterBuffData>
	{
	}
}