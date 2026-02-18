using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Provides shared process execution, console prompting, file download,
	/// and logging utilities used by installer classes.
	/// </summary>
	public static class InstallerProcessHelper
	{
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
		/// Shared HttpClient instance for all download operations.
		/// Reusing a single instance avoids socket exhaustion and improves performance.
		/// </summary>
		public static readonly HttpClient SharedHttpClient = new HttpClient();

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
			return await RunProcessAsync(shell, $"{argPrefix} \"{command}\"", (exitCode, output, error) =>
			{
				if (exitCode != 0)
				{
					Log($"{errorMessage} Error: {error}");
					return false;
				}
				return true;
			});
		}

		/// <summary>
		/// Detects the Linux package manager and returns the appropriate update and install command templates.
		/// </summary>
		/// <param name="packageNames">Dictionary mapping package manager name to install package list.</param>
		/// <returns>Tuple of (updateCommand, installCommand, packageManagerName), or null if none found.</returns>
		public static async Task<(string updateCommand, string installCommand, string managerName)?> DetectLinuxPackageManagerAsync(
			Dictionary<string, string> packageNames)
		{
			(string shell, string argPrefix) = GetShellCommand();

			if (packageNames.ContainsKey("pacman") &&
				await RunProcessAsync(shell, $"{argPrefix} \"which pacman\"", (e, o, err) => e == 0))
			{
				return ("sudo pacman -Sy", $"sudo pacman -S --noconfirm {packageNames["pacman"]}", "pacman (Arch/CachyOS)");
			}
			if (packageNames.ContainsKey("apt-get") &&
				await RunProcessAsync(shell, $"{argPrefix} \"which apt-get\"", (e, o, err) => e == 0))
			{
				return ("sudo apt-get update", $"sudo apt-get install -y {packageNames["apt-get"]}", "apt-get (Debian/Ubuntu)");
			}
			if (packageNames.ContainsKey("dnf") &&
				await RunProcessAsync(shell, $"{argPrefix} \"which dnf\"", (e, o, err) => e == 0))
			{
				return ("sudo dnf check-update", $"sudo dnf install -y {packageNames["dnf"]}", "dnf");
			}
			if (packageNames.ContainsKey("yum") &&
				await RunProcessAsync(shell, $"{argPrefix} \"which yum\"", (e, o, err) => e == 0))
			{
				return ("sudo yum check-update", $"sudo yum install -y {packageNames["yum"]}", "yum");
			}

			return null;
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
		/// Logs a message to the console and to FishMMO.Logging if available.
		/// </summary>
		/// <param name="message">Message to log.</param>
		/// <param name="logTime">Whether to include a timestamp prefix.</param>
		public static void Log(string message, bool logTime = false)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}
			if (logTime)
			{
				string formatted = $"{DateTime.Now}: {message}";
				Console.WriteLine(formatted);
				FishMMO.Logging.Log.Debug("FishMMOInstaller", formatted);
			}
			else
			{
				Console.WriteLine(message);
				FishMMO.Logging.Log.Debug("FishMMOInstaller", message);
			}
		}

		/// <summary>
		/// Downloads a file asynchronously from the specified URL to the working directory.
		/// If the file already exists locally, the download is skipped.
		/// </summary>
		/// <param name="url">File URL.</param>
		/// <param name="fileName">Desired local filename.</param>
		/// <returns>Full path to the downloaded file.</returns>
		public static async Task<string> DownloadFileAsync(string url, string fileName)
		{
			try
			{
				string tempDir = GetWorkingDirectory();
				string outputPath = Path.Combine(tempDir, fileName);

				if (File.Exists(outputPath))
				{
					Log(outputPath + " already exists... Skipping download.");
					return outputPath;
				}

				Log($"Downloading file from {url}");
				Log("Please wait...");
				using (HttpResponseMessage response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
				{
					response.EnsureSuccessStatusCode();

					if (response.Content.Headers.ContentDisposition != null)
					{
						fileName = response.Content.Headers.ContentDisposition.FileNameStar
								   ?? response.Content.Headers.ContentDisposition.FileName
								   ?? fileName;
						outputPath = Path.Combine(tempDir, fileName);
					}

					using (Stream streamToReadFrom = await response.Content.ReadAsStreamAsync())
					{
						using (Stream streamToWriteTo = File.Open(outputPath, FileMode.Create))
						{
							await streamToReadFrom.CopyToAsync(streamToWriteTo);
						}
					}
					Log($"File successfully downloaded to {outputPath}");
					return outputPath;
				}
			}
			catch (Exception ex)
			{
				throw new Exception($"Error downloading file: {ex.Message}");
			}
		}
	}
}