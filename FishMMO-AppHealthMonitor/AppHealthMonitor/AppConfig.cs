namespace AppHealthMonitor
{
	// Represents the configuration for a single application to be monitored.
	public class AppConfig
	{
		public string Name { get; set; }
		public string ApplicationExePath { get; set; }
		public string ApplicationProcessName { get; set; }
		public int MonitoredPort { get; set; }
		public List<PortType> PortTypes { get; set; }
		public string LaunchArguments { get; set; }
		public int CheckIntervalSeconds { get; set; }
		public int LaunchDelaySeconds { get; set; }
	}
}