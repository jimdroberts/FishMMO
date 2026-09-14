namespace FishMMO.Database.Data
{
	/// <summary>
	/// What an outbound text message in <c>sms_queue</c> is <i>for</i>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The SMS counterpart of <see cref="EmailKind"/>, and a column for the same reason: whatever
	/// the drain does after a delivery must be decided by what the message is, never by reading
	/// its text.
	/// </para>
	/// <para>
	/// <see cref="Verification"/> is 0 so the column's <c>NOT NULL DEFAULT 0</c> means the same
	/// thing as a caller that names no kind.
	/// </para>
	/// </remarks>
	public enum SmsKind : int
	{
		/// <summary>A phone-number verification code.</summary>
		Verification = 0,

		/// <summary>An informational message that changes nothing about the account.</summary>
		Notification = 1,
	}
}
