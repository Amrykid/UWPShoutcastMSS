using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastStationInfo
    {
        internal ShoutcastStationInfo()
        {

        }

        public string StationName { get; internal set; }
        public string StationGenre { get; internal set; }
    }
}
