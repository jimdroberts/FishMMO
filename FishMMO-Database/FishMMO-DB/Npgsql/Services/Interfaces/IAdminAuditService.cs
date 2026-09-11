using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The operator audit log: who did what, to what, and why.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>There is no update and no delete on this interface, and there must never be one.</b>
	/// An audit log that can be edited is not evidence of anything. Retention, if it is ever
	/// needed, belongs in a scheduled database job that an operator cannot reach through the
	/// application, not in a method sitting next to the one that writes the rows.
	/// </para>
	/// <para>
	/// Both the Control Panel and the game server's elevated chat commands write here. One log,
	/// because "show me everything this person did" must not be two queries against two logs.
	/// </para>
	/// </remarks>
	public interface IAdminAuditService
	{
		/// <summary>
		/// Records one action.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Call this for refusals as well as successes. A log holding only what worked cannot
		/// distinguish an operator who never tried from one who tried and was stopped, and the
		/// second is the one worth knowing about.
		/// </para>
		/// <para>
		/// Returns the new row's id. Callers that cannot usefully react to a failure should still
		/// check the result and log it: a write that silently did not happen is the one failure
		/// mode that makes the whole table untrustworthy.
		/// </para>
		/// </remarks>
		/// <param name="entry">The action. <c>OccurredUtc</c> is filled in when left at default.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<long>> AppendAsync(AdminAuditData entry, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads a page of the log, newest first.
		/// </summary>
		/// <param name="query">Filters. Every field is optional.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<AdminAuditPage>> SearchAsync(AdminAuditQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// The distinct action identifiers present in the log, for populating a filter.
		/// </summary>
		/// <remarks>
		/// Read from the data rather than from a constant list, so an action added by a future
		/// command appears in the filter without anybody remembering to register it.
		/// </remarks>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<System.Collections.Generic.IReadOnlyList<string>>> FetchActionsAsync(CancellationToken cancellationToken = default);
	}
}
