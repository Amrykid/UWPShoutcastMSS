using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
using Windows.Media.Core;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Providers
{
    internal class MP3AudioProvider : IAudioProvider
    {
        public uint GetSampleSize()
        {
            return MP3Parser.mp3_sampleSize;
        }

        public async Task<Tuple<MediaStreamSample, uint>> ParseSampleAsync(ShoutcastStreamProcessor processor, DataReader socketReader, bool partial = false, byte[] partialBytes = null)
        {
            //http://www.mpgedit.org/mpgedit/mpeg_format/MP3Format.html

            IBuffer buffer = null;
            MediaStreamSample sample = null;
            uint sampleLength = 0;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                sampleLength = MP3Parser.mp3_sampleSize - (uint)partialBytes.Length;
                processor.byteOffset += sampleLength;
            }
            else
            {
                var read = await socketReader.LoadAsync(MP3Parser.mp3_sampleSize);

                if (read < MP3Parser.mp3_sampleSize)
                {
                    buffer = socketReader.ReadBuffer(read);
                }
                else
                {
                    buffer = socketReader.ReadBuffer(MP3Parser.mp3_sampleSize);
                }

                processor.byteOffset += MP3Parser.mp3_sampleSize;

                sampleLength = MP3Parser.mp3_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, processor.timeOffSet);
            sample.Duration = MP3Parser.mp3_sampleDuration;
            sample.KeyFrame = true;

            processor.timeOffSet = processor.timeOffSet.Add(MP3Parser.mp3_sampleDuration);


            return new Tuple<MediaStreamSample, uint>(sample, sampleLength);
        }

        
    }
}
