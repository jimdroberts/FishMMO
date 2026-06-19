using FishMMO.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles installation of Visual Studio Build Tools on Windows.
	/// Downloads the bootstrapper and runs an automated silent installation.
	/// </summary>
	public static class VSBuildToolsInstaller
	{
		/// <summary>
		/// Installs Visual Studio Build Tools on Windows.
		/// Downloads the bootstrapper and launches a silent installation with
		/// .NET desktop development and C++ desktop development workloads.
		/// </summary>
		public static async Task InstallVSBuildTools()
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Install Visual Studio Build Tools ---");

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await Log.Info("FishMMOInstaller", "Visual Studio Build Tools can only be installed on Windows. Skipping.");
				return;
			}

			if (!InstallerProcessHelper.PromptForYesNo("This option will download and launch the Visual Studio Build Tools installer. Would you like to proceed?"))
			{
				await Log.Info("FishMMOInstaller", "Visual Studio Build Tools installation cancelled by user.");
				return;
			}

			DownloadHelper.CheckDiskSpace(3L * 1024 * 1024 * 1024); // ~3 GB
			try
			{
				string? installerPath = await DownloadHelper.DownloadFileWithProgressAsync(
					InstallationConstants.VSBuildToolsUrl,
					InstallationConstants.VSBuildToolsFileName,
					new DownloadHelper.ConsoleProgress());

				if (installerPath == null)
				{
					await Log.Error("FishMMOInstaller", "Failed to download Visual Studio Build Tools installer.");
					return;
				}

				await Log.Info("FishMMOInstaller", "Automated installation of Visual Studio Build Tools will begin.");
				await Log.Info("FishMMOInstaller", "The following workloads and components will be installed:");
				await Log.Info("FishMMOInstaller", "  1. '.NET desktop development' workload (includes .NET Framework development tools)");
				await Log.Info("FishMMOInstaller", "  2. 'Desktop development with C++' workload (MSVC 64-bit compiler for x86, x64, ARM, and ARM64)");
				await Log.Info("FishMMOInstaller", "  3. Windows 10 SDK");
				await Log.Info("FishMMOInstaller", "After installation, restart your computer if prompted by the installer.");

				string arguments = "--quiet --wait --norestart --nocache " +
					"--add Microsoft.VisualStudio.Workload.ManagedDesktop " +
					"--add Microsoft.VisualStudio.Workload.NativeDesktop " +
					"--add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 " +
					"--add Microsoft.VisualStudio.Component.Windows10SDK.19041";

				InstallerProcessHelper.LogElevatedProcessEnvironmentWarning("Visual Studio Build Tools installer");

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = installerPath,
					Arguments = arguments,
					WorkingDirectory = Path.GetDirectoryName(installerPath) ?? InstallerProcessHelper.GetWorkingDirectory(),
					UseShellExecute = true,
					Verb = "runas"
				};

				await Log.Info("FishMMOInstaller", $"Launching installer with arguments: {arguments}");
				Process? process = Process.Start(startInfo);
				if (process == null)
				{
					await Log.Error("FishMMOInstaller", "Failed to start Visual Studio Build Tools installer process.");
					return;
				}
				await process.WaitForExitAsync();

				await Log.Info("FishMMOInstaller", "Visual Studio Build Tools automated installation finished. Please verify the installation and restart your computer if required.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error installing Visual Studio Build Tools", ex);
			}
		}
	}
}