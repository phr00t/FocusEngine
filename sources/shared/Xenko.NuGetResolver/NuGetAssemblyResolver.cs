// Copyright (c) Xenko contributors (https://xenko.com)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Xenko.Core.Assets
{
    class NuGetAssemblyResolver
    {
        static bool assembliesResolved;
        static object assembliesLock = new object();
        static Dictionary<string, string> assemblyLocationWithVersion = new Dictionary<string, string>();
        static Dictionary<string, List<string>> allAssemblies = new Dictionary<string, List<string>>();

        internal static void DisableAssemblyResolve()
        {
            assembliesResolved = true;
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
                        var nugetAssemblies = new List<string>(Directory.GetFiles(installPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));
                        nugetAssemblies.AddRange(Directory.GetFiles(installPath, "*.exe", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));

                        var extraPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\dotnet\\shared\\Microsoft.NETCore.App";
                        var extraPath86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\dotnet\\shared\\Microsoft.NETCore.App";
                        var extraAssemblies = new List<string>(Directory.GetFiles(extraPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));
                        extraAssemblies.AddRange(Directory.GetFiles(extraPath86, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));

                        // only include stuff we want
                        for (int i=0; i<nugetAssemblies.Count; i++)
                        {
                            var assembly = nugetAssemblies[i];

                            if (assembly.Contains("linux") || assembly.Contains("osx"))
                                continue;

                            var dir = assembly.Split('\\');
                            var version = dir[Array.IndexOf(dir, "packages") + 2].Split('.', '-');
                            var assmName = Path.GetFileNameWithoutExtension(assembly);
                            var versionProcessed = version[0] + "." + version[1] + "." + version[2];
                            if (version.Length > 3 && int.TryParse(version[3], out int res))
                                versionProcessed += "." + res;
                            else
                                versionProcessed += ".0";
                            var key = assmName + "-" + versionProcessed;

                            if (assemblyLocationWithVersion.ContainsKey(key) == false)
                                assemblyLocationWithVersion[key] = assembly;

                            if (allAssemblies.TryGetValue(assmName, out var list))
                                list.Add(assembly);
                            else
                                allAssemblies[assmName] = new List<string>() { assembly };
                        }

                        // get the rest of assembly versions
                        for (int i=0; i<extraAssemblies.Count; i++)
                        {
                            var assembly = extraAssemblies[i];
                            var assmName = Path.GetFileNameWithoutExtension(assembly);

                            if (allAssemblies.TryGetValue(assmName, out var list))
                                list.Add(assembly);
                            else
                                allAssemblies[assmName] = new List<string>() { assembly };
                        }
                    }

                    if (assembliesResolved)
                    {
                        var aname = new AssemblyName(eventArgs.Name);
                        if (aname.Name.StartsWith("Microsoft.Build") && aname.Name != "Microsoft.Build.Locator")
                            return null;

                        List<string> keysToTry = new List<string>();
                        string assemblyPath;

                        // numerics has terrible version naming
                        if (aname.Name == "System.Numerics.Vectors")
                        {
                            keysToTry.Add(aname.Name + "-4.4.0.0");
                            keysToTry.Add(aname.Name + "-4.5.0.0");
                        } else keysToTry.Add(aname.Name + "-" + aname.Version.ToString());

                        for (int i=0; i<keysToTry.Count; i++)
                        {
                            var key = keysToTry[i];

                            if (assemblyLocationWithVersion.TryGetValue(key, out assemblyPath))
                            {
                                try
                                {
                                    return Assembly.LoadFrom(assemblyPath);
                                } 
                                catch (Exception e) { } // try another key
                            }
                        }                       

                        // fallback searching all assemblies
                        if (allAssemblies.TryGetValue(aname.Name, out var list))
                        {
                            for(int i=0; i<list.Count; i++)
                            {
                                try
                                {
                                    if ((aname.Version.Major == 6 || list[i].Contains("\\6.")) && list[i].Contains("runtimes")) continue; // version 6 stuff don't use runtime libraries
                                    if (list[i].Contains("\\ref\\") || list[i].Contains("netcoreapp3.1")) continue; // don't use these things in backups
                                    return Assembly.LoadFrom(list[i]);
                                }
                                catch (Exception e) { }
                            }
                        }
                    }
                }
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
