using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lightweight EventData subtype carrying the absolute network tick for prediction-aware triggers.
	/// Added as a sub-payload to existing EventData instances so actions can extract the tick.
	/// </summary>
	public class TickEventData : EventData
	{
		// Was: public uint Tick;
		public PredictionTick Tick { get; }

		public TickEventData(ICharacter character, PredictionTick tick) : base(character)
		{
			Tick = tick;
		}

		// Internal ctor for non-prediction callers within FishMMO.Shared
		internal TickEventData(ICharacter character, uint serverTick) : base(character)
		{
			Tick = new PredictionTick(serverTick);
		}
	}
}