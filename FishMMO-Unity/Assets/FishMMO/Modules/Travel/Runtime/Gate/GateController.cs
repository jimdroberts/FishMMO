using System;
using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared.Core;

// Every player carries the owner's end of the Gate and Call to Arms spells (added at wake, not authored on the prefab).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.GateController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The owner client's end of the Gate and Call to Arms spells (issue #241): turns the server's gate broadcasts into the map's
	/// gate mode (<see cref="GateStoneChoice"/>), the portals and lit stones drawn in the world (<see cref="GateVisuals"/>), the
	/// travel tickets kept between scene servers (<see cref="TravelTicketWallet"/>) and the summons prompt
	/// (<see cref="OnCallToArms"/>).
	/// </summary>
	/// <remarks>
	/// The server keeps nothing here: its gate state is the gate system's (Travel.Server), keyed by character id, so a pooled
	/// character inherits nothing.
	/// </remarks>
	public class GateController : CharacterPart
	{
		/// <summary>
		/// Client: a party member calls this character to arms (caster name, scene name, seconds to accept, accept). Invoke the
		/// accept action to go; ignoring it declines. The Travel module's client assembly answers it with a dialog.
		/// </summary>
		public static event Action<string, string, float, Action> OnCallToArms;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			OnCallToArms = null;
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<GateChooseBroadcast>(OnGateChoose);
			ClientManager.RegisterBroadcast<GatePortalBroadcast>(OnGatePortal);
			ClientManager.RegisterBroadcast<GateStoneLitBroadcast>(OnGateStoneLit);
			ClientManager.RegisterBroadcast<TravelTicketBroadcast>(OnTravelTicket);
			ClientManager.RegisterBroadcast<CallToArmsBroadcast>(OnCallToArmsReceived);

			GateStoneChoice.Submitter = SubmitChoice;
			GateStoneChoice.Canceller = CancelChoice;

			// The tickets this client carries are presented once on every scene server the character loads into.
			List<byte[]> live = TravelTicketWallet.Live(Character.ID);
			for (int i = 0; i < live.Count; ++i)
			{
				ClientManager.Broadcast(new TravelTicketPresentBroadcast() { Ticket = live[i] }, Channel.Reliable);
			}
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (!IsOwner)
			{
				return;
			}
			ClientManager.UnregisterBroadcast<GateChooseBroadcast>(OnGateChoose);
			ClientManager.UnregisterBroadcast<GatePortalBroadcast>(OnGatePortal);
			ClientManager.UnregisterBroadcast<GateStoneLitBroadcast>(OnGateStoneLit);
			ClientManager.UnregisterBroadcast<TravelTicketBroadcast>(OnTravelTicket);
			ClientManager.UnregisterBroadcast<CallToArmsBroadcast>(OnCallToArmsReceived);

			if (GateStoneChoice.Submitter == (Action<string, int>)SubmitChoice)
			{
				GateStoneChoice.Submitter = null;
				GateStoneChoice.Canceller = null;
			}
			// The server's wait ends with this character on this server; the map must not keep offering it.
			GateStoneChoice.End();
			GateVisuals.Clear();
		}

		private void SubmitChoice(string sceneName, int waypointIndex)
		{
			if (ClientManager != null && ClientManager.Started)
			{
				ClientManager.Broadcast(new GateChoiceBroadcast() { SceneName = sceneName, WaypointIndex = waypointIndex }, Channel.Reliable);
			}
		}

		private void CancelChoice()
		{
			if (ClientManager != null && ClientManager.Started)
			{
				ClientManager.Broadcast(new GateChoiceCancelBroadcast(), Channel.Reliable);
			}
		}

		private void OnGateChoose(GateChooseBroadcast msg, Channel channel)
		{
			if (msg.Seconds > 0.0f)
			{
				GateStoneChoice.Begin(msg.Seconds);
			}
			else
			{
				GateStoneChoice.End();
			}
		}

		private void OnGatePortal(GatePortalBroadcast msg, Channel channel)
		{
			GateVisuals.SetPortal(Character, msg.GateID, msg.Position, msg.Yaw, msg.Radius, msg.RemainingSeconds);
		}

		private void OnGateStoneLit(GateStoneLitBroadcast msg, Channel channel)
		{
			GateVisuals.SetStone(Character, msg.WaypointIndex, msg.Position, msg.Yaw, msg.RemainingSeconds);
		}

		private void OnTravelTicket(TravelTicketBroadcast msg, Channel channel)
		{
			TravelTicketWallet.Keep(Character.ID, msg.Ticket, msg.Seconds);
		}

		private void OnCallToArmsReceived(CallToArmsBroadcast msg, Channel channel)
		{
			byte[] ticket = msg.Ticket;
			if (ticket == null || ticket.Length == 0)
			{
				return;
			}
			double expires = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.0f, msg.Seconds);
			void Accept()
			{
				if (Time.realtimeSinceStartupAsDouble <= expires && ClientManager != null && ClientManager.Started)
				{
					ClientManager.Broadcast(new CallToArmsAnswerBroadcast() { Ticket = ticket }, Channel.Reliable);
				}
			}

			if (OnCallToArms != null)
			{
				OnCallToArms.Invoke(msg.CasterName, msg.SceneName, msg.Seconds, Accept);
			}
			else
			{
				// No prompt installed (the client assembly is absent): a summons nobody can answer is declined.
				FishMMO.Logging.Log.Debug("GateController", $"Call to Arms from {msg.CasterName} with no prompt to show it; declined.");
			}
		}
#endif
	}
}
