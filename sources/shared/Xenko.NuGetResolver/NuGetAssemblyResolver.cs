// Copyright (c) Xenko contributors (https://xenko.com)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Versioning;

/*
 *  System.Private.CoreLib is only being found in (x86) directories?
 *    - need to add debug lines for every library found.. see whats happening this this library
 *    - library fails because it can't find x64 version
 *    - sort things x64 to the top?
 */

namespace Xenko.Core.Assets
{
    class NuGetAssemblyResolver
    {
        const bool DEBUG_RESOLVE = false;

        static bool assembliesResolved;
        static object assembliesLock = new object();
        static Dictionary<string, List<LibraryInfo>> allAssemblies = new Dictionary<string, List<LibraryInfo>>();

        internal static void DisableAssemblyResolve()
        {
            assembliesResolved = true;
        }

        internal class LibraryInfo : IComparable<LibraryInfo>
        {
            public string Name;
            public string Version;
            public string FullPath;
            public int LibraryRating;
            public int[] versionInfo;

            public LibraryInfo(string vers)
            {
                Version = vers;
                string[] sv = vers.Split('.');
                versionInfo = new int[sv.Length];
                try
                {
                    for (int i = 0; i < sv.Length; i++)
                        versionInfo[i] = int.Parse(sv[i]);
                }
                catch (Exception e)
                {
                    if (DEBUG_RESOLVE)
                        System.Console.Error.WriteLine("Couldn't parse version: " + vers);
                }
            }

            public int CompareTo(object obj)
            {
                if (obj is LibraryInfo li)
                    return li.CompareTo(this);

                return 0;
            }

            public int CompareTo(LibraryInfo other)
            {
                if (LibraryRating > other.LibraryRating) return -1;
                if (LibraryRating < other.LibraryRating) return 1;
                for (int i = 0; i < versionInfo.Length && i < other.versionInfo.Length; i++)
                {
                    if (versionInfo[i] > other.versionInfo[i]) return -1;
                    if (versionInfo[i] < other.versionInfo[i]) return 1;
                }
                return 0;
            }

            public static int GetLibraryRating(string path)
            {
                if (path.Contains("net48"))
                    return 10;
                if (path.Contains("net47"))
                    return 9;
                if (path.Contains("net46"))
                    return 8;
                if (path.Contains("net4"))
                    return 7;
                if (path.Contains("net3"))
                    return 6;
                if (path.Contains("net2"))
                    return 5;
                if (path.Contains("net1"))
                    return 4;
                if (path.Contains("netstandard2.0"))
                    return 2;
                if (path.Contains("netstandard"))
                    return 1;
                if (path.Contains("net6.0"))
                    return 0;
                if (path.Contains("net5.0"))
                    return -1;
                return -2;
            }
        }

        [ModuleInitializer(-100000)]
        internal static void __Initialize__()
        {
            // Only perform this for entry assembly
            if (!(Assembly.GetEntryAssembly() == null // .NET FW: null during module .ctor
                || Assembly.GetEntryAssembly() == Assembly.GetCallingAssembly())) // .NET Core: check against calling assembly
                return;

            // delete old temp files if we can
            var dirs = Directory.GetDirectories(Path.GetTempPath(), "Xenko*");
            if (dirs != null)
            {
                foreach (string s in dirs)
                {
                    try
                    {
                        Directory.Delete(s, true);
                    } catch (Exception e) { 
                        // might have been in use, oh well
                    }
                }
            }

            // Make sure our nuget local store is added to nuget config
            var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string xenkoFolder = null;
            while (folder != null)
            {
                if (File.Exists(Path.Combine(folder, @"build\Xenko.sln")))
                {
                    xenkoFolder = folder;
                    var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
                    // Remove non-existing sources: https://github.com/xenko3d/xenko/issues/338
                    RemoveDeletedSources(settings, "Xenko");
                    CheckPackageSource(settings, $"Xenko Dev {xenkoFolder}", Path.Combine(xenkoFolder, @"bin\packages"));
                    settings.SaveToDisk();
                    break;
                }
                folder = Path.GetDirectoryName(folder);
            }
            string xenkoLibFolder = xenkoFolder == null ? null : Path.Combine(xenkoFolder, @"sources\editor\Xenko.GameStudio\bin\Release");

            // Note: we perform nuget restore inside the assembly resolver rather than top level module ctor (otherwise it freezes)
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                lock (assembliesLock)
                {
                    if (!assembliesResolved)
                    {
                        assembliesResolved = true;

                        var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
                        var packageSourceProvider = new PackageSourceProvider(settings);

                        var installPath = SettingsUtility.GetGlobalPackagesFolder(settings);
                        var extraPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\dotnet\\shared\\Microsoft.NETCore.App";
                        var extraPath86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\dotnet\\shared\\Microsoft.NETCore.App";

                        var nugetAssemblies = new List<string>(Directory.GetFiles(installPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));
                        nugetAssemblies.AddRange(Directory.GetFiles(installPath, "*.exe", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));
                        nugetAssemblies.AddRange(Directory.GetFiles(extraPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));
                        nugetAssemblies.AddRange(Directory.GetFiles(extraPath86, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));

                        if (xenkoLibFolder != null)
                            nugetAssemblies.AddRange(Directory.GetFiles(xenkoLibFolder, "*.dll", SearchOption.AllDirectories));

                        // only include stuff we want
                        for (int i=0; i<nugetAssemblies.Count; i++)
                        {
                            var assembly = nugetAssemblies[i];

                            if (assembly.Contains("linux") || assembly.Contains("osx") || 
                                assembly.Contains("\\runtimes\\") && !assembly.Contains("xenko") ||
                                assembly.Contains("\\ref\\"))
                                continue;

                            LibraryInfo li;

                            var assmName = Path.GetFileNameWithoutExtension(assembly);

                            if (assembly.Contains("Xenko.GameStudio"))
                            {
                                // xenko
                                li = new LibraryInfo("9.9.1.0")
                                {
                                    FullPath = assembly,
                                    Name = assmName,
                                    LibraryRating = 15                                     
                                };
                            } 
                            else
                            {
                                var dir = assembly.Split('\\');
                                if (assembly.Contains("dotnet\\shared"))
                                {
                                    // dotnetshared
                                    var version = dir[dir.Length - 2] + ".0";
                                    li = new LibraryInfo(version)
                                    {
                                        FullPath = assembly,
                                        Name = assmName,
                                        LibraryRating = LibraryInfo.GetLibraryRating(assembly)
                                    };
                                } 
                                else
                                {
                                    // nuget stuff
                                    var version = dir[Array.IndexOf(dir, "packages") + 2].Split('.', '-');
                                    var versionProcessed = version[0] + "." + version[1] + "." + version[2];
                                    if (version.Length > 3 && int.TryParse(version[3], out int res))
                                        versionProcessed += "." + res;
                                    else
                                        versionProcessed += ".0";

                                    li = new LibraryInfo(versionProcessed)
                                    {
                                        Name = assmName,
                                        FullPath = assembly,
                                        LibraryRating = LibraryInfo.GetLibraryRating(assembly)
                                    };
                                }
                            }

                            if (allAssemblies.TryGetValue(assmName, out var sorted))
                            {
                                if (DEBUG_RESOLVE)
                                    System.Console.Error.WriteLine("[" + assmName + "] added with " + li.FullPath);

                                sorted.Add(li);
                            }
                            else
                            {
                                if (DEBUG_RESOLVE)
                                    System.Console.Error.WriteLine("[" + assmName + "] Started with " + li.FullPath);

                                allAssemblies[assmName] = new List<LibraryInfo>() { li };
                            }
                        }

                        // sort all the lists
                        foreach (var l in allAssemblies.Values)
                            l.Sort();
                    }

                    if (assembliesResolved)
                    {
                        var aname = new AssemblyName(eventArgs.Name);
                        if (aname.Name.StartsWith("Microsoft.Build") && aname.Name != "Microsoft.Build.Locator")
                            return null;

                        if (allAssemblies.TryGetValue(aname.Name, out var libs))
                        {
                            if (DEBUG_RESOLVE)
                                System.Console.Error.WriteLine("[" + eventArgs.Name + "] Found with " + libs.Count + " libraries.");

                            List<string> versionsToTry = new List<string>();

                            if (aname.Name == "System.Numerics.Vectors")
                            {
                                versionsToTry.Add("4.4.0.0");
                                versionsToTry.Add("4.5.0.0");
                            }
                            else versionsToTry.Add(aname.Version.ToString());

                            for (int i=0; i<versionsToTry.Count; i++)
                            {
                                for (int j=0; j<libs.Count; j++)
                                {
                                    var libToTry = libs[j];
                                    if (libToTry.Version != versionsToTry[i]) continue;
                                    try
                                    {
                                        if (DEBUG_RESOLVE)
                                            System.Console.Error.WriteLine("[" + eventArgs.Name + "] Trying version specific load: " + libToTry.FullPath);

                                        return Assembly.LoadFrom(libToTry.FullPath);
                                    } catch(Exception) { }
                                }
                            }

                            // versions didn't work... try them in order
                            for (int i=0; i<libs.Count; i++)
                            {
                                try
                                {
                                    if (DEBUG_RESOLVE)
                                        System.Console.Error.WriteLine("[" + eventArgs.Name + "] Trying version priority load: " + libs[i].FullPath);

                                    return Assembly.LoadFrom(libs[i].FullPath);
                                }
                                catch (Exception) { }
                            }
                        }
                    }
                }

                if (DEBUG_RESOLVE)
                    System.Console.Error.WriteLine("[" + eventArgs.Name + "] No library found!");

                return null;
            };
        }

        private static void RemoveDeletedSources(ISettings settings, string prefixName)
        {
            var packageSources = settings.GetSection("packageSources");
            if (packageSources != null)
            {
                foreach (var packageSource in packageSources.Items.OfType<SourceItem>().ToList())
                {
                    var path = packageSource.GetValueAsPath();

                    if (packageSource.Key.StartsWith(prefixName)
                        && Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile // make sure it's a valid file URI
                        && !Directory.Exists(path)) // detect if directory has been deleted
                    {
                        // Remove entry from packageSources
                        settings.Remove("packageSources", packageSource);
                    }
                }
            }
        }

        private static void CheckPackageSource(ISettings settings, string name, string url)
        {
            settings.AddOrUpdate("packageSources", new SourceItem(name, url));
        }

        public class Logger : ILogger
        {
            private object logLock = new object();
            public List<(LogLevel Level, string Message)> Logs { get; } = new List<(LogLevel, string)>();

            public void LogDebug(string data)
            {
                Log(LogLevel.Debug, data);
            }

            public void LogVerbose(string data)
            {
                Log(LogLevel.Verbose, data);
            }

            public void LogInformation(string data)
            {
                Log(LogLevel.Information, data);
            }

            public void LogMinimal(string data)
            {
                Log(LogLevel.Minimal, data);
            }

            public void LogWarning(string data)
            {
                Log(LogLevel.Warning, data);
            }

            public void LogError(string data)
            {
                Log(LogLevel.Error, data);
            }

            public void LogInformationSummary(string data)
            {
                Log(LogLevel.Information, data);
            }

            public void LogErrorSummary(string data)
            {
                Log(LogLevel.Error, data);
            }

            public void Log(LogLevel level, string data)
            {
                lock (logLock)
                {
                    Debug.WriteLine($"[{level}] {data}");
                    Logs.Add((level, data));
                }
            }

            public Task LogAsync(LogLevel level, string data)
            {
                Log(level, data);
                return Task.CompletedTask;
            }

            public void Log(ILogMessage message)
            {
                Log(message.Level, message.Message);
            }

            public Task LogAsync(ILogMessage message)
            {
                Log(message);
                return Task.CompletedTask;
            }
        }
    }
}
