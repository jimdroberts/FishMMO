#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using FishMMO.Logging;

namespace FishMMO.Shared.CustomBuildTool
{
    /// <summary>
    /// Generates a link.xml file for managed assemblies, preserving all types and namespaces.
    /// </summary>
    public class LinkerGenerator : ILinkerGenerator
    {
        /// <summary>
        /// Generates a link.xml file for managed assemblies, preserving all types and namespaces.
        /// </summary>
        /// <param name="rootPath">Root path for the link.xml file.</param>
        /// <param name="directoryPath">Directory to scan for assemblies (currently unused).</param>
        public void GenerateLinker(string rootPath, string directoryPath)
        {
            string linkerPath = Path.Combine(rootPath, "link.xml");

            try
            {
                // Create a new XML document
                XmlDocument xmlDoc = new XmlDocument();

                // Create the root element named "linker"
                XmlElement rootElement = xmlDoc.CreateElement("linker");
                xmlDoc.AppendChild(rootElement);

                HashSet<string> nameSpaces = new HashSet<string>();

                // Get all loaded assemblies in the current AppDomain
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in assemblies)
                {
                    XmlElement assemblyElement = xmlDoc.CreateElement("assembly");
                    assemblyElement.SetAttribute("fullname", assembly.FullName);
                    rootElement.AppendChild(assemblyElement);

                    // Get all unique namespaces in the assembly
                    var assemblyNameSpaces = assembly.GetTypes()
                        .Select(type => type.Namespace)
                        .Where(n => !string.IsNullOrEmpty(n) && !nameSpaces.Contains(n))
                        .Distinct()
                        .OrderBy(n => n);

                    foreach (string nameSpace in assemblyNameSpaces)
                    {
                        nameSpaces.Add(nameSpace);

                        XmlElement typeElement = xmlDoc.CreateElement("type");
                        typeElement.SetAttribute("fullname", nameSpace);
                        typeElement.SetAttribute("preserve", "all");
                        assemblyElement.AppendChild(typeElement);
                    }
                }

                // Save the XML document to the specified file
                xmlDoc.Save(linkerPath);

                Log.Debug("LinkerGenerator", $"link.xml file generated successfully at '{linkerPath}'.");
            }
            catch (Exception ex)
            {
                Log.Error("LinkerGenerator", $"An error occurred while generating link.xml: {ex.Message}");
            }
        }
    }
}
#endif