
namespace nji
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentHttp;
    using SimpleJson;

    public class NjiApi
    {
        public string WorkingDirectory { get; set; }
        public string TempDir { get; set; }

        public string RegistryUrlBase { get; set; }
        public TextWriter Out { get; set; }
        public int Verbose { get; set; }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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
                    metadata = SimpleJson.DeserializeObject(File.ReadAllText(packageJson));

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
                            throw downloadTask.Exception.GetBaseException();

                        var downloadFilePath = downloadTask.Result;
                        return Extract(downloadFilePath, destinationDir);
                    }).Unwrap()
                    .ContinueWith(extractTask =>
                    {
                        if (extractTask.IsFaulted)
                            throw extractTask.Exception.GetBaseException();

                        var packageExtractedDir = Directory.GetDirectories(destinationDir)[0];
                        string packageJson = Path.Combine(packageExtractedDir, "package.json");

                        dynamic metadata = null;
                        if (File.Exists(Path.Combine(packageJson)))
                        {
                            metadata = SimpleJson.DeserializeObject(File.ReadAllText(packageJson));
                            if (metadata.ContainsKey("name"))
                                packageName = metadata.name;
                        }

                        var moduleFinalDir = Path.Combine(WorkingDirectory, "node_modules", packageName);

                        CleanDir(moduleFinalDir);
                        Directory.Move(Directory.GetDirectories(destinationDir)[0], moduleFinalDir);

                        if (Verbose > 0)
                            Out.WriteLine("Successfully installed {0} in {1}", packageName, moduleFinalDir);

                        return InstallDependencies((object)metadata, cancellationToken, installDependencies);
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
                            throw metadataTask.Exception.GetBaseException();

                        dynamic metadata = metadataTask.Result;

                        // don't download if package is already the latest version.
                        string packageJson = Path.Combine(WorkingDirectory, "node_modules",
                            package.Split(new[] { '@' }, 2)[0], // drop everything after the @ sign, since modules get installed to directories without a version string
                            "package.json");
                        if (File.Exists(Path.Combine(packageJson)))
                        {
                            dynamic localMetadata = SimpleJson.DeserializeObject(File.ReadAllText(packageJson));
                            if (localMetadata.ContainsKey("name") && localMetadata.ContainsKey("version"))
                            {
                                if (metadata.name == localMetadata.name && metadata.version == localMetadata.version)
                                {
                                    if (Verbose > 0)
                                        Out.WriteLine("Skipping {0}. Already on latest version", package);
                                    return metadataTask;
                                    // should it check dependencies too?
                                    // return InstallDependencies((object)metadata, cancellationToken, installDependencies);
                                }
                            }
                        }

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
                return InstallAsync(string.Empty, cancellationToken, installDependencies);
            }
            else
            {
                return ForEachContinueWith(packages, (package, task) =>
                {
                    if (task != null && task.IsFaulted)
                        throw task.Exception.GetBaseException();

                    return InstallAsync(package, cancellationToken, installDependencies);
                });
            }
        }

        public virtual Task InstallAsync(IEnumerable<string> packages)
        {
            return InstallAsync(packages, CancellationToken.None, true);
        }

        private Task InstallDependencies(object metadata, CancellationToken cancellationToken, bool installDependencies = true)
        {
            if (metadata == null || !installDependencies)
                return NoopTask();

            var packageName = string.Empty;

            dynamic meta = metadata;
            if (meta.ContainsKey("name"))
                packageName = meta.name;

            if (Verbose > 0)
                Out.WriteLine("Checking dependencies for {0} ...", packageName);

            if (meta.ContainsKey("dependencies"))
            {
                var dependencies = meta.dependencies as IDictionary<string, object>;
                if (dependencies == null || dependencies.Count == 0)
                    return NoopTask();

                return ForEachContinueWith(dependencies, (package, task) =>
                {
                    if (task != null && task.IsFaulted)
                        throw task.Exception.GetBaseException();

                    var version = package.Value as string;
                    var packageToInstall = package.Key;
                    if (version != null)
                    {
                        packageToInstall = string.Concat(packageToInstall, "@", version);
                    }

                    return InstallAsync(packageToInstall, cancellationToken, installDependencies);
                });
            }

            return NoopTask();
        }

        private bool IsSpecificVersion(string version)
        {
            // Based on http://npmjs.org/doc/semver.html:
            // "A version is the following things, in this order:
            //  * a number (Major)
            //  * a period
            //  * a number (minor)
            //  * a period
            //  * a number (patch)
            //  * OPTIONAL: a hyphen, followed by a number (build)
            //  * OPTIONAL: a collection of pretty much any non-whitespace characters (tag)
            //  A leading "=" or "v" character is stripped off and ignored."
            // Also supporting the specific string "latest"

            return Regex.IsMatch(version, @"^[=v]?\d+\.\d+\.\d+\S*|latest$");
        }

        public virtual Task<object> GetPackageMetadataAsync(string package, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace("package"))
                throw new ArgumentNullException("package");

            var split = package.Split(new[] { '@' }, 2);
            var packageName = split[0];
            var packageVersion = split.Length > 1 ? split[1] : "latest";

            bool specificVersion = IsSpecificVersion(packageVersion);

            string url = RegistryUrlBase + packageName;
            if (specificVersion) url += "/" + packageVersion;

            var request = HttpWebRequestFactory(url);
            request.Method = "GET";

            var httpHelper = new HttpHelper(request);
            return httpHelper
                .OpenReadTaskAsync(cancellationToken)
                .ContinueWith<object>(task =>
                                          {
                                              if (task.IsFaulted)
                                              {
                                                  var ex = task.Exception.GetBaseException();
                                                  var webException = ex as WebExceptionWrapper;
                                                  if (webException == null || httpHelper.HttpWebResponse == null)
                                                      throw ex;
                                              }
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
                                          })
                .ContinueWith(task =>
                                          {
                                              if (task.IsFaulted)
                                                  throw task.Exception.GetBaseException();
                                              if (specificVersion) return task;

                                              dynamic json = task.Result;

                                              string matched = GetBestVersion(json.versions.Keys, packageVersion);
                                              if (string.IsNullOrEmpty(matched))
                                              {
                                                  Out.WriteLine("Not smart enough to understand version '{0}', so using 'latest' instead for package '{1}'.", packageVersion, packageName);
                                                  // get the @latest if can't find the best match
                                                  return GetPackageMetadataAsync(packageName, cancellationToken);
                                              }
                                              else
                                              {
                                                  return GetPackageMetadataAsync(packageName + "@" + matched, cancellationToken);
                                              }
                                          }).Unwrap();
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
                        modules.Add(metadata.name, metadata);
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
            return ForEachContinueWith(metadata, (package, task) =>
            {
                if (task != null && task.IsFaulted)
                    throw task.Exception.GetBaseException();

                string packageName = ((dynamic)package).name;
                return GetPackageMetadataAsync(packageName, cancellationToken)
                        .ContinueWith(t2 =>
                        {
                            if (t2.IsFaulted)
                                throw t2.Exception;
                            return InstallAsync(packageName, cancellationToken);
                        }).Unwrap();
            });
        }

        public virtual Task UpdateAsync(CancellationToken cancellationToken)
        {
            return GetInstalledPackagesAsync(cancellationToken)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        throw t.Exception.GetBaseException();
                    return UpdateAsync(t.Result.Values, cancellationToken);
                }).Unwrap();
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
                    if (task.IsFaulted)
                        throw task.Exception.GetBaseException();

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
            get { return "0.3.0.0"; }
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

        private Task ForEachContinueWith<T>(IEnumerable<T> collection, Func<T, Task, Task> oneach)
        {
            Task task = null;

            if (collection != null)
            {
                foreach (var item in collection)
                {
                    if (task == null)
                    {
                        task = oneach(item, null);
                    }
                    else
                    {
                        var item_captured = item;
                        task = task.ContinueWith(t => oneach(item_captured, t)).Unwrap();
                    }
                }
            }

            return task ?? NoopTask();
        }

        public virtual string GetBestVersion(IEnumerable<string> versions, string versionPattern)
        {
            if (string.IsNullOrEmpty(versionPattern) || versionPattern == "latest") return versionPattern;
            var constraints = new List<Constraint>();
            var m = Regex.Match(versionPattern, @"^([.\da-zA-Z]*)\.(\d)\.x$"); // match things like "1.7.x"
            if (m.Success)
            {
                int n = int.Parse(m.Groups[2].Value);
                versionPattern = string.Format(">= {0}.{1} < {0}.{2}", m.Groups[1].Value, n, n + 1); // rewrite "1.7.x" to ">= 1.7 < 1.8"
            }
            foreach (Match match in Regex.Matches(versionPattern, @"([<>=]+)\s*(\S*)"))
            {
                constraints.Add(new Constraint(match.Groups[1].Value, match.Groups[2].Value)); // e.g. ">=" "1.7"
            }
            if (constraints.Count == 0)
            {
                if (Regex.IsMatch(versionPattern, @"^[\.\da-zA-Z]*$")) // handle the case of just an exact version string
                {
                    return versionPattern;
                }
                else // give up
                {
                    return null;
                }
            }
            string best = null;
            foreach (var ver in versions) // versions appear to be in ascending order
            {
                if (constraints.All(c => c.SatisfiedBy(ver))) best = ver; // track the newest version that satisfies all constraints
            }
            return best;
        }
    }
}
