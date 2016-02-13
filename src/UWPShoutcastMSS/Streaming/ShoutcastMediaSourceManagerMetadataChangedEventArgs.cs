using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastMediaSourceManagerMetadataChangedEventArgs: EventArgs
    {
        public ShoutcastMediaSourceManagerMetadataChangedEventArgs()
        {

        }

        public ShoutcastMediaSourceManagerMetadataChangedEventArgs(string track, string artist)
        {
            Artist = artist;
            Title = track;
        }

        public string Artist { get; internal set; }
        public string Title { get; internal set; }
    }
}
