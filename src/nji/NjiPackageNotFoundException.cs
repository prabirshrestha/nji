
namespace nji
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Runtime.Serialization;

#if !SILVERLIGHT
    [Serializable]
#endif
    public class NjiPackageNotFoundException : NjiException
    {
        public string PackageName { get; private set; }
        public string PackageVersion { get; private set; }

        public NjiPackageNotFoundException(string packageName, string packageVersion)
            : base(string.Format("Package - '{0}@{1}' not found.", packageName, packageVersion))
        {
            PackageName = packageName;
            PackageVersion = packageVersion;
        }

#if !SILVERLIGHT

        protected NjiPackageNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

#endif
    }
}
