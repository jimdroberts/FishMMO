namespace FishMMO.Shared
{
    public enum AgentAvoidancePriority : byte
    {
        None = 0,       // Lowest priority, agent avoids others less
        Low = 25,       // Low priority
        Medium = 50,    // Default priority
        High = 75,      // High priority
        Critical = 100  // Highest priority, agent avoids others at all costs
    }
}