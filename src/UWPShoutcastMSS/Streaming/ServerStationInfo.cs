using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWPShoutcastMSS.Streaming
{
    public class ServerStationInfo
    {
        internal ServerStationInfo()
        {

        }

        public string StationName { get; internal set; }
        public string StationGenre { get; internal set; }
        public string StationDescription { get; internal set; }
    }
}
