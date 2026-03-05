# Core Collection Trackers

## Overview

This folder contains reusable queue/index tracker primitives used by server systems to implement low-GC, bounded TTL cleanup under heavy load.

These trackers are transport-agnostic and can be used from both Core and Implementation layers.

## Components

- `ExpiringKeyTracker<TKey>`
  - For debounce/rate-limit windows keyed by `TKey`.
  - Supports `TryBegin(...)` and bounded head-first `SweepExpired(...)`.

- `LastSeenCacheTracker<TKey, TValue>`
  - For caches where entries expire by inactivity (`last-seen`).
  - Supports `TryGetAndTouch(...)`, `Upsert(...)`, and bounded `SweepExpired(...)`.

- `TimedCache<TKey, TValue>`
  - Write-through TTL cache where entries expire after a fixed duration from storage.
  - Reads do NOT extend the lifetime (unlike `LastSeenCacheTracker`).
  - Supports `TryGet(...)`, `Set(...)`, `Invalidate(...)`, and bounded `SweepExpired(...)`.

- `ArrivalOrderTracker<TKey>`
  - For oldest-first stale-entry processing with O(1) `TrackIfMissing(...)` / `Remove(...)`.
  - Useful when first-seen ordering drives TTL purge semantics.

## Current Usage

- `ServerAuthenticator`
  - `ExpiringKeyTracker<string>` for kick/account/IP debounce maps.
  - `LastSeenCacheTracker<int, string>` for connection IP cache.

- `WorldSceneSystem`
  - `ExpiringKeyTracker<string>` for instance-lookup debounce.
  - `TimedCache<string, List<SceneData>>` for available-scene-instance result cache.
  - `TimedCache<long, (string, ushort)>` for scene-server address cache.

- `SceneChannelSystem`
  - `TimedCache<string, List<SceneData>>` for available-scene-instance result cache.
  - `TimedCache<long, (string, ushort)>` for scene-server address cache.

- `AccountManager`
  - `ArrivalOrderTracker<NetworkConnection>` for unauthenticated SRP/encryption stale-state sweeps.

## Design Goals

- Avoid full dictionary enumeration in hot-path maintenance loops.
- Bound per-sweep work via `maxScan` and `maxRemove` parameters.
- Keep update-loop cleanup predictable during large attack spikes.
- Nested structs reuse enclosing type parameters (no generic shadows).
