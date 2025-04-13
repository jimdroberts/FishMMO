public class PatchVersionService
{
	private readonly ILogger<PatchVersionService> logger;
	public string? LatestVersion { get; private set; }

	public PatchVersionService(ILogger<PatchVersionService> logger)
	{
		this.logger = logger;
	}

	public void LoadFromFile(string filePath)
	{
		if (!File.Exists(filePath))
		{
			logger.LogWarning("Version config file not found: {Path}", filePath);

			// Fall back to manual input if the file is missing
			PromptForManualVersion();
			return;
		}

		try
		{
			foreach (var line in File.ReadLines(filePath))
			{
				if (line.Contains('='))
				{
					var parts = line.Split('=', 2);
					if (parts[0].Trim() == "Version")
					{
						LatestVersion = parts[1].Trim();
						logger.LogInformation("Loaded version: {Version}", LatestVersion);
						return;
					}
				}
			}

			// If the version key was not found, prompt for manual input
			logger.LogWarning("Version not found in config file.");
			PromptForManualVersion();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error loading version from file.");
			// Fall back to manual input in case of any error
			PromptForManualVersion();
		}
	}

	private void PromptForManualVersion()
	{
		Console.WriteLine("Version config file is missing or invalid. Please enter the version manually:");

		// Prompt user until a valid version is entered
		while (true)
		{
			Console.Write("Enter Version: ");
			string input = Console.ReadLine()?.Trim();

			if (!string.IsNullOrEmpty(input))
			{
				LatestVersion = input;
				logger.LogInformation("Manually set version: {Version}", LatestVersion);
				break;
			}
			else
			{
				Console.WriteLine("Invalid input. Please enter a valid version.");
			}
		}
	}
}