
namespace nji
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Ionic;
    using SimpleJson;

    class Program
    {
        private static readonly string ModulesDir;
        private static readonly string TempDir;

        static Program()
        {
            ModulesDir = ".\\node_modules";
            TempDir = Path.Combine(ModulesDir, ".tmp");
        }

        static void Main(string[] args)
        {
            CleanUpDir(TempDir);
            if (args.Length < 1)
                Usage();
            if (args[0] == "install")
            {
                if (args.Length < 2)
                    Usage();
                foreach (var i in args.Skip(1))
                {
                    Install(i);
                }
            }
            else if (args[0] == "deps")
            {
                Deps();
            }
            else if (args[0] == "update")
            {
                Update();
            }
            else
            {
                Usage();
            }

            CleanUpDir(TempDir);
            Console.WriteLine("All done");
        }

        private static void Update()
        {
            var pkgs = GetInstalled();
            foreach (var pkg in pkgs)
            {
                var meta = GetMetaDataForPkg((string)pkg["name"]);
                if (meta["version"].ToString() != pkg["version"].ToString())
                {
                    Install((string)pkg["name"]);
                }
            }
        }

        private static IList<IDictionary<string, object>> GetInstalled()
        {
            var dirs = Directory.GetDirectories(ModulesDir);
            var meta = new List<IDictionary<string, object>>();
            foreach (var dir in dirs)
            {
                var d = Path.Combine(dir, "package.json");
                if (!File.Exists(d))
                    continue;
                var data = File.ReadAllText(d);
                meta.Add((IDictionary<string, object>)SimpleJson.DeserializeObject(data));
            }
            return meta;
        }

        private static void Deps()
        {
            InstallDependencies(Environment.CurrentDirectory);
            Console.WriteLine("Dependencies done");
        }

        private static void CleanUpDir(string tempDir)
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        private static void Install(string pkg)
        {
            // Installs pkg(s) into ./node_modules
            var meta = GetMetaDataForPkg(pkg);
            var destPath = SaveAndExtractPackage(meta);
            InstallDependencies(destPath);
        }

        private static void InstallDependencies(string pkgDir)
        {
            // Recursively install dependencies
            var s = pkgDir.Split('\\');
            var packageJson = Path.Combine(pkgDir, "package.json");
            if (!File.Exists(packageJson))
                return;
            Console.WriteLine("Checking dependencies for {0} ...", s[s.Length - 1]);

            var metaData = (IDictionary<string, object>)SimpleJson.DeserializeObject(File.ReadAllText(packageJson));
            if (metaData.ContainsKey("dependencies") && !(metaData["dependencies"] is IList<object>))
            {
                foreach (var dep in (IDictionary<string, object>)metaData["dependencies"])
                {
                    Install(dep.Key);
                }
            }
        }

        private static string SaveAndExtractPackage(IDictionary<string, object> meta)
        {
            var pkgName = (string)meta["name"];
            var destPath = Path.Combine(ModulesDir, pkgName);
            var url = (string)((IDictionary<string, object>)meta["dist"])["tarball"];
            var urlSplit = url.Split('/');
            var filename = urlSplit[urlSplit.Length - 1];
            var tmpFilePath = Path.Combine(TempDir, filename);
            if (File.Exists(tmpFilePath)) // make sure we don't re-download and reinstall anything
                return destPath;
            Console.WriteLine("Installing {0} into {1} ...", url, destPath);
            CleanUpDir(destPath);
            if (Directory.Exists(destPath))
                Directory.Delete(destPath, true);
            if (!Directory.Exists(TempDir))
                Directory.CreateDirectory(TempDir);
            var tempPkgDir = Path.Combine(TempDir, pkgName);
            if (!Directory.Exists(tempPkgDir))
                Directory.CreateDirectory(tempPkgDir);

            new WebClient().DownloadFile(url, tmpFilePath);

            var workingDir = Environment.CurrentDirectory;
            Environment.CurrentDirectory = tempPkgDir;

            Tar.Extract(Path.Combine(workingDir, tmpFilePath));
            Environment.CurrentDirectory = workingDir;
            // getting error when doing move, so copy recursively instead
            CopyDirectory(Path.Combine(tempPkgDir, "package"), destPath, true);
            return destPath;
        }

        private static bool CopyDirectory(string SourcePath, string DestinationPath, bool overwriteexisting)
        {
            bool ret = false;
            try
            {
                SourcePath = SourcePath.EndsWith(@"\") ? SourcePath : SourcePath + @"\";
                DestinationPath = DestinationPath.EndsWith(@"\") ? DestinationPath : DestinationPath + @"\";

                if (Directory.Exists(SourcePath))
                {
                    if (Directory.Exists(DestinationPath) == false)
                        Directory.CreateDirectory(DestinationPath);

                    foreach (string fls in Directory.GetFiles(SourcePath))
                    {
                        FileInfo flinfo = new FileInfo(fls);
                        flinfo.CopyTo(DestinationPath + flinfo.Name, overwriteexisting);
                    }
                    foreach (string drs in Directory.GetDirectories(SourcePath))
                    {
                        DirectoryInfo drinfo = new DirectoryInfo(drs);
                        if (CopyDirectory(drs, DestinationPath + drinfo.Name, overwriteexisting) == false)
                            ret = false;
                    }
                }
                ret = true;
            }
            catch (Exception ex)
            {
                ret = false;
            }
            return ret;
        }
        private static IDictionary<string, object> GetMetaDataForPkg(string pkg)
        {
            var url = string.Format("http://registry.npmjs.org/{0}/latest", pkg);
            string response = null;
            try
            {
                response = new WebClient().DownloadString(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine("No module named {0} in package registry! Aborting!", pkg);
                Environment.Exit(-1);
            }
            return (IDictionary<string, object>)SimpleJson.DeserializeObject(response);
        }

        private static void Usage()
        {
            Console.WriteLine();
            Console.WriteLine(@"
Usage:
 nji deps                  - Install dependencies from package.json file
 nji install <pkg> [<pkg>] - Install package(s), and it's/there dependencies
 nji update                - Checks for different version of packages in online repository, and updates as needed
Example:
 nji install express socket.io mongolian underscore");

            Environment.Exit(0);
        }

        public static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[32768];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
            }
        }
    }
}
