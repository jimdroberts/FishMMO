using System.Diagnostics;
using System.Runtime.InteropServices;
using System;

namespace FishMMO.Shared
{
	public static class MemoryAccess
	{
		[Flags]
		public enum ProcessAccessFlags : uint
		{
			All = 0x001F0FFF,
			Terminate = 0x00000001,
			CreateThread = 0x00000002,
			VMOperation = 0x00000008,
			VMRead = 0x00000010,
			VMWrite = 0x00000020,
			DupHandle = 0x00000040,
			SetInformation = 0x00000200,
			QueryInformation = 0x00000400,
			Synchronize = 0x00100000,
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, UInt32 dwProcessID);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern Int32 CloseHandle(IntPtr hObject);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern Int32 ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, UInt32 nSize, out IntPtr lpNumberOfBytesRead);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UInt32 nSize, out IntPtr lpNumberOfBytesWritten);

		public static int PointerRead(Process process, int address, int[] offsets, uint bytesToRead, out byte[] buffer)
		{
			IntPtr processHandle = OpenProcess(ProcessAccessFlags.All, false, (uint)process.Id);

			buffer = new byte[4];

			IntPtr numBytesRead;
			ReadProcessMemory(processHandle, new IntPtr(address), buffer, 4, out numBytesRead);
			address = BitConverter.ToInt32(buffer, 0);
			for (int i = 0; i < offsets.Length; ++i)
			{
				ReadProcessMemory(processHandle, new IntPtr(address + offsets[i]), buffer, 4, out numBytesRead);
				address = BitConverter.ToInt32(buffer, 0);
			}
			int result = CloseHandle(processHandle);
			if (result == 0)
			{
				throw new Exception("Close Handle Failed.");
			}
			return numBytesRead.ToInt32();
		}

		public static bool PointerWrite(Process process, int address, int[] offsets, byte[] bytesToWrite, out int bytesWritten)
		{
			IntPtr processHandle = OpenProcess(ProcessAccessFlags.All, false, (uint)process.Id);

			byte[] buffer = new byte[4];
			IntPtr numBytesRead;
			ReadProcessMemory(processHandle, new IntPtr(address), buffer, 4, out numBytesRead);
			address = BitConverter.ToInt32(buffer, 0);
			for (int i = 0; i < offsets.Length; ++i)
			{
				ReadProcessMemory(processHandle, new IntPtr(address + offsets[i]), buffer, 4, out numBytesRead);
				address = BitConverter.ToInt32(buffer, 0);
				string hexAddress = address.ToString("X");
				if (i + 2 >= offsets.Length)
				{
					address += offsets[i + 1];
					break;
				}
			}

			IntPtr numBytesWritten;
			bool writeResult = WriteProcessMemory(processHandle, new IntPtr(address), bytesToWrite, (uint)bytesToWrite.Length, out numBytesWritten);
			bytesWritten = numBytesWritten.ToInt32();

			int result = CloseHandle(processHandle);
			if (result == 0)
			{
				throw new Exception("Close Handle Failed.");
			}
			return writeResult;
		}
	}
}