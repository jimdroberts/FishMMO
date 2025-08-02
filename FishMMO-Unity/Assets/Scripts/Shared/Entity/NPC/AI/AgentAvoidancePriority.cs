namespace FishMMO.Shared
{
	/// <summary>
	/// Defines avoidance priority levels for AI agents. Higher values cause agents to avoid others more aggressively.
	/// Used to control how strongly an agent tries to avoid collisions with other agents in the navigation system.
	/// </summary>
	public enum AgentAvoidancePriority : byte
	{
		/// <summary>
		/// Lowest priority. Agent avoids others the least and is most likely to yield.
		/// </summary>
		None = 0,

		/// <summary>
		/// Low avoidance priority. Agent yields to higher priority agents.
		/// </summary>
		Low = 25,

		/// <summary>
		/// Medium avoidance priority. Default value for most agents.
		/// </summary>
		Medium = 50,

		/// <summary>
		/// High avoidance priority. Agent actively avoids others and is less likely to yield.
		/// </summary>
		High = 75,

		/// <summary>
		/// Critical avoidance priority. Agent avoids others at all costs and rarely yields.
		/// </summary>
		Critical = 100
	}
}