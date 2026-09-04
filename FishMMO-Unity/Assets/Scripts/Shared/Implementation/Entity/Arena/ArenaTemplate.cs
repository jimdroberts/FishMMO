using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// What is fought over in an arena.
	/// </summary>
	public enum ArenaMode : byte
	{
		/// <summary>Kills score. First team to the score limit, or the higher score at time, wins.</summary>
		TeamDeathmatch = 0,
		/// <summary>Carry the enemy flag home. Captures score.</summary>
		CaptureTheFlag = 1,
		/// <summary>Hold the point. Time held scores.</summary>
		KingOfTheHill = 2,
	}

	/// <summary>
	/// One way to play an arena: how many per team.
	/// </summary>
	/// <remarks>
	/// A list on the template rather than a fixed set, for the same reason dungeon difficulties
	/// are: arenas do not agree on their sizes. A duelling pit offers 1v1 only; a battleground
	/// offers 4v4 and 8v8. The index into this list is what the queue row records.
	/// </remarks>
	[Serializable]
	public class ArenaFormat
	{
		/// <summary>Seats per team.</summary>
		[Tooltip("Players per team.")]
		[Min(1)]
		public int TeamSize = 1;

		/// <summary>Optional display name; "2v2" is generated when empty.</summary>
		[Tooltip("Optional tab name. Empty generates e.g. 2v2 from the team count and size.")]
		public string Name;
	}

	/// <summary>
	/// Something that happens at one second of the start countdown, or at start or end.
	/// </summary>
	/// <remarks>
	/// The triggers run on each player's own client as ECA — a sound, particles, a camera shake,
	/// anything an action can do — so designers wire cues without code. Programmers get the same
	/// moments as C# events on <c>ArenaClientEvents</c>.
	/// </remarks>
	[Serializable]
	public class ArenaCountdownCue
	{
		/// <summary>Seconds left on the start timer when this fires. 0 is the start itself.</summary>
		[Tooltip("Seconds remaining on the start timer when this cue fires. 0 fires at the start itself.")]
		[Min(0)]
		public int SecondsRemaining;

		/// <summary>Triggers invoked on the local player at that moment.</summary>
		[Tooltip("Triggers invoked on the local player's character at that moment.")]
		public List<Trigger> Triggers = new List<Trigger>();
	}

	/// <summary>
	/// An arena: the scene it is fought in, its mode, its formats, and its match rules.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The arena counterpart of <see cref="DungeonTemplate"/>. Referenced by an arena board so
	/// the board's panel can describe the arena, and by the match row so the scene server that
	/// hosts the instance — not necessarily the one that formed the match — can read the rules.
	/// </para>
	/// <para>
	/// Team spawn points are ordinary <c>CharacterRespawnPosition</c> objects in the scene whose
	/// names start with the team's prefix, e.g. <c>Team1_A</c>. A team without any is spawned at
	/// the scene's respawn points at random, which is wrong for an arena but not fatal.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Arena", menuName = "FishMMO/World/Arena", order = 2)]
	public class ArenaTemplate : CachedScriptableObject<ArenaTemplate>, ICachedObject
	{
		[Tooltip("Display name shown on the arena board. Defaults to the asset name.")]
		public string DisplayName;

		[Tooltip("Scene name of the arena. Must be a world scene with respawn points.")]
		public string ArenaSceneName;

		[Tooltip("Description shown on the arena board.")]
		[TextArea(3, 6)]
		public string Description;

		[Tooltip("Artwork shown beside the description on the arena board.")]
		public AssetReferenceSprite IconReference;

		[Header("Match")]
		[Tooltip("What is fought over.")]
		public ArenaMode Mode = ArenaMode.TeamDeathmatch;

		[Tooltip("Teams in a match. Two for every mode shipped so far.")]
		[Min(2)]
		public int TeamCount = 2;

		[Tooltip("Formats offered, one tab each. Empty means a single 1v1.")]
		public List<ArenaFormat> Formats = new List<ArenaFormat>();

		[Tooltip("Match length in minutes once live. 0 means no time limit.")]
		[Min(0)]
		public int MatchMinutes = 10;

		[Tooltip("Team score that ends the match immediately. 0 means play to time.")]
		[Min(0)]
		public int ScoreLimit = 20;

		[Tooltip("Seconds a dead player waits before respawning at their team's spawn. 0 means no respawn: an elimination round.")]
		[Min(0)]
		public int RespawnSeconds = 8;

		[Header("Objectives")]
		[Tooltip("Capture the Flag: score a team earns per capture.")]
		[Min(1)]
		public int FlagCaptureScore = 1;

		[Tooltip("King of the Hill: interactions with a control point needed to take it. Another team's interaction resets progress.")]
		[Min(1)]
		public int ControlPointCaptureInteractions = 3;

		[Tooltip("King of the Hill: seconds a control point must be held to score one point for its owner.")]
		[Min(1)]
		public int ControlPointHoldSecondsPerPoint = 1;

		[Tooltip("King of the Hill: personal score credited to the player who completes a capture.")]
		[Min(0)]
		public int ControlPointCaptureScore = 5;

		[Header("Timing")]
		[Tooltip("Seconds of countdown once every player has arrived.")]
		[Min(1)]
		public int CountdownSeconds = 10;

		[Tooltip("Seconds to wait for every player to arrive before the match starts anyway, or is cancelled if a team is empty.")]
		[Min(10)]
		public int GatheringTimeoutSeconds = 90;

		[Tooltip("Seconds the results screen stays before everyone is returned to the world.")]
		[Min(3)]
		public int ResultsSeconds = 15;

		[Header("Spawns")]
		[Tooltip("Per team, the name prefix of the scene's respawn points that belong to that team.")]
		public List<string> TeamSpawnPrefixes = new List<string> { "Team1", "Team2" };

		[Header("Rating")]
		[Tooltip("PvP rank points awarded to every member of the winning team.")]
		[Min(0)]
		public int WinRankPoints = 10;

		[Tooltip("PvP rank points removed from every member of a losing team. Rank never drops below zero.")]
		[Min(0)]
		public int LossRankPoints = 5;

		[Header("Client cues")]
		[Tooltip("Triggers fired on each player's client at seconds of the start countdown. 0 fires at the start.")]
		public List<ArenaCountdownCue> CountdownCues = new List<ArenaCountdownCue>();

		[Tooltip("Triggers fired on each player's client when the match ends, before the results screen.")]
		public List<Trigger> MatchEndTriggers = new List<Trigger>();

		[NonSerialized]
		private Sprite loadedIcon;

		/// <summary>Artwork, once loaded on the client. Null on the server and before the load completes.</summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>Asset name.</summary>
		public string Name { get { return this.name; } }

		/// <summary>The display name, or the asset name when none is authored.</summary>
		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;

		/// <summary>How many formats the arena offers. Never below one.</summary>
		public int FormatCount => Formats == null || Formats.Count < 1 ? 1 : Formats.Count;

		/// <summary>Whether an index names one of the arena's formats.</summary>
		public bool IsValidFormat(int index)
		{
			if (Formats == null || Formats.Count < 1)
			{
				return index == 0;
			}
			return index >= 0 && index < Formats.Count;
		}

		/// <summary>Seats per team at a format index, or 1 when the arena declares no formats.</summary>
		public int GetTeamSize(int index)
		{
			if (Formats == null || Formats.Count < 1)
			{
				return 1;
			}
			if (index < 0 || index >= Formats.Count || Formats[index] == null)
			{
				return 1;
			}
			return Math.Max(1, Formats[index].TeamSize);
		}

		/// <summary>Tab name for a format: the authored one, or "NvN".</summary>
		public string GetFormatName(int index)
		{
			int size = GetTeamSize(index);
			if (Formats != null && index >= 0 && index < Formats.Count && Formats[index] != null &&
				!string.IsNullOrWhiteSpace(Formats[index].Name))
			{
				return Formats[index].Name;
			}
			return $"{size}v{size}";
		}

		/// <summary>Players in a full match at a format.</summary>
		public int GetMatchSize(int index)
		{
			return Math.Max(2, TeamCount) * GetTeamSize(index);
		}

		/// <summary>The spawn prefix for a team, or null when none is authored for it.</summary>
		public string GetTeamSpawnPrefix(int team)
		{
			if (TeamSpawnPrefixes == null || team < 0 || team >= TeamSpawnPrefixes.Count)
			{
				return null;
			}
			string prefix = TeamSpawnPrefixes[team];
			return string.IsNullOrWhiteSpace(prefix) ? null : prefix;
		}

		/// <summary>Human-readable mode name.</summary>
		public static string DescribeMode(ArenaMode mode)
		{
			switch (mode)
			{
				case ArenaMode.CaptureTheFlag: return "Capture the Flag";
				case ArenaMode.KingOfTheHill: return "King of the Hill";
				default: return "Team Deathmatch";
			}
		}

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(ArenaTemplate))
			{
				return;
			}

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
					{
						loadedIcon = handle.Result;
					}
				};
			}
#endif
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(ArenaTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
