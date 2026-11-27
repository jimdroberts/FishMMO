using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for a character's hired mercenary (the Mercenaries module). See <see cref="CharacterMercenaryData"/>.
	/// </summary>
	public interface ICharacterMercenaryService :
		IFetchByKeyAction<long, CharacterMercenaryData>,
		IDeleteByKeyAction<long>
	{
		/// <summary>Writes the mercenary, replacing any row the character had.</summary>
		Task<DatabaseResult> SaveAsync(CharacterMercenaryData mercenary, CancellationToken cancellationToken = default);
	}
}
