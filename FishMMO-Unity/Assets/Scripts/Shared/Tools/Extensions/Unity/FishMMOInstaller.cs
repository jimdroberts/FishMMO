using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using TMPro;
using FishMMO.Database;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Shared
{
	public class FishMMOInstaller : MonoBehaviour
	{
		public string ProjectPath = "./FishMMO-Database/FishMMO-DB/FishMMO-DB.csproj";
		public string StartupProject = "./FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj";

		public TMP_Text OutputLog;
		public TMP_InputField CommandInput;
		public List<Button> Buttons = new List<Button>();

		public void InstallEverything()
		{
			InstallWSL();
			InstallDotNet();
			InstallDocker();
			InstallDatabase();
		}

		private void Log(string message)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}
			Debug.Log(message);
			if (OutputLog != null)
			{
				OutputLog.text = "Output:\r\n" + message;
			}
		}

		private void SetButtonsActive(bool active)
		{
			if (Buttons != null)
			{
				foreach (Button b in Buttons)
				{
					b.enabled = active;
				}
			}
		}

		public async void OnSubmitCommand()
		{
			SetButtonsActive(false);
			if (CommandInput == null)
			{
				SetButtonsActive(true);
				return;
			}
			if (string.IsNullOrWhiteSpace(CommandInput.text))
			{
				SetButtonsActive(true);
				return;
			}
			if (CommandInput.text.StartsWith("docker"))
			{
				string command = CommandInput.text.Substring(6, CommandInput.text.Length - 6).Trim();

				string output = await RunDockerCommandAsync(command);
				Log(output);
			}
			else if (CommandInput.text.StartsWith("dism"))
			{
				string command = CommandInput.text.Substring(4, CommandInput.text.Length - 4).Trim();

				await RunDismCommandAsync(command);
			}
			else if (CommandInput.text.StartsWith("dotnet"))
			{
				string command = CommandInput.text.Substring(6, CommandInput.text.Length - 6).Trim();

				string output = await RunDotNetCommandAsync(command);
				Log(output);
			}
			SetButtonsActive(true);
		}

		#region WSL
		public async void InstallWSL()
		{
			SetButtonsActive(false);
			if (await IsVirtualizationEnabledAsync())
			{
				if (!await IsWSLInstalledAsync())
				{
					Log("Installing WSL...");
					await RunDismCommandAsync("/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart");
					Log("WSL has been installed.");
				}
				else
				{
					Log("WSL is already installed.");
				}
			}
			else
			{
				Log("Virtualization is not enabled. Please enable Virtualization in your system BIOS.");
			}
			SetButtonsActive(true);
		}

		public async Task<bool> IsVirtualizationEnabledAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "powershell.exe";
				process.StartInfo.Arguments = "-Command \"(Get-WmiObject -Namespace 'root\\cimv2' -Class Win32_Processor).VirtualizationFirmwareEnabled\"";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();

				string output = process.StandardOutput.ReadToEnd();
				await process.WaitForExitAsync();

				return bool.Parse(output.Trim());
			}
		}

		public async Task<bool> IsWSLInstalledAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "where";
				process.StartInfo.Arguments = "/q wsl.exe";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				string output = await process.StandardOutput.ReadToEndAsync();
				await process.WaitForExitAsync();

				return process.ExitCode == 0;
			}
		}

		public async Task RunDismCommandAsync(string arguments)
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "dism.exe";
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync(); // Use asynchronous wait
			}
		}
		#endregion

		#region DotNet
		public async void InstallDotNet()
		{
			SetButtonsActive(false);
			if (!await IsDotNetInstalledAsync())
			{
				Log("Installing DotNet...");
				await DownloadAndInstallDotNetAsync();
				Log("DotNet has been installed.");
			}
			else
			{
				Log("DotNet is already installed.");

				if (!await IsDotNetEFInstalledAsync())
				{
					Log("Installing DotNet-EF...");
					string output = await RunDotNetCommandAsync("tool install --global dotnet-ef");
					Log(output);
				}
				else
				{
					Log("DotNet-EF is already installed.");
				}
			}
			SetButtonsActive(true);
		}

		public async Task<bool> IsDotNetInstalledAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				using (Process process = new Process())
				{
					process.StartInfo.FileName = "where";
					process.StartInfo.Arguments = "dotnet";
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.CreateNoWindow = true;

					process.Start();
					await process.WaitForExitAsync(); // Use asynchronous wait

					return process.ExitCode == 0;
				}
			}
			return false;
		}

		public async Task DownloadAndInstallDotNetAsync()
		{
			string downloadUrl = "https://dot.net/v1/dotnet-install.ps1";
			string psScriptFile = "dotnet-install.ps1";
			string installDir = "C:\\Program Files\\dotnet";
			string version = "7.0.202";

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "curl";
				process.StartInfo.Arguments = $"-o {psScriptFile} {downloadUrl}";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync(); // Use asynchronous wait
			}

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "powershell";
				process.StartInfo.Arguments = $"-ExecutionPolicy Bypass -Command \"& .\\{psScriptFile} -Version {version} -InstallDir '{installDir}'\"";
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync(); // Use asynchronous wait
			}
		}

		public async Task<bool> IsDotNetEFInstalledAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "dotnet";
				process.StartInfo.Arguments = "tool list --global";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				string output = await process.StandardOutput.ReadToEndAsync(); // Use asynchronous read
				await process.WaitForExitAsync(); // Use asynchronous wait

				return output.Contains("dotnet-ef");
			}
		}

		public async Task<string> RunDotNetCommandAsync(string arguments)
		{
#if !UNITY_EDITOR
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "dotnet";
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				string output = await process.StandardOutput.ReadToEndAsync(); // Use asynchronous read
				await process.WaitForExitAsync(); // Use asynchronous wait
				return output;
			}
#else
			return null;
#endif
		}
		#endregion

		#region Docker
		public async void InstallDocker()
		{
			SetButtonsActive(false);
			if (await IsDockerInstalledAsync())
			{
				Log("Docker is already installed.");
				SetButtonsActive(true);
				return;
			}

			Log("Installing Docker... Please wait.");

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "curl";
				process.StartInfo.Arguments = "https://download.docker.com/win/stable/Docker%20Desktop%20Installer.exe -o DockerInstaller.exe";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				process.WaitForExit();
			}

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "start";
				process.StartInfo.Arguments = "/wait DockerInstaller.exe";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				process.WaitForExit();
			}

			Log("Docker has been installed.");

			// Delete DockerInstaller.exe
			if (File.Exists("DockerInstaller.exe"))
			{
				File.Delete("DockerInstaller.exe");
			}
			SetButtonsActive(true);
		}

		public async Task<bool> IsDockerInstalledAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "docker";
				process.StartInfo.Arguments = "--version";
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync(); // Use asynchronous wait

				return process.ExitCode == 0;
			}
		}

		public async Task<string> RunDockerCommandAsync(string commandArgs)
		{
#if !UNITY_EDITOR
        using (Process process = new Process())
        {
            process.StartInfo.FileName = "docker";
            process.StartInfo.Arguments = commandArgs;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new System.Text.StringBuilder();
            var errorOutputBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutputBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            string output = outputBuilder.ToString();
            string errorOutput = errorOutputBuilder.ToString();

            return output + "\r\n" + errorOutput;
        }
#else
			return null;
#endif
		}

		/// <summary>
		/// Docker-Compose commands are not available in the editor.
		/// </summary>
		public async Task<string> RunDockerComposeCommandAsync(string commandArgs, Dictionary<string, string> environmentVariables = null)
		{
#if !UNITY_EDITOR
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "docker-compose";
				process.StartInfo.Arguments = commandArgs;
				if (environmentVariables != null)
				{
					foreach (KeyValuePair<string, string> kvp in environmentVariables)
					{
						process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
					}
				}
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				var outputBuilder = new System.Text.StringBuilder();
				var errorOutputBuilder = new System.Text.StringBuilder();

				process.OutputDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
					{
						outputBuilder.AppendLine(e.Data);
					}
				};

				process.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
					{
						errorOutputBuilder.AppendLine(e.Data);
					}
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				await process.WaitForExitAsync();

				string output = outputBuilder.ToString();
				string errorOutput = errorOutputBuilder.ToString();

				return output + "\r\n" + errorOutput;
			}
#else
			return null;
#endif
		}
		#endregion

		private async Task<bool> IsEverythingInstalled()
		{
			Log("Checking installs... Please wait.");

			if (!await IsWSLInstalledAsync())
			{
				Log("WSL needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			if (!await IsDotNetInstalledAsync())
			{
				Log("DotNet needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			if (!await IsDotNetEFInstalledAsync())
			{
				Log("DotNet-EF needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			if (!await IsDockerInstalledAsync())
			{
				Log("Docker needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			return true;
		}

		#region Database
		public async void InstallDatabase()
		{
			SetButtonsActive(false);

			if (!await IsEverythingInstalled())
			{
				SetButtonsActive(true);
				return;
			}

			Log("Installing database... Please wait.");

			string workingDirectory = GetWorkingDirectory();
			//Log(workingDirectory);

#if UNITY_EDITOR
			string setup = Path.Combine(workingDirectory, "FishMMO-Setup");
			//Log(setup);

			string envConfigurationPath = Path.Combine(setup, "Development");
			//Log(envConfigurationPath);

			string jsonPath = Path.Combine(envConfigurationPath, "appsettings.json");
			//Log(jsonPath);

			if (File.Exists(jsonPath))
			{
#else
			string jsonPath = Path.Combine(workingDirectory, "appsettings.json");
			//Log(jsonPath);

			if (File.Exists(jsonPath))
			{
#endif
				string jsonContent = File.ReadAllText(jsonPath);

				//Log(jsonContent);

				AppSettings appSettings = JsonUtility.FromJson<AppSettings>(jsonContent);

				// docker-compose up
				string output = await RunDockerComposeCommandAsync("up -d", new Dictionary<string, string>()
				{
					{ "POSTGRES_DB", appSettings.Npgsql.Database },
					{ "POSTGRES_USER", appSettings.Npgsql.Username },
					{ "POSTGRES_PASSWORD", appSettings.Npgsql.Password },
					{ "POSTGRES_HOST", appSettings.Npgsql.Host },
					{ "POSTGRES_PORT", appSettings.Npgsql.Port },
					{ "REDIS_HOST", appSettings.Redis.Host },
					{ "REDIS_PORT", appSettings.Redis.Port },
					{ "REDIS_PASSWORD", appSettings.Redis.Password },
				});

				// Run 'dotnet ef migrations add Initial' command
				string migrationOut = await RunDotNetCommandAsync($"ef migrations add Initial -p {ProjectPath} -s {StartupProject}");

				// Run 'dotnet ef database update' command
				string updateOut = await RunDotNetCommandAsync($"ef database update -p {ProjectPath} -s {StartupProject}");

				Log("Redis Host: " + appSettings.Redis.Host + "\r\n" +
					"Redis Port: " + appSettings.Redis.Port + "\r\n" +
					"Redis Password: " + appSettings.Redis.Password + "\r\n" +
					"Npgsql Database: " + appSettings.Npgsql.Database + "\r\n" +
					"Npgsql Username: " + appSettings.Npgsql.Username + "\r\n" +
					"Npgsql Password: " + appSettings.Npgsql.Password + "\r\n" +
					"Npgsql Host: " + appSettings.Npgsql.Host + "\r\n" +
					"Npgsql Port: " + appSettings.Npgsql.Port + "\r\n" +
					output + "\r\n" +
					"\r\n" +
					migrationOut + "\r\n" +
					"\r\n" +
					updateOut);
			}
			else
			{
				Log("appsettings.json file not found.");
			}
			SetButtonsActive(true);
		}

		public async void CreateMigration()
		{
			SetButtonsActive(false);

			if (!await IsEverythingInstalled())
			{
				SetButtonsActive(true);
				return;
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

			// Run 'dotnet ef migrations add Initial' command
			await RunDotNetCommandAsync($"ef migrations add {timestamp} -p \"{ProjectPath}\" -s \"{StartupProject}\"");

			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p \"{ProjectPath}\" -s \"{StartupProject}\"");

			SetButtonsActive(true);
		}
		#endregion

		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif

		}
		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}
	}
}