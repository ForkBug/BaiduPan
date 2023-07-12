using System.Net;

namespace BaiduPanConsole
{
    class NoProxy : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination)
        {
            return destination;
        }

        public bool IsBypassed(Uri host)
        {
            return true;
        }
    }
}