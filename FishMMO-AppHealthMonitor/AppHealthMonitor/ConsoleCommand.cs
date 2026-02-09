namespace AppHealthMonitor
{
	/// <summary>
	/// Represents a console command that can be executed by the daemon.
	/// Encapsulates command name, description, and execution logic.
	/// </summary>
	/// <param name="Name">The keyword used to invoke the command (e.g., "start", "help").</param>
	/// <param name="Description">A brief description of what the command does.</param>
	/// <param name="Action">An asynchronous delegate representing the action to perform when the command is invoked.</param>
	public sealed record ConsoleCommand(string Name, string Description, Func<Task> Action);
}