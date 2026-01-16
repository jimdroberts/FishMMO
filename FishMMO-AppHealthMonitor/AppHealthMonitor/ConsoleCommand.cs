namespace AppHealthMonitor
{
	/// <summary>
	/// Represents a console command that can be executed by the daemon.
	/// Encapsulates command name, description, and execution logic.
	/// </summary>
	public class ConsoleCommand
	{
		/// <summary>
		/// Gets the command keyword used to invoke this command.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Gets a brief description of what the command does.
		/// </summary>
		public string Description { get; }

		/// <summary>
		/// Gets the asynchronous action to execute when the command is invoked.
		/// </summary>
		public Func<Task> Action { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="ConsoleCommand"/> class.
		/// </summary>
		/// <param name="name">The keyword used to invoke the command (e.g., "start", "help").</param>
		/// <param name="description">A brief description of what the command does.</param>
		/// <param name="action">An asynchronous delegate representing the action to perform when the command is invoked.</param>
		public ConsoleCommand(string name, string description, Func<Task> action)
		{
			Name = name;
			Description = description;
			Action = action;
		}
	}
}