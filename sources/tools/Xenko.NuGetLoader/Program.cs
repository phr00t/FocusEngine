using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xenko.Core.Assets;

namespace Xenko.NuGetLoader
{
    class Program
    {
#if XENKO_STA_THREAD_ATTRIBUTE_ON_MAIN
        [STAThread]
#endif
        static void Main(string[] args)
        {
            // Get loader data (text file, format is "PackageName/PackageId")
            var loaderDataFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Xenko.NuGetLoader.loaderdata");
            var loaderData = File.ReadLines(loaderDataFile).First().Split('/');

            var packageName = loaderData[0];
            var packageVersion = loaderData[1];

            NuGetAssemblyResolver.SetupNuGet(packageName, packageVersion);
            AppDomain.CurrentDomain.ExecuteAssemblyByName(packageName, args);
        }
    }
}
