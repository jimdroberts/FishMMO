using System.Collections.Generic;
using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.HotkeyModule))]

namespace FishMMO.Shared
{
	/// <summary>A player's hotkey slots, on the player's Core entity (server). Every slot exists, empty or bound.</summary>
	public struct HotkeyBar
	{
		/// <summary>One entry per slot, in slot order; its own list per player.</summary>
		public List<HotkeyData> Slots;

		/// <summary>A bar of <see cref="Constants.Configuration.MaximumPlayerHotkeys"/> empty slots.</summary>
		public static HotkeyBar Empty()
		{
			var slots = new List<HotkeyData>(Constants.Configuration.MaximumPlayerHotkeys);
			for (int i = 0; i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
			{
				slots.Add(new HotkeyData()
				{
					Slot = i,
					ReferenceID = HotkeyData.UnsetReferenceID,
				});
			}
			return new HotkeyBar { Slots = slots };
		}
	}

	/// <summary>Hotkeys as a module: gives every player on the server an empty <see cref="HotkeyBar"/>, which the load fills.</summary>
	public sealed class HotkeyModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "hotkeys";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
			if (builder.Side == SimulationSide.Server)
			{
				builder.Archetype(Archetypes.Player).Add(HotkeyBar.Empty);
			}
		}
	}
}
