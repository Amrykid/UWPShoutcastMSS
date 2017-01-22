using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UWPShoutcastMSS.Streaming.ShoutcastMediaSourceStream;

namespace UWPShoutcastMSS.Streaming
{
    public class ServerAudioInfo
    {
        internal ServerAudioInfo()
        {

        }
        public uint SampleRate { get; internal set; } = 44100;
        public uint ChannelCount { get; internal set; } = 2;
        public uint BitRate { get; internal set; }
        public StreamAudioFormat AudioFormat { get; internal set; }
    }
}
