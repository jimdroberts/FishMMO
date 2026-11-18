namespace FishMMO.Core
{
	/// <summary>
	/// When in a tick a system runs. Phases run in this order; structural changes queued during a phase are
	/// applied when it ends, so every system in the next phase sees them.
	/// </summary>
	/// <remarks>The values leave room between phases for modules that need a phase of their own later.</remarks>
	public enum SystemPhase
	{
		/// <summary>Commands and input become intents.</summary>
		Input = 0,
		/// <summary>Movement and physics queries.</summary>
		Movement = 100,
		/// <summary>Abilities activate, travel and hit.</summary>
		Abilities = 200,
		/// <summary>Effects are applied: damage, healing, timed modifiers, items granted.</summary>
		Effects = 300,
		/// <summary>Stats are recalculated from their modifiers; resources regenerate.</summary>
		Stats = 400,
		/// <summary>What died this tick is handled; what has run its course is removed.</summary>
		Death = 500,
		/// <summary>Quests, achievements and other counters read the tick's events.</summary>
		Objectives = 600,
		/// <summary>Changed state is sent to observers.</summary>
		Replication = 700,
		/// <summary>Changed state is queued for saving.</summary>
		Persistence = 800,
	}

	/// <summary>What a system is told about the tick it runs in.</summary>
	public readonly struct TickContext
	{
		/// <summary>The simulation tick number.</summary>
		public readonly uint Tick;

		/// <summary>Simulation time at this tick, seconds.</summary>
		public readonly double Time;

		/// <summary>Seconds one tick covers.</summary>
		public readonly float DeltaTime;

		/// <summary>Whether this world is the server's (authoritative) one.</summary>
		public readonly bool IsServer;

		/// <summary>Builds a tick context.</summary>
		public TickContext(uint tick, double time, float deltaTime, bool isServer)
		{
			Tick = tick;
			Time = time;
			DeltaTime = deltaTime;
			IsServer = isServer;
		}
	}

	/// <summary>
	/// One behaviour, run over every entity holding the components it needs, once per tick: regeneration over
	/// everything with a resource and a regeneration rate, expiry over everything with a lifetime.
	/// </summary>
	/// <remarks>
	/// A system holds no per-entity state of its own (that is what components are for), so the same system
	/// serves one entity or ten thousand. It reads and writes components through the world's stores and asks for
	/// structural changes through <see cref="World.Commands"/>.
	/// </remarks>
	public abstract class GameSystem
	{
		/// <summary>When in the tick it runs.</summary>
		public abstract SystemPhase Phase { get; }

		/// <summary>Position within its phase; lower runs first.</summary>
		public virtual int Order => 0;

		/// <summary>The name used in profiling and logs.</summary>
		public virtual string Name => GetType().Name;

		/// <summary>Called once when the system is added to a scheduler with its world.</summary>
		public virtual void OnAdded(World world)
		{
		}

		/// <summary>Does this tick's work.</summary>
		public abstract void Update(World world, in TickContext tick);
	}

	/// <summary>Told when each system starts and ends, so an engine adapter can time it (profiler markers).</summary>
	public interface ISystemProfiler
	{
		/// <summary>A system is about to run.</summary>
		void Begin(GameSystem system);

		/// <summary>The system has finished.</summary>
		void End(GameSystem system);
	}
}
