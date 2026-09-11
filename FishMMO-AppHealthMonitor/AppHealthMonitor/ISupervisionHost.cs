namespace AppHealthMonitor
{
	/// <summary>
	/// The seam through which a <see cref="HealthMonitor"/> whose monitoring loop has ended asks to
	/// be supervised again, without knowing anything about how the monitoring cycle is run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A monitor that exhausted its restart attempts has stopped watching its application, and an
	/// operator asking for that application back is precisely the case the daemon exists to serve.
	/// Launching it from the monitor alone would leave a process nothing supervises, so a revival is
	/// a two-party transaction: the monitor asks the host to hold a place in the running cycle, and
	/// only launches once the host has agreed to take a monitoring task back.
	/// </para>
	/// <para>
	/// <b>Nothing about what runs crosses this seam.</b> The host is told a name, for logging, and is
	/// handed a <see cref="Task"/>. The process is launched by the monitor from the
	/// <see cref="System.Diagnostics.ProcessStartInfo"/> it built in its own constructor.
	/// </para>
	/// </remarks>
	public interface ISupervisionHost
	{
		/// <summary>
		/// Reserves a place in the running monitoring cycle for a monitor that is about to be revived.
		/// </summary>
		/// <remarks>
		/// While a reservation is outstanding the cycle cannot conclude, which is what makes it safe
		/// for the caller to launch a process before handing the monitoring task back. The caller must
		/// dispose the reservation on every path.
		/// </remarks>
		/// <param name="monitorName">The monitor's name, used only for logging.</param>
		/// <param name="refusal">When no reservation could be made, a sentence saying why.</param>
		/// <returns>A reservation, or null when supervision cannot be resumed.</returns>
		ISupervisionReservation? TryReserveRevival(string monitorName, out string refusal);
	}

	/// <summary>
	/// A held place in a running monitoring cycle. See <see cref="ISupervisionHost.TryReserveRevival"/>.
	/// </summary>
	/// <remarks>
	/// Disposal releases the hold. It is safe, and required, to dispose a reservation whether or not
	/// <see cref="Resume"/> was called: a reservation released without a monitoring task simply lets
	/// the cycle carry on as it would have.
	/// </remarks>
	public interface ISupervisionReservation : IDisposable
	{
		/// <summary>
		/// Gets whether the cycle this reservation belongs to can still supervise. False once the
		/// cycle or the daemon has been cancelled, at which point the caller must not launch.
		/// </summary>
		bool IsSupervisionAvailable { get; }

		/// <summary>
		/// Hands a monitoring task back to the cycle, which then awaits it like any other monitor's.
		/// </summary>
		/// <param name="monitoringTask">The running monitoring loop to supervise.</param>
		void Resume(Task monitoringTask);
	}
}
