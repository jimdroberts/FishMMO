namespace AppHealthMonitor
{
	/// <summary>
	/// Represents a console command that can be executed by the daemon.
	/// Encapsulates command name, description, and execution logic.
	/// Validates all parameters at construction time to fail fast on misconfiguration.
	/// </summary>
	public sealed record ConsoleCommand
	{
		/// <summary>
		/// The keyword used to invoke the command (e.g., "start", "help").
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// A brief description of what the command does.
		/// </summary>
		public string Description { get; }

		/// <summary>
		/// An asynchronous delegate representing the action to perform when the command is invoked.
		/// </summary>
		public Func<Task> Action { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="ConsoleCommand"/> record.
		/// </summary>
		/// <param name="name">The keyword used to invoke the command.</param>
		/// <param name="description">A brief description of what the command does.</param>
		/// <param name="action">The asynchronous action to perform when invoked.</param>
		/// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
		public ConsoleCommand(string name, string description, Func<Task> action)
		{
			ArgumentNullException.ThrowIfNull(name);
			ArgumentNullException.ThrowIfNull(description);
			ArgumentNullException.ThrowIfNull(action);

			Name = name;
			Description = description;
			Action = action;
		}
	}
}