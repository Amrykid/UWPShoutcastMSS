using System;
using UWPShoutcastMSS.Streaming.Providers;

namespace UWPShoutcastMSS.Streaming
{
    internal class AudioProviderFactory
    {
        internal static IAudioProvider GetAudioProvider(StreamAudioFormat audioFormat)
        {
            switch(audioFormat)
            {
                case StreamAudioFormat.MP3:
                    return new MP3AudioProvider();
                case StreamAudioFormat.AAC_ADTS:
                    return new AACADTSAudioProvider();
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}