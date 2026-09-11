using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Support tickets: filed by players in the game, worked by staff in the Control Panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Whether internal notes are returned is decided here, not by the caller.</b> Every read
	/// takes an explicit <c>includeInternal</c>, and a player-facing caller passes false. Making
	/// it the caller's job to filter afterwards is the shape in which internal notes eventually
	/// leak: it only takes one endpoint that forgets, and the failure is silent until a player
	/// quotes a game master's private note back at them.
	/// </para>
	/// <para>
	/// Nothing here checks who is asking. Authorization is the panel's, because it depends on
	/// the caller's access level, which this layer cannot see — but every read that can reach a
	/// player forces the decision to be made.
	/// </para>
	/// </remarks>
	public interface ISupportTicketService
	{
		/// <summary>
		/// Files a ticket.
		/// </summary>
		/// <remarks>
		/// Refuses when the account already holds too many unfinished tickets, or filed one
		/// moments ago. A support queue with no such limit is one a single annoyed player can
		/// make unusable for everybody, and the limit belongs here rather than in one caller
		/// because the game and the panel can both file.
		/// </remarks>
		/// <param name="ticket">The ticket as filed.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The new ticket's id.</returns>
		Task<DatabaseResult<long>> CreateAsync(SupportTicketCreate ticket, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches one ticket and its conversation.
		/// </summary>
		/// <param name="ticketId">The ticket.</param>
		/// <param name="includeInternal">
		/// Whether staff-only notes are included. <b>False for anything a player can reach.</b>
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<SupportTicketData>> FetchAsync(long ticketId, bool includeInternal, CancellationToken cancellationToken = default);

		/// <summary>Searches tickets.</summary>
		/// <param name="query">Filters. Every field is optional.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<SupportTicketPage>> SearchAsync(SupportTicketQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Appends a message and moves the activity timestamp.
		/// </summary>
		/// <remarks>
		/// One method for a player's reply and a staff note alike, because they are the same
		/// row with different flags — and because two methods would be two places for the
		/// internal flag to be got wrong.
		/// </remarks>
		/// <param name="ticketId">The ticket.</param>
		/// <param name="authorAccount">Who wrote it.</param>
		/// <param name="authorIsStaff">Whether they were acting as staff.</param>
		/// <param name="internalNote">Whether the player must never see it.</param>
		/// <param name="body">The text.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<long>> AppendMessageAsync(
			long ticketId,
			string authorAccount,
			bool authorIsStaff,
			bool internalNote,
			string body,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Assigns a ticket, or clears the assignment when <paramref name="staffAccount"/> is null.
		/// </summary>
		/// <remarks>
		/// Moves an <see cref="SupportTicketStatus.Open"/> ticket to
		/// <see cref="SupportTicketStatus.InProgress"/> as a side effect, because taking a
		/// ticket and saying you are working on it are the same act, and a queue where those
		/// are two clicks is a queue full of assigned tickets still marked open.
		/// </remarks>
		Task<DatabaseResult> AssignAsync(long ticketId, string staffAccount, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the status, and the resolution when finishing.
		/// </summary>
		/// <remarks>
		/// A ticket moved to <see cref="SupportTicketStatus.Resolved"/> or
		/// <see cref="SupportTicketStatus.Closed"/> must carry a resolution: the whole value of
		/// a closed ticket is being able to answer "what was decided" a month later.
		/// </remarks>
		Task<DatabaseResult> SetStatusAsync(
			long ticketId,
			SupportTicketStatus status,
			string staffAccount,
			string resolution,
			CancellationToken cancellationToken = default);

		/// <summary>Sets the staff priority.</summary>
		Task<DatabaseResult> SetPriorityAsync(long ticketId, int priority, CancellationToken cancellationToken = default);

		/// <summary>
		/// Counts a player's unfinished tickets, for showing them why they cannot file another.
		/// </summary>
		Task<DatabaseResult<int>> CountOpenForAccountAsync(string accountName, CancellationToken cancellationToken = default);
	}
}
