namespace FishMMO.Shared
{
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

		/// <summary>One of a fixed set of words, listed in the command's argument spec.</summary>
		Choice = 8,
	}
}
