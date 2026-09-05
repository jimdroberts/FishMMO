using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Holds what was standing on a plot when its owner lost it.
	/// </summary>
	public interface IPlotVaultService
	{
		/// <summary>
		/// Moves everything built on a plot into its owner's vault, and clears the plot.
		/// </summary>
		/// <remarks>
		/// One transaction, deliberately. Storing and clearing as two writes has two failure modes
		/// and both are bad: stop after the store and the owner has their furniture in the vault
		/// <em>and</em> still standing on land somebody else is about to buy; stop before it and the
		/// house is gone with nothing to show for it. Neither is recoverable by retrying, because
		/// the retry cannot tell which half already happened.
		/// </remarks>
		/// <returns>How many vault rows the move produced.</returns>
		Task<DatabaseResult<int>> StorePlotContentsAsync(long plotID, long characterID, long baseFeePerEntry, float feeRatePerDay, CancellationToken cancellationToken = default);

		/// <summary>
		/// Everything one character is owed.
		/// </summary>
		Task<DatabaseResult<List<PlotVaultData>>> FetchByCharacterAsync(long characterID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads one vault row, pinned to the character who owns it.
		/// </summary>
		/// <remarks>
		/// The owner is part of the query rather than checked afterwards, so a request naming
		/// somebody else's row finds nothing instead of finding something the caller then has to
		/// remember to reject.
		/// </remarks>
		Task<DatabaseResult<PlotVaultData?>> FetchEntryAsync(long vaultID, long characterID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes one entry out of the vault, pinned to the character who owns it.
		/// </summary>
		/// <remarks>
		/// The single atomic step in both retrieval and forfeiting. Retrieval charges first and then
		/// calls this: whoever gets the 1 back is the one who removed it, so a player clicking twice
		/// pays once and a row cannot be handed out to two requests at the same moment.
		/// </remarks>
		/// <returns>One when the entry was removed, zero when it was already gone.</returns>
		Task<DatabaseResult<int>> TryRemoveEntryAsync(long vaultID, long characterID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Empties a character's vault.
		/// </summary>
		Task<DatabaseResult<int>> ForfeitAllAsync(long characterID, CancellationToken cancellationToken = default);
	}
}
