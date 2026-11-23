using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>What the Gate spell's template sets (issue #241). Read off <see cref="OpenGateAction"/> when it is cast.</summary>
	public struct GateSettings
	{
		/// <summary>Seconds the gate stays open once its stone is chosen.</summary>
		public float LifetimeSeconds;

		/// <summary>Seconds the caster has to choose a stone.</summary>
		public float ChooseSeconds;

		/// <summary>How far in front of the caster the portal opens, metres.</summary>
		public float ForwardDistance;

		/// <summary>The portal's walk-in radius, metres.</summary>
		public float Radius;

		/// <summary>Who may step through.</summary>
		public GateAccess Access;
	}

	/// <summary>What the Call to Arms spell's template sets.</summary>
	public struct CallToArmsSettings
	{
		/// <summary>Seconds a summoned member has to accept.</summary>
		public float AcceptSeconds;
	}

	/// <summary>
	/// The scene server's gate system, as the travel spells reach it (issue #241). Installed by the Travel module's server
	/// assembly; null on a client or a server without it, where the spells refuse.
	/// </summary>
	public interface IGateHost
	{
		/// <summary>Begins waiting for <paramref name="caster"/>'s Gate Stone choice. Returns what to tell the caster on refusal, else null.</summary>
		string BeginChoice(IPlayerCharacter caster, in GateSettings settings);

		/// <summary>Summons <paramref name="caster"/>'s party. Returns what to tell the caster on refusal, else null.</summary>
		string CallToArms(IPlayerCharacter caster, in CallToArmsSettings settings);

		/// <summary>
		/// <paramref name="player"/> used a discovered Gate Stone: takes it home when the stone is lit and it holds a return ticket
		/// for it. Returns false when there was nothing to do (the stone is dormant for this character).
		/// </summary>
		bool UseStone(IPlayerCharacter player, IWaypoint stone);
	}

	/// <summary>Where the travel spells find the <see cref="IGateHost"/>.</summary>
	public static class GateHost
	{
		/// <summary>The running gate system; null where there is none.</summary>
		public static IGateHost Current { get; set; }

		/// <summary>What a spell says when there is no gate system.</summary>
		public const string UnavailableText = "Gate magic does not work here.";
	}

	/// <summary>Using a discovered Gate Stone (the waypoint interaction, issue #241).</summary>
	public static class GateStoneUse
	{
		/// <summary>What a character is told at a stone with no gate for it.</summary>
		public const string DormantText = "The Gate Stone is dormant. Cast Gate elsewhere to open a portal to it.";

		/// <summary>Takes <paramref name="player"/> home through a lit stone, or tells it the stone is dormant. Server only.</summary>
		public static void Use(IPlayerCharacter player, IWaypoint stone)
		{
			IGateHost host = GateHost.Current;
			if (host == null || !host.UseStone(player, stone))
			{
				TravelSpellFeedback.Toast(player, DormantText, ToastSeverity.Info);
			}
		}
	}
}
