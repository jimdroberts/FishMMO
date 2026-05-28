using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
    /// <summary>
    /// Lightweight EventData subtype carrying the absolute network tick for prediction-aware triggers.
    /// Added as a sub-payload to existing EventData instances so actions can extract the tick.
    /// </summary>
    public class TickEventData : EventData
    {
        public uint Tick { get; }

        public TickEventData(ICharacter initiator, uint tick)
            : base(initiator)
        {
            Tick = tick;
        }
    }
}
