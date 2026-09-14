using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/*
	 * The staff console's wire contract.
	 *
	 * The client ships an EMPTY console. Nothing in the client build names a staff command, an
	 * argument or a help line: the console is populated entirely from StaffConsoleCatalogBroadcast,
	 * which the server sends only to an account at GameMaster or above, and only in answer to the
	 * elevated `/gm console` command. A player who digs the console out of the build finds a shell
	 * with nothing in it.
	 *
	 * That is not where the security is, and nothing here should be read as if it were. Every
	 * action the console takes is an ordinary chat command, checked and audited at the server's
	 * single access gate exactly as if it had been typed; and every read request below is refused
	 * server-side unless the sender's own access level — loaded from the character row, never from
	 * anything the client sends — is high enough. A refused read is recorded in the operator audit
	 * log as a refused command, because a forged staff request is the probing pattern that log
	 * exists to show. The empty shell keeps the command surface out of the build; the gate is what
	 * keeps it closed.
	 *
	 * Allowed reads are audited too, matching the Control Panel, except an automatic refresh: the
	 * roster the console re-reads on a timer sets StaffRosterRequestBroadcast.AutoRefresh, and that
	 * request is answered without a row, since a roster refreshed every few seconds would otherwise
	 * bury every row that matters. The first read and every read somebody asked for are recorded,
	 * and a refused request is recorded whatever the flag says.
	 */

	/// <summary>
	/// The shape of one argument a console command takes, so the console can offer the right
	/// input for it without knowing anything about the command.
	/// </summary>
	public enum StaffArgumentKind : byte
	{
		/// <summary>Free text. Always the last argument: it consumes the rest of the line.</summary>
		Text = 0,

		/// <summary>A single word with no spaces.</summary>
		Word = 1,

		/// <summary>A character name. The console offers roster names.</summary>
		Character = 2,

		/// <summary>An account name.</summary>
		Account = 3,

		/// <summary>A whole number.</summary>
		Integer = 4,

		/// <summary>A decimal number.</summary>
		Number = 5,

		/// <summary>A duration such as <c>30m</c>, <c>2h</c> or <c>7d</c>.</summary>
		Duration = 6,

		/// <summary>A support ticket number.</summary>
		Ticket = 7,

		/// <summary>One of a fixed set of words, listed in <see cref="StaffCommandEntry.Arguments"/>.</summary>
		Choice = 8,
	}

	/// <summary>
	/// One command the receiving account may run, as the console needs to present it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Built on the server from the same table the command dispatcher runs from, so the console can
	/// never offer a command the dispatcher does not have, nor describe one differently from the
	/// server's own help.
	/// </para>
	/// <para>
	/// <see cref="Arguments"/> is a compact spec rather than a nested array, because a flat struct of
	/// strings is the shape FishNet's generated serializers are known to handle. Entries are
	/// separated by <c>;</c>, and each is <c>label:Kind</c>, optionally followed by
	/// <c>=a,b,c</c> for a <see cref="StaffArgumentKind.Choice"/> and by a trailing <c>?</c> when
	/// the argument may be left out. Example: <c>character:Character?;amount:Integer</c>.
	/// </para>
	/// <para>
	/// The line the console sends is <c>Command Name arg1 arg2 ...</c>, and it travels as an
	/// ordinary <see cref="ChatBroadcast"/>, so it is subject to
	/// <see cref="ChatBroadcast.MaxTextLength"/> like anything typed into chat.
	/// </para>
	/// </remarks>
	public struct StaffCommandEntry
	{
		/// <summary>The registered slash command this belongs to, including its slash.</summary>
		public string Command;

		/// <summary>The sub-command word.</summary>
		public string Name;

		/// <summary>The group the console files it under.</summary>
		public string Category;

		/// <summary>One line saying what it does.</summary>
		public string Summary;

		/// <summary>The argument spec. See the type's remarks for the grammar.</summary>
		public string Arguments;

		/// <summary>True when the first argument is a character, so the console may offer it on a roster row.</summary>
		public bool RosterAction;

		/// <summary>True when the first argument is a ticket number, so the console may offer it on a ticket.</summary>
		public bool TicketAction;

		/// <summary>True when the command changes something that cannot simply be undone, so the console should confirm first.</summary>
		public bool Destructive;
	}

	/// <summary>
	/// Server to staff client: everything the console may show this account.
	/// </summary>
	/// <remarks>
	/// Never sent to an account below GameMaster. Sent in answer to <c>/gm console</c>, which is
	/// itself an elevated command, so opening the console leaves an audit row.
	/// </remarks>
	public struct StaffConsoleCatalogBroadcast : IBroadcast
	{
		/// <summary>The receiving account's access level, as the server holds it.</summary>
		public byte AccessLevel;

		/// <summary>True when the console should open on receipt.</summary>
		public bool Open;

		/// <summary>The data views this account may use, by id: <c>players</c>, <c>tickets</c>.</summary>
		public string[] Views;

		/// <summary>Every command this account may run.</summary>
		public StaffCommandEntry[] Commands;
	}

	/// <summary>Staff client to server: send the roster of the scene I am standing in.</summary>
	public struct StaffRosterRequestBroadcast : IBroadcast
	{
		/// <summary>
		/// True when the console sent this on its refresh timer rather than because somebody opened
		/// the view or asked. An allowed automatic refresh is not audited; a refusal always is.
		/// </summary>
		/// <remarks>
		/// The client's word, trusted only because this is a read: it can hide no more than a client
		/// that simply stopped polling, and nothing a staff member DOES arrives through this request.
		/// </remarks>
		public bool AutoRefresh;
	}

	/// <summary>One character on a staff roster.</summary>
	public struct StaffRosterEntry
	{
		/// <summary>Character id.</summary>
		public long CharacterID;

		/// <summary>Character name.</summary>
		public string Name;

		/// <summary>Owning account.</summary>
		public string Account;

		/// <summary>The account's access level.</summary>
		public byte AccessLevel;

		/// <summary>True while the character is dead.</summary>
		public bool Dead;

		/// <summary>True while the character is in combat.</summary>
		public bool InCombat;

		/// <summary>True while a chat mute applies to the character.</summary>
		public bool Muted;

		/// <summary>Distance from the requesting staff member, in metres.</summary>
		public float Distance;
	}

	/// <summary>
	/// Server to staff client: every character in the requester's scene instance.
	/// </summary>
	/// <remarks>
	/// The requester's scene INSTANCE, compared by handle — not every character on the scene
	/// server, which hosts several scenes and several copies of some. Capped at
	/// <see cref="MaxEntries"/>; <see cref="TotalInScene"/> is always the real count.
	/// </remarks>
	public struct StaffRosterBroadcast : IBroadcast
	{
		/// <summary>Most rows one roster carries.</summary>
		public const int MaxEntries = 200;

		/// <summary>The scene the roster describes.</summary>
		public string SceneName;

		/// <summary>How many characters are in that scene instance, including any past the cap.</summary>
		public int TotalInScene;

		/// <summary>How many characters this scene server holds across all its scenes.</summary>
		public int TotalOnServer;

		/// <summary>The rows, nearest first.</summary>
		public StaffRosterEntry[] Characters;
	}

	/// <summary>Which support tickets a staff queue request asks for.</summary>
	public enum StaffTicketFilter : byte
	{
		/// <summary>Unfinished tickets nobody has taken.</summary>
		Unassigned = 0,

		/// <summary>Unfinished tickets assigned to the requester.</summary>
		Mine = 1,

		/// <summary>Every unfinished ticket.</summary>
		AllOpen = 2,
	}

	/// <summary>Staff client to server: send a page of the support ticket queue.</summary>
	public struct StaffTicketQueueRequestBroadcast : IBroadcast
	{
		/// <summary>Which tickets.</summary>
		public StaffTicketFilter Filter;

		/// <summary>1-based page.</summary>
		public int Page;
	}

	/// <summary>One support ticket as a staff queue lists it.</summary>
	/// <remarks>
	/// Status and category travel as their names. The client has no reference to the database's
	/// enums and needs them only to display, so a name is both sufficient and unambiguous.
	/// </remarks>
	public struct StaffTicketSummary
	{
		/// <summary>Ticket number.</summary>
		public long TicketID;

		/// <summary>Status name.</summary>
		public string Status;

		/// <summary>Category name.</summary>
		public string Category;

		/// <summary>Staff priority.</summary>
		public int Priority;

		/// <summary>Subject line.</summary>
		public string Subject;

		/// <summary>Reporting account.</summary>
		public string ReporterAccount;

		/// <summary>Reporting character.</summary>
		public string ReporterCharacter;

		/// <summary>Reported character, for player reports.</summary>
		public string TargetCharacter;

		/// <summary>Assigned staff account, or empty.</summary>
		public string AssignedTo;

		/// <summary>Last activity, as UTC ticks.</summary>
		public long LastActivityUtcTicks;
	}

	/// <summary>Server to staff client: a page of the ticket queue.</summary>
	public struct StaffTicketQueueBroadcast : IBroadcast
	{
		/// <summary>Most tickets one page carries.</summary>
		public const int PageSize = 25;

		/// <summary>The filter this page answers.</summary>
		public StaffTicketFilter Filter;

		/// <summary>1-based page.</summary>
		public int Page;

		/// <summary>Tickets matching the filter across every page.</summary>
		public int TotalCount;

		/// <summary>This page's tickets, longest-waiting first.</summary>
		public StaffTicketSummary[] Tickets;
	}

	/// <summary>Staff client to server: send one ticket in full.</summary>
	public struct StaffTicketDetailRequestBroadcast : IBroadcast
	{
		/// <summary>Ticket number.</summary>
		public long TicketID;
	}

	/// <summary>One message on a ticket.</summary>
	public struct StaffTicketMessageEntry
	{
		/// <summary>When it was written, as UTC ticks.</summary>
		public long CreatedUtcTicks;

		/// <summary>Author account.</summary>
		public string Author;

		/// <summary>True when written by staff.</summary>
		public bool AuthorIsStaff;

		/// <summary>True for a staff-only note the player can never see.</summary>
		public bool Internal;

		/// <summary>The text.</summary>
		public string Body;
	}

	/// <summary>Server to staff client: one ticket in full, internal notes included.</summary>
	public struct StaffTicketDetailBroadcast : IBroadcast
	{
		/// <summary>Most messages one detail carries; the newest are kept.</summary>
		public const int MaxMessages = 30;

		/// <summary>False when no such ticket exists; every other field is then empty.</summary>
		public bool Found;

		/// <summary>The ticket's summary fields.</summary>
		public StaffTicketSummary Ticket;

		/// <summary>What the reporter wrote.</summary>
		public string Body;

		/// <summary>Scene the reporter was in when filing.</summary>
		public string SceneName;

		/// <summary>The recorded resolution, once resolved or closed.</summary>
		public string Resolution;

		/// <summary>Messages, oldest first, capped at <see cref="MaxMessages"/>.</summary>
		public StaffTicketMessageEntry[] Messages;
	}
}
