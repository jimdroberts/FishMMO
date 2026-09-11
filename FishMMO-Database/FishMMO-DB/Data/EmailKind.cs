namespace FishMMO.Database.Data
{
	/// <summary>
	/// What an outbound email in <c>email_queue</c> is <i>for</i>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The kind exists because the sender does something after a successful delivery that only
	/// makes sense for one of them. A verification mail ends the new account's grace period:
	/// once <c>verification_email_sent_at</c> is stamped, an unverified account can no longer
	/// sign in to the game or the panel until it verifies. A password reset mail must leave
	/// that stamp alone — stamping it would lock an unverified player out of everything at the
	/// exact moment they finished recovering their password.
	/// </para>
	/// <para>
	/// <b>This is a column and not a look at the subject line.</b> Five separate subject
	/// literals already exist across the LoginServer, the panel's registration, email-change
	/// and reset paths; deciding a lockout from one of them would mean a reworded subject
	/// silently changes who can log in, with nothing anywhere saying so.
	/// </para>
	/// <para>
	/// <see cref="Verification"/> is 0 so that the column's <c>NOT NULL DEFAULT 0</c> gives
	/// every row written before this existed — and every row still written by a caller that
	/// does not name a kind — the meaning it already had.
	/// </para>
	/// </remarks>
	public enum EmailKind : int
	{
		/// <summary>
		/// Account or email-address verification. Delivering one ends the unverified grace
		/// period by stamping <c>verification_email_sent_at</c>.
		/// </summary>
		Verification = 0,

		/// <summary>
		/// Password recovery. Delivering one changes nothing about the account's verification
		/// state.
		/// </summary>
		PasswordReset = 1,
	}
}
