using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Shared
{
	public static class MemoryAccess
	{
		[Flags]
		public enum ProcessAccessFlags : uint
		{
			All = 0x001F0FFF,
			VMOperation = 0x00000008,
			VMRead = 0x00000010,
			VMWrite = 0x00000020,
			QueryInformation = 0x00000400,
			Synchronize = 0x00100000,
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessID);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr hObject);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

		/// <summary>
		/// Reads a multi-level pointer chain. Supports both 32-bit and 64-bit processes.
		/// </summary>
		public static byte[]? ReadPointerChain(Process process, IntPtr baseAddress, int[] offsets, uint length)
		{
			IntPtr handle = OpenProcess(ProcessAccessFlags.VMRead | ProcessAccessFlags.QueryInformation, false, (uint)process.Id);
			if (handle == IntPtr.Zero) return null;

			try
			{
				IntPtr currentAddress = baseAddress;
				byte[] pointerBuffer = new byte[IntPtr.Size]; // Dynamically adjusts to 4 or 8 bytes

				// Traverse the offsets
				for (int i = 0; i < offsets.Length; i++)
				{
					if (!ReadProcessMemory(handle, currentAddress, pointerBuffer, (nuint)pointerBuffer.Length, out _))
						return null;

					currentAddress = (IntPtr.Size == 8)
						? (IntPtr)(BitConverter.ToInt64(pointerBuffer, 0) + offsets[i])
						: (IntPtr)(BitConverter.ToInt32(pointerBuffer, 0) + offsets[i]);
				}

				// Final read
				byte[] finalBuffer = new byte[length];
				if (ReadProcessMemory(handle, currentAddress, finalBuffer, (nuint)length, out _))
				{
					return finalBuffer;
				}
				return null;
			}
			finally
			{
				CloseHandle(handle);
			}
		}

		public static bool WritePointerChain(Process process, IntPtr baseAddress, int[] offsets, byte[] data)
		{
			IntPtr handle = OpenProcess(ProcessAccessFlags.VMRead | ProcessAccessFlags.VMWrite | ProcessAccessFlags.VMOperation, false, (uint)process.Id);
			if (handle == IntPtr.Zero) return false;

			try
			{
				IntPtr currentAddress = baseAddress;
				byte[] pointerBuffer = new byte[IntPtr.Size];

				// Follow pointers to the final address
				for (int i = 0; i < offsets.Length; i++)
				{
					if (!ReadProcessMemory(handle, currentAddress, pointerBuffer, (nuint)pointerBuffer.Length, out _))
						return false;

					currentAddress = (IntPtr.Size == 8)
						? (IntPtr)(BitConverter.ToInt64(pointerBuffer, 0) + offsets[i])
						: (IntPtr)(BitConverter.ToInt32(pointerBuffer, 0) + offsets[i]);
				}

				return WriteProcessMemory(handle, currentAddress, data, (nuint)data.Length, out _);
			}
			finally
			{
				CloseHandle(handle);
			}
		}
	}
}