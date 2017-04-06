using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Web.Http;

namespace UWPShoutcastMSS.Streaming
{
    public static class ShoutcastStreamFactory
    {
        public static async Task<ShoutcastStream> ConnectAsync(Uri serverUrl, 
            ShoutcastStreamFactoryConnectionSettings settings = default(ShoutcastStreamFactoryConnectionSettings))
        {
            HttpClient httpClient = new HttpClient();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, serverUrl);
            request.Headers.Add("Icy-MetaData", settings.GetMetadata ? "1" : "0");
            request.Headers.Add("User-Agent", (settings.UserAgent ?? "Shoutcast Player (http://github.com/Amrykid/UWPShoutcastMSS)"));

            var response = await httpClient.SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
    }
}
