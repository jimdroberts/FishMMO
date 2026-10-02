# FishNet 4.6.12 -> 4.7.3R upgrade ledger

Independent "nothing was missed" audit of branch `upgrade/fishnet-4.7.3r` (worktree /home/jim/Dev/FishMMO-Upgrade).
Audited 2026-10-01 (local time ~22:05). Read-only audit; this file is the only thing it wrote.

Commits audited:

| Commit | What | Audit result |
|---|---|---|
| 672cbe264 | Vendor FishNet 4.7.3R | Byte-identical (CR-insensitive) to upstream `7c4a644` (4.7.3R `dad140e` + FUNDING) except: `Demos/`, `Upgrading/` (git-ignored), `Runtime/Transporting/Transports/Synapse` (left out on purpose), `Plugins/WebTransport` (FishMMO's own, kept and byte-identical to dev). |
| 84ae68547 | Apply FishMMO's 20 upstream PR branches | Verified by construction: a scratch clone merged all 20 branches from /home/jim/Dev/FishNet-PRs onto `7c4a644` with no conflicts, and that tree is identical (CR-insensitive) to 84ae68547's `Assets/Plugins/FishNet`. Branch SHAs used: see section 1.4. |
| efe70474a | Re-apply 4 FishMMO-only edits | Diff vs 84ae68547 is exactly PredictionManager, DefaultSceneProcessor, NetworkObject.Prediction (SetStateForwarding), ByteArrayPool (exact alloc). |
| 7a7cf6ea4 | (landed during the audit) PR branch `fix/sceneid-formerly-serialized` @ cf1394c2 | One line, identical to the branch diff: `[FormerlySerializedAs("<SceneId>k__BackingField")]` on `NetworkObject.SceneId` (NetworkObject.Serialized.cs:59). |
| 076b0a99b | (landed during the audit) settings/YAML migration | Defines, `_maximumClientPacketSize`, `_reduceReconcilesWithFramerate`, NT packing YAML, SceneId YAML, `_localReconcileCorrectionType`, TransportTrafficPinsTests rewrite. Checked against section 2. |

Uncommitted work in the checkout at audit time (other agents, in flight): ~50 files - FishMMO delta serializers,
KCCPlatform/Region, WeatherExposureTick, CharacterAttributeController, LagCompensationWiringTool, RegionSimHarness,
~25 test files, and a Unity re-serialization of 13 NetworkObject prefabs (new NetworkObject fields + new
`<AssetPathHash>k__BackingField` values). No uncommitted change touches `Assets/Plugins/FishNet`.

Line numbers below for FishNet files are in the branch tree at HEAD (076b0a99b), path prefix
`FishMMO-Unity/Assets/Plugins/FishNet/`. Line numbers for the 4.6.12 side are hunk headers of
`diff -u -w --strip-trailing-cr pristine-4.6.12 dev`.

Fate codes: **A** carried by a PR branch (code confirmed present in the 84ae68547 tree), **B** re-applied in
efe70474a, **C** superseded by upstream 4.7.3R, **D** intentionally dropped, **E** MISSING, **F** formatting only.

---

## 1. FishMMO's 4.6.12 FishNet vs pristine 4.6.12

Method: `git archive dev FishMMO-Unity/Assets/Plugins/FishNet` vs `git archive 4.6.12 Assets/FishNet` (FishNet-PRs),
every file, `Demos/` and `Upgrading/` excluded (git-ignored on both sides of FishMMO), `Plugins/WebTransport` excluded
(not FishNet; see section 3.4).

- 528 files differ byte-wise. 504 differ only in line endings (FishMMO's `* text=auto` stores LF; upstream ships CRLF).
  **F**, nothing to carry. 1 differs only in whitespace (`package.json`, item 1.2). 23 have real hunks (72 hunks, 1.1).
- Files only in FishMMO: `Runtime/Observing/IObserverSendFilter.cs(.meta)` (item 1.2), `Runtime/Object/Synchronizing/SyncHashset.cs(.meta)` (item 1.2), `Plugins.meta` (folder of WebTransport).
- Files only in pristine: `Runtime/Object/Synchronizing/SyncHashSet.cs(.meta)` (the other half of the case rename).
- Every file that carries a `FISHMMO` tag in the dev copy (13 files) is covered below.

### 1.1 Hunk-by-hunk

#### CodeGenerating/Extension/MethodDefinitionExtensions.cs (issue #229 weaver crash)
| # | 4.6.12 hunk | What it did | Fate |
|---|---|---|---|
| 1 | @@-111 | `CreateParameters`: import the parameter type into this module, seal the attribute list at row 0, clone copied attributes with an imported ctor (Cecil Param-row collision -> foreign `[IsReadOnly]` -> ILPP abort). | **C** - 4.7.3R `ParameterDefinition.CloneImported` (CodeGenerating/Helpers/Extension/ParameterDefinitionExtensions.cs:27) used at MethodDefinitionExtensions.cs:116; attributes cloned via CustomAttributeExtensions.CloneImported. |
| 2 | @@-206 | `CreateCopy`: use the imported return type. | **C** - MethodDefinitionExtensions.cs:197 (`TypeReference returnType = session.ImportReference(...)`). |

#### CodeGenerating/Extension/TypeDefinitionExtensions.cs
| 3 | @@-156 | `GetOrCreateMethodDefinition`: clone parameters of a foreign-module template instead of re-parenting. | **C** - TypeDefinitionExtensions.cs:162 clones every template parameter with `CloneImported`. |

#### CodeGenerating/Helpers/GeneralHelper.cs
| 4 | @@-896 | `CreateParameter(MethodDefinition, ParameterDefinition)`: use imported type + `SealCustomAttributes` helper. | **D** - upstream left this overload unchanged (GeneralHelper.cs:897) but it has no callers in 4.7.3R codegen (grep of all `CreateParameter(` calls); the crash path (CreateParameters) is fixed upstream. |
| 5 | @@-918 | `SealCustomAttributes` in `CreateParameter(MethodDefinition, TypeReference, ...)`. | **D** - defensive only; all callers pass module-imported references. Residual note: upstream `CloneImported` does not seal an attribute-less parameter, so the lazy lookup now goes to the *same* module's image (cannot throw "declared in another module"; could at worst copy a stale same-module attribute). Verify with one Unity weave of FishMMO.Shared (memory: fishnet-ilpp-isreadonly-trap). |

#### Runtime/Editor/PrefabCollectionGenerator/Generator.cs
| 6 | @@-251 | Only `SetDirty`/`SaveAssets` when dirtied. | **A** - fix/prefab-generator-build-guard (#1092), Generator.cs:248. |
| 7 | @@-576 | Skip retry refresh while `BuildPipeline.isBuildingPlayer`. | **A** - same PR, Generator.cs:583. |
| 8 | @@-597 | Skip iteration while building a player. | **A** - same PR, Generator.cs:610. |

#### Runtime/Generated/Component/NetworkTransform/NetworkTransform.cs
| 9 | @@-368 | `_positionMultiplier` field (+default/guard) and 24-bit `WritePackedPosition`/`ReadPackedPosition`, `PACKED_POSITION_MAX`. Unconditional. | **A** - feat/networktransform-position-scale (PR 18, unsubmitted) behind `FISHNET_NETWORKTRANSFORM_POSITION_PACKING`: enum `PositionPackingBits`, `_positionPackingBits` (NetworkTransform.cs:397, **default Sixteen**), `_positionCompressionScale` (:403), `POSITION_PACKING_24_MAX_VALUE` (:1233), writer :1244 (rounds AwayFromZero, same as fork), reader :1262 (sign-extends `(v<<8)>>8`, same result). Serialized field RENAMED - see 3.2; YAML migrated in 076b0a99b (10 prefabs, `_positionPackingBits: 1`, `_positionCompressionScale: 100`). |
| 10 | @@-415 | `ExcludeOwnerFromUnbufferedObserversRpcs => !_clientAuthoritative && !_sendToOwner` (public override). | **A** - feat/networktransform-skip-owner-discarded (PR 19), NetworkTransform.cs:461. Now `internal override` and `=> _clientAuthoritative \|\| !_sendToOwner` (also skips a client-authoritative owner, which discards these too). |
| 11 | @@-1170 | `positionMultiplier`/`positionMaxValue` locals; scale keeps stock 100/Int16. | **A** - PR 18 (same locals, `#if` + fallback to stock values). |
| 12-14 | @@-1183/-1200/-1217 | X/Y/Z write sites use position scale + packed writer. | **A** - PR 18. |
| 15 | @@-1352 | X/Y/Z read sites use `ReadPackedPosition`. | **A** - PR 18. |

#### Runtime/Generated/Component/Prediction/NetworkColliderBase.cs
| 16 | @@-62 | Public `QueryLayers` get/set over protected `Layers`. | **A** - feat/networkcollider-layers-accessor (#1093) as `GetLayers()`/`SetLayers()` (NetworkColliderBase.cs:68/:73). API differs: FishMMO callers must move (section 2, A1). |

#### Runtime/Generated/Component/Prediction/RigidbodyPauser.cs, RigidbodyState.cs; Runtime/Object/Prediction/PredictionRigidbody.cs, PredictionRigidbody2D.cs
| 17-20 | RigidbodyPauser @@-49/-98/-379/-416 | `velocity` -> `linearVelocity` (Unity 6 obsoletion). | **C** - upstream `#if UNITY_6000_1_OR_NEWER` at RigidbodyPauser.cs:68/138/390/440. |
| 21-26 | RigidbodyState @@-20/-29/-49/-59/-152/-172 | same | **C** - RigidbodyState.cs:26/56/185/217 (+ 2D twins). |
| 27 | PredictionRigidbody @@-311 | same | **C** - PredictionRigidbody.cs:360. |
| 28 | PredictionRigidbody2D @@-237 | same | **C** - PredictionRigidbody2D.cs:286. |

#### Runtime/Managing/Prediction/PredictionManager.cs
| 29 | @@-421 | `ReconcileToStates`: null-guard `_networkManager` (Addressables 2.7.4 NRE). | **B** - efe70474a, PredictionManager.cs:551. |
| 30 | @@-432 | null-guard `TimeManager`. | **B** - PredictionManager.cs:559. |

#### Runtime/Managing/Scened/DefaultSceneProcessor.cs
| 31 | @@-68 | Log instead of NRE when `LoadSceneAsync` returns null. | **B** - efe70474a, DefaultSceneProcessor.cs:72. |

#### Runtime/Managing/Scened/SceneManager.cs
| 32 | @@-236 | Prefer an existing `SceneProcessorBase` on the object before adding `DefaultSceneProcessor`. | **A** - fix/scene-processor-selection (#1089), SceneManager.cs:424. |

#### Runtime/Object/NetworkBehaviour/NetworkBehaviour.cs
| 33 | @@-211 | `ResetState`: `_observersRpcSettled = true`. | **A** - feat/observer-send-filter (PR 17), NetworkBehaviour.cs:225. |
| 34 | @@-222 | `TryAddNetworkObject`: discard `_addedNetworkObject` outside own hierarchy (PR #212); `SyncNetworkObjectCache()`. | **A** - fix/networkbehaviour-foreign-owner-cache (#1090), NetworkBehaviour.cs:242-245. Behaviour difference: a foreign `_networkObjectCache` is **nulled** (runtime init re-sets it, NetworkBehaviour.cs:173) instead of repaired to the discovered owner, and a null cache is no longer filled in edit mode. NetworkBehaviourOwnerRebindTests is being rewritten for this in flight. |
| 35 | @@-254 | Local functions `SyncNetworkObjectCache`, `IsInOwnHierarchy`. | **A** - `IsSelfOrParent` NetworkBehaviour.cs:282; `SyncNetworkObjectCache` dropped with the repair (see 34). |

#### Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs
| 36 | @@-194 | `_reconcileSendSequence` counter. | **D** - PR 01 (feat/delta-prediction-beta, #1095) replaces sequence numbering with "delta against last FULL reconcile + 7-bit id" (no counter needed). |
| 37 | @@-422 | Enable delta reconcile unconditionally (`#if DO_NOT_USE` removed) + stamp sequence before `WriteDeltaReconcile`. | **A** for the delta write - PR 01 `#if FISHNET_DELTA_PREDICTION` (NetworkBehaviour.Prediction.cs:441) -> `Reconcile_WriteDelta` (:497); `GetDeltaSerializeOption` given a real body (:921). **D** for the stamp (design replaced). Needs the define on every target (done in 076b0a99b). |
| 38 | @@-748 | `GetDefaultedLastReplicateTick` seeds from `Owner.PacketTick` (stamina-regen bug). | **C** - 4.7.3R `ReplicateDefaultData` uses `Owner.ReplicateTick` / `Owner.PacketTick.Value()` on the server (NetworkBehaviour.Prediction.cs:776); `GetDefaultedLastReplicateTick` no longer exists (source-scan test affected, 3.1). |
| 39 | @@-937 | `Replicate_SendAuthoritative`: self-contained `WriteDeltaReplicate`. | **A** - PR 01, NetworkBehaviour.Prediction.cs:977. |
| 40 | @@-1012 | `Replicate_Reader`: `ReadDeltaReplicate<T>(tick)`. | **A** - PR 01, :1060. |
| 41 | @@-1102 | `Replicate_SendNonAuthoritative`: `WriteDeltaReplicate`. | **A** - PR 01, :1150. |
| 42 | @@-1505 | `Reconcile_Reader`: delta read (typo fix), `ReconcileDeltaGuard` rejection, advance baseline before the old-state return. | **A** (redesigned) - `Reconcile_Reader_Remote` -> `Reconcile_ReadDelta` (:1538/:1564): a delta is used only with the full reconcile it names (id + tick window); the full baseline is stored inside `Reconcile_ReadDelta` before the old-state return, so the "baseline advances even when old" property holds. Note the weaver-visible rename `Reconcile_Reader` -> `Reconcile_Reader_Remote` (upstream). |
| 43 | @@-1549 | Public types `ReconcileSequenceStamper<T>` and `ReconcileDeltaGuard`. | **D** - not in PR 01 by design. FishMMO users must go (section 2, D1). |

#### Runtime/Object/NetworkBehaviour/NetworkBehaviour.RPCs.cs
| 44 | @@-1 | BOM removed. | **F** |
| 45 | @@-76 | `_observersRpcSettled` latch field. | **A** - PR 17, NetworkBehaviour.RPCs.cs:89. |
| 46 | @@-357 | `SendObserversRpc`: owner exclusion when `ExcludeOwnerFromUnbufferedObserversRpcs`; per-observer `IObserverSendFilter` for unreliable unbuffered sends; first unreliable after a reliable goes to everyone. | **A** - owner part PR 19 (:378, operand order changed: `... && Owner.IsValid && ExcludeOwner...`); filter part PR 17 `AddFilteredObserversToNetworkConnectionCache` (:382, body :507). Same semantics; code moved into a helper (source-scan tests affected, 3.1). |
| 47 | @@-463 | `public virtual bool ExcludeOwnerFromUnbufferedObserversRpcs => false`. | **A** - PR 19 (:490), now `internal virtual`. |

#### Runtime/Object/NetworkObject/NetworkObject.Observers.cs
| 48 | @@-1 | BOM removed. | **F** |
| 49 | @@-39 | `[NonSerialized] public IObserverSendFilter ObserverSendFilter`. | **A** - PR 17, NetworkObject.Observers.cs:47. |

#### Runtime/Object/NetworkObject/NetworkObject.Prediction.cs
| 50 | @@-1 | BOM removed. | **F** |
| 51 | @@-102 | `SetStateForwarding(bool)` runtime switch (+ ObserverAddedTick stamp). | **B** - efe70474a, NetworkObject.Prediction.cs:157. Still forces a full reconcile under PR 01 (`ObserverAddedTick >= _lastFullReconcileWriteTick`). |

#### Runtime/Serializing/Reader.cs / Writer.cs
| 52 | Reader @@-1275 | ZigZag decode rewrite `(long)(v>>1) ^ -((long)v&1)`, returns `long`; tab re-indent. | **C** - identical in 4.7.3R (Reader.cs:1311). Re-indent **F**. |
| 53 | Writer @@-1087 | ZigZag encode takes `long`. | **C** - Writer.cs:1130. |

#### Runtime/Serializing/Reader.Delta.cs
| 54 | @@-1 | BOM + usings for the replicate reader. | **A** (usings, PR 01) / **F** (BOM). |
| 55 | @@-393 | Self-contained `ReadDeltaReplicate<T>(uint tick)`. | **A** - PR 01, Reader.Delta.cs:421 (+ falls back to `ReadReplicate` without delta serializers). |
| 56 | @@-455 | "Read delta method not found" Error -> Warning (#159, 14,442 lines/4 min). | **D** - PR 01's `Writer.HasDeltaSerializers<T>()` (Writer.Delta.cs:783) sends a prediction type without both delta serializers in full, so the per-tick trigger cannot fire on the prediction path. The `LogError` remains (Reader.Delta.cs:465) for an explicit/nested `ReadDelta` of an unregistered type; `DeltaSerializerRegistrationTests` is the guard. |

#### Runtime/Serializing/Writer.Delta.cs
| 57 | @@-1 | BOM added. | **F** |
| 58 | @@-9 | `using GameKit.Dependencies.Utilities.Types` (RingBuffer). | **A** - PR 01. |
| 59 | @@-185 | `WriteDeltaSingle`: round (AwayFromZero) + clamp instead of floor. | **A** - PR 01 (Writer.Delta.cs:197-204), `Math.Round` default (ToEven) without clamps, and also applied to double/decimal writers. Only exact .5 midpoints differ. |
| 60-61 | @@-524/-543 | `WriteDeltaTransformProperties` rewinds `Length` with `Position`. | **A** - PR 01, :535. |
| 62 | @@-568 | `IsQuaternionChanged` threshold 0.0025 -> 0.0001. | **A** - PR 01, :585 `precision * 0.5f` = 0.00005 at the default `QUATERNION_PRECISION` 0.0001 (Writer.Delta.cs:55); finer than the fork. |
| 63-64 | @@-592/-606 | Vector2 delta: Length rewind. | **A** - PR 01, :609. |
| 65-66 | @@-615/-631 | Vector3 delta: Length rewind. | **A** - PR 01, :635. |
| 67 | @@-752 | Self-contained `WriteDeltaReplicate` (RingBuffer + BasicQueue overloads). | **A** - PR 01, :820/:845 (+ fallback to `WriteReplicate`). |
| 68 | @@-819 | "Write delta method not found" Error -> Warning. | **D** - as 56 (Writer.Delta.cs:888 still LogError). |

#### Runtime/Utility/Performance/ByteArrayPool.cs
| 69 | @@-1 | BOM removed; "thread-safe" doc line. | **F** / **A** (PR 10 documents the lock field). |
| 70 | @@-14 | `_lock` + locked dequeue; allocate exactly `minimumLength` (was double). | **A** lock - fix/bytearraypool-thread-safety (#1091), ByteArrayPool.cs:27. **B** exact alloc - efe70474a, ByteArrayPool.cs:33. |
| 71 | @@-37 | Locked `Store`. | **A** - ByteArrayPool.cs:47. |
| 72 | @@-47 | Trailing newline. | **F** |

### 1.2 File-level items
| Item | Fate |
|---|---|
| `Runtime/Observing/IObserverSendFilter.cs` (new, GUID 83ac2fc6...) | **A** - PR 17, same path and same GUID (Runtime/Observing/IObserverSendFilter.cs:28); remarks now document the per-new-observer first-send rule left to the filter. |
| `SyncHashset.cs` -> `SyncHashSet.cs` (content identical to pristine 4.6.12; GUID b8627bf3... both sides) | **C** - upstream spelling, tracked as `SyncHashSet.cs` on the branch. Case-only rename hazard in Jim's tree: section 4.2. |
| `package.json` ("https:// x" -> "https://x" URL repair; whitespace under -w) | **C** - 4.7.3R package.json has the corrected URLs (version 4.7.3). |
| 504 files differing only in CRLF/LF | **F** - git `text=auto` normalisation; no content. |

### 1.3 Uncommitted 2026-10-02 fixes in the OLD tree (/home/jim/Dev/FishMMO-Dev working copy)
| File / hunk | What it did | Fate |
|---|---|---|
| ServerManager.cs @@-757 | Kick `UnusualActivity` when split count is outside 1..`GetMaximumClientSplitMessageCount()`, before the reader is sized; pass connection id + byte cap to the shared reader. | **A** kick - fix/split-message-count-clamp (#1088), ServerManager.cs:771 (before `connection.TryGetSplitReader`). **C** connection key - 4.7.3R has per-connection readers (Connection/NetworkConnection.Buffer.cs:47). |
| SplitReader.cs @@-21 (`_connectionId`), @@-97 (`Reset` key) | Key the single shared reader on connection id (cross-connection splicing). | **C** - per-connection readers. |
| SplitReader.cs @@-43 (`Write` cap) | Cap the reservation at `maximumBufferSize`. | **A** - #1088, SplitReader.cs:52 (`isSenderClient && estimatedBufferSize > maximumClientBytes`). |
| TransportManager.cs @@-626 | Client refuses to send a split larger than `MAXIMUM_CLIENT_SPLIT_BYTES`. | **C** - 4.7.3R refuses a client split above `_maximumClientPacketSize` (TransportManager.cs:613). |
| TransportManager.cs @@-642 | `MAXIMUM_CLIENT_SPLIT_BYTES = 64 KiB` (+ derivation) and `GetMaximumClientSplitMessageCount()`. | **A** - #1088 `GetMaximumClientSplitMessageCount()` (TransportManager.cs:654, uses `_maximumClientPacketSize` / `_maximumSplitPacketSegmentLength`). The constant becomes the serialized `_maximumClientPacketSize: 65536` in Login/World/Scene server + ClientPostboot (076b0a99b; upstream default 20480). |
| GenericReader.cs / GenericWriter.cs | Negate the inverted `HasCustomSerializer` test. | **A** - PR 01 commit 03ab0f1f (GenericReader.cs:34, GenericWriter.cs:32). |
| QuaternionDeltaPrecisionCompression.cs (2 hunks) | Omit the largest-magnitude component, not the most-changed. Wire change. | **A** - PR 01 commit 3f4d2984 (QuaternionDeltaPrecisionCompression.cs:52). |

### 1.4 PR branches applied (all 21 verified present; 13 have no 4.6.12 counterpart - they fix 4.7.3R/main defects)
`feat/delta-prediction-beta 6f766e59` (#1095) · `feat/networkcollider-layers-accessor 4a3c78aa` (#1093) ·
`feat/networktransform-position-scale 329cd343` · `feat/networktransform-skip-owner-discarded 203496b6` ·
`feat/observer-send-filter 47c9a1c8` · `fix/bytearraypool-thread-safety 92492c53` (#1091) ·
`fix/client-attribute-check a3c972ba` (#1084) · `fix/fallback-channel-bounds 2e21a742` (#1087) ·
`fix/lowest-mtu-across-channels 7600affa` · `fix/mtu-reserve-bundles c9531ec6` ·
`fix/networkbehaviour-foreign-owner-cache c0761ee7` (#1090) · `fix/networkcollider-per-object-bookkeeping f32c34fa` (#1085) ·
`fix/networkcollider2d-layer-fallback d182b9e6` · `fix/observers-active-last-removal 4317b7d7` (#1086) ·
`fix/prefab-generator-build-guard 6f06c6e2` (#1092) · `fix/replicate-start-delay fefbe66f` (#1094) ·
`fix/scene-processor-selection b261ce8b` (#1089) · `fix/sceneid-instantiated-copies 4af394e9` ·
`fix/scenelookupdata-non-build-scenes 356a3841` (#1083) · `fix/split-message-count-clamp 30bb3ffa` (#1088) ·
`fix/sceneid-formerly-serialized cf1394c2` (7a7cf6ea4).

### 1.5 Fate totals
| Fate | 4.6.12 hunks (72) + file items (2) | Old-tree uncommitted hunks (10) |
|---|---|---|
| A carried by PR | 40 (incl. IObserverSendFilter.cs) | 7 |
| B re-applied (efe70474a) | 4 (+ exact-alloc half of #70) | 0 |
| C superseded upstream | 20 (incl. SyncHashSet rename, package.json) | 3 |
| D intentionally dropped | 6 | 0 |
| E MISSING | **0** | **0** |
| F formatting | 5 (+504 line-ending-only files) | 0 |

Semantic differences inside A/C/D that someone should consciously accept (none is a lost fix):
NetworkBehaviour cache nulled not repaired (#34); NT owner skip widened to client-authoritative (#10);
NT 24-bit packing now opt-in per component, default Sixteen (#9); delta "not found" stays an Error off the
prediction path (#56/#68); rounding ToEven vs AwayFromZero (#59); weaver attribute seal dropped (#4/#5).

---

## 2. FishMMO-side work 4.7.3R requires (checklist; owners act, this audit only lists)

Sources: memory fishnet-473-upgrade-assessment, fishnet-upstream-prs, fishmmo-delta-serializers,
fishnet-position-compression, fishmmo-observer-streaming, fishnet-networkcollider-layers-trap,
fishmmo-network-object-binding-guard, fishmmo-nt-distance-lod-tuning, fishmmo-prediction-audit-2026-08-28.
The 2026-09-27 reports (`report-prediction/-wire/-platform/-compile.md`) no longer exist (scratchpad wiped);
their must-do items survive in fishnet-473-upgrade-assessment and are all listed here.

Status: DONE = in a commit on the branch; FLIGHT = uncommitted change present at audit time (unverified);
OPEN = nothing seen.

| ID | Area | Item | Status |
|---|---|---|---|
| D1 | delta | Remove `ReconcileSequenceStamper<T>.Stamp` registrations (CharacterReconcileDataDeltaSerializer.cs:404, KCCPlatformDeltaSerializers.cs:134 at dev) and `ReconcileDeltaGuard.RejectLastRead()` (…:679, …:291). | FLIGHT (no code refs left; stale doc comments at KCCPlatform.cs:107, CharacterPredictionController.cs:500) |
| D2 | delta | Delta serializer contract under PR 01: consume the same bytes whatever the baseline; never mutate the baseline (reused until the next full); FullSerialize now goes through the regular serializer, so the per-serializer mode byte and `CharacterReconcileData.Sequence` are redundant. | FLIGHT (CharacterReconcileData*.cs, KCC*DeltaSerializers.cs modified) |
| D3 | delta/assets | `FISHNET_DELTA_PREDICTION` on every build target incl. `Server` (PR 01 menu uses all-targets). | DONE 076b0a99b (ProjectSettings scriptingDefineSymbols, 20 targets) |
| D4 | delta/assets | ClientPostboot PredictionManager `_reduceReconcilesWithFramerate: 0` (new 50 fps throttle drops reconciles; default true, `_minimumClientReconcileFramerate` 50). | DONE 076b0a99b (ClientPostboot.unity:392) |
| D5 | delta | WeatherExposureTick: `ClientStateTick`/`ServerStateTick` are cleared after every reconcile in 4.7. | FLIGHT (WeatherExposureTick.cs:190 comment + tests) |
| D6 | delta/tests | Tests built on the dropped hooks: ReconcileDeltaChainTests, KCCPlatformDeltaChainTests, PredictionAuditRegressionTests (stamper anchor + `ReconcileDeltaGuard`), ReplicateDeltaPacketTests (reflects `WriteDeltaReplicate`/`ReadDeltaReplicate` overloads), PredictionBandwidthBenchmarkTests (15.0 B vs 12.3 B per reconcile), DeltaSerializerStreamAlignmentTests. | FLIGHT (all modified) |
| D7 | delta/tests | Port Jim's uncommitted pins: RotationPrecisionTests.DeltaQuaternion_SmallTurn_SurvivesTheDeltaCodec, DeltaSerializerRegistrationTests.CustomDeltaSerializer_SurvivesARegularSerializerRegisteredAfterIt (both valid vs PR 01). | FLIGHT (both modified, comments adapted) |
| D8 | delta | KCCPredictionDeltaSerializers (dev :663) uses `WriteDeltaQuaternion` for motor rotation: now fixed by PR 01's quaternion codec + threshold; wire change, ship client+server together. | FLIGHT (file modified) |
| A1 | api | `NetworkColliderBase.QueryLayers` -> `GetLayers()`/`SetLayers()`: KCCPlatform.cs:267-272, PlatformCatchUpTests.cs:191/199-213 (+ its source anchor). | FLIGHT (both switched) |
| A2 | api | NetworkCollider/Trigger events are `(Collider, uint)` in 4.7.3R: KCCPlatform, Region.cs handlers. | FLIGHT (both modified) |
| A3 | api | `ExcludeOwnerFromUnbufferedObserversRpcs` is internal (PR 19): ObserverSendShapingTests.cs:64-93 used it publicly; also expectation flips for a client-authoritative NT. | FLIGHT (now via reflection) |
| A4 | api/tests | NetworkBehaviourOwnerRebindTests expect cache repair; PR 09 nulls it. | FLIGHT (rewritten to `IsNull`) |
| A5 | api | `NetworkObject.SetStateForwarding` kept (B); LateJoinerReplayTests anchor still valid. | DONE efe70474a |
| A6 | api | `IObserverSendFilter` contract unchanged (namespace, signature) - ObserverStreamingEntry compiles as is; new-observer first-send rule stays FishMMO's (ObserverInterestBoundaryTests). | nothing to do |
| A7 | api | SceneLookupData/Addressable scenes: fixed by PR 02 + PR 03 + DefaultSceneProcessor guard; SceneId on instantiated copies by PR 21 (the 4.7.3R Awake blocker). | DONE (FishNet side) |
| S1 | assets | NT prefabs: `_positionMultiplier: 100` -> `_positionPackingBits: 1` + `_positionCompressionScale: 100` on all 10 NetworkTransforms (only NT instances in committed and ignored assets); `FISHNET_NETWORKTRANSFORM_POSITION_PACKING` on all targets. | DONE 076b0a99b |
| S2 | assets | TransportManager `_maximumClientPacketSize: 65536` in LoginServer/WorldServer/SceneServer/ClientPostboot. | DONE 076b0a99b |
| S3 | assets | NetworkObject `<SceneId>k__BackingField` -> `SceneId` (renamed in 4.6.16 without FormerlySerializedAs): 32 objects + 4 prefab overrides, plus PR `fix/sceneid-formerly-serialized` for git-ignored scenes. | DONE 7a7cf6ea4 + 076b0a99b |
| S4 | assets | New NetworkObject `_localReconcileCorrectionType` (default TransformAndVelocities) -> Disabled on Human/Elf/Orc. | DONE 076b0a99b |
| S5 | assets | Unity re-serialization of NetworkObject prefabs (new fields `_useLevelOfDetail`, `_useRootLevelOfDetail`, `_localLevelOfDetailCalculationType`, `_initializedTimestamp`, NT `_useScaledTime`; new `<AssetPathHash>` values). Commit as one unit; regenerate DefaultPrefabObjects (section 4.6). | FLIGHT (13 prefabs modified) |
| S6 | assets/tests | NetworkTransformPrecisionTests: `_positionMultiplier` reflection (dev :56) and YAML regexes (:217, :376, :408) -> new field names. | FLIGHT (modified) |
| S7 | assets/tests | RegenReplicateTickSeedTests source anchor `GetDefaultedLastReplicateTick` no longer exists -> pin `Owner.ReplicateTick`/`Owner.PacketTick.Value(` in `ReplicateDefaultData`. | FLIGHT (modified) |
| S8 | assets/tests | TransportTrafficPinsTests split-count pin rewritten for #1088 + `_maximumClientPacketSize` scene values. | DONE 076b0a99b |
| S9 | api/tests | ObserverSendShapingTests source anchors (latch/owner-exclusion strings moved into `AddFilteredObserversToNetworkConnectionCache`, operand order changed). | FLIGHT (modified) |
| X1 | info | Drop list confirmed: ZigZag (identical), #229 weaver (CloneImported), owner-packet-tick seeding (upstream per-connection), linearVelocity, SyncHashset spelling. | n/a |
| X2 | info | Inert in 4.7.3R: LevelOfDetail and ColliderRollback are Pro stubs (`_useLevelOfDetail` default false) - keep IObserverSendFilter. | n/a |
| X3 | info | Pre-existing, unrelated: BaseCharacter.OnAwake adds a client-only NetworkAnimator to NPCs/pets. | n/a |
| X4 | info | New upstream defaults accepted silently: NT `_useScaledTime` true, NetworkTickSmoother `_favorPredictionNetworkTransform` true, NetworkColliderBase `InvokeStayOnEnter`, Tugboat MTU now `1350 - header` (no field). | review |

---

## 3. References the compiler cannot catch

Method: every string passed to `GetField/GetProperty/GetMethod/GetNestedType/FindProperty/...`, every `nameof`, every
`"_x"` literal, and every FishNet source path read by a test, in Assets/Scripts, Assets/UnitTests, Assets/TestHarness,
Assets/ZZRenderScratch, Assets/Editor (committed dev state), checked against the 4.6.12 copy and the branch. Plus every
YAML key (any depth, incl. `<X>k__BackingField`) on a FishNet script GUID in committed .prefab/.unity/.asset files.

### 3.1 Names that changed or disappeared (dev file:line)
| Reference | Where | Branch status |
|---|---|---|
| `GetField("_positionMultiplier")` | UnitTests/Prediction/NetworkTransformPrecisionTests.cs:56 | gone -> `_positionCompressionScale` (+ `_positionPackingBits`) |
| YAML regex `_positionMultiplier` | NetworkTransformPrecisionTests.cs:217-218, :376-378, :408 | gone (YAML migrated) |
| source anchor `"!bufferLast && !excludeOwner && ExcludeOwnerFromUnbufferedObserversRpcs && Owner.IsValid"` | UnitTests/Prediction/ObserverSendShapingTests.cs:116 | order changed (RPCs.cs:378) |
| source anchors `bool firstSinceReliable = _observersRpcSettled;`, `if (sendFilter != null && !firstSinceReliable)` | ObserverSendShapingTests.cs:151-153 (ResetState anchors :165-166 still valid) | gone (logic in `AddFilteredObserversToNetworkConnectionCache`, RPCs.cs:507-528) |
| `nt.ExcludeOwnerFromUnbufferedObserversRpcs` (public access) | ObserverSendShapingTests.cs:64/70/77/93 | internal (compile error in a non-friend assembly) |
| source anchor `uint GetDefaultedLastReplicateTick()` / `Owner.PacketTick.Value(` in it | UnitTests/Prediction/RegenReplicateTickSeedTests.cs:180-205 | method gone |
| source anchors `ReconcileSequenceStamper<T>.Stamp(reconcileData` / `methodWriter.WriteDeltaReconcile(lastReconcileData, reconcileData` | UnitTests/Prediction/PredictionAuditRegressionTests.cs:220-223 | gone (`Reconcile_WriteDelta(...)`, NB.Prediction.cs:442) |
| reflection on `WriteDeltaReplicate`/`ReadDeltaReplicate` overload shapes | UnitTests/Prediction/ReplicateDeltaPacketTests.cs:67-93 | present with same shapes in PR 01 (RingBuffer/BasicQueue, `(uint)`); messages say "FISHMMO EDIT" |
| `_networkObjectCache` repair expectation | UnitTests/NetworkBehaviourOwnerRebindTests.cs:80/94/118 | behaviour changed (nulled) |
| YAML `_unreliableMtu: 1023` (Tugboat) | TestHarness/Combat/CombatSimNetwork.prefab:52 | field removed; orphan key ignored by Unity; Tugboat MTU now fixed `1350 - header` (combat sim only) |
| YAML `<SceneId>k__BackingField` | 32 objects / 4 overrides in WorldScene scenes and 3 interactable prefabs | migrated in 076b0a99b; FormerlySerializedAs covers ignored scenes |

### 3.2 Checked and still valid on the branch
`GetNestedType("ChangedDelta"/"ChangedFull")` NonPublic and `"TransformData"` Public (BandwidthCompositionTests.cs:285,
NetworkTransformPrecisionTests.cs:70/87/88/268/280/281, PredictionModeBandwidthMapTests.cs:504, ScaleProjectionTests.cs:178);
`_clientAuthoritative`, `_sendToOwner` (ObserverSendShapingTests.cs:62-76); `_addedNetworkObject`, `_networkObjectCache`
(NetworkObjectBindingValidator.cs:42-43, NetworkBehaviourOwnerRebindTests.cs:30/32, PrefabNetworkObjectBindingTests.cs:83/85,
AbilityObserverReproductionTests.cs:391, ObserverSynchronizationProofTests.cs:932, NetworkTransformPrecisionTests.cs:62,
ZZRenderScratch/Rig.cs:78, PlatformRiderSmoothingTests.cs:193); `NetworkBehaviour.OnValidate` (NetworkBehaviourOwnerRebindTests.cs:34);
`_networkTransform` FindProperty on NetworkObject (LagCompensationWiringTool.cs:264); NetworkObject `_ownerInterpolation`,
`_ownerSmoothedProperties`, `_enableTeleport`, `_teleportThreshold`, `_detachGraphicalObject` (PlatformRiderSmoothingTests.cs:130-143);
NetworkTickSmoother YAML `_spectatorMovementSettings`, `_controllerMovementSettings`, `InterpolationValue`,
`AdaptiveInterpolationValue` (PlatformRiderSmoothingTests.cs:198-230; MovementSettings is the non-threaded variant, FishMMO
does not define FISHNET_THREADED_TICKSMOOTHERS); script paths NetworkObject.cs and TickSmoothing/NetworkTickSmoother.cs
(PlatformRiderSmoothingTests.cs:65-66) exist with unchanged GUIDs; `GetField("Layers")` on NetworkColliderBase
(TestHarness/Region/RegionSimHarness.cs:207 - still a protected field; `SetLayers` is the supported path);
NetworkObject.Prediction.cs `public void SetStateForwarding` + `ObserverAddedTick` (LateJoinerReplayTests.cs:129-145).
Other hits (`InitializeOnce`, `isServer`, `tickDelta`, `Condition`, `Fill`, `OnTick`, `Layer`, `IsEnabled`, `Run`) target
FishMMO types, not FishNet. FishNet asmdef names and GUIDs (FishNet.Runtime, GameKit.Dependencies, Unity.FishNet.Codegen,
FishNet.Codegen.Cecil) and their contents are unchanged. Moved upstream files keep their GUIDs (Quaternion32/64 ->
*Compression, TransformTickSmoother/SnappedAxes/LocalTransformTickSmoother, ILCore resolvers); the two GUIDs upstream
deleted (AdaptiveInterpolationType, AdaptiveLocalTransformSmoother in Runtime/Utility) are referenced by no FishMMO asset.

### 3.3 YAML on FishNet components (committed assets)
Every key under a FishNet script GUID was checked: only NT `_positionMultiplier` (10 prefabs, migrated), Tugboat
`_unreliableMtu` (1 test prefab, orphan), and `<SceneId>k__BackingField` (migrated) disappeared. The other NetworkObject
auto-property backing fields (`PrefabId`, `SpawnableCollectionId`, `AssetPathHash`, `IsNested`, `ComponentIndex`,
`PredictedSpawn`, `PredictedOwner`) are still `[field: SerializeField]` auto-properties - unchanged names.

### 3.4 Plugins/WebTransport against the branch
- Byte-identical to dev; `Runtime/Transporting/*.cs` (Transport, args structs, enums, Channels) are identical
  (whitespace-insensitive) between 4.6.12 and the branch, so every `override` in WebTransport.cs still matches.
  FishNet APIs it also uses: `NetworkManager.Log*`, `ByteArrayPool.Retrieve/Store` (from socket threads - now locked by
  PR 10, and exact-size by B), `FishNet.Managing.Transporting` namespace only.
- Behavioural: split segment = `GetLowestMTU(reliable) - header - tick` (TransportManager.cs:137); with WebTransport's
  reliable MTU 1200 (`SetReliableMTU` never called) and `_maximumClientPacketSize` 65536 the server accepts up to
  ceil(65536 / segment) parts. Client and server must keep the same reliable MTU or a legitimate large client message
  can be kicked (same assumption as the 4.6.12 clamp). PR 20 makes the all-channel lowest MTU 1150 (datagram); nothing in
  FishMMO calls `GetLowestMTU()`. PR 15 only matters if `SetMTUReserve` is called (FishMMO never does). PR 12 falls back
  to reliable for `channelId >= CHANNEL_COUNT` - WebTransport only uses 0/1.

---

## 4. Outside git: what the merge into Jim's tree must handle

1. **Git-ignored `Demos/` and `Upgrading/`** (`FishMMO-Unity/.gitignore:64,66`). The checkout holds the FULL upstream
   4.7.3R Demos (Authenticator, Benchmarks, Prediction, ...). Jim's tree keeps a trimmed set (FishNet.Demos.asmdef,
   Prefabs/NetworkHudCanvas.prefab, Scripts/NetworkHudCanvases.cs + metas). Against 4.7.3R only
   `Demos/Scripts/NetworkHudCanvases.cs` changed (cosmetic `new(...)`); 4.7.3R adds `Demos/Prefabs/NetworkManager.prefab`
   (Jim had removed it); Jim's `Demos/Prefabs.meta` has a local GUID (keep his). `Upgrading/EdgegapMenu.cs(.meta)` is
   deleted in 4.7.3R (fully commented out) - delete it in Jim's tree; MirrorUpgrade.cs / UpgradeFromMirrorMenu.cs are
   unchanged. No FishMMO asset references the HUD prefab.
2. **core.ignorecase=true** (both repos) on a case-sensitive filesystem + `SyncHashset.cs -> SyncHashSet.cs`. After the
   merge, confirm exactly `SyncHashSet.cs` and `SyncHashSet.cs.meta` exist on disk and in `git ls-files`; a leftover
   `SyncHashset.cs` duplicates the class and the meta GUID (b8627bf3...). New spelling may need `git add -f`.
3. **Synapse** (`Runtime/Transporting/Transports/Synapse`, auto-referenced `SynapseSocket` asmdef) is intentionally not
   vendored; it is not on disk in the checkout either. A later Asset Store/package re-import would bring it back.
4. **Jim's uncommitted FishNet edits conflict with the merge**: ServerManager.cs, SplitReader.cs, TransportManager.cs,
   GenericReader.cs, GenericWriter.cs, QuaternionDeltaPrecisionCompression.cs (all fully carried/superseded, section 1.3)
   plus TransportTrafficPinsTests.cs (rewritten on the branch), RotationPrecisionTests.cs and
   DeltaSerializerRegistrationTests.cs (ported on the branch, in flight). Git will refuse the merge while these are dirty.
   Discard Jim's versions of those 9 files deliberately (no `git stash` - concurrent sessions) after confirming the branch
   versions are committed. His other 28 dirty paths do not overlap the branch.
5. **Scripting defines** live in ProjectSettings.asset (committed 076b0a99b, 20 named targets incl. `Server`). No FishMMO
   build script or Build Profile rewrites defines. A target added later must get both defines or it will speak a
   different wire format.
6. **DefaultPrefabObjects.asset is git-ignored** (Assets/DefaultPrefabObjects.asset). Prefab ids come from it, sorted by
   `AssetPathHash`, and the 4.7.3R re-serialization changes every NetworkObject prefab's hash. Run Fish-Networking ->
   Refresh Default Prefabs in Jim's tree (and in every build machine's tree) after the merge, before building server and
   client.
7. **Library**: the checkout's Library is a copy of the editor's (stale FishNet DLLs/ILPP). On first open in Jim's tree
   Unity recompiles FishNet, reweaves every assembly (verify FishMMO.Shared weaves: grep the log for
   `ILPostProcessor has thrown`), reimports moved/renamed files (GUIDs preserved) and re-serializes NetworkObject prefabs.
   Commit what that re-serialization produces in one commit.
8. **Git hooks**: `core.hooksPath .githooks` must still be set in Jim's clone (NetworkObject binding pre-commit) - PR 09
   now nulls foreign caches on validate, so expect `_networkObjectCache: {fileID: 0}` churn on re-save.

---

## Open items
1. No MISSING (E) hunk found. All 72 hunks + 2 file items + 10 uncommitted hunks are A/B/C/D/F.
2. Decide/accept the semantic differences listed under 1.5 (cache nulled, owner-skip widened, packing opt-in default
   Sixteen - consider a test that fails if any NetworkTransform lacks `_positionPackingBits: 1`, delta "not found"
   severity, weaver seal).
3. Run one Unity compile (ILPP) of FishMMO.Shared on the branch - projcheck does not run the weaver; the #229 fix is now
   upstream's CloneImported, not FishMMO's seal.
4. Commit the in-flight work (section 2 FLIGHT rows, the 13 re-serialized prefabs), then re-run this audit's
   section 3.1 anchors against the result.
5. Stale doc comments naming `ReconcileSequenceStamper`: KCCPlatform.cs:107, CharacterPredictionController.cs:500.
6. TestHarness/Combat/CombatSimNetwork.prefab:52 carries an orphan `_unreliableMtu`; Tugboat's MTU changed (1023 -> 1350
   minus header) - re-save if the combat sim numbers matter.
7. Merge-time steps for Jim's tree: section 4 items 1, 2, 4, 6, 7.

---

## 5. Verification (lead, 2026-10-02)

Branch commits after the audit, on top of those in the table at the top:

| Commit | What |
|---|---|
| 672cbe264 | Vendor FishNet 4.7.3R |
| 84ae68547 | Apply FishMMO's upstream FishNet PRs |
| efe70474a | Re-apply FishMMO-only FishNet edits |
| 7a7cf6ea4 | Apply FishNet PR: keep SceneIds serialized before 4.6.16 |
| 076b0a99b | Migrate FishMMO's FishNet settings and serialized data to 4.7.3R |
| 8dee7cc77 | Adapt FishMMO code and tests to FishNet 4.7.3R |
| 4c8a60d8b | Reserialize prefabs under FishNet 4.7.3R |
| a78ec0466 | Port FishMMO's delta serializers to FishNet's delta prediction (#1095) |
| 4325030c0 | Remove the dead platform reconcile Sequence |
| 55044cfd5 | Fill an empty NetworkBehaviour owner cache, as FishMMO's 4.6.12 edit did |

- **Typecheck:** `FishNet-PRs-notes/tools/projcheck.py` with both beta defines: 39 of 39 assemblies, 0 errors
  (FishNet, GameKit, Codegen, WebTransport, KCC, every FishMMO assembly).
- **Unity compile + FishNet weaver (ILPP) + full EditMode suite** on this checkout, unfiltered:
  **4067 tests, 4063 passed, 3 failed, 1 inconclusive.** None of the four is FishNet-related:
  - `SurfaceShaderTests.TheSurfaceLibrary_DoesWetSnowAndTheDissolve` - pre-existing (2026-09-26 baseline).
  - `WorldMapDefinitionTests` x2 (1 failed, 1 inconclusive) - pre-existing, environmental (bake present).
  - `Weather.FogCloudTests.TheMarchedFogIsTheLayerThePhysicsMade` - weather calibration; the fixture references no
    FishNet code. Not in the 09-26 baseline; came with later weather work on dev.
  - The 5 reconcile fixtures failing since weather P4 now PASS: they were catching a real serializer bug (null
    Exposure on a full reconcile), fixed in a78ec0466 and, uncommitted, in the main tree.
- **Found by the first Unity run and fixed:**
  - #1090 carries only half of FishMMO's owner-cache edit (it discards a foreign cache; FishMMO also filled an
    empty one). Re-applied the fill as a FISHMMO EDIT (55044cfd5); WaypointTests' NRE gone.
  - The checkout first had upstream's FULL Demos/ folder on disk; FishMMO keeps a trimmed, git-ignored set
    (4 scripts). Restored the trimmed set with NetworkHudCanvases.cs at 4.7.3R. Full demos trip
    NetworkTransformPrecisionTests and InterestManagementWiringTests and would feed demo prefabs to the generator.
- **Not verified (needs Jim, in the editor):** play mode. See section 7.

## 6. Merging into Jim's tree

1. In /home/jim/Dev/FishMMO-Dev, decide on the uncommitted FishNet work. These files conflict with the branch, and
   the branch supersedes each one:
   - Assets/Plugins/FishNet: ServerManager.cs, SplitReader.cs, TransportManager.cs (4.6.12 split clamp; now #1088 +
     `_maximumClientPacketSize`), QuaternionDeltaPrecisionCompression.cs, GenericWriter.cs, GenericReader.cs (now #1095).
   - Assets/UnitTests: RotationPrecisionTests.cs, DeltaSerializerRegistrationTests.cs (ported on the branch),
     TransportTrafficPinsTests.cs (rewritten on the branch).
   - CharacterReconcileDataDeltaSerializer.cs (null-Exposure fix; the branch has it too).
   If live servers need the 4.6.12 fixes before the upgrade ships, commit them to dev first and resolve the merge
   conflicts in those files to the branch's version (`git checkout --theirs <file>` during the merge).
   Commit or set aside other sessions' unrelated work first (never `git stash` here: other sessions share the tree).
2. With the editor CLOSED: `git merge upgrade/fishnet-4.7.3r` (a merge commit; the history shows each step).
3. `/home/jim/Dev/FishNet-PRs-notes/tools/sync-ignored-fishnet.sh` (dry run), then `--apply`: updates the git-ignored
   Demos/ (trimmed set) and Upgrading/ (drops EdgegapMenu.cs) and lists SyncHashSet*.cs.
4. Check that only `Runtime/Object/Synchronizing/SyncHashSet.cs` (+ .meta) exists: the repo has
   `core.ignorecase=true` and this is a case-only rename.
5. Open the editor and let it reimport, then run **Fish-Networking > Refresh Default Prefabs**. 4.7.3R changed the
   AssetPathHash formula, so prefab ids are re-sorted. Build the server and every client from the same tree.
6. Git-ignored local scenes (Assets/LOCAL) keep their scene ids through PR 22's FormerlySerializedAs.
   Re-save them once in the editor.
7. Play-test (section 7). Then `git worktree remove /home/jim/Dev/FishMMO-Upgrade` and
   `git branch -d upgrade/fishnet-4.7.3r`.

## 7. Play-test checklist (behaviour no automated test covers)

- **World/instance scene loads (Addressables):** scene objects spawn (#1083 scene name, PR 21/22 SceneId).
- **Platform riding:** boarding, riding, leaving, a second rider, late joiner.
  Despawning a platform no longer detaches riders (4.7 clears entered sets without OnExit); low risk while
  platforms live as long as their scene.
- **Regions:** enter, stay on the entry tick, exit, nested regions, teleport flush.
- **Movement feel:** the replicate start delay is back (#1094; matches 4.6.12) and delta reconciles use the new
  design (about 875 B/s per walking character).
- **A client capped at 30-49 fps** reconciles every tick (throttle disabled in ClientPostboot).
- **Weather exposure on the owning client:** no correction fights after a reconcile or a reconnect.
- **24-bit NetworkTransform:** NPCs far from the origin, and the far-field LOD and observer streaming (#17 filter).
- **Large client messages** such as mail sends succeed, and split messages work over WebTransport (65536 cap).
