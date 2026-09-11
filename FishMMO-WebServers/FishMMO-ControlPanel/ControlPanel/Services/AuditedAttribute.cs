namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Names the audit action an endpoint records.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Declarative rather than a call inside the method, because a call is something an author
	/// can forget and an attribute is something a startup check can enumerate. See
	/// <see cref="AuditCoverage"/>: the panel refuses to start in development if a privileged
	/// write endpoint has no attribute, so the mistake is caught at the first run rather than
	/// discovered later as a hole in the record.
	/// </para>
	/// <para>
	/// Applying this is not what causes the row to be written — <see cref="AuditActionFilter"/>
	/// writes a row for every privileged write whether or not the attribute is present. The
	/// attribute is what makes the row <em>good</em>: a stable action name that queries and
	/// retention rules can be written against, rather than one derived from a controller name
	/// that a rename would silently change.
	/// </para>
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
	public sealed class AuditedAttribute : Attribute
	{
		/// <summary>The stable dotted action identifier, such as <c>account.ban</c>.</summary>
		public string Action { get; }

		/// <summary>What kind of thing is acted on: <c>account</c>, <c>character</c>.</summary>
		public string TargetType { get; init; }

		/// <summary>
		/// The route value naming the target, such as <c>id</c> or <c>username</c>.
		/// </summary>
		/// <remarks>
		/// Read from the route rather than the body so that a request which fails model binding,
		/// or one whose body names a different target than its URL, still records the target the
		/// authorization check was actually made against.
		/// </remarks>
		public string TargetRouteValue { get; init; }

		/// <summary>Creates the attribute.</summary>
		/// <param name="action">The stable dotted action identifier.</param>
		public AuditedAttribute(string action)
		{
			Action = action;
		}
	}
}
