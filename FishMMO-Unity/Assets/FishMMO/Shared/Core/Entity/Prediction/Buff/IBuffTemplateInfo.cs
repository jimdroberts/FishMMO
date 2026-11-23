using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What the core reads of a buff template (buff templates are the Buffs module's): its identity, how long
	/// it lasts, whether it is a debuff, whether a player may dismiss it, whether it is permanent (the environment's), and
	/// its icon. The target frame's strip, weather exposure and the evade rule read buffs through it.
	/// </summary>
	public interface IBuffTemplateInfo
	{
		/// <summary>The template's cached ID.</summary>
		int ID { get; }

		/// <summary>Seconds it lasts.</summary>
		float Duration { get; }

		/// <summary>True for a debuff.</summary>
		bool IsDebuff { get; }

		/// <summary>True when the player may remove it themselves.</summary>
		bool CanBeDismissedByPlayer { get; }

		/// <summary>True for a buff with no expiry (the environment's: weather exposure, a buff volume).</summary>
		bool IsPermanent { get; }

		/// <summary>The loaded icon, or null.</summary>
		Sprite Icon { get; }
	}
}
