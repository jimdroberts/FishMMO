using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU path's self-check: on request, one camera's culling results are read back with
	/// <see cref="AsyncGPUReadback"/> (no stall) and compared with the CPU mirror of the kernel
	/// (<see cref="TerrainGpuMath.Cull"/>) run over the same work items, view and instances.
	/// </summary>
	public sealed partial class TerrainGpuRenderer
	{
		private Camera diagnosticCamera;
		private Action<string> diagnosticReport;

		/// <summary>The next time <paramref name="camera"/> renders, read its results back and report them.</summary>
		public void RequestDiagnostics(Camera camera, Action<string> report)
		{
			diagnosticCamera = camera;
			diagnosticReport = report;
		}

		/// <summary>
		/// What the last culling drew, read back now (a stall: for probes, not the game) — instances and triangles for
		/// the main view and the shadows, by level, and the meshes that cost the most triangles in the main view.
		/// </summary>
		public string DescribeDraws()
		{
			if (argsBuffer == null || commands.Count == 0)
			{
				return "no draws";
			}
			AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(argsBuffer);
			request.WaitForCompletion();
			if (request.hasError)
			{
				return "readback failed";
			}
			NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> args = request.GetData<GraphicsBuffer.IndirectDrawIndexedArgs>();
			var instancesBy = new long[2, 4];
			var trianglesBy = new long[2, 4];
			var byMesh = new Dictionary<string, long>();
			for (int c = 0; c < commands.Count && c < args.Length; c++)
			{
				Command command = commands[c];
				int view = command.View == 0 ? 0 : 1;
				int level = Mathf.Clamp(command.Level, 0, 3);
				long triangles = (long)args[c].instanceCount * (args[c].indexCountPerInstance / 3);
				instancesBy[view, level] += args[c].instanceCount;
				trianglesBy[view, level] += triangles;
				if (view == 0 && command.Mesh != null)
				{
					string key = $"{command.Mesh.name} L{level}";
					byMesh[key] = (byMesh.TryGetValue(key, out long t) ? t : 0) + triangles;
				}
			}
			var sb = new StringBuilder();
			for (int view = 0; view < 2; view++)
			{
				sb.Append(view == 0 ? "main" : "; shadows");
				for (int level = 0; level < 4; level++)
				{
					if (instancesBy[view, level] > 0)
					{
						sb.Append($" L{level} {instancesBy[view, level]} inst {trianglesBy[view, level] / 1000}k tri");
					}
				}
			}
			sb.Append("; heaviest:");
			foreach (KeyValuePair<string, long> pair in byMesh.OrderByDescending(p => p.Value).Take(6))
			{
				sb.Append($" {pair.Key} {pair.Value / 1000}k");
			}
			return sb.ToString();
		}

		private sealed class Snapshot
		{
			public Vector3 Eye;
			public Plane[] Planes;
			public Vector4 Light;
			public Vector3 Lod;
			public FishWork[] Work;
			public FishInstance[][] WorkInstances;
			public int[] Bases;
			public int[] Capacities;
			public Command[] Commands;
			public GraphicsBuffer.IndirectDrawIndexedArgs[] ExpectedArgs;
			public uint[] Counts, Args;
			public FishVisible[] Visible;
			public FishInstance[] GpuInstances;
			public FishModelData[] GpuModels;
			public FishModelData[] CpuModels;
			public uint[] CommandBases;
			public FishInstance[] Mirror;
			public KeyValuePair<int, Resident>[] Ranges;
			public int Pending;
			public Action<string> Report;
			public string Failure;
		}

		private void StartDiagnostics(in TerrainTreeField.View view, Action<string> report, CommandBuffer recording)
		{
			var snap = new Snapshot
			{
				Eye = view.Position,
				Planes = (Plane[])view.Planes.Clone(),
				Light = view.Shadows ? new Vector4(view.LightDirection.x, view.LightDirection.y, view.LightDirection.z, view.ShadowDistance) : Vector4.zero,
				Lod = new Vector3(view.ScreenFactor, view.Orthographic ? 1f : 0f, view.MaximumLodLevel),
				Work = work.GetSubArray(0, workCount).ToArray(),
				Commands = commands.ToArray(),
				Report = report,
			};

			// The instances each work item covers, copied now: the sources may page out before the readback lands.
			var ranges = residents.OrderBy(r => r.Key).ToArray();
			snap.WorkInstances = new FishInstance[snap.Work.Length][];
			for (int w = 0; w < snap.Work.Length; w++)
			{
				FishWork item = snap.Work[w];
				var copy = new FishInstance[item.Count];
				for (int i = 0; i < item.Count; i++)
				{
					int index = (int)item.Start + i;
					copy[i] = default;
					foreach (KeyValuePair<int, Resident> r in ranges)
					{
						if (index >= r.Key && index < r.Key + r.Value.Count)
						{
							copy[i] = mirror[index];
							break;
						}
					}
				}
				snap.WorkInstances[w] = copy;
			}

			int slots = slotCount;
			snap.Capacities = new int[slots];
			snap.Bases = new int[slots];
			int s = 0;
			foreach (Model m in models)
			{
				for (int l = 0; l < m.Levels.Length; l++)
				{
					for (int v = 0; v < TerrainGpuMath.Views; v++)
					{
						snap.Capacities[s++] = SlotCapacity(m, l, v);
					}
				}
			}
			TerrainGpuMath.LayoutSlots(snap.Capacities, snap.Bases);
			snap.ExpectedArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[snap.Commands.Length];
			for (int c = 0; c < snap.Commands.Length; c++)
			{
				Command command = snap.Commands[c];
				snap.ExpectedArgs[c] = new GraphicsBuffer.IndirectDrawIndexedArgs
				{
					indexCountPerInstance = command.Mesh.GetIndexCount(command.Submesh),
					startIndex = command.Mesh.GetIndexStart(command.Submesh),
					baseVertexIndex = (uint)command.Mesh.GetBaseVertex(command.Submesh),
				};
			}

			snap.CpuModels = models.Select(m => m.Data).ToArray();
			snap.Mirror = mirror.ToArray();
			snap.Ranges = ranges;

			// Recorded after the three dispatches in this camera's own command buffer, so they read exactly
			// what this camera's draws will read.
			snap.Pending = 6;
			recording.RequestAsyncReadback(countBuffer, r => Landed(snap, r, 0));
			recording.RequestAsyncReadback(argsBuffer, r => Landed(snap, r, 1));
			recording.RequestAsyncReadback(visibleBuffer, r => Landed(snap, r, 2));
			recording.RequestAsyncReadback(instances, r => Landed(snap, r, 3));
			recording.RequestAsyncReadback(modelBuffer, r => Landed(snap, r, 4));
			recording.RequestAsyncReadback(commandBaseBuffer, r => Landed(snap, r, 5));
		}

		private void Landed(Snapshot snap, AsyncGPUReadbackRequest request, int which)
		{
			if (request.hasError)
			{
				snap.Failure = $"readback {which} failed";
			}
			else if (which == 0)
			{
				snap.Counts = request.GetData<uint>().ToArray();
			}
			else if (which == 1)
			{
				snap.Args = request.GetData<uint>().ToArray();
			}
			else if (which == 2)
			{
				snap.Visible = request.GetData<FishVisible>().ToArray();
			}
			else if (which == 3)
			{
				snap.GpuInstances = request.GetData<FishInstance>().ToArray();
			}
			else if (which == 4)
			{
				snap.GpuModels = request.GetData<FishModelData>().ToArray();
			}
			else
			{
				snap.CommandBases = request.GetData<uint>().ToArray();
			}
			if (--snap.Pending == 0)
			{
				snap.Report(snap.Failure ?? Compare(snap));
			}
		}

		private string Compare(Snapshot snap)
		{
			int slots = snap.Capacities.Length;
			var expected = new int[slots];
			var modelOfIndex = new Dictionary<uint, int>();
			var emits = new List<TerrainGpuMath.Emit>();
			float minDistance = float.MaxValue, maxDistance = 0f;
			int zeroLodBias = 0, missingInstances = 0;
			for (int w = 0; w < snap.Work.Length; w++)
			{
				FishWork item = snap.Work[w];
				minDistance = Mathf.Min(minDistance, item.DrawDistance);
				maxDistance = Mathf.Max(maxDistance, item.DrawDistance);
				zeroLodBias += item.LodBiasMultiplier <= 0f ? 1 : 0;
				Model m = models[(int)item.Model];
				for (int i = 0; i < item.Count; i++)
				{
					FishInstance inst = snap.WorkInstances[w][i];
					if (inst.Row0 == Vector4.zero && inst.Row1 == Vector4.zero)
					{
						missingInstances++;
						continue;
					}
					modelOfIndex[item.Start + (uint)i] = (int)item.Model;
					TerrainGpuMath.Cull(inst.ToMatrix(), in m.Data, item.DrawDistance, item.LodBiasMultiplier, snap.Eye, snap.Planes, snap.Light, snap.Lod, emits);
					foreach (TerrainGpuMath.Emit e in emits)
					{
						expected[TerrainGpuMath.Slot((int)m.Data.SlotBase, e.Level, e.View)]++;
					}
				}
			}

			var sb = new StringBuilder();
			int mismatches = 0;
			sb.AppendLine($"[Terrain GPU diagnostics] {models.Count} models, {slots} slots, {snap.Commands.Length} commands, {snap.Work.Length} work items, _FishLod.x (screen factor) {snap.Lod.x:F4}, ortho {snap.Lod.y}, max LOD {snap.Lod.z}, shadow distance {snap.Light.w:F0}, work draw distance {minDistance:F0}–{maxDistance:F0} m.");
			if (snap.Lod.x <= 0f)
			{
				sb.AppendLine("  MISMATCH: the screen factor is 0 — every instance culls by LOD.");
				mismatches++;
			}
			if (zeroLodBias > 0)
			{
				sb.AppendLine($"  MISMATCH: {zeroLodBias} work items carry a LOD bias multiplier of 0.");
				mismatches++;
			}
			if (missingInstances > 0)
			{
				sb.AppendLine($"  MISMATCH: {missingInstances} work-item instances are not inside any resident range (stale offsets).");
				mismatches++;
			}
			// The GPU's own instance and model data against the CPU copies the mirror used.
			int badInstances = 0;
			var examples = new StringBuilder();
			for (int w = 0; w < snap.Work.Length; w++)
			{
				FishWork item = snap.Work[w];
				for (int i = 0; i < item.Count; i++)
				{
					int index = (int)item.Start + i;
					FishInstance cpu = snap.WorkInstances[w][i];
					if (index >= snap.GpuInstances.Length)
					{
						badInstances++;
						continue;
					}
					FishInstance gpu = snap.GpuInstances[index];
					if (gpu.Row0 != cpu.Row0 || gpu.Row1 != cpu.Row1 || gpu.Row2 != cpu.Row2)
					{
						if (badInstances < 3)
						{
							examples.Append($" [#{index} work {w}: GPU pos {gpu.Position}, CPU pos {cpu.Position}]");
						}
						badInstances++;
					}
				}
			}
			if (badInstances > 0)
			{
				sb.AppendLine($"  MISMATCH: {badInstances} work-item instances in _FishInstances differ from the CPU mirror (buffer {snap.GpuInstances.Length} long).{examples}");
				mismatches++;
			}
			// Every resident range, not only what this camera culls: which owners' ranges the GPU lost.
			int badRanges = 0;
			foreach (KeyValuePair<int, Resident> range in snap.Ranges)
			{
				int wrong = 0, first = -1;
				for (int i = range.Key; i < range.Key + range.Value.Count; i++)
				{
					bool differs = i >= snap.GpuInstances.Length || i >= snap.Mirror.Length
						|| snap.GpuInstances[i].Row0 != snap.Mirror[i].Row0 || snap.GpuInstances[i].Row1 != snap.Mirror[i].Row1 || snap.GpuInstances[i].Row2 != snap.Mirror[i].Row2;
					if (differs)
					{
						wrong++;
						first = first < 0 ? i : first;
					}
				}
				if (wrong > 0)
				{
					if (badRanges < 8)
					{
						sb.AppendLine($"  MISMATCH: range {range.Value.Owner} [{range.Key}, {range.Key + range.Value.Count}): {wrong} instances differ on the GPU, first #{first} (GPU pos {(first < snap.GpuInstances.Length ? snap.GpuInstances[first].Position : Vector3.zero)}, mirror pos {snap.Mirror[first].Position}).");
					}
					badRanges++;
				}
			}
			sb.AppendLine(badRanges == 0
				? $"  _FishInstances: all {snap.Ranges.Length} resident ranges match the CPU mirror."
				: $"  MISMATCH: {badRanges} of {snap.Ranges.Length} resident ranges differ from the CPU mirror.");
			mismatches += badRanges > 0 ? 1 : 0;
			int badModels = 0;
			for (int i = 0; i < snap.CpuModels.Length; i++)
			{
				if (i >= snap.GpuModels.Length || !snap.GpuModels[i].Equals(snap.CpuModels[i]))
				{
					if (badModels < 3)
					{
						sb.AppendLine($"  MISMATCH: _FishModels[{i}] ({models[i].Name}) differs: GPU slotBase {(i < snap.GpuModels.Length ? snap.GpuModels[i].SlotBase : 0)} levels {(i < snap.GpuModels.Length ? snap.GpuModels[i].LevelCount : 0)}, CPU slotBase {snap.CpuModels[i].SlotBase} levels {snap.CpuModels[i].LevelCount}.");
					}
					badModels++;
				}
			}
			if (badModels > 0)
			{
				sb.AppendLine($"  MISMATCH: {badModels} of {snap.CpuModels.Length} model records differ on the GPU.");
				mismatches++;
			}
			int badBases = 0;
			for (int c = 0; c < snap.Commands.Length; c++)
			{
				int slot = snap.Commands[c].Slot;
				if (c >= snap.CommandBases.Length || snap.CommandBases[c] != (uint)snap.Bases[slot])
				{
					if (badBases < 3)
					{
						sb.AppendLine($"  MISMATCH: {CommandBasesName}[{c}] = {(c < snap.CommandBases.Length ? snap.CommandBases[c].ToString() : "missing")}, slot {slot} base {snap.Bases[slot]}.");
					}
					badBases++;
				}
			}
			sb.AppendLine(badBases == 0
				? $"  {CommandBasesName}: all {snap.Commands.Length} command bases match their slots."
				: $"  MISMATCH: {badBases} of {snap.Commands.Length} command bases differ from their slots.");
			mismatches += badBases > 0 ? 1 : 0;
			if (snap.Counts.Length < slots)
			{
				sb.AppendLine($"  MISMATCH: the count buffer holds {snap.Counts.Length} slots, the layout {slots}.");
				mismatches++;
			}

			for (int id = 0; id < models.Count; id++)
			{
				Model m = models[id];
				for (int l = 0; l < m.Levels.Length; l++)
				{
					for (int v = 0; v < TerrainGpuMath.Views; v++)
					{
						int slot = TerrainGpuMath.Slot((int)m.Data.SlotBase, l, v);
						uint gpu = slot < snap.Counts.Length ? snap.Counts[slot] : 0;
						if (gpu == 0 && expected[slot] == 0)
						{
							continue;
						}
						bool bad = gpu != expected[slot] || gpu > snap.Capacities[slot];
						var line = new StringBuilder($"  {(bad ? "MISMATCH " : "")}{m.Name} LOD{l} {(v == 0 ? "camera" : "shadow")}: GPU {gpu}, CPU {expected[slot]}, capacity {snap.Capacities[slot]}, base {snap.Bases[slot]}{(gpu > snap.Capacities[slot] ? $" OVERFLOW ({gpu - snap.Capacities[slot]} dropped)" : "")}");
						// The first entries the GPU wrote must belong to this model.
						int wrong = 0;
						int check = (int)Mathf.Min(gpu, 64);
						for (int k = 0; k < check; k++)
						{
							int at = snap.Bases[slot] + k;
							if (at >= snap.Visible.Length || !modelOfIndex.TryGetValue(snap.Visible[at].Index, out int owner) || owner != id)
							{
								wrong++;
							}
						}
						if (wrong > 0)
						{
							bad = true;
							line.Append($", {wrong}/{check} visible entries point at another model's or no instance");
						}
						for (int c = 0; c < snap.Commands.Length; c++)
						{
							Command command = snap.Commands[c];
							if (command.Slot != slot)
							{
								continue;
							}
							uint instanceCount = (uint)(c * 5 + 1) < snap.Args.Length ? snap.Args[c * 5 + 1] : 0;
							uint indexCount = (uint)(c * 5) < snap.Args.Length ? snap.Args[c * 5] : 0;
							Material clone = command.Params.material;
							Material source = clones.FirstOrDefault(p => p.Value == clone).Key;
							bool supported = clone != null && clone.shader != null && clone.shader.isSupported;
							string keys = clone == null ? "no clone" : Keywords(clone, source);
							bool argBad = instanceCount != TerrainGpuMath.Finalize(gpu, (uint)snap.Capacities[slot]) || indexCount != snap.ExpectedArgs[c].indexCountPerInstance
								|| (uint)(c * 5 + 3) >= snap.Args.Length || snap.Args[c * 5 + 2] != snap.ExpectedArgs[c].startIndex || snap.Args[c * 5 + 3] != snap.ExpectedArgs[c].baseVertexIndex;
							bad |= argBad || !supported;
							line.Append($"\n      cmd {c} {command.Mesh.name}: args instances {instanceCount}, indices {indexCount}/{snap.ExpectedArgs[c].indexCountPerInstance}{(argBad ? " (ARGS MISMATCH)" : "")}, material '{(clone != null ? clone.name : "none")}' shader {(supported ? "supported" : "NOT SUPPORTED")}, keywords {keys}");
						}
						if (bad)
						{
							mismatches++;
						}
						sb.AppendLine(line.ToString());
					}
				}
			}
			sb.Insert(0, mismatches == 0 ? "[Terrain GPU diagnostics] OK: every slot matches the CPU mirror.\n" : $"[Terrain GPU diagnostics] {mismatches} MISMATCH(ES) — see the lines marked.\n");
			return sb.ToString();
		}

		private static string Keywords(Material clone, Material source)
		{
			string[] c = clone.shaderKeywords.OrderBy(k => k).ToArray();
			string[] s = source != null ? source.shaderKeywords.OrderBy(k => k).ToArray() : Array.Empty<string>();
			string same = c.SequenceEqual(s) ? "same as source" : $"DIFFER (source: {string.Join(" ", s)})";
			return $"[{string.Join(" ", c)}] {same}";
		}
	}
}
