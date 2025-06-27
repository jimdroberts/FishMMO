namespace AppHealthMonitor
{
	// Represents a console command that can be executed by the daemon.
	// This class is defined here for clarity, though it's nested within Program.cs
	// in the main application to encapsulate related functionality.
	public class ConsoleCommand
	{
		public string Name { get; }
		public string Description { get; }
		public Func<Task> Action { get; }

		/// <summary>
		/// Initializes a new instance of the ConsoleCommand class.
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