using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	public static class DirectoryExtensions
	{
		/// <summary>
		/// Gets all files recursively inside a root directory with an optional extension.
		/// </summary>
		public static HashSet<string> GetAllFiles(string rootDirectory, string extension = "")
		{
			HashSet<string> files = new HashSet<string>();
			Stack<string> directories = new Stack<string>();

			// Start with the root directory
			directories.Push(rootDirectory);

			while (directories.Count > 0)
			{
				string currentDir = directories.Pop();

				// Get files in the current directory
				try
				{
					string[] currentFiles = Directory.GetFiles(currentDir);
					foreach (string file in currentFiles)
					{
						// Skip configuration files
						if (!string.IsNullOrWhiteSpace(extension) &&
							!file.EndsWith(extension))
						{
							continue;
						}
						files.Add(file);
					}
				}
				catch (UnauthorizedAccessException)
				{
					Log.Debug("DirectoryExtensions", $"Access to directory '{currentDir}' is denied.");
					continue; // Skip this directory if access is denied
				}
				catch (DirectoryNotFoundException)
				{
					Log.Debug("DirectoryExtensions", $"Directory '{currentDir}' not found.");
					continue; // Skip this directory if it's not found
				}

				// Get subdirectories and push them to the stack
				try
				{
					string[] subdirectories = Directory.GetDirectories(currentDir);
					foreach (string subdir in subdirectories)
					{
						directories.Push(subdir);
					}
				}
				catch (UnauthorizedAccessException)
				{
					Log.Debug("DirectoryExtensions", $"Access to directory '{currentDir}' is denied.");
					continue; // Skip subdirectories if access is denied
				}
				catch (DirectoryNotFoundException)
				{
					Log.Debug("DirectoryExtensions", $"Directory '{currentDir}' not found.");
					continue; // Skip subdirectories if the current directory is not found
				}
			}

			return files;
		}
	}
}