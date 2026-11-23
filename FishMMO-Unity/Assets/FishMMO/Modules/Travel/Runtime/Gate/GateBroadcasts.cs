using FishNet.Broadcast;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → owner: the Gate spell has been cast and waits for a Gate Stone (issue #241). The map opens in gate mode for
	/// <see cref="Seconds"/>; 0 ends gate mode (a stone was chosen, the choice was cancelled or timed out, or damage broke it).
	/// </summary>
	public struct GateChooseBroadcast : IBroadcast
	{
		public float Seconds;
	}

	/// <summary>Owner → server: open the waiting gate to this Gate Stone.</summary>
	public struct GateChoiceBroadcast : IBroadcast
	{
		public string SceneName;
		public int WaypointIndex;
	}

	/// <summary>Owner → server: give up the waiting gate (the map was closed, or Cancel pressed).</summary>
	public struct GateChoiceCancelBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Server → clients in the scene: a gate's portal is open where it stands (<see cref="RemainingSeconds"/> above 0), or
	/// has closed (0). Sent when it opens and closes, and to each character that arrives in the scene while it stands.
	/// </summary>
	public struct GatePortalBroadcast : IBroadcast
	{
		public long GateID;
		public Vector3 Position;
		public float Yaw;
		public float Radius;
		public float RemainingSeconds;
	}

	/// <summary>
	/// Server → clients in the scene: a Gate Stone is lit (a gate to it is open; its travellers may use it to go home) for
	/// <see cref="RemainingSeconds"/>, or dark again (0).
	/// </summary>
	public struct GateStoneLitBroadcast : IBroadcast
	{
		public int WaypointIndex;

		/// <summary>Where the stone stands, so the client needs neither the stone's object nor the map cache to light it.</summary>
		public Vector3 Position;

		/// <summary>The stone's facing, degrees about up.</summary>
		public float Yaw;

		public float RemainingSeconds;
	}

	/// <summary>
	/// Server → owner: keep this signed ticket (<see cref="TravelTicket"/>) for <see cref="Seconds"/> and present it on every
	/// scene server the character loads into until then.
	/// </summary>
	public struct TravelTicketBroadcast : IBroadcast
	{
		public byte[] Ticket;
		public float Seconds;
	}

	/// <summary>Owner → server: a ticket this client holds, presented once after the character loads in.</summary>
	public struct TravelTicketPresentBroadcast : IBroadcast
	{
		public byte[] Ticket;
	}

	/// <summary>Server → party member: <see cref="CasterName"/> calls you to arms; accept within <see cref="Seconds"/>.</summary>
	public struct CallToArmsBroadcast : IBroadcast
	{
		public byte[] Ticket;
		public string CasterName;
		public string SceneName;
		public float Seconds;
	}

	/// <summary>Party member → server: I accept this summons. A decline sends nothing.</summary>
	public struct CallToArmsAnswerBroadcast : IBroadcast
	{
		public byte[] Ticket;
	}
}
