using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Where an email queue row is in its life.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>There is no state column.</b> <c>email_queue</c> models delivery with three nullable
	/// columns — <c>sent_at</c>, <c>claimed_at</c> and <c>last_error</c> — and the state is the
	/// combination of them. This enum is that combination named, decided in one place so the
	/// filter, the counts and the badge in the browser cannot disagree about what "failed"
	/// means.
	/// </para>
	/// <para>
	/// The order of the tests matters and is part of the definition:
	/// </para>
	/// <list type="number">
	/// <item><description><c>sent_at</c> set — <see cref="Sent"/>. Delivered; nothing else about the row matters.</description></item>
	/// <item><description><c>claimed_at</c> null — <see cref="Pending"/>. In line, waiting for a login server to take it. This is the state the queue drains from, and the state that piles up when no login server is running.</description></item>
	/// <item><description><c>last_error</c> set — <see cref="Failed"/>. A login server took it and the delivery attempt errored.</description></item>
	/// <item><description>otherwise — <see cref="Claimed"/>. A login server holds it and has not reported an outcome.</description></item>
	/// </list>
	/// <para>
	/// <b>Pending is tested before the error</b> so that a retried row reads as pending again:
	/// retrying releases the claim but deliberately keeps <c>last_error</c>, because the text of
	/// the failure is the only evidence of why it failed and an operator retrying a second time
	/// needs it. Were the order reversed, a row put back in line would still read as failed.
	/// </para>
	/// </remarks>
	public enum EmailQueueState : int
	{
		/// <summary>Queued and unclaimed. Waiting for a login server to pick it up.</summary>
		Pending = 0,

		/// <summary>Claimed by a login server, which has not yet said whether it went out.</summary>
		Claimed = 1,

		/// <summary>Claimed, and the last delivery attempt recorded an error.</summary>
		Failed = 2,

		/// <summary>Delivered.</summary>
		Sent = 3,
	}

	/// <summary>
	/// One outbound email, as an operator needs to see it.
	/// </summary>
	/// <remarks>
	/// <b>The body is deliberately absent.</b> A verification email's body contains the
	/// verification link, and that link is a credential: anybody who can read it can verify the
	/// account it belongs to without ever holding the mailbox. The projection that fills this
	/// type does not name the <c>body</c> column, so the text never enters the panel process at
	/// all rather than being fetched and then carefully not serialized. What kind of mail it is
	/// — the only question the page asks of a message it cannot read — comes from
	/// <see cref="Kind"/>, which is the column the sender itself acts on, rather than from
	/// pattern-matching the subject line.
	/// </remarks>
	public sealed class EmailQueueAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Who it is addressed to.</summary>
		public string RecipientEmail { get; set; }

		/// <summary>The account it belongs to.</summary>
		public string RecipientUsername { get; set; }

		/// <summary>The subject line.</summary>
		public string Subject { get; set; }

		/// <summary>
		/// What the mail is for, as stored on the row.
		/// </summary>
		/// <remarks>
		/// The page used to infer this from the subject text. It no longer has to: this is the
		/// same value the drain reads to decide whether delivering the message ends the
		/// account's unverified grace period, so what an operator sees and what the sender does
		/// cannot disagree.
		/// </remarks>
		public EmailKind Kind { get; set; }

		/// <summary>When it was enqueued (UTC).</summary>
		public DateTime CreatedAt { get; set; }

		/// <summary>When it went out (UTC), or null.</summary>
		public DateTime? SentAt { get; set; }

		/// <summary>Delivery attempts recorded so far.</summary>
		public int Attempts { get; set; }

		/// <summary>The login server holding it, or null.</summary>
		public string ClaimedBy { get; set; }

		/// <summary>When it was claimed (UTC), or null.</summary>
		public DateTime? ClaimedAt { get; set; }

		/// <summary>The error from the most recent failed attempt, or null.</summary>
		public string LastError { get; set; }

		/// <summary>The state the four columns above add up to. See <see cref="EmailQueueState"/>.</summary>
		public EmailQueueState State { get; set; }
	}

	/// <summary>How many rows are in each state.</summary>
	/// <remarks>
	/// Counted over the whole table, never over the page and never through the state filter.
	/// Counts that moved when the operator changed a filter would be useless as an alarm, which
	/// is the only reason they are here.
	/// </remarks>
	public sealed class EmailQueueCounts
	{
		/// <summary>Queued and unclaimed.</summary>
		public int Pending { get; set; }

		/// <summary>Held by a login server with no outcome reported.</summary>
		public int Claimed { get; set; }

		/// <summary>Held by a login server whose last attempt errored.</summary>
		public int Failed { get; set; }

		/// <summary>Delivered.</summary>
		public int Sent { get; set; }

		/// <summary>Every row in the table.</summary>
		public int Total => Pending + Claimed + Failed + Sent;
	}

	/// <summary>
	/// One page of the email queue, with the numbers that say whether it is moving.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b><see cref="OldestPendingCreatedAt"/> is the number this whole read exists for.</b>
	/// Account verification reaches a player through this table and nothing else. When no login
	/// server is claiming rows — the process is down, SMTP is refusing, the credentials expired
	/// — registration stops working for every new account, and there is no other symptom: the
	/// website accepts the registration, the row is written, and the player simply never gets
	/// their mail. A depth count does not show it either, because a healthy queue and a dead one
	/// both read as a small number on a quiet shard. The <em>age</em> of the oldest unclaimed row
	/// is what separates them, and it is the one value on this page an operator should be
	/// alarmed by.
	/// </para>
	/// <para>
	/// Both ages are supplied because they answer different questions.
	/// <see cref="OldestPendingCreatedAt"/> asks whether anything is draining the queue.
	/// <see cref="OldestUnsentCreatedAt"/> asks how long the longest-waiting account has been
	/// waiting, which includes rows a login server claimed and then stopped reporting on — those
	/// are stuck too, and a pending-only number would show zero while they sat there forever.
	/// </para>
	/// </remarks>
	public sealed class EmailQueuePage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<EmailQueueAdminData> Items { get; set; } = Array.Empty<EmailQueueAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page, after clamping.</summary>
		public int PageSize { get; set; }

		/// <summary>Rows matching the filter, across every page.</summary>
		public int TotalCount { get; set; }

		/// <summary>How many rows are in each state, over the whole table.</summary>
		public EmailQueueCounts Counts { get; set; } = new EmailQueueCounts();

		/// <summary>
		/// When the oldest <see cref="EmailQueueState.Pending"/> row was enqueued (UTC), or null
		/// when nothing is waiting to be claimed.
		/// </summary>
		public DateTime? OldestPendingCreatedAt { get; set; }

		/// <summary>
		/// When the oldest row that has not been delivered was enqueued (UTC), or null when
		/// everything has gone out. Includes claimed and failed rows.
		/// </summary>
		public DateTime? OldestUnsentCreatedAt { get; set; }

		/// <summary>
		/// When the read was taken (UTC).
		/// </summary>
		/// <remarks>
		/// The ages above are differences against this instant, computed by the caller rather
		/// than stored, so a panel clock that disagrees with the database's cannot turn a young
		/// queue into an alarm or an old one into silence.
		/// </remarks>
		public DateTime ReadAtUtc { get; set; }
	}

	/// <summary>What a retry did, or why it did nothing.</summary>
	/// <remarks>
	/// A refusal is a successful read of a row that could not be retried, not a database
	/// failure, so it comes back here rather than as an error result: the caller has something
	/// specific to tell the operator and something specific to record.
	/// </remarks>
	public sealed class EmailRetryResult
	{
		/// <summary>The row acted on.</summary>
		public long ID { get; set; }

		/// <summary>Whether the claim was released and the row put back in line.</summary>
		public bool Retried { get; set; }

		/// <summary>Why nothing was done, when <see cref="Retried"/> is false. Null otherwise.</summary>
		public string Refusal { get; set; }

		/// <summary>The account the email belongs to.</summary>
		public string RecipientUsername { get; set; }

		/// <summary>Who it is addressed to.</summary>
		public string RecipientEmail { get; set; }

		/// <summary>Attempts recorded on the row. Not reset by a retry: it is the evidence.</summary>
		public int Attempts { get; set; }

		/// <summary>The login server whose claim was released, when there was one.</summary>
		public string ClaimedBy { get; set; }
	}

	/// <summary>One character waiting in, or matched out of, the group finder queue.</summary>
	public sealed class GroupFinderQueueAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>The world server the queue row belongs to.</summary>
		public long WorldServerID { get; set; }

		/// <summary>The waiting character.</summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// The character's name, or null when the row outlives the character.
		/// </summary>
		/// <remarks>
		/// The foreign key cascades, so this should never be null in practice. It is nullable
		/// because a left join that silently dropped rows would hide exactly the corruption
		/// worth seeing.
		/// </remarks>
		public string CharacterName { get; set; }

		/// <summary>The account the character belongs to, or null alongside the name.</summary>
		public string AccountName { get; set; }

		/// <summary>Instance kind: the shared <c>SceneType</c> value (Group = dungeon, PvP = arena).</summary>
		public int SceneType { get; set; }

		/// <summary>The dungeon or arena being queued for.</summary>
		public string SceneName { get; set; }

		/// <summary>Difficulty, or arena format, as an index into the template's list.</summary>
		public int Difficulty { get; set; }

		/// <summary>Row status. See <see cref="Enums.GroupFinderQueueStatus"/>.</summary>
		public int Status { get; set; }

		/// <summary>The pre-made group queued with, or 0.</summary>
		public long GroupID { get; set; }

		/// <summary>The party matched into, or 0.</summary>
		public long PartyID { get; set; }

		/// <summary>The instance matched into, or 0.</summary>
		public long InstanceID { get; set; }

		/// <summary>When the character joined the queue (UTC).</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// The last heartbeat from the scene server holding the character (UTC).
		/// </summary>
		/// <remarks>
		/// Worth showing beside the wait: a row whose heartbeat stopped is excluded from matching
		/// and will be swept away, so a long wait on a stale row is not the matcher failing, it
		/// is a scene server that went away with somebody queued on it.
		/// </remarks>
		public DateTime LastPulse { get; set; }

		/// <summary>When the row was matched (UTC), or null while waiting.</summary>
		public DateTime? TimeMatched { get; set; }
	}

	/// <summary>How many group finder rows are in each state.</summary>
	public sealed class GroupFinderQueueCounts
	{
		/// <summary>Still waiting for a group.</summary>
		public int Waiting { get; set; }

		/// <summary>Matched, and waiting to be moved by their scene server.</summary>
		public int Matched { get; set; }

		/// <summary>Every row in the table.</summary>
		public int Total => Waiting + Matched;
	}

	/// <summary>One page of the group finder queue.</summary>
	public sealed class GroupFinderQueuePage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<GroupFinderQueueAdminData> Items { get; set; } = Array.Empty<GroupFinderQueueAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page, after clamping.</summary>
		public int PageSize { get; set; }

		/// <summary>Rows matching the filter, across every page.</summary>
		public int TotalCount { get; set; }

		/// <summary>How many rows are in each state, over the whole table.</summary>
		public GroupFinderQueueCounts Counts { get; set; } = new GroupFinderQueueCounts();

		/// <summary>When the read was taken (UTC).</summary>
		public DateTime ReadAtUtc { get; set; }
	}
}
