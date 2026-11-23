using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: FishMMO.Server.Implementation.ModuleServerSystem(typeof(FishMMO.Server.Implementation.World.SceneServer.MapFogSystem),
	FishMMO.Server.Implementation.ModuleServerSystemAttribute.SceneServer)]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Fog of war on the server (issue #241): what each account has explored, worked out from where its
	/// character walks, saved per account per scene (<c>account_map_fog</c>) and sent to the owner.
	/// </summary>
	/// <remarks>
	/// <para><b>Derived, never uploaded.</b> The server reveals the chunk under each character every
	/// <see cref="RevealIntervalSeconds"/> (the line between two samples, so a mount or a flight skips nothing), plus what
	/// <c>ExploreMapAction</c> and <c>/gm reveal map</c> grant. The client reveals the same way for an instant map, but
	/// nothing it says is stored, so a forged map is not possible and the scenes list cannot be faked.</para>
	/// <para><b>Account-wide.</b> One explored map per account per scene, shared by all its characters. A save merges into
	/// the stored row under a row lock (<see cref="IAccountMapFogService.MergeAsync"/>), and an account's saves run on one
	/// async lane, so two writers (a combat-logout body and the account's next character) converge on the union.</para>
	/// <para><b>When.</b> Loaded at spawn, then sent to the owner with the account's scene list; saved every
	/// <see cref="SaveIntervalSeconds"/> while new chunks are waiting, and as the character leaves this server.</para>
	/// </remarks>
	public class MapFogSystem : ServerBehaviour
	{
		/// <summary>Seconds between reveal samples; the client's own rate.</summary>
		public const float RevealIntervalSeconds = 0.25f;

		/// <summary>Seconds between saves of the chunks explored since the last one.</summary>
		public const float SaveIntervalSeconds = 30.0f;

		/// <summary>A sample further than this from the last is a jump (a teleport), not travel: only the arrival is explored.</summary>
		public const float MaxTravelPerSampleMetres = 120.0f;

		/// <summary>Seconds a connection waits between fog requests.</summary>
		public const float RequestCooldownSeconds = 0.5f;

		/// <summary>Longest scene name a request may name (the column's width).</summary>
		private const int MaxSceneNameLength = 64;

		/// <summary>One character's fog on this server: its account's map of the scene it stands in.</summary>
		private sealed class Track
		{
			public IPlayerCharacter Character;
			public NetworkConnection Connection;
			public string Account;
			public string SceneName;
			public FogChunkSet Fog;
			public FogChunkSet Unsaved;
			public bool Loaded;
			public bool HasLastPosition;
			public Vector3 LastPosition;
		}

		private readonly Dictionary<long, Track> tracks = new Dictionary<long, Track>();
		private readonly Dictionary<int, float> nextRequestTime = new Dictionary<int, float>();
		private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
		private readonly List<Track> scratch = new List<Track>();

		private ICharacterSystem<NetworkConnection, Scene> characterSystem;
		private float revealTimer;
		private float saveTimer;

		/// <summary>The running system, for the operator command. Null when none runs.</summary>
		internal static MapFogSystem Current { get; private set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.BehaviourRegistry.TryGet(out characterSystem) || characterSystem == null)
			{
				Log.Error("MapFogSystem", "InitializeOnce: ICharacterSystem not found; fog of war is not tracked on this server.");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!TryGetDbService(out IAccountMapFogService _))
			{
				Log.Warning("MapFogSystem", "IAccountMapFogService unavailable: explored maps are kept for the session only.");
			}

			characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			ExploreMapAction.OnServerExploreMap += ExploreMapAction_OnServerExploreMap;
			Server.NetworkWrapper.RegisterBroadcast<FogOfWarRequestBroadcast>(OnFogRequestReceived, true);
			Current = this;
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void OnDeinitialize()
		{
			if (characterSystem != null)
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				characterSystem = null;
			}
			ExploreMapAction.OnServerExploreMap -= ExploreMapAction_OnServerExploreMap;
			Server?.NetworkWrapper?.UnregisterBroadcast<FogOfWarRequestBroadcast>(OnFogRequestReceived);

			// One last write of whatever is waiting; the merge is a union, so a duplicate of one in flight is harmless.
			foreach (Track track in tracks.Values)
			{
				Save(track);
			}
			tracks.Clear();
			nextRequestTime.Clear();
			if (ReferenceEquals(Current, this))
			{
				Current = null;
			}
		}

		protected override void OnUpdate(float deltaTime)
		{
			while (mainThread.TryDequeue(out Action action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					Log.Error("MapFogSystem", $"Main-thread work failed: {ex}");
				}
			}

			revealTimer += deltaTime;
			if (revealTimer >= RevealIntervalSeconds)
			{
				revealTimer = 0.0f;
				foreach (Track track in tracks.Values)
				{
					Sample(track);
				}
			}

			saveTimer += deltaTime;
			if (saveTimer >= SaveIntervalSeconds)
			{
				saveTimer = 0.0f;
				foreach (Track track in tracks.Values)
				{
					Save(track);
				}
			}
		}

		// ── Tracking ────────────────────────────────────────────────────────────────────

		private void CharacterSystem_OnSpawnCharacter(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null || conn == null || string.IsNullOrEmpty(character.Account))
			{
				return;
			}

			string sceneName = character.CurrentSceneName();
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}

			float chunkSize = ChunkSizeFor(sceneName);
			Track track = new Track
			{
				Character = character,
				Connection = conn,
				Account = character.Account,
				SceneName = sceneName,
				Fog = new FogChunkSet(chunkSize),
				Unsaved = new FogChunkSet(chunkSize),
			};
			tracks[character.ID] = track;
			Sample(track);
			BeginLoad(track);
		}

		private void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || !tracks.TryGetValue(character.ID, out Track track))
			{
				return;
			}
			// Raised while the character still stands where it is leaving from, so this is the last sample of that scene.
			Sample(track);
			Save(track);
			tracks.Remove(character.ID);
			if (conn != null)
			{
				nextRequestTime.Remove(conn.ClientId);
			}
		}

		/// <summary>Explores where the character stands, and the way it came.</summary>
		private static void Sample(Track track)
		{
			if (track.Character?.Transform == null || !string.Equals(track.Character.CurrentSceneName(), track.SceneName, StringComparison.Ordinal))
			{
				return;
			}
			Vector3 position = track.Character.Transform.position;
			bool travelled = track.HasLastPosition &&
				(position - track.LastPosition).sqrMagnitude <= MaxTravelPerSampleMetres * MaxTravelPerSampleMetres;
			if (travelled)
			{
				track.Fog.AddLine(track.LastPosition, position);
				track.Unsaved.AddLine(track.LastPosition, position);
			}
			else
			{
				track.Fog.AddAt(position);
				track.Unsaved.AddAt(position);
			}
			track.LastPosition = position;
			track.HasLastPosition = true;
		}

		/// <summary>Server: <c>ExploreMapAction</c> ran for a character (an item, a region reward).</summary>
		private void ExploreMapAction_OnServerExploreMap(ICharacter character, Vector3 center, float radius)
		{
			if (character == null || !tracks.TryGetValue(character.ID, out Track track))
			{
				return;
			}
			if (float.IsPositiveInfinity(radius))
			{
				RevealEverything(track);
				return;
			}
			track.Fog.AddAround(center, radius);
			track.Unsaved.AddAround(center, radius);
		}

		/// <summary>Explores the whole of the scene <paramref name="character"/> stands in (<c>/gm reveal map</c>).</summary>
		internal bool RevealEverything(IPlayerCharacter character)
		{
			if (character == null || !tracks.TryGetValue(character.ID, out Track track))
			{
				return false;
			}
			RevealEverything(track);
			return true;
		}

		private void RevealEverything(Track track)
		{
			Rect area = MapRectFor(track.SceneName);
			if (area.width <= 0.0f || area.height <= 0.0f)
			{
				return;
			}
			track.Fog.AddArea(area);
			track.Unsaved.AddArea(area);
		}

		// ── Loading and saving ──────────────────────────────────────────────────────────

		private void BeginLoad(Track track)
		{
			if (!TryGetDbService(out IAccountMapFogService service))
			{
				track.Loaded = true;
				SendScene(track.Connection, track.SceneName, track.Fog);
				SendScenes(track, null);
				return;
			}

			string account = track.Account, sceneName = track.SceneName;
			long characterID = track.Character.ID;
			bool queued = TryEnqueueAsyncWork(async () =>
			{
				byte[] stored = null;
				IReadOnlyList<AccountMapFogSummary> scenes = null;
				try
				{
					DatabaseResult<byte[]> fog = await service.FetchAsync(account, sceneName);
					if (fog.IsSuccess)
					{
						stored = fog.Data;
					}
					else
					{
						await Log.Warning("MapFogSystem", $"Fog read failed for account '{account}', scene '{sceneName}': [{fog.ErrorCode}] {fog.ErrorMessage}");
					}
					DatabaseResult<IReadOnlyList<AccountMapFogSummary>> list = await service.FetchScenesAsync(account);
					if (list.IsSuccess)
					{
						scenes = list.Data;
					}
				}
				catch (Exception ex)
				{
					await Log.Error("MapFogSystem", $"Fog read threw for account '{account}': {ex}");
				}
				mainThread.Enqueue(() => ApplyLoad(characterID, track, stored, scenes));
			}, AccountLane(account));

			if (!queued)
			{
				track.Loaded = true;
				SendScene(track.Connection, track.SceneName, track.Fog);
			}
		}

		private void ApplyLoad(long characterID, Track track, byte[] stored, IReadOnlyList<AccountMapFogSummary> scenes)
		{
			// Left, or spawned again, while the read was out: the newer track has its own load.
			if (!tracks.TryGetValue(characterID, out Track current) || !ReferenceEquals(current, track))
			{
				return;
			}
			if (stored != null && FogCodec.TryDecode(stored, out FogChunkSet loaded))
			{
				track.Fog.UnionWith(loaded);
			}
			track.Loaded = true;
			SendScene(track.Connection, track.SceneName, track.Fog);
			SendScenes(track, scenes);
		}

		/// <summary>Writes the chunks explored since the last save, merged into the stored map.</summary>
		private void Save(Track track)
		{
			if (track == null || track.Unsaved.IsEmpty || !TryGetDbService(out IAccountMapFogService service))
			{
				return;
			}

			FogChunkSet pending = track.Unsaved;
			track.Unsaved = new FogChunkSet(pending.ChunkSize);
			string account = track.Account, sceneName = track.SceneName;

			bool queued = TryEnqueueAsyncWork(async () =>
			{
				DatabaseResult result;
				try
				{
					result = await service.MergeAsync(account, sceneName, stored => Merge(stored, pending));
				}
				catch (Exception ex)
				{
					result = DatabaseResult.Failure(DatabaseErrorCodes.DatabaseError, ex.Message);
				}
				if (!result.IsSuccess)
				{
					await Log.Warning("MapFogSystem", $"Fog save failed for account '{account}', scene '{sceneName}': [{result.ErrorCode}] {result.ErrorMessage}. Retried with the next save.");
					// Back into the waiting set, if the character is still here; otherwise it is lost (a few chunks, re-walked).
					mainThread.Enqueue(() => track.Unsaved.UnionWith(pending));
				}
			}, AccountLane(account));

			if (!queued)
			{
				track.Unsaved.UnionWith(pending);
			}
		}

		/// <summary>The stored map with <paramref name="pending"/> added: pure, so a retried transaction merges again safely.</summary>
		public static AccountMapFogMerge Merge(byte[] stored, FogChunkSet pending)
		{
			FogChunkSet merged = new FogChunkSet(pending.ChunkSize);
			if (stored != null && FogCodec.TryDecode(stored, out FogChunkSet existing))
			{
				merged.UnionWith(existing);
			}
			merged.UnionWith(pending);
			return new AccountMapFogMerge(FogCodec.Encode(merged), merged.Count);
		}

		/// <summary>One async lane per account, so an account's reads and merges never race each other on this server.</summary>
		private static long AccountLane(string account)
		{
			unchecked
			{
				long hash = 1469598103934665603L;
				foreach (char c in account.ToLowerInvariant())
				{
					hash = (hash ^ c) * 1099511628211L;
				}
				return hash == 0 ? 1 : hash;
			}
		}

		// ── Requests ────────────────────────────────────────────────────────────────────

		/// <summary>The owner's map is showing a scene the character is not in: answer with the account's fog for it.</summary>
		private void OnFogRequestReceived(NetworkConnection conn, FogOfWarRequestBroadcast msg, Channel channel)
		{
			if (conn == null || !conn.IsActive || string.IsNullOrWhiteSpace(msg.SceneName) || msg.SceneName.Length > MaxSceneNameLength)
			{
				return;
			}
			float now = Time.unscaledTime;
			if (nextRequestTime.TryGetValue(conn.ClientId, out float next) && now < next)
			{
				return;
			}
			nextRequestTime[conn.ClientId] = now + RequestCooldownSeconds;

			Track track = null;
			foreach (Track candidate in tracks.Values)
			{
				if (ReferenceEquals(candidate.Connection, conn))
				{
					track = candidate;
					break;
				}
			}
			if (track == null)
			{
				return;
			}

			string sceneName = msg.SceneName;
			if (string.Equals(sceneName, track.SceneName, StringComparison.Ordinal) && track.Loaded)
			{
				SendScene(conn, sceneName, track.Fog);
				return;
			}
			if (!TryGetDbService(out IAccountMapFogService service))
			{
				return;
			}

			string account = track.Account;
			TryEnqueueAsyncWork(async () =>
			{
				byte[] stored = null;
				try
				{
					DatabaseResult<byte[]> fog = await service.FetchAsync(account, sceneName);
					if (fog.IsSuccess)
					{
						stored = fog.Data;
					}
				}
				catch (Exception ex)
				{
					await Log.Error("MapFogSystem", $"Fog request read threw for account '{account}': {ex}");
					return;
				}
				mainThread.Enqueue(() =>
				{
					if (conn.IsActive)
					{
						conn.Broadcast(new FogOfWarSceneBroadcast { SceneName = sceneName, Data = stored ?? Array.Empty<byte>() }, true, Channel.Reliable);
					}
				});
			}, AccountLane(account));
		}

		private static void SendScene(NetworkConnection conn, string sceneName, FogChunkSet fog)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}
			conn.Broadcast(new FogOfWarSceneBroadcast { SceneName = sceneName, Data = FogCodec.Encode(fog) }, true, Channel.Reliable);
		}

		/// <summary>Sends the account's scene list, the current scene always among them.</summary>
		private static void SendScenes(Track track, IReadOnlyList<AccountMapFogSummary> stored)
		{
			if (track.Connection == null || !track.Connection.IsActive)
			{
				return;
			}
			var names = new List<string>();
			var counts = new List<int>();
			if (stored != null)
			{
				foreach (AccountMapFogSummary scene in stored)
				{
					if (!string.Equals(scene.SceneName, track.SceneName, StringComparison.Ordinal))
					{
						names.Add(scene.SceneName);
						counts.Add(scene.ExploredCount);
					}
				}
			}
			names.Add(track.SceneName);
			counts.Add(track.Fog.Count);
			track.Connection.Broadcast(new FogOfWarScenesBroadcast { SceneNames = names.ToArray(), ExploredCounts = counts.ToArray() }, true, Channel.Reliable);
		}

		// ── Scene data ──────────────────────────────────────────────────────────────────

		private WorldSceneDetails DetailsFor(string sceneName)
		{
			return Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
				sceneServerSystem.WorldSceneDetailsCache?.Scenes != null &&
				sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details)
				? details
				: null;
		}

		/// <summary>The scene's chunk size: its map definition's, else the default (the client uses the same rule).</summary>
		private float ChunkSizeFor(string sceneName)
		{
			WorldMapDefinition definition = DetailsFor(sceneName)?.MapDefinition;
			return definition != null && definition.FogChunkSize > 0.0f ? definition.FogChunkSize : FogChunkSet.DefaultChunkSize;
		}

		private Rect MapRectFor(string sceneName)
		{
			WorldSceneDetails details = DetailsFor(sceneName);
			return MapBoundsResolver.Resolve(details?.MapDefinition, details);
		}
	}
}
