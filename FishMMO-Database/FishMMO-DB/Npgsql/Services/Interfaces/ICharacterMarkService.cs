using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for a character's one marked location (issue #241). See <see cref="CharacterMarkData"/>.
	/// </summary>
	public interface ICharacterMarkService :
		IFetchByKeyAction<long, CharacterMarkData?>,
		IDeleteByKeyAction<long>
	{
		/// <summary>Writes the mark, replacing any the character had.</summary>
		Task<DatabaseResult> SaveAsync(CharacterMarkData mark, CancellationToken cancellationToken = default);
	}
}
