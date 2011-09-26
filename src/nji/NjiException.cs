
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
    public class NjiException : Exception
    {
        public NjiException()
        {
        }

        public NjiException(string message)
            : base(message)
        {
        }

        public NjiException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

#if !SILVERLIGHT

        protected NjiException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

#endif
    }
}
