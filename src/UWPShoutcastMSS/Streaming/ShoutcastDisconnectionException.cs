using System;

namespace UWPShoutcastMSS.Streaming
{
    internal class ShoutcastDisconnectionException : Exception
    {
        public ShoutcastDisconnectionException()
        {
        }

        public ShoutcastDisconnectionException(string message) : base(message)
        {
        }

        public ShoutcastDisconnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}