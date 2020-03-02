﻿using BlazorMobile.Build.Cli.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorMobile.Build.Core
{
    internal static class PublishAndZipHelper
    {
        internal static string artifactName = "app";

        internal static string artifactZipName { get { return artifactName + ".zip"; } }

        private static void CopyStaticWebAssetsToDistDir(string distDir)
        {
            string staticWebAssetsManifest = Path.Combine(Path.GetFullPath(Path.Combine(distDir, "..")), artifactName + ".StaticWebAssets.xml");

            if (!File.Exists(staticWebAssetsManifest))
            {
                //If the *.StaticWebAssets.xml file does not exist, it may means that there is actually no Razor Class Library referenced
                //in the current project. We have nothing to do then.
                return;
            }

            Regex groupPathAndBasePath = new Regex("<ContentRoot BasePath=\"(?<BasePath>.+)\" Path=\"(?<Path>.+)\" />");

            string staticWebAssetManifestContent = File.ReadAllText(staticWebAssetsManifest);
            var matchCollection = groupPathAndBasePath.Matches(staticWebAssetManifestContent);

            //Respectively, Item1 is Source/Path, and Item2 is Destination/BasePath in dist folder
            List<Tuple<string, string>> sourceDestination = new List<Tuple<string, string>>();

            var groupList = matchCollection.Select(p => p.Groups).ToList();
            foreach (var group in groupList)
            {
                if (group.ContainsKey("BasePath") && group.ContainsKey("Path"))
                {
                    //If previous statement is true, they should exist
                    group.TryGetValue("BasePath", out Group basePathGroup);
                    group.TryGetValue("Path", out Group pathGroup);

                    //Destination dir as an absolute path should be the distDir + _content/xxx if the format has not changed
                    Tuple<string, string> pair = new Tuple<string, string>(pathGroup.Value, Path.Combine(distDir, basePathGroup.Value));
                    sourceDestination.Add(pair);
                }
            }

            #region Clearing folders

            //We should clear the _content folder generated by this script if already existing
            string baseContentFolder = Path.Combine(distDir, "_content");
            if (Directory.Exists(baseContentFolder))
            {
                try
                {
                    Directory.Delete(baseContentFolder, true);
                }
                catch (Exception)
                {
                }
            }

            try
            {
                Directory.CreateDirectory(baseContentFolder);
            }
            catch (Exception)
            {
                //Let's bubble up
                throw;
            }

            #endregion Clearing folders

            foreach (var sourceDestinationItem in sourceDestination)
            {
                DirectoryHelper.DirectoryCopy(sourceDestinationItem.Item1, sourceDestinationItem.Item2, true);
            }
        }

        public static void PublishAndZip(string inputFile, string outputPath, string distDir)
        {
            if (!File.Exists(inputFile) || !inputFile.EndsWith(".csproj", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new InvalidOperationException("The input file does not exist or is not a Blazor csproj file");
            }

            artifactName = Path.GetFileNameWithoutExtension(inputFile);

            outputPath = PathHelper.MSBuildQuoteFixer(outputPath);
            distDir = PathHelper.MSBuildQuoteFixer(distDir);

            //Warning invalid outputPath
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new InvalidOperationException("The outputPath is not set");
            }

            //If the base output directory does not exist, create it
            if (!Directory.Exists(outputPath))
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                }
                catch (Exception)
                {
                    //Let's bubble up
                    throw;
                }
            }

            //If the base ouput exist, assuming we need to clean everything in it if we do consecutive call
            ClearDirectory(outputPath);

            CopyStaticWebAssetsToDistDir(distDir);
            string wwwrootFolder = Path.GetDirectoryName(inputFile) + Path.DirectorySeparatorChar + "wwwroot";

            string artifactAbsolutePath = GetArtifactZipAbsolutePath(outputPath);

            //Zip only the good folder and create the zip in the right directory
            ZipAppFolder(distDir, wwwrootFolder, artifactAbsolutePath);

            //Show success message
            Console.WriteLine($"BlazorMobile Build result -> App package present in {artifactAbsolutePath}.");
        }


        /// <summary>
        /// Zip the given app folder, and return the 
        /// </summary>
        /// <param name="appFolder"></param>
        /// <returns></returns>
        private static void ZipAppFolder(string appFolder, string wwwFolder, string outputZip)
        {
            //ZipFile.CreateFromDirectory(appFolder, outputZip);

            using (FileStream zipToOpen = new FileStream(outputZip, FileMode.Create))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    //Adding files from dist folder
                    string[] distFilesToAdd = Directory.GetFiles(appFolder, "*.*", SearchOption.AllDirectories);

                    //Add files from wwwroot
                    string[] wwwrootFilesToAdd = Directory.GetFiles(wwwFolder, "*.*", SearchOption.AllDirectories);

                    //Merging results
                    var mergedFilesList = new List<string>();
                    mergedFilesList.AddRange(wwwrootFilesToAdd);
                    mergedFilesList.AddRange(distFilesToAdd);

                    foreach (string distFile in mergedFilesList)
                    {
                        string distFileEntry = string.Empty;

                        if (distFile.StartsWith(appFolder))
                        {
                            distFileEntry = distFile.Replace(appFolder, string.Empty).TrimStart('\\').TrimStart('/').Replace('\\', '/');
                        }
                        else if (distFile.StartsWith(wwwFolder))
                        {
                            distFileEntry = distFile.Replace(wwwFolder, string.Empty).TrimStart('\\').TrimStart('/').Replace('\\', '/');
                        }
                        else
                        {
                            //Sanity check. Should not happen
                            continue;
                        }

                        ZipArchiveEntry currentFile = archive.CreateEntry(distFileEntry);
                        using (Stream writer = currentFile.Open())
                        {
                            using (FileStream distFileStream = File.OpenRead(distFile))
                            {
                                distFileStream.CopyTo(writer);
                            }
                        }
                    }
                }
            }
        }

        private static string GetArtifactZipAbsolutePath(string directory)
        {
            directory = directory.TrimEnd(Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            return directory + Path.DirectorySeparatorChar + artifactZipName;
        }

        private static void ClearDirectory(string directoryToClear)
        {
            try
            {
                //Remove artifact result
                var absolutePathToArtifactZipFile = GetArtifactZipAbsolutePath(directoryToClear);
                if (File.Exists(absolutePathToArtifactZipFile))
                {
                    File.Delete(absolutePathToArtifactZipFile);
                }
            }
            catch (Exception)
            {
                //Bubble up
                throw;
            }
        }
    }
}
