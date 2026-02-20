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
			InstallerProcessHelper.Log("--- Install Visual Studio Build Tools ---");

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				InstallerProcessHelper.Log("Visual Studio Build Tools can only be installed on Windows. Skipping.");
				return;
			}

			if (!InstallerProcessHelper.PromptForYesNo("This option will download and launch the Visual Studio Build Tools installer. Would you like to proceed?"))
			{
				InstallerProcessHelper.Log("Visual Studio Build Tools installation cancelled by user.");
				return;
			}

			try
			{
				string installerPath = await InstallerProcessHelper.DownloadFileAsync(
					InstallationConstants.VSBuildToolsUrl,
					InstallationConstants.VSBuildToolsFileName);

				InstallerProcessHelper.Log("Automated installation of Visual Studio Build Tools will begin.");
				InstallerProcessHelper.Log("The following workloads and components will be installed:");
				InstallerProcessHelper.Log("  1. '.NET desktop development' workload (includes .NET Framework development tools)");
				InstallerProcessHelper.Log("  2. 'Desktop development with C++' workload (MSVC 64-bit compiler for x86, x64, ARM, and ARM64)");
				InstallerProcessHelper.Log("  3. Windows 10 SDK");
				InstallerProcessHelper.Log("After installation, restart your computer if prompted by the installer.");

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

				InstallerProcessHelper.Log($"Launching installer with arguments: {arguments}");
				Process? process = Process.Start(startInfo);
				if (process == null)
				{
					InstallerProcessHelper.Log("Failed to start Visual Studio Build Tools installer process.");
					return;
				}
				await process.WaitForExitAsync();

				InstallerProcessHelper.Log("Visual Studio Build Tools automated installation finished. Please verify the installation and restart your computer if required.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error installing Visual Studio Build Tools: {ex.Message}");
			}
		}
	}
}