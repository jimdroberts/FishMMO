using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using ZstdSharp.Unsafe;

namespace FishMMO.Shared
{
	/// <summary>
	/// Binary deltas between two versions of a file, as Zstandard frames that use the old file as
	/// a reference prefix (what <c>zstd --patch-from</c> does).
	/// </summary>
	/// <remarks>
	/// Used by the native client patches: the patch generator stores each changed file as a delta
	/// against the version the player already has, and the Updater rebuilds the new file from the
	/// old one plus the delta. Unlike a same-offset chunk diff, a zstd match can point anywhere in
	/// the old file, so inserted or removed bytes do not invalidate everything after them; and a
	/// file Addressables renamed because a dependency's name inside it changed costs a few
	/// kilobytes instead of its whole size.
	/// <para>
	/// The reference file is memory-mapped and handed to zstd as a prefix, so neither side copies
	/// it into managed memory. A frame is a single zstd frame with the content size and a checksum,
	/// readable by the stock <c>zstd</c> tool (<c>zstd -d --patch-from=old delta</c>).
	/// </para>
	/// <para>
	/// The window is sized from the larger of the two files, capped at <see cref="MaxWindowLog"/>
	/// (1 GiB), the largest window every decoder, 32-bit included, accepts. A file larger than
	/// <see cref="MaxReferenceBytes"/> cannot be referenced in full; callers ship such files whole
	/// (see <see cref="CanReference"/>).
	/// </para>
	/// </remarks>
	public static unsafe class ZstdDelta
	{
		/// <summary>Highest compression level zstd offers. Slow to encode, no slower to decode.</summary>
		public const int MaxLevel = 22;

		/// <summary>Default level: the smallest output, since patches are encoded once and downloaded many times.</summary>
		public const int DefaultLevel = MaxLevel;

		/// <summary>Largest window log used or accepted (1 GiB).</summary>
		public const int MaxWindowLog = 30;

		/// <summary>Smallest window log used.</summary>
		private const int MinWindowLog = 20;

		/// <summary>Largest file (either side) a delta can fully reference.</summary>
		public const long MaxReferenceBytes = 1L << MaxWindowLog;

		/// <summary>Bytes read from the new file per compression call.</summary>
		private const int InputChunkBytes = 1 << 20;

		/// <summary>True when a delta between files of these sizes can reference the whole of the old one.</summary>
		public static bool CanReference(long referenceLength, long newLength)
		{
			return referenceLength >= 0 && newLength >= 0 &&
				referenceLength <= MaxReferenceBytes && newLength <= MaxReferenceBytes;
		}

		/// <summary>The window log for a delta between files of these sizes.</summary>
		public static int WindowLogFor(long referenceLength, long newLength)
		{
			long span = Math.Max(1, Math.Max(referenceLength, newLength));
			int log = 0;
			while ((1L << log) < span && log < 62)
			{
				log++;
			}
			// One step above the size, as zstd --patch-from does, so a match near the end of the
			// new file can still reach the start of the old one.
			return Math.Max(MinWindowLog, Math.Min(MaxWindowLog, log + 1));
		}

		/// <summary>
		/// Writes a delta that turns <paramref name="referencePath"/> into <paramref name="newPath"/>.
		/// A null <paramref name="referencePath"/> writes plain zstd compression of the new file.
		/// </summary>
		/// <returns>The number of bytes written to <paramref name="output"/>.</returns>
		/// <exception cref="InvalidOperationException">Either file is larger than <see cref="MaxReferenceBytes"/>, or zstd reported an error.</exception>
		public static long Encode(string? referencePath, string newPath, Stream output, int level = DefaultLevel)
		{
			if (newPath == null) throw new ArgumentNullException(nameof(newPath));
			if (output == null) throw new ArgumentNullException(nameof(output));

			using (FileStream source = new FileStream(newPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
			using (MappedFile reference = MappedFile.Open(referencePath))
			{
				long newLength = source.Length;
				if (!CanReference(reference.Length, newLength))
				{
					throw new InvalidOperationException(
						$"Delta refused: {reference.Length} -> {newLength} bytes exceeds the {MaxReferenceBytes}-byte reference limit.");
				}

				ZSTD_CCtx_s* cctx = Methods.ZSTD_createCCtx();
				if (cctx == null)
				{
					throw new InvalidOperationException("zstd: could not create a compression context.");
				}
				long written = 0;
				try
				{
					int clampedLevel = Math.Max(1, Math.Min(MaxLevel, level));
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_compressionLevel, clampedLevel), "level");

					/* The same parameters zstd --patch-from uses: the level's table entry for this
					 * source and reference size, set explicitly, with the window widened to span
					 * both files and long-distance matching on (it finds matches beyond the
					 * match finder's reach, anywhere in the reference). Leaving them to the
					 * library's own selection with a prefix attached produced deltas far larger
					 * than the CLI's (2.9 MB against 50 KB for a 300 MB bundle). The CLI's
					 * further tuning suggestions (chainLog 30, targetLength 4096, ldm 30/0) were
					 * measured at about 1.6% smaller for twice the time and over 10 GB of memory,
					 * so they are not used. */
					ZSTD_compressionParameters cp = Methods.ZSTD_getCParams(clampedLevel, (ulong)newLength, (nuint)reference.Length);
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_windowLog, WindowLogFor(reference.Length, newLength)), "windowLog");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_chainLog, (int)cp.chainLog), "chainLog");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_hashLog, (int)cp.hashLog), "hashLog");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_searchLog, (int)cp.searchLog), "searchLog");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_minMatch, (int)cp.minMatch), "minMatch");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_targetLength, (int)cp.targetLength), "targetLength");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_strategy, (int)cp.strategy), "strategy");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, 1), "long distance matching");

					/* The multi-threaded engine, even with a single worker. Its long-distance
					 * matcher runs over the whole input with the prefix in view, and on a 300 MB
					 * bundle it finds what the single-threaded path misses: 50 KB against 2.9 MB
					 * (measured with the zstd CLI: -T1 vs --single-thread, the same as this port).
					 * The output does not depend on the worker count; more workers only add speed
					 * and memory, so one keeps the memory of a large encode predictable. */
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_nbWorkers, 1), "workers");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_checksumFlag, 1), "checksum");
					Check(Methods.ZSTD_CCtx_setParameter(cctx, ZSTD_cParameter.ZSTD_c_contentSizeFlag, 1), "content size");
					Check(Methods.ZSTD_CCtx_setPledgedSrcSize(cctx, (ulong)newLength), "pledged size");
					if (reference.Length > 0)
					{
						Check(Methods.ZSTD_CCtx_refPrefix(cctx, reference.Pointer, (nuint)reference.Length), "reference");
					}

					byte[] inBuffer = new byte[InputChunkBytes];
					byte[] outBuffer = new byte[(int)Methods.ZSTD_CStreamOutSize()];
					fixed (byte* inPtr = inBuffer)
					fixed (byte* outPtr = outBuffer)
					{
						bool last;
						do
						{
							int read = ReadFully(source, inBuffer);
							last = read < inBuffer.Length;
							ZSTD_EndDirective mode = last ? ZSTD_EndDirective.ZSTD_e_end : ZSTD_EndDirective.ZSTD_e_continue;
							ZSTD_inBuffer_s input = new ZSTD_inBuffer_s { src = inPtr, size = (nuint)read, pos = 0 };
							bool done;
							do
							{
								ZSTD_outBuffer_s outBuf = new ZSTD_outBuffer_s { dst = outPtr, size = (nuint)outBuffer.Length, pos = 0 };
								nuint remaining = Methods.ZSTD_compressStream2(cctx, &outBuf, &input, mode);
								Check(remaining, "compress");
								if (outBuf.pos > 0)
								{
									output.Write(outBuffer, 0, (int)outBuf.pos);
									written += (long)outBuf.pos;
								}
								done = last ? remaining == 0 : input.pos == input.size;
							} while (!done);
						} while (!last);
					}
				}
				finally
				{
					Methods.ZSTD_freeCCtx(cctx);
				}
				return written;
			}
		}

		/// <summary>
		/// Rebuilds a file from <paramref name="referencePath"/> (the old version, or null for a
		/// delta written without one) and a delta written by <see cref="Encode"/>.
		/// </summary>
		/// <param name="referencePath">The exact file the delta was made against, or null.</param>
		/// <param name="delta">The delta stream; read to its end.</param>
		/// <param name="output">Receives the rebuilt file.</param>
		/// <param name="expectedLength">The rebuilt file's size; anything else is an error, and output beyond it is never written.</param>
		/// <returns>The number of bytes written.</returns>
		/// <exception cref="InvalidDataException">The delta is corrupt, truncated, does not match the reference, or has trailing data.</exception>
		public static long Decode(string? referencePath, Stream delta, Stream output, long expectedLength)
		{
			if (delta == null) throw new ArgumentNullException(nameof(delta));
			if (output == null) throw new ArgumentNullException(nameof(output));
			if (expectedLength < 0) throw new ArgumentOutOfRangeException(nameof(expectedLength));

			using (MappedFile reference = MappedFile.Open(referencePath))
			{
				ZSTD_DCtx_s* dctx = Methods.ZSTD_createDCtx();
				if (dctx == null)
				{
					throw new InvalidOperationException("zstd: could not create a decompression context.");
				}
				long written = 0;
				try
				{
					CheckData(Methods.ZSTD_DCtx_setParameter(dctx, ZSTD_dParameter.ZSTD_d_windowLogMax, MaxWindowLog), "window limit");
					if (reference.Length > 0)
					{
						CheckData(Methods.ZSTD_DCtx_refPrefix(dctx, reference.Pointer, (nuint)reference.Length), "reference");
					}

					byte[] inBuffer = new byte[(int)Methods.ZSTD_DStreamInSize()];
					byte[] outBuffer = new byte[(int)Methods.ZSTD_DStreamOutSize()];
					bool frameComplete = false;
					fixed (byte* inPtr = inBuffer)
					fixed (byte* outPtr = outBuffer)
					{
						int read;
						while ((read = delta.Read(inBuffer, 0, inBuffer.Length)) > 0)
						{
							if (frameComplete)
							{
								throw new InvalidDataException("Delta has data after its frame.");
							}
							ZSTD_inBuffer_s input = new ZSTD_inBuffer_s { src = inPtr, size = (nuint)read, pos = 0 };
							while (input.pos < input.size)
							{
								ZSTD_outBuffer_s outBuf = new ZSTD_outBuffer_s { dst = outPtr, size = (nuint)outBuffer.Length, pos = 0 };
								nuint hint = Methods.ZSTD_decompressStream(dctx, &outBuf, &input);
								CheckData(hint, "decompress");
								if (outBuf.pos > 0)
								{
									written += (long)outBuf.pos;
									if (written > expectedLength)
									{
										throw new InvalidDataException($"Delta produces more than the expected {expectedLength} bytes.");
									}
									output.Write(outBuffer, 0, (int)outBuf.pos);
								}
								if (hint == 0)
								{
									frameComplete = true;
									if (input.pos < input.size)
									{
										throw new InvalidDataException("Delta has data after its frame.");
									}
									break;
								}
							}
						}

						// Output still held inside the decoder once the input has run out.
						while (!frameComplete)
						{
							ZSTD_inBuffer_s empty = new ZSTD_inBuffer_s { src = inPtr, size = 0, pos = 0 };
							ZSTD_outBuffer_s outBuf = new ZSTD_outBuffer_s { dst = outPtr, size = (nuint)outBuffer.Length, pos = 0 };
							nuint hint = Methods.ZSTD_decompressStream(dctx, &outBuf, &empty);
							CheckData(hint, "decompress");
							if (outBuf.pos > 0)
							{
								written += (long)outBuf.pos;
								if (written > expectedLength)
								{
									throw new InvalidDataException($"Delta produces more than the expected {expectedLength} bytes.");
								}
								output.Write(outBuffer, 0, (int)outBuf.pos);
							}
							if (hint == 0)
							{
								frameComplete = true;
							}
							else if (outBuf.pos == 0)
							{
								throw new InvalidDataException("Delta is truncated.");
							}
						}
					}
				}
				finally
				{
					Methods.ZSTD_freeDCtx(dctx);
				}

				if (written != expectedLength)
				{
					throw new InvalidDataException($"Delta produced {written} bytes, expected {expectedLength}.");
				}
				return written;
			}
		}

		private static int ReadFully(Stream stream, byte[] buffer)
		{
			int total = 0;
			int read;
			while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
			{
				total += read;
			}
			return total;
		}

		private static void Check(nuint code, string what)
		{
			if (Methods.ZSTD_isError(code))
			{
				throw new InvalidOperationException($"zstd {what}: {Methods.ZSTD_getErrorName(code)}");
			}
		}

		private static void CheckData(nuint code, string what)
		{
			if (Methods.ZSTD_isError(code))
			{
				throw new InvalidDataException($"zstd {what}: {Methods.ZSTD_getErrorName(code)}");
			}
		}

		/// <summary>A read-only memory mapping of a whole file, or an empty stand-in for no file.</summary>
		private sealed class MappedFile : IDisposable
		{
			private FileStream? file;
			private MemoryMappedFile? map;
			private MemoryMappedViewAccessor? view;
			private bool pointerAcquired;

			public byte* Pointer { get; private set; }
			public long Length { get; private set; }

			public static MappedFile Open(string? path)
			{
				MappedFile mapped = new MappedFile();
				if (string.IsNullOrEmpty(path))
				{
					return mapped;
				}
				try
				{
					mapped.file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
					mapped.Length = mapped.file.Length;
					if (mapped.Length == 0)
					{
						return mapped;
					}
					mapped.map = MemoryMappedFile.CreateFromFile(mapped.file, null, 0, MemoryMappedFileAccess.Read,
						HandleInheritability.None, leaveOpen: true);
					mapped.view = mapped.map.CreateViewAccessor(0, mapped.Length, MemoryMappedFileAccess.Read);
					byte* p = null;
					mapped.view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
					mapped.pointerAcquired = true;
					mapped.Pointer = p + mapped.view.PointerOffset;
					return mapped;
				}
				catch
				{
					mapped.Dispose();
					throw;
				}
			}

			public void Dispose()
			{
				if (pointerAcquired && view != null)
				{
					view.SafeMemoryMappedViewHandle.ReleasePointer();
					pointerAcquired = false;
				}
				view?.Dispose();
				map?.Dispose();
				file?.Dispose();
				view = null;
				map = null;
				file = null;
				Pointer = null;
			}
		}
	}
}
