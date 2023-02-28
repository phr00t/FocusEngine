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
        /*static Dictionary<string, string> assemblyLocation = new Dictionary<string, string>(), assemblyLocationWithoutVersion = new Dictionary<string, string>();

        static Dictionary<string, string> extraLocationsx86 = new Dictionary<string, string>();
        static Dictionary<string, string> extraLocations = new Dictionary<string, string>();*/
        static List<string> assemblies;

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
                // Check if already loaded.
                // Somehow it happens for Microsoft.NET.Build.Tasks -> NuGet.ProjectModel, probably due to the specific way it's loaded.
                var matchingAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.FullName == eventArgs.Name);
                if (matchingAssembly != null)
                    return matchingAssembly;

                if (!assembliesResolved)
                {
                    lock (assembliesLock)
                    {
                        // Note: using NuGet will try to recursively resolve NuGet.*.resources.dll, so set assembliesResolved right away so that it bypasses everything
                        assembliesResolved = true;

                        var logger = new Logger();

#if STRIDE_NUGET_RESOLVER_UX
                        var dialogNotNeeded = new TaskCompletionSource<bool>();
                        var dialogClosed = new TaskCompletionSource<bool>();

                        // Display splash screen after a 500 msec (when NuGet takes some time to restore)
                        var newWindowThread = new Thread(() =>
                        {
                            Thread.Sleep(500);
                            if (!dialogNotNeeded.Task.IsCompleted)
                            {
                                var splashScreen = new Stride.NuGetResolver.SplashScreenWindow();
                                splashScreen.Show();

                                // Register log
                                logger.SetupLogAction((level, message) =>
                                {
                                    splashScreen.Dispatcher.InvokeAsync(() =>
                                    {
                                        splashScreen.AppendMessage(level, message);
                                    });
                                });

                                dialogNotNeeded.Task.ContinueWith(t =>
                                {
                                    splashScreen.Dispatcher.Invoke(() => splashScreen.Close());
                                });

                                splashScreen.Closed += (sender2, e2) =>
                                    splashScreen.Dispatcher.InvokeShutdown();

                                System.Windows.Threading.Dispatcher.Run();

                                splashScreen.Close();
                            }
                            dialogClosed.SetResult(true);
                        });
                        newWindowThread.SetApartmentState(ApartmentState.STA);
                        newWindowThread.IsBackground = true;
                        newWindowThread.Start();
#endif

                        var previousSynchronizationContext = SynchronizationContext.Current;
                        try
                        {
                            // Since we execute restore synchronously, we don't want any surprise concerning synchronization context (i.e. Avalonia one doesn't work with this)
                            SynchronizationContext.SetSynchronizationContext(null);

                            // Determine current TFM
                            var framework = metadataAssembly
                                .GetCustomAttribute<TargetFrameworkAttribute>()?
                                .FrameworkName ?? ".NETFramework,Version=v4.7.2";
                            var nugetFramework = NuGetFramework.ParseFrameworkName(framework, DefaultFrameworkNameProvider.Instance);

#if NETCOREAPP
                            // Add TargetPlatform to net6.0 TFM (i.e. net6.0 to net6.0-windows7.0)
                            var platform = metadataAssembly?.GetCustomAttribute<TargetPlatformAttribute>()?.PlatformName ?? string.Empty;
                            if (framework.StartsWith(FrameworkConstants.FrameworkIdentifiers.NetCoreApp) && platform != string.Empty)
                            {
                                var platformParseResult = Regex.Match(platform, @"([a-zA-Z]+)(\d+.*)");
                                if (platformParseResult.Success && Version.TryParse(platformParseResult.Groups[2].Value, out var platformVersion))
                                {
                                    var platformName = platformParseResult.Groups[1].Value;
                                    nugetFramework = new NuGetFramework(nugetFramework.Framework, nugetFramework.Version, platformName, platformVersion);
                                }
                            }
#endif

                            // Only allow this specific version
                            var versionRange = new VersionRange(new NuGetVersion(packageVersion), true, new NuGetVersion(packageVersion), true);
                            var (request, result) = RestoreHelper.Restore(logger, nugetFramework, "win", packageName, versionRange);
                            if (!result.Success)
                            {
                                throw new InvalidOperationException($"Could not restore NuGet packages");
                            }

                            assemblies = RestoreHelper.ListAssemblies(result.LockFile);
                        }
                        catch (Exception e)
                        {
#if STRIDE_NUGET_RESOLVER_UX
                            logger.LogError($@"Error restoring NuGet packages: {e}");
                            dialogClosed.Task.Wait();
#else
                            // Display log in console
                            var logText = $@"Error restoring NuGet packages!
==== Exception details ====
{e}
==== Log ====
{string.Join(Environment.NewLine, logger.Logs.Select(x => $"[{x.Level}] {x.Message}"))}
";
                            Console.WriteLine(logText);
#endif
                            Environment.Exit(1);
                        }
                        finally
                        {
#if STRIDE_NUGET_RESOLVER_UX
                            dialogNotNeeded.TrySetResult(true);
#endif
                            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
                        }
                    }
                }

                if (assemblies != null)
                {
                    var aname = new AssemblyName(eventArgs.Name);
                    if (aname.Name.StartsWith("Microsoft.Build") && aname.Name != "Microsoft.Build.Locator")
                        return null;
                    var assemblyPath = assemblies.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == aname.Name);
                    if (assemblyPath != null)
                    {
                        return Assembly.LoadFrom(assemblyPath);
                    }
                }
                return null;

                /*lock (assembliesLock)
                {
                    if (!assembliesResolved)
                    {
                        assembliesResolved = true;
                        var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
                        var packageSourceProvider = new PackageSourceProvider(settings);
                        var installPath = SettingsUtility.GetGlobalPackagesFolder(settings);
                        var assemblies = new List<string>(Directory.GetFiles(installPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));
                        assemblies.AddRange(Directory.GetFiles(installPath, "*.exe", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));

                        var extraPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\dotnet\\shared\\Microsoft.NETCore.App";
                        var extraAssemblies = new List<string>(Directory.GetFiles(extraPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));

                        var extraPath86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\dotnet\\shared\\Microsoft.NETCore.App";
                        var extraAssemblies86 = new List<string>(Directory.GetFiles(extraPath, "*.dll", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).CreationTime));

                        // grab any latest numerics from here
                        for (int i=0; i<extraAssemblies.Count; i++)
                        {
                            var ea = extraAssemblies[i];
                            var assName = Path.GetFileNameWithoutExtension(ea);

                            if (extraLocations.ContainsKey(assName) == false)
                            {
                                // find the right match
                                System.Console.Error.WriteLine("found extra: " + ea);

                                extraLocations[assName] = ea;
                            }
                        }

                        // grab any latest numerics from here
                        for (int i = 0; i < extraAssemblies86.Count; i++)
                        {
                            var ea = extraAssemblies86[i];
                            var assName = Path.GetFileNameWithoutExtension(ea);

                            if (extraLocationsx86.ContainsKey(assName) == false)
                            {
                                // find the right match
                                System.Console.Error.WriteLine("found extra: " + ea);

                                extraLocationsx86[assName] = ea;
                            }
                        }

                        // only include stuff we want
                        for (int i=0; i<assemblies.Count; i++)
                        {
                            var assembly = assemblies[i];

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

                            if (assemblyLocationWithoutVersion.ContainsKey(assmName) == false)
                                assemblyLocationWithoutVersion[assmName] = assembly;

                            if (assemblyLocation.ContainsKey(key) == false)
                            {
                                // find the right match
                                System.Console.Error.WriteLine("Added: " + assembly + " key " + key);

                                assemblyLocation[key] = assembly;
                            }
                        }
                    }

                    if (assembliesResolved)
                    {
                        var aname = new AssemblyName(eventArgs.Name);
                        if (aname.Name.StartsWith("Microsoft.Build") && aname.Name != "Microsoft.Build.Locator")
                            return null;

                        List<string> keysToTry = new List<string>();
                        string assemblyPath;

                        if (aname.Name == "System.Numerics.Vectors")
                        {
                            // find the right match
                            System.Console.Error.WriteLine("numerics version: " + aname.Version.ToString());

                            keysToTry.Add(aname.Name + "-4.4.0.0");
                            keysToTry.Add(aname.Name + "-4.5.0.0");
                        } else keysToTry.Add(aname.Name + "-" + aname.Version.ToString());

                        for (int i=0; i<keysToTry.Count; i++)
                        {
                            var key = keysToTry[i];

                            // find the right match
                            System.Console.Error.WriteLine("Trying to load: " + key);

                            if (assemblyLocation.TryGetValue(key, out assemblyPath))
                            {
                                try
                                {
                                    return Assembly.LoadFrom(assemblyPath);
                                } 
                                catch (Exception e) { } // try another key
                            }
                        }
                        
                        try
                        {
                            // if this is numerics, try loading the latest fallback
                            if (extraLocationsx86.TryGetValue(aname.Name, out assemblyPath))
                            {
                                System.Console.Error.WriteLine("Fallback extra86 version for " + aname.Name + ", version " + aname.Version.ToString());

                                return Assembly.LoadFrom(assemblyPath);
                            }
                        } catch (Exception e) { }

                        try
                        {
                            // if this is numerics, try loading the latest fallback
                            if (extraLocations.TryGetValue(aname.Name, out assemblyPath))
                            {
                                System.Console.Error.WriteLine("Fallback extra version for " + aname.Name + ", version " + aname.Version.ToString());

                                return Assembly.LoadFrom(assemblyPath);
                            }
                        }
                        catch (Exception e) { }

                        // fallback to no version attempt to load
                        try
                        {
                            if (assemblyLocationWithoutVersion.TryGetValue(aname.Name, out assemblyPath))
                            {
                                System.Console.Error.WriteLine("Fallback without version for " + aname.Name + ", version " + aname.Version.ToString());

                                return Assembly.LoadFrom(assemblyPath);
                            }
                        }
                        catch (Exception e) { }
                    }
                }

                System.Console.Error.WriteLine("Couldn't find any working package...");

                return null;*/
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
