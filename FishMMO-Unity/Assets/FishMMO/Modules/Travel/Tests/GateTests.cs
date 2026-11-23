using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the Gate and Call to Arms spells' pure pieces (issue #241): who may enter a gate, where it stands and
	/// where its travellers land, the choice's refusal order, the signed travel ticket, and the party signal that carries a
	/// summons across scene servers.
	/// </summary>
	[TestFixture]
	public class GateTests
	{
		private static readonly byte[] Key = TravelTicket.DeriveKey(new byte[32]);

		// ── Rules ───────────────────────────────────────────────────────────────────

		[TestCase(GateAccess.Anyone, false, false, false, true)]
		[TestCase(GateAccess.PartyAndGuild, false, false, false, false)]
		[TestCase(GateAccess.PartyAndGuild, false, true, false, true)]
		[TestCase(GateAccess.PartyAndGuild, false, false, true, true)]
		[TestCase(GateAccess.Party, false, false, true, false)]
		[TestCase(GateAccess.Party, false, true, false, true)]
		[TestCase(GateAccess.CasterOnly, false, true, true, false)]
		[TestCase(GateAccess.CasterOnly, true, false, false, true)]
		public void CanEnter_FollowsTheTemplatesAccess(GateAccess access, bool isCaster, bool sameParty, bool sameGuild, bool expected)
		{
			LogAssert.AreEqual(expected, GateRules.CanEnter(access, isCaster, sameParty, sameGuild));
		}

		[Test]
		public void Portal_StandsAheadOfTheCaster_AndIsEnteredByWalkingIn()
		{
			Vector3 portal = GateRules.PortalPosition(new Vector3(10, 2, 10), 90.0f, 3.0f);
			LogAssert.IsTrue(Vector3.Distance(portal, new Vector3(13, 2, 10)) < 1e-4f, $"facing +x, three metres ahead: {portal}");

			LogAssert.IsTrue(GateRules.IsInside(portal, 1.2f, portal + new Vector3(1.0f, 0.5f, 0.0f)));
			LogAssert.IsFalse(GateRules.IsInside(portal, 1.2f, portal + new Vector3(1.3f, 0.0f, 0.0f)), "outside the radius");
			LogAssert.IsFalse(GateRules.IsInside(portal, 1.2f, portal + new Vector3(0.0f, GateRules.EntryHeight + 0.1f, 0.0f)), "flying over it");
			LogAssert.IsFalse(GateRules.IsInside(portal, 1.2f, new Vector3(10, 2, 10)), "the caster's own spot is outside it, so a return does not bounce");
		}

		[Test]
		public void ArrivalSpots_SpreadAPartyAroundTheStone()
		{
			Vector3 arrival = new Vector3(5, 1, 5);
			LogAssert.AreEqual(arrival, GateRules.ArrivalSpot(arrival, 0.0f, 0), "the first lands on the arrival point");
			var spots = new HashSet<Vector3>();
			for (int slot = 0; slot < GateRules.ArrivalSpots; ++slot)
			{
				Vector3 spot = GateRules.ArrivalSpot(arrival, 30.0f, slot);
				LogAssert.AreEqual(arrival.y, spot.y, "only spread across the ground");
				if (slot > 0)
				{
					LogAssert.IsTrue(Mathf.Abs(Vector3.Distance(spot, arrival) - GateRules.ArrivalRingMetres) < 1e-4f);
				}
				spots.Add(spot);
			}
			LogAssert.AreEqual(GateRules.ArrivalSpots, spots.Count, "every spot differs");
			LogAssert.AreEqual(GateRules.ArrivalSpot(arrival, 30.0f, 1), GateRules.ArrivalSpot(arrival, 30.0f, 1 + GateRules.ArrivalSpots), "slots wrap");
		}

		[TestCase(false, true, true, true, true, true, GateChoiceRefusal.NotChoosing)]
		[TestCase(true, false, true, true, true, true, GateChoiceRefusal.UnknownStone)]
		[TestCase(true, true, false, true, true, true, GateChoiceRefusal.NotAGateStone)]
		[TestCase(true, true, true, false, true, true, GateChoiceRefusal.NotDiscovered)]
		[TestCase(true, true, true, true, false, true, GateChoiceRefusal.CannotAct)]
		[TestCase(true, true, true, true, true, false, GateChoiceRefusal.CannotOpenHere)]
		[TestCase(true, true, true, true, true, true, GateChoiceRefusal.None)]
		public void DecideChoice_RefusesInOrder(bool choosing, bool known, bool isStone, bool discovered, bool canAct, bool canOpen, GateChoiceRefusal expected)
		{
			LogAssert.AreEqual(expected, GateRules.DecideChoice(choosing, known, isStone, discovered, canAct, canOpen));
		}

		[Test]
		public void StoneChannel_StaysInTheCastersChannelWithinItsScene()
		{
			LogAssert.AreEqual(7L, GateRules.StoneChannel("Moron", 7, false, "Moron"), "same open-world scene: a local move");
			LogAssert.AreEqual(0L, GateRules.StoneChannel("Moron", 7, false, "Forest"), "another scene: any channel");
			LogAssert.AreEqual(0L, GateRules.StoneChannel("Moron", 7, true, "Moron"), "from an instance: any channel");
			LogAssert.IsTrue(CharacterTransfer.IsLocalMove("Moron", 7, false, "Moron", GateRules.StoneChannel("Moron", 7, false, "Moron")));
		}

		[TestCase(false, true, true, false, TravelSpellRefusal.CannotAct)]
		[TestCase(true, true, true, false, TravelSpellRefusal.InArena)]
		[TestCase(true, false, true, true, TravelSpellRefusal.NoTravelZone)]
		[TestCase(true, false, false, true, TravelSpellRefusal.InInstance)]
		[TestCase(true, false, false, false, TravelSpellRefusal.None)]
		public void DecideOpening_RefusesInstancesAfterTheSharedRules(bool canAct, bool inArena, bool inZone, bool inInstance, TravelSpellRefusal expected)
		{
			LogAssert.AreEqual(expected, TravelSpellRules.DecideOpening(canAct, inArena, inZone, inInstance));
			if (expected == TravelSpellRefusal.InInstance)
			{
				LogAssert.AreEqual(OpenGateAction.InInstanceText, TravelSpellRules.Describe(expected, null, OpenGateAction.InInstanceText));
			}
		}

		[Test]
		public void Settings_AreClamped()
		{
			var action = new OpenGateAction() { LifetimeSeconds = 100000.0f, ChooseSeconds = 0.0f, Radius = 50.0f, ForwardDistance = -2.0f };
			GateSettings settings = action.Settings;
			LogAssert.AreEqual(GateRules.MaxLifetimeSeconds, settings.LifetimeSeconds);
			LogAssert.AreEqual(GateRules.MinChooseSeconds, settings.ChooseSeconds);
			LogAssert.AreEqual(GateRules.MaxRadius, settings.Radius);
			LogAssert.AreEqual(1.0f, settings.ForwardDistance);
			LogAssert.AreEqual(30.0f, new OpenGateAction().Settings.LifetimeSeconds, "a gate lasts 30 s by default");
			LogAssert.AreEqual(GateAccess.Anyone, new OpenGateAction().Settings.Access, "anyone may enter by default");
		}

		// ── Tickets ─────────────────────────────────────────────────────────────────

		private static TravelTicket SampleReturn()
		{
			return new TravelTicket()
			{
				Kind = TravelTicketKind.GateReturn,
				ID = 0x1234_5678_9ABC,
				CharacterID = 42,
				ExpiresUnixMs = 1_800_000_000_000,
				OriginScene = "Moron 2",
				OriginSceneID = 17,
				OriginPosition = new Vector3(1.5f, -2.25f, 300.125f),
				OriginYaw = 271.0f,
				StoneScene = "Forest",
				StoneIndex = 9,
				CasterName = string.Empty,
			};
		}

		[Test]
		public void Ticket_RoundTrips()
		{
			TravelTicket ticket = SampleReturn();
			byte[] bytes = TravelTicket.Encode(ticket, Key);
			LogAssert.IsNotNull(bytes);
			LogAssert.IsTrue(bytes.Length <= TravelTicket.MaxBytes);
			LogAssert.IsTrue(TravelTicket.TryDecode(bytes, Key, out TravelTicket read));
			LogAssert.AreEqual(ticket.Kind, read.Kind);
			LogAssert.AreEqual(ticket.ID, read.ID);
			LogAssert.AreEqual(ticket.CharacterID, read.CharacterID);
			LogAssert.AreEqual(ticket.ExpiresUnixMs, read.ExpiresUnixMs);
			LogAssert.AreEqual(ticket.OriginScene, read.OriginScene);
			LogAssert.AreEqual(ticket.OriginSceneID, read.OriginSceneID);
			LogAssert.AreEqual(ticket.OriginPosition, read.OriginPosition);
			LogAssert.AreEqual(ticket.OriginYaw, read.OriginYaw);
			LogAssert.AreEqual(ticket.StoneScene, read.StoneScene);
			LogAssert.AreEqual(ticket.StoneIndex, read.StoneIndex);

			var summons = new TravelTicket()
			{
				Kind = TravelTicketKind.CallToArms, ID = 5, CharacterID = 1, PartyID = 99, ExpiresUnixMs = 10,
				OriginScene = "Moron", OriginPosition = Vector3.one, StoneScene = string.Empty, StoneIndex = -1, CasterName = "Aldric",
			};
			LogAssert.IsTrue(TravelTicket.TryDecode(TravelTicket.Encode(summons, Key), Key, out TravelTicket readSummons));
			LogAssert.AreEqual(99L, readSummons.PartyID);
			LogAssert.AreEqual("Aldric", readSummons.CasterName);
		}

		[Test]
		public void Ticket_RefusesAnyTamperingAndAnotherKey()
		{
			byte[] bytes = TravelTicket.Encode(SampleReturn(), Key);
			for (int i = 0; i < bytes.Length; ++i)
			{
				byte[] forged = (byte[])bytes.Clone();
				forged[i] ^= 0x01;
				LogAssert.IsFalse(TravelTicket.TryDecode(forged, Key, out _), $"a flipped bit at byte {i} is refused");
			}
			byte[] otherKey = TravelTicket.DeriveKey(new byte[] { 1, 2, 3 });
			LogAssert.IsFalse(TravelTicket.TryDecode(bytes, otherKey, out _), "another deployment's key");
			LogAssert.IsFalse(TravelTicket.TryDecode(bytes.AsSpan(0, bytes.Length - 1).ToArray(), Key, out _), "truncated");
			LogAssert.IsFalse(TravelTicket.TryDecode(null, Key, out _));
			LogAssert.IsFalse(TravelTicket.TryDecode(bytes, null, out _), "no key, nothing is trusted");
			LogAssert.IsFalse(TravelTicket.TryDecode(new byte[TravelTicket.MaxBytes + 1], Key, out _), "oversized");
			LogAssert.IsNull(TravelTicket.Encode(SampleReturn(), null), "no key, nothing is signed");
			LogAssert.IsNull(TravelTicket.DeriveKey(null));
		}

		[Test]
		public void Ticket_ExpiresWithASkewGrace()
		{
			TravelTicket ticket = SampleReturn();
			LogAssert.IsTrue(ticket.IsLive(ticket.ExpiresUnixMs));
			LogAssert.IsTrue(ticket.IsLive(ticket.ExpiresUnixMs + (long)(TravelTicket.ClockSkewSeconds * 1000.0) - 1));
			LogAssert.IsFalse(ticket.IsLive(ticket.ExpiresUnixMs + (long)(TravelTicket.ClockSkewSeconds * 1000.0) + 1));
			LogAssert.AreEqual(2.5f, ticket.RemainingSeconds(ticket.ExpiresUnixMs - 2500));
			LogAssert.AreEqual(0.0f, ticket.RemainingSeconds(ticket.ExpiresUnixMs + 1));
		}

		// ── Party signals ───────────────────────────────────────────────────────────

		[Test]
		public void PartySignal_SurvivesTheChatLine_AndIsNotWhiteSpace()
		{
			LogAssert.IsFalse(char.IsWhiteSpace(PartySignals.Marker), "a chat Trim must not eat the marker");
			string text = PartySignals.Format("cta", "QUJD");
			string word = ChatHelper.GetWordAndTrimmed("77 " + text, out string trimmed);
			LogAssert.AreEqual("77", word);
			LogAssert.IsTrue(PartySignals.IsSignal(trimmed), "the party pump's split keeps it a signal");
			LogAssert.IsTrue(PartySignals.TryParse(trimmed, out string kind, out string payload));
			LogAssert.AreEqual("cta", kind);
			LogAssert.AreEqual("QUJD", payload);
			LogAssert.IsFalse(PartySignals.IsSignal("hello party"));
		}

		[Test]
		public void PartySignal_DispatchesToItsKind_AndSwallowsUnknownOnes()
		{
			long seenParty = 0;
			string seenPayload = null;
			Action<long, string> handler = (party, payload) => { seenParty = party; seenPayload = payload; };
			PartySignals.Register("testkind", handler);
			try
			{
				LogAssert.IsTrue(PartySignals.TryDispatch(5, PartySignals.Format("testkind", "x y")));
				LogAssert.AreEqual(5L, seenParty);
				LogAssert.AreEqual("x y", seenPayload);
				LogAssert.IsTrue(PartySignals.TryDispatch(5, PartySignals.Format("nobody", "z")), "an unknown signal is still never shown");
				LogAssert.IsFalse(PartySignals.TryDispatch(5, "just talking"));
				LogAssert.IsFalse(PartySignals.Post(null, 5, "testkind", "p"), "no poster, no sender: nothing posted");
			}
			finally
			{
				PartySignals.Unregister("testkind", handler);
			}
		}

		[Test]
		public void ChatDropsSignalsAPlayerTyped()
		{
			string group = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/Chat/Server/ChatChannelSystem.GroupChat.cs");
			int signal = group.IndexOf("if (PartySignals.IsSignal(trimmed))", StringComparison.Ordinal);
			int persist = group.IndexOf("EnqueuePersist(sender.ID", StringComparison.Ordinal);
			LogAssert.IsTrue(signal > 0 && persist > signal, "the signal check comes before a player's party line is persisted or shown");
			LogAssert.IsTrue(group.IndexOf("if (sender == null)", signal, StringComparison.Ordinal) > signal, "only a pumped (server) row is dispatched");
		}

		// ── Gate Stones are not fast travel ─────────────────────────────────────────

		[Test]
		public void GateStones_AreNeverFastTravelTargetsOrOrigins()
		{
			string travel = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/Travel/Runtime/WaypointTravel.cs");
			LogAssert.IsTrue(travel.Contains("waypoint.Kind == WaypointKind.GateStone") && travel.Contains("WaypointTravelRefusalReason.NotATravelPoint"),
				"fast travel refuses a Gate Stone");
			string registry = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Waypoint/WaypointRegistry.cs");
			LogAssert.IsTrue(registry.Contains("waypoint.Kind == WaypointKind.GateStone"), "a Gate Stone is not a travel origin");
			string policy = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Waypoint/WaypointTravelPolicy.cs");
			LogAssert.IsTrue(policy.Contains("!entry.Value.IsGateStone"), "nor is it one on the map's hint");
			LogAssert.AreEqual(10, (int)WaypointTravelRefusalReason.NotATravelPoint, "appended: the wire values before it are stable");
			LogAssert.AreEqual(1, (int)WaypointKind.GateStone, "serialized and baked: never renumber");
		}
	}
}
