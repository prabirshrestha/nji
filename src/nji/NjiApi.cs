
namespace nji
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentHttp;
    using SimpleJson;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Linq;

    public class NjiApi
    {
        public string WorkingDirectory { get; set; }
        public string TempDir { get; set; }

        public string RegistryUrlBase { get; set; }
        public TextWriter Out { get; set; }
        public int Verbose { get; set; }

        public Func<string, HttpWebRequestWrapper> HttpWebRequestFactory { get; set; }

        public NjiApi()
        {
            RegistryUrlBase = "http://registry.npmjs.org/";
            HttpWebRequestFactory = url => new HttpWebRequestWrapper((HttpWebRequest)WebRequest.Create(url));
            Out = Console.Out;
            WorkingDirectory = ".\\";
            TempDir = ".\\node_modules\\.tmp";
            Verbose = 1;
        }

        /// <summary>
        /// Installs the specified package
        /// </summary>
        /// <param name="package"></param>
        /// <returns></returns>
        /// <remarks>
        /// 
        ///  currently only a, c, d, e and f are supported
        ///  
        /// package:
        ///     a) null or empty: a folder containing a program described by a package.json file
        ///     b) tarball file: a gzipped tarball containing (a)
        ///     c) tarball url: a url that resolves to (b)
        ///     d) name@version: a name@version that is published on the registry with (c)
        ///     e) name@tag: a name@tag that points to (d)
        ///     f) name: a name that has a "latest" tag satisfying (e)
        ///     g) a 'git remote url' that resolves to (b)
        ///     
        /// </remarks>
        public virtual Task InstallAsync(string package, CancellationToken cancellationToken, bool installDependencies = true)
        {
            if (string.IsNullOrWhiteSpace(package))
            {
                // use package.json from the working directory
                var packageJson = Path.Combine(WorkingDirectory, "package.json");
                dynamic metadata = null;
                if (File.Exists(packageJson))
                {
                    metadata = SimpleJson.DeserializeObject(File.ReadAllText(packageJson));
                }

                if (Verbose > 0 && metadata == null)
                    Out.WriteLine("Nothing to install");

                return InstallDependencies(metadata, cancellationToken, installDependencies);
            }
            else if (package.StartsWith("http://") || package.StartsWith("https://"))
            {
                // package is a url (npm install http://registry.npmjs.org/easy/-/easy-0.0.1.tgz)

                var packageFilename = package.Substring(package.LastIndexOf("/") + 1);
                var packageName = Path.GetFileNameWithoutExtension(packageFilename);
                var destinationDir = Path.Combine(TempDir, packageName);

                if (Verbose > 0)
                    Out.WriteLine("Downloading {0}", package);
                var task =
                    DownloadAsync(package, TempDir, packageFilename, cancellationToken)
                    .ContinueWith(downloadTask =>
                    {
                        if (downloadTask.IsFaulted)
                            throw downloadTask.Exception;

                        var downloadFilePath = downloadTask.Result;
                        return Extract(downloadFilePath, destinationDir);
                    }).Unwrap()
                    .ContinueWith(extractTask =>
                    {
                        if (extractTask.IsFaulted)
                            throw extractTask.Exception;

                        var packageExtractedDir = Directory.GetDirectories(destinationDir)[0];
                        string packageJson = Path.Combine(packageExtractedDir, "package.json");

                        dynamic metadata = null;
                        if (File.Exists(Path.Combine(packageJson)))
                        {
                            metadata = SimpleJson.DeserializeObject(File.ReadAllText(packageJson));
                            if (metadata.ContainsKey("name"))
                            {
                                packageName = metadata.name;
                            }
                        }

                        var moduleFinalDir = Path.Combine(WorkingDirectory, "node_modules", packageName);
                        CleanDir(moduleFinalDir);
                        Directory.Move(Directory.GetDirectories(destinationDir)[0], moduleFinalDir);

                        if (Verbose > 0)
                            Out.WriteLine("Successfully installed {0} in {1}", packageName, moduleFinalDir);

                        return (Task)InstallDependencies(metadata, cancellationToken, installDependencies);
                    }).Unwrap();

                return task;
            }
            else if (package.Contains("/") || package.Contains("\\"))
            {
                //package is a local folder/file
                throw new NotSupportedException();
            }
            else
            {
                //package is a name
                if (Verbose > 1)
                    Out.WriteLine("Retrieving metadata for {0} ...", package);

                var task = GetPackageMetadataAsync(package)
                    .ContinueWith(metadataTask =>
                    {
                        if (metadataTask.IsFaulted)
                        {
                            throw metadataTask.Exception;
                        }

                        dynamic metadata = metadataTask.Result;
                        string tarballUrl = metadata.dist.tarball;

                        // recursively reuse InstallAsync :) InstallAsync accepts url
                        return InstallAsync(tarballUrl, cancellationToken, installDependencies);

                    }).Unwrap();

                return task;
            }
        }

        public virtual Task InstallAsync(string package)
        {
            return InstallAsync(package, CancellationToken.None, true);
        }

        public virtual Task InstallAsync(IEnumerable<string> packages, CancellationToken cancellationToken, bool installDependencies = true)
        {
            if (packages == null || packages.Count() == 0)
            {
                throw new ArgumentNullException("packages");
            }

            Task task = null;
            foreach (var package in packages)
            {
                if (task == null)
                    task = InstallAsync(package, cancellationToken, installDependencies);
                else
                    task = task.ContinueWith(t2 =>
                    {
                        return InstallAsync(package, cancellationToken, installDependencies);
                    }).Unwrap();
            }

            return task;
        }

        public virtual Task InstallAsync(IEnumerable<string> packages)
        {
            return InstallAsync(packages, CancellationToken.None, true);
        }

        private Task InstallDependencies(object metadata, CancellationToken cancellationToken, bool installDependencies = true)
        {
            if (metadata == null || !installDependencies)
            {
                return NoopTask();
            }

            var packageName = string.Empty;

            dynamic meta = metadata;
            if (meta.ContainsKey("name"))
            {
                packageName = meta.name;
            }

            if (Verbose > 0)
                Out.WriteLine("Checking dependencies for {0} ...", packageName);

            if (meta.ContainsKey("dependencies"))
            {
                var dependencies = meta.dependencies as IDictionary<string, object>;
                if (dependencies == null || dependencies.Count == 0)
                {
                    return NoopTask();
                }

                Task t = null;
                foreach (var package in dependencies)
                {
                    var version = package.Value as string;
                    var packageToInstall = package.Key;
                    if (version != null)
                    {
                        if (!Regex.Match(version, @"^[\.\da-zA-Z]*$").Success)
                        {
                            Out.WriteLine("Not smart enough to understand version '{0}', so using 'latest' instead for package '{1}'.", version, packageName);
                            version = "latest";
                        }
                        packageToInstall = string.Concat(packageToInstall, "@", version);
                    }

                    if (t == null)
                        t = InstallAsync(packageToInstall, cancellationToken, installDependencies);
                    else
                        t = t.ContinueWith(t2 =>
                        {
                            return InstallAsync(packageToInstall, cancellationToken, installDependencies);
                        }).Unwrap();
                }

                if (t != null) return t;
            }

            return NoopTask();
        }

        public virtual Task<object> GetPackageMetadataAsync(string package, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace("package"))
            {
                throw new ArgumentNullException("package");
            }

            var split = package.Split(new[] { '@' }, 2);
            var packageName = split[0];
            var packageVersion = split.Length > 1 ? split[1] : "latest";

            string url = string.Format(RegistryUrlBase + "{0}/{1}", packageName, packageVersion);

            var request = HttpWebRequestFactory(url);
            request.Method = "GET";

            var httpHelper = new HttpHelper(request);
            return httpHelper
                .OpenReadTaskAsync(cancellationToken)
                .ContinueWith<object>(task =>
                {
                    // todo: handle errors
                    var response = httpHelper.HttpWebResponse;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var responseStream = task.Result;
                        var jsonStr = ToString(responseStream);
                        return SimpleJson.DeserializeObject(jsonStr);
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new NjiPackageNotFoundException(packageName, packageVersion);
                    }
                    else
                    {
                        throw new NjiException("Error occurred.");
                    }
                });
        }

        public virtual Task<object> GetPackageMetadataAsync(string package)
        {
            return GetPackageMetadataAsync(package, CancellationToken.None);
        }

        public virtual Task<IDictionary<string, object>> GetInstalledPackagesAsync(CancellationToken cancellationToken)
        {
            var nodeModulesDir = Path.Combine(WorkingDirectory, "node_modules");
            var tcs = new TaskCompletionSource<IDictionary<string, object>>();

            var modules = new Dictionary<string, object>();
            if (Directory.Exists(nodeModulesDir))
            {
                // todo: async
                foreach (var module in Directory.GetDirectories(nodeModulesDir))
                {
                    var packageJson = Path.Combine(module, "package.json");
                    if (!File.Exists(packageJson))
                        continue;
                    dynamic metadata = SimpleJson.DeserializeObject(File.ReadAllText(packageJson));
                    if (metadata.ContainsKey("name"))
                    {
                        modules.Add(metadata.name, metadata);
                    }
                }
            }

            tcs.SetResult(modules);

            return tcs.Task;
        }

        public virtual Task<IDictionary<string, object>> GetInstalledPackagesAsync()
        {
            return GetInstalledPackagesAsync(CancellationToken.None);
        }

        public virtual Task UpdateAsync(IEnumerable<object> metadata, CancellationToken cancellationToken)
        {
            Task t = null;
            foreach (dynamic package in metadata)
            {
                if (t == null)
                {
                    t = GetPackageMetadataAsync((string)package.name, cancellationToken)
                        .ContinueWith(t2 =>
                        {
                            return InstallAsync((string)package.name, cancellationToken);
                        }).Unwrap();
                }
                else
                {
                    t = t.ContinueWith(t2 =>
                        {
                            return GetPackageMetadataAsync((string)package.name, cancellationToken);
                        }).Unwrap()
                        .ContinueWith(t2 =>
                        {
                            return InstallAsync((string)package.name, cancellationToken);
                        }).Unwrap();
                }
            }

            return t == null ? NoopTask() : t;
        }

        public virtual Task UpdateAsync(CancellationToken cancellationToken)
        {
            return GetInstalledPackagesAsync(cancellationToken)
                .ContinueWith(t => UpdateAsync(t.Result.Values, cancellationToken)).Unwrap();
        }

        public virtual Task<string> DownloadAsync(string url, string destinationDirectory, string filename, CancellationToken cancellationToken)
        {
            CreateDirectory(new DirectoryInfo(destinationDirectory));

            var request = HttpWebRequestFactory(url);
            request.Method = "GET";

            var httpHelper = new HttpHelper(request);
            return httpHelper
                .OpenReadTaskAsync(cancellationToken)
                .ContinueWith<string>(task =>
                {
                    // todo: handle errors
                    using (var responseStream = task.Result)
                    {
                        var response = httpHelper.HttpWebResponse;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string downloadedFilePath = Path.Combine(destinationDirectory, filename);
                            using (var fs = File.OpenWrite(downloadedFilePath))
                            {
                                byte[] buffer = new byte[1024 * 4]; // 4 kb
                                while (true)
                                {
                                    int read = responseStream.Read(buffer, 0, buffer.Length);
                                    if (read <= 0)
                                        break; ;
                                    fs.Write(buffer, 0, read);
                                }
                                return downloadedFilePath;
                            }
                        }
                        else
                        {
                            throw new NjiException("Invalid url - " + url);
                        }
                    }
                });
        }

        public virtual Task Extract(string tarball, string destinationDir)
        {
            var task = new Task(() =>
            {
                try
                {
                    if (Verbose > 1)
                        Out.WriteLine("Extracting {0} ...", tarball);
                    Ionic.Tar.Extract(tarball, destinationDir);
                }
                catch (Exception ex)
                {
                    throw new NjiException("Error occured while extracting  - " + tarball, ex);
                }
            });
            task.Start();
            return task;
        }

        public virtual void CleanTempDir()
        {
            CleanDir(TempDir);
        }

        public virtual string Version
        {
            get { return "0.2.0.0"; }
        }

        private static void CleanDir(string dir)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private static void CreateDirectory(DirectoryInfo directoryInfo)
        {
            if (directoryInfo.Parent != null)
                CreateDirectory(directoryInfo.Parent);
            if (!directoryInfo.Exists)
                directoryInfo.Create();
        }

        private static string ToString(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static Task NoopTask()
        {
            var task = new Task(() => { });
            task.Start();
            return task;
        }
    }
}
