namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// What a daemon may be asked to do.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A closed enumeration, and it must stay closed.</b> A command queue that can start
	/// processes on other machines is the most dangerous thing in this system, well past
	/// banning an account — and the property that contains it is that a command row selects
	/// from a fixed list of verbs against an application the daemon already knows, rather than
	/// carrying anything the daemon executes.
	/// </para>
	/// <para>
	/// There is no path here, no arguments, no environment and no shell. Those live in the
	/// daemon's own configuration file on disk, so changing what a name actually runs requires
	/// filesystem access to that host, not a database insert. Anyone who reaches the database
	/// can restart a FishMMO process; they cannot make the daemon run something new. If a verb
	/// is ever added that takes free text, that containment is gone.
	/// </para>
	/// </remarks>
	public enum DaemonCommandVerb
	{
		/// <summary>Launch a supervised application that is not running.</summary>
		Start = 0,

		/// <summary>Stop a supervised application and leave it stopped.</summary>
		Stop = 1,

		/// <summary>Stop and launch again.</summary>
		Restart = 2,
	}
}
