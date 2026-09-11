namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// What a support ticket is about.
	/// </summary>
	/// <remarks>
	/// The category decides who should look at it and how urgently, so it is a column rather
	/// than something inferred from the text. A player reporting harassment and a player
	/// reporting a broken quest need different people and different response times.
	/// </remarks>
	public enum SupportTicketCategory
	{
		/// <summary>Something is wrong with the game itself.</summary>
		Bug = 0,

		/// <summary>
		/// A complaint about another player: harassment, cheating, exploiting.
		/// </summary>
		/// <remarks>
		/// The only category that names a target, and the one that leads to a moderation
		/// action, so the ticket carries the accused account and character alongside the time
		/// and place — which is what makes the chat log searchable around the incident.
		/// </remarks>
		PlayerReport = 1,

		/// <summary>A request for help: stuck, lost an item, cannot progress.</summary>
		Help = 2,

		/// <summary>A player asking staff to reconsider a ban or other action.</summary>
		Appeal = 3,

		/// <summary>Anything else.</summary>
		Other = 4,
	}
}
