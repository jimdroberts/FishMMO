using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Who is on which team in each arena scene, and whether that arena is currently live.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The team primitive. Arenas cannot use parties for sides — a party is capped well below an
	/// 8v8 team — and cannot use factions, because two guildmates or two members of the same
	/// faction must be able to fight each other in an arena. So the alliance between two
	/// characters standing in an arena scene is decided here, before faction, party or guild are
	/// consulted: same team is ally, different team is enemy, and while the match is not live —
	/// gathering, counting down, or over — everybody in it is an ally so nobody can be hit before
	/// the start or after the end.
	/// </para>
	/// <para>
	/// Keyed by Unity scene handle, like <see cref="DungeonDifficultyRegistry"/>, because that is
	/// the one thing a character and the thing checking it both know. The server publishes from
	/// the match rows; the client publishes from the match state broadcast, so its predicted
	/// targeting agrees with the server. A handle is reused after its scene unloads, so an entry
	/// is removed when the scene is, not left behind.
	/// </para>
	/// </remarks>
	public static class ArenaTeamRegistry
	{
		private sealed class Entry
		{
			public readonly Dictionary<long, int> TeamByCharacter = new Dictionary<long, int>();
			public bool Live;
			public UnityEngine.Color[] TeamColors;
		}

		private static readonly Dictionary<int, Entry> entriesBySceneHandle = new Dictionary<int, Entry>();

		/// <summary>Records or replaces the roster for a scene.</summary>
		/// <param name="sceneHandle">Scene the arena runs in.</param>
		/// <param name="teamByCharacter">Character to team index.</param>
		/// <param name="live">Whether play is on.</param>
		public static void Publish(int sceneHandle, IReadOnlyDictionary<long, int> teamByCharacter, bool live)
		{
			Publish(sceneHandle, teamByCharacter, live, null);
		}

		/// <summary>Records or replaces the roster for a scene, with the colours its teams are drawn in.</summary>
		/// <param name="sceneHandle">Scene the arena runs in.</param>
		/// <param name="teamByCharacter">Character to team index.</param>
		/// <param name="live">Whether play is on.</param>
		/// <param name="teamColors">Colour per team, or null to keep the previous colours or the default palette.</param>
		public static void Publish(int sceneHandle, IReadOnlyDictionary<long, int> teamByCharacter, bool live, UnityEngine.Color[] teamColors)
		{
			if (sceneHandle == 0)
			{
				return;
			}

			if (!entriesBySceneHandle.TryGetValue(sceneHandle, out Entry entry))
			{
				entry = new Entry();
				entriesBySceneHandle[sceneHandle] = entry;
			}

			entry.TeamByCharacter.Clear();
			if (teamByCharacter != null)
			{
				foreach (KeyValuePair<long, int> kvp in teamByCharacter)
				{
					entry.TeamByCharacter[kvp.Key] = kvp.Value;
				}
			}
			entry.Live = live;
			if (teamColors != null)
			{
				entry.TeamColors = teamColors;
			}
		}

		/// <summary>
		/// The colour a character is drawn in because of the team they sit on, when they stand in
		/// an arena and hold a seat.
		/// </summary>
		/// <remarks>
		/// Consulted by the alliance colour, so nameplates and target frames show team colours
		/// inside an arena without knowing what an arena is. Spectators and strangers have no seat
		/// and fall through to the ordinary colouring.
		/// </remarks>
		public static bool TryGetTeamColor(ICharacter character, out UnityEngine.Color color)
		{
			color = default;
			if (character?.GameObject == null)
			{
				return false;
			}

			int handle = character.GameObject.scene.handle;
			if (!entriesBySceneHandle.TryGetValue(handle, out Entry entry) ||
				!entry.TeamByCharacter.TryGetValue(character.ID, out int team))
			{
				return false;
			}

			color = GetTeamColor(handle, team);
			return true;
		}

		/// <summary>The colour of a team in a scene: the published one, else the default palette's.</summary>
		public static UnityEngine.Color GetTeamColor(int sceneHandle, int team)
		{
			if (entriesBySceneHandle.TryGetValue(sceneHandle, out Entry entry) &&
				entry.TeamColors != null && team >= 0 && team < entry.TeamColors.Length)
			{
				return entry.TeamColors[team];
			}
			return ArenaTeamColors.Default(team);
		}

		/// <summary>Turns play on or off for a scene without changing its roster.</summary>
		public static void SetLive(int sceneHandle, bool live)
		{
			if (entriesBySceneHandle.TryGetValue(sceneHandle, out Entry entry))
			{
				entry.Live = live;
			}
		}

		/// <summary>Forgets a scene's arena.</summary>
		public static void Unpublish(int sceneHandle)
		{
			entriesBySceneHandle.Remove(sceneHandle);
		}

		/// <summary>Forgets everything. For server restarts and client disconnects.</summary>
		public static void Clear()
		{
			entriesBySceneHandle.Clear();
		}

		/// <summary>Whether a scene hosts an arena match this registry knows about.</summary>
		public static bool IsArena(int sceneHandle)
		{
			return entriesBySceneHandle.ContainsKey(sceneHandle);
		}

		/// <summary>Whether play is on in a scene.</summary>
		public static bool IsLive(int sceneHandle)
		{
			return entriesBySceneHandle.TryGetValue(sceneHandle, out Entry entry) && entry.Live;
		}

		/// <summary>The team a character is on in a scene, or -1.</summary>
		public static int GetTeam(int sceneHandle, long characterID)
		{
			if (entriesBySceneHandle.TryGetValue(sceneHandle, out Entry entry) &&
				entry.TeamByCharacter.TryGetValue(characterID, out int team))
			{
				return team;
			}
			return -1;
		}

		/// <summary>
		/// Decides the alliance between two characters when at least one stands in an arena.
		/// </summary>
		/// <returns>True when the arena decided it; false to fall through to faction rules.</returns>
		/// <remarks>
		/// <list type="bullet">
		/// <item>Neither in an arena scene: not decided here.</item>
		/// <item>In different scenes, or only one of them seated: they cannot fight; ally.</item>
		/// <item>Match not live: ally, so nothing lands before the start or after the end.</item>
		/// <item>Same team: ally. Different team: enemy.</item>
		/// </list>
		/// </remarks>
		public static bool TryResolveAlliance(ICharacter a, ICharacter b, out FactionAllianceLevel level)
		{
			level = FactionAllianceLevel.Neutral;
			if (a?.GameObject == null || b?.GameObject == null)
			{
				return false;
			}

			int handleA = a.GameObject.scene.handle;
			int handleB = b.GameObject.scene.handle;

			bool arenaA = entriesBySceneHandle.TryGetValue(handleA, out Entry entryA);
			bool arenaB = entriesBySceneHandle.ContainsKey(handleB);
			if (!arenaA && !arenaB)
			{
				return false;
			}

			if (!arenaA || handleA != handleB)
			{
				level = FactionAllianceLevel.Ally;
				return true;
			}

			if (!entryA.TeamByCharacter.TryGetValue(a.ID, out int teamA) ||
				!entryA.TeamByCharacter.TryGetValue(b.ID, out int teamB))
			{
				// A spectator, a pet, or a straggler with no seat: not a target.
				level = FactionAllianceLevel.Ally;
				return true;
			}

			if (!entryA.Live)
			{
				level = FactionAllianceLevel.Ally;
				return true;
			}

			level = teamA == teamB ? FactionAllianceLevel.Ally : FactionAllianceLevel.Enemy;
			return true;
		}
	}
}
