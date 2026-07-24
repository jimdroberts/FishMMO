using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Provides shared process execution, console prompting, and Linux DotNet
	/// environment bootstrapping used by installer classes.
	/// </summary>
	public static class InstallerProcessHelper
	{
		private static bool dotNetEnvironmentPrepared = false;
		private static readonly object dotNetEnvironmentLock = new object();

		/// <summary>
		/// When true, <see cref="PromptForYesNo"/> returns the default answer without
		/// displaying a prompt. Set via <c>--accept-defaults</c> / <c>-y</c>.
		/// </summary>
		public static bool AcceptDefaults = false;

		/// <summary>
		/// Gets the working directory for the current application domain.
		/// </summary>
		/// <returns>Base directory path.</returns>
		public static string GetWorkingDirectory()
		{
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		/// <summary>
		/// Gets the appropriate shell command and argument prefix for the current OS.
		/// Windows returns cmd.exe /c. Linux prefers fish (-lc) when available, otherwise /bin/bash -c.
		/// </summary>
		/// <returns>Tuple of shell executable and argument prefix.</returns>
		public static (string shell, string argPrefix) GetShellCommand()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return ("cmd.exe", "/c");
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				const string fishShellPath = "/usr/bin/fish";
				if (File.Exists(fishShellPath))
				{
					return (fishShellPath, "-lc");
				}

				return ("/bin/bash", "-c");
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system. Only Windows and Linux are supported.");
			}
		}

		/// <summary>
		/// Runs a shell command using the OS-appropriate shell and logs errors on failure.
		/// </summary>
		/// <param name="shell">Shell executable.</param>
		/// <param name="argPrefix">Shell argument prefix.</param>
		/// <param name="command">The shell command to execute.</param>
		/// <param name="errorMessage">Error message to log on failure.</param>
		/// <returns>True if command succeeded, otherwise false.</returns>
		public static async Task<bool> RunShellCommandAsync(string shell, string argPrefix, string command, string errorMessage)
		{
			string escapedCommand = EscapeShellCommand(command);
			return await RunProcessAsync(shell, $"{argPrefix} \"{escapedCommand}\"", (exitCode, output, error) =>
			{
				if (exitCode != 0)
				{
					_ = FishMMO.Logging.Log.Warning("FishMMOInstaller", $"{errorMessage} Error: {error}");
					return false;
				}
				return true;
			});
		}

		/// <summary>
		/// Escapes shell command content inserted into a quoted -c/-lc argument.
		/// </summary>
		/// <param name="command">Unescaped shell command.</param>
		/// <returns>Escaped shell command safe for double-quoted command argument usage.</returns>
		private static string EscapeShellCommand(string command)
		{
			return command.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		/// <summary>
		/// Ensures Linux DotNet runtime/tooling environment variables are configured once per process.
		/// Adds ~/.dotnet and ~/.dotnet/tools to PATH when missing and sets DOTNET_ROOT.
		/// </summary>
		public static void EnsureDotNetEnvironment()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				return;
			}

			lock (dotNetEnvironmentLock)
			{
				if (dotNetEnvironmentPrepared)
				{
					return;
				}

				string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				if (string.IsNullOrWhiteSpace(homePath))
				{
					_ = FishMMO.Logging.Log.Warning("FishMMOInstaller", "The HOME environment variable is not set. DotNet commands may fail.");
					dotNetEnvironmentPrepared = true;
					return;
				}

				string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
				string dotnetPath = Path.Combine(homePath, ".dotnet");
				string dotnetToolsPath = Path.Combine(homePath, ".dotnet", "tools");

				var pathEntries = currentPath
					.Split(':', StringSplitOptions.RemoveEmptyEntries)
					.Select(entry => entry.Trim())
					.ToHashSet(StringComparer.Ordinal);

				if (!pathEntries.Contains(dotnetPath))
				{
					currentPath = string.IsNullOrWhiteSpace(currentPath)
						? dotnetPath
						: $"{currentPath}:{dotnetPath}";
					_ = FishMMO.Logging.Log.Debug("FishMMOInstaller", $"Updated PATH to include: {dotnetPath}");
				}

				if (!pathEntries.Contains(dotnetToolsPath))
				{
					currentPath = string.IsNullOrWhiteSpace(currentPath)
						? dotnetToolsPath
						: $"{currentPath}:{dotnetToolsPath}";
					_ = FishMMO.Logging.Log.Debug("FishMMOInstaller", $"Updated PATH to include: {dotnetToolsPath}");
				}

				Environment.SetEnvironmentVariable("PATH", currentPath);

				string userDotnetRoot = Path.Combine(homePath, ".dotnet");
				if (Directory.Exists(userDotnetRoot))
				{
					Environment.SetEnvironmentVariable("DOTNET_ROOT", userDotnetRoot);
					_ = FishMMO.Logging.Log.Debug("FishMMOInstaller", $"Set DOTNET_ROOT to: {userDotnetRoot}");
				}
				else
				{
					const string systemDotnetRoot = "/usr/share/dotnet";
					if (Directory.Exists(systemDotnetRoot))
					{
						Environment.SetEnvironmentVariable("DOTNET_ROOT", systemDotnetRoot);
						_ = FishMMO.Logging.Log.Debug("FishMMOInstaller", $"Set DOTNET_ROOT to: {systemDotnetRoot}");
					}
				}

				dotNetEnvironmentPrepared = true;
			}
		}

		/// <summary>
		/// Runs a DotNet command after preparing Linux DotNet environment variables.
		/// </summary>
		/// <param name="arguments">DotNet command arguments.</param>
		/// <param name="processResult">Optional callback receiving (exitCode, stdout, stderr) to determine success.</param>
		/// <returns>True if command succeeded, otherwise false.</returns>
		public static async Task<bool> RunDotNetProcessAsync(string arguments, Func<int, string, string, bool>? processResult = null)
		{
			EnsureDotNetEnvironment();

			try
			{
				return await RunProcessAsync("dotnet", arguments, processResult);
			}
			catch (Exception ex)
			{
				await FishMMO.Logging.Log.Error("FishMMOInstaller", $"Failed to run 'dotnet {arguments}'", ex);
				return false;
			}
		}

		/// <summary>
		/// Logs a warning that elevated Windows processes may not inherit all per-process environment overrides.
		/// </summary>
		/// <param name="processName">Human-readable process name.</param>
		public static void LogElevatedProcessEnvironmentWarning(string processName)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return;
			}

			_ = FishMMO.Logging.Log.Warning("FishMMOInstaller", $"Note: '{processName}' runs elevated (UAC) and may not inherit this process's temporary environment-variable overrides.");
		}

		/// <summary>
		/// Runs a process asynchronously and returns true if successful.
		/// </summary>
		/// <param name="command">Process executable.</param>
		/// <param name="arguments">Arguments for the process.</param>
		/// <param name="processResult">Optional callback receiving (exitCode, stdout, stderr) to determine success.</param>
		/// <returns>True if process succeeded, otherwise false.</returns>
		public static async Task<bool> RunProcessAsync(string command, string arguments, Func<int, string, string, bool>? processResult = null)
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = command;
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;

				process.Start();

				var outputTask = process.StandardOutput.ReadToEndAsync();
				var errorTask = process.StandardError.ReadToEndAsync();

				await Task.WhenAll(outputTask, errorTask);
				await process.WaitForExitAsync();

				string output = outputTask.Result;
				string error = errorTask.Result;

				if (processResult != null)
				{
					return processResult.Invoke(process.ExitCode, output, error);
				}
				else
				{
					return process.ExitCode == 0;
				}
			}
		}

		/// <summary>
		/// Runs a process asynchronously, streaming stdout and stderr to the console
		/// in real-time while also capturing the full output. Use this for long-running
		/// processes where the user needs to see progress (e.g., win-acme, certbot).
		/// </summary>
		/// <param name="command">Process executable.</param>
		/// <param name="arguments">Arguments for the process.</param>
		/// <param name="processResult">Optional callback receiving (exitCode, stdout, stderr) to determine success.</param>
		/// <returns>True if process succeeded, otherwise false.</returns>
		public static async Task<bool> RunProcessWithLiveOutputAsync(string command, string arguments, Func<int, string, string, bool>? processResult = null)
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = command;
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;

				process.Start();

				var outputBuilder = new System.Text.StringBuilder();
				var errorBuilder = new System.Text.StringBuilder();

				// Read stdout and stderr line-by-line, writing to console and collecting
				var outputTask = ReadAndStreamLinesAsync(process.StandardOutput, outputBuilder, Console.Out);
				var errorTask = ReadAndStreamLinesAsync(process.StandardError, errorBuilder, Console.Error);

				await Task.WhenAll(outputTask, errorTask);
				await process.WaitForExitAsync();

				string output = outputBuilder.ToString();
				string error = errorBuilder.ToString();

				if (processResult != null)
				{
					return processResult.Invoke(process.ExitCode, output, error);
				}
				else
				{
					return process.ExitCode == 0;
				}
			}
		}

		/// <summary>
		/// Reads lines from a StreamReader, appends each to a StringBuilder, and
		/// writes each line to a TextWriter for live console feedback.
		/// </summary>
		private static async Task ReadAndStreamLinesAsync(StreamReader reader, System.Text.StringBuilder builder, TextWriter liveWriter)
		{
			string? line;
			while ((line = await reader.ReadLineAsync()) != null)
			{
				builder.AppendLine(line);
				liveWriter.WriteLine(line);
			}
		}

		/// <summary>
		/// Runs a process asynchronously, writing the supplied text to the
		/// process's standard input before closing it. Used to deliver
		/// secrets (e.g. passwords for <c>psql</c> meta-commands) without
		/// exposing them on the process command line or in shell history.
		/// </summary>
		/// <param name="command">Process executable.</param>
		/// <param name="arguments">Arguments for the process.</param>
		/// <param name="stdinText">Text to write to stdin (followed by EOF).</param>
		/// <param name="processResult">Optional callback receiving (exitCode, stdout, stderr).</param>
		/// <returns>True if process succeeded, otherwise false.</returns>
		public static async Task<bool> RunProcessWithStdinAsync(string command, string arguments, string stdinText, Func<int, string, string, bool>? processResult = null)
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = command;
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardInput = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;

				process.Start();

				var outputTask = process.StandardOutput.ReadToEndAsync();
				var errorTask = process.StandardError.ReadToEndAsync();

				try
				{
					if (!string.IsNullOrEmpty(stdinText))
					{
						await process.StandardInput.WriteAsync(stdinText);
					}
				}
				finally
				{
					process.StandardInput.Close();
				}

				await Task.WhenAll(outputTask, errorTask);
				await process.WaitForExitAsync();

				string output = outputTask.Result;
				string error = errorTask.Result;

				if (processResult != null)
				{
					return processResult.Invoke(process.ExitCode, output, error);
				}
				return process.ExitCode == 0;
			}
		}

		/// <summary>
		/// Prompts the user for input in the console.
		/// </summary>
		/// <param name="prompt">Prompt message.</param>
		/// <returns>User input string.</returns>
		public static string? PromptForInput(string prompt)
		{
			Console.Write(prompt);
			return Console.ReadLine();
		}

		/// <summary>
		/// Prompts the user for a yes/no response in the console.
		/// </summary>
		/// <param name="prompt">Prompt message.</param>
		/// <returns>True for yes, false for no.</returns>
		public static bool PromptForYesNo(string prompt)
		{
			if (AcceptDefaults)
			{
				Console.WriteLine($"{prompt} (Y/N): Y [auto-accepted]");
				return true;
			}

			while (true)
			{
				Console.Write($"{prompt} (Y/N): ");
				ConsoleKeyInfo key = Console.ReadKey();
				Console.WriteLine();

				if (key.Key == ConsoleKey.Y)
				{
					return true;
				}
				else if (key.Key == ConsoleKey.N)
				{
					return false;
				}
				else
				{
					Console.WriteLine("Invalid input. Please enter Y or N.");
				}
			}
		}

		/// <summary>
		/// Prompts the user for a password in the console, masking input with asterisks.
		/// </summary>
		/// <param name="prompt">Prompt message.</param>
		/// <returns>Password string.</returns>
		public static string PromptForPassword(string prompt)
		{
			Console.Write(prompt);
			var chars = new List<char>();
			ConsoleKeyInfo key;

			do
			{
				key = Console.ReadKey(true);

				if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
				{
					chars.Add(key.KeyChar);
					Console.Write("*");
				}
				else if (key.Key == ConsoleKey.Backspace && chars.Count > 0)
				{
					chars.RemoveAt(chars.Count - 1);
					Console.Write("\b \b");
				}
			}
			while (key.Key != ConsoleKey.Enter);

			Console.WriteLine();
			return new string(chars.ToArray());
		}

		/// <summary>
		/// Prompts for a required password. Loops until a non-empty value is entered.
		/// Pressing Enter with no input re-prompts.
		/// </summary>
		/// <param name="prompt">The prompt text.</param>
		/// <returns>A non-empty password string.</returns>
		public static string PromptForRequiredPassword(string prompt)
		{
			while (true)
			{
				string result = PromptForPassword(prompt);
				if (!string.IsNullOrWhiteSpace(result))
					return result;
				Console.WriteLine("  Password cannot be empty. Please enter a password.");
			}
		}
		
		/// <summary>
		/// Prompts for required input. Loops until a non-empty value is entered.
		/// Pressing Enter with no input re-prompts.
		/// </summary>
		/// <param name="prompt">The prompt text.</param>
		/// <returns>A non-empty input string.</returns>
		public static string PromptForRequiredInput(string prompt)
		{
			while (true)
			{
				string? result = PromptForInput(prompt);
				if (!string.IsNullOrWhiteSpace(result))
					return result.Trim();
				Console.WriteLine("  Value cannot be empty. Please enter a value.");
			}
		}

}
}