namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// The audit row being built for the current request, which an action may enrich.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="AuditActionFilter"/> fills this in from the route and the bound arguments
	/// before the action runs, and writes it afterwards. An action that knows something the
	/// filter cannot — the name a character had <em>before</em> it was renamed, or which fields
	/// an edit actually changed — assigns to it in one line. An action that knows nothing extra
	/// assigns nothing and is still recorded.
	/// </para>
	/// <para>
	/// Scoped per request. It is the reason no controller needs to call an audit method, which
	/// is the reason no controller can forget to.
	/// </para>
	/// </remarks>
	public sealed class AuditScope
	{
		/// <summary>The stable action identifier. Set from the attribute; rarely overridden.</summary>
		public string Action { get; set; }

		/// <summary>What kind of thing was acted on.</summary>
		public string TargetType { get; set; }

		/// <summary>The target's identifier, as text.</summary>
		public string TargetID { get; set; }

		/// <summary>
		/// The target's name as it read at the time.
		/// </summary>
		/// <remarks>
		/// Worth setting for anything whose name can change. A rename recorded under the new
		/// name cannot answer "what happened to the character called X", which is the question
		/// somebody actually asks.
		/// </remarks>
		public string TargetName { get; set; }

		/// <summary>The reason the operator gave. Taken from the request body's Reason property.</summary>
		public string Reason { get; set; }

		/// <summary>Structured detail of what changed. Serialized to JSON.</summary>
		public object Details { get; set; }

		/// <summary>
		/// Overrides the outcome text. Left null, the filter derives it from the status code.
		/// </summary>
		public string Outcome { get; set; }

		/// <summary>
		/// Whether this request should be recorded at all.
		/// </summary>
		/// <remarks>
		/// Set false ONLY for a privileged endpoint that is genuinely a read — the audit-log
		/// listing itself is the case, since every read would write a row whose listing is a
		/// read. It must never be used to quieten a write.
		/// </remarks>
		public bool Record { get; set; } = true;
	}
}
