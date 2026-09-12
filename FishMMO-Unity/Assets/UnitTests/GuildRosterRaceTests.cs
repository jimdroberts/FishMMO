using System;
using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the roster's race column on the create and join paths (issue #284).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A guild member's row reaches a client by one of two routes. The roster pump reads the
	/// membership rows back from the database — where the race is joined in from the character
	/// row — and the create/join paths answer the caller IMMEDIATELY, before any such read has
	/// happened, from the live character alone.
	/// </para>
	/// <para>
	/// Those two immediate rows used to be built by hand, and both omitted
	/// <c>GuildAddEntry.RaceID</c>. The client renders an unresolved race as an em dash, so the
	/// founder's own row read as unknown from the moment the guild was founded. Nothing
	/// corrected it: <c>CreateGuildAsync</c> was also the only membership mutation that never
	/// wrote a cross-server update marker, so the pump had nothing to fetch for a guild that had
	/// only ever been created — the wrong row survived until a relog re-read the roster.
	/// </para>
	/// <para>
	/// The sweep is therefore over the shape of those two sends plus the marker write, rather
	/// than over a value: the defect was never that the race was computed wrongly, it was that
	/// these two sends did not ask for it at all. The projections that DO include it are pinned
	/// alongside so a future edit cannot quietly move a send back to an inline initialiser.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class GuildRosterRaceTests
	{
		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.cs";
		private const string AuthorityPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.Authority.cs";
		private const string ClientPath =
			"Assets/Scripts/Client/GUI/World/Guild/UITKGuild.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private static string Between(string source, string start, string end, string what)
		{
			int s = source.IndexOf(start, StringComparison.Ordinal);
			LogAssert.IsTrue(s >= 0, $"{what}: must still contain '{start}'");
			int e = source.IndexOf(end, s, StringComparison.Ordinal);
			LogAssert.IsTrue(e > s, $"{what}: the end marker '{end}' must follow");
			return source.Substring(s, e - s);
		}

		/// <summary>
		/// The consuming half of the contract. The race is deliberately NOT resolved into a name
		/// on the wire — the client resolves the identifier through its own template cache — so
		/// the client has to keep storing the raw value and rendering the row from that store.
		/// </summary>
		[Test]
		public void TheClientStoresTheWireRaceAndRendersTheRowFromIt()
		{
			string client = ReadSource(ClientPath);

			LogAssert.IsTrue(client.Contains("model.RaceID = member.RaceID;"),
				"the panel stores the race the server sent rather than resolving one of its own");
			LogAssert.IsTrue(client.Contains("row.Race.text = ResolveRaceName(model.RaceID);"),
				"and the race column renders from that stored value");
		}

		[Test]
		public void TheFoundersOwnRowIsProjectedFromTheLiveCharacter()
		{
			string create = Between(ReadSource(ServerPath),
				"// tell the character we made their guild successfully", "}, true, Channel.Reliable);",
				"create path immediate add");

			LogAssert.IsTrue(create.Contains("BuildSelfRosterEntry"),
				"the founder's row goes through the shared projector, which is the only place that sets RaceID");
			LogAssert.IsFalse(create.Contains("new GuildAddEntry"),
				"the founder's row must not be hand-built — that is exactly how RaceID was lost");
		}

		[Test]
		public void TheNewMembersOwnRowIsProjectedFromTheLiveCharacter()
		{
			string join = Between(ReadSource(ServerPath),
				"// tell the new member they joined immediately", "}, true, Channel.Reliable);",
				"join path immediate add");

			LogAssert.IsTrue(join.Contains("BuildSelfRosterEntry"),
				"the joining member's row goes through the shared projector");
			LogAssert.IsFalse(join.Contains("new GuildAddEntry"),
				"the joining member's row must not be hand-built");
		}

		/// <summary>
		/// The projector must take the race from the same value the character table is written
		/// from, so the row sent now and the row the next database read produces agree.
		/// </summary>
		[Test]
		public void TheProjectorSourcesTheRaceFromTheCharacter()
		{
			string source = ReadSource(AuthorityPath);
			string projector = Between(source, "internal static GuildAddEntry BuildSelfRosterEntry", "\n\t\t}", "self projector");

			LogAssert.IsTrue(projector.Contains("character.RaceID"),
				"the projector reads the race off the live character");
			LogAssert.IsTrue(projector.Contains("character != null"),
				"and tolerates a connection whose character component could not be resolved");
		}

		/// <summary>
		/// Every other membership mutation notifies the pump; create did not, so a guild that was
		/// founded and then left alone was polled but never refreshed.
		/// </summary>
		[Test]
		public void CreatingAGuildNotifiesTheUpdatePump()
		{
			string create = Between(ReadSource(ServerPath),
				"private async Task CreateGuildAsync", "public void OnServerGuildInviteBroadcastReceived",
				"CreateGuildAsync");

			LogAssert.IsTrue(create.Contains("guildUpdateService.PersistAsync(newGuildID)"),
				"CreateGuildAsync writes the same update marker every other mutation writes");
		}

		/// <summary>
		/// The marker is freshness, not part of the purchase — so it has to be written once the
		/// guild is known to exist. Written earlier, a failure here would reach the catch block
		/// with <c>guildExists</c> still false and refund a fee that bought a real guild, which
		/// is the exact failure <c>GuildCreationFeeTests</c> exists to prevent.
		/// </summary>
		[Test]
		public void TheMarkerIsWrittenOnlyAfterTheGuildIsKnownToExist()
		{
			string create = Between(ReadSource(ServerPath),
				"private async Task CreateGuildAsync", "public void OnServerGuildInviteBroadcastReceived",
				"CreateGuildAsync");

			int exists = create.IndexOf("guildExists = true;", StringComparison.Ordinal);
			int marker = create.IndexOf("guildUpdateService.PersistAsync(newGuildID)", StringComparison.Ordinal);

			LogAssert.IsTrue(exists >= 0, "CreateGuildAsync still records that the guild exists");
			LogAssert.IsTrue(marker > exists,
				"the update marker is written after guildExists is set, so its failure cannot refund the fee");
		}
	}
}
