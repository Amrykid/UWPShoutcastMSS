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
    internal class AACADTSAudioProvider : IAudioProvider
    {
        public uint GetSampleSize()
        {
            return AAC_ADTSParser.aac_adts_sampleSize;
        }

        public async Task<Tuple<MediaStreamSample, uint>> ParseSampleAsync(ShoutcastStreamProcessor processor, DataReader socketReader, bool partial = false, byte[] partialBytes = null)
        {
            IBuffer buffer = null;
            MediaStreamSample sample = null;
            uint sampleLength = 0;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                sampleLength = AAC_ADTSParser.aac_adts_sampleSize - (uint)partialBytes.Length;
                processor.byteOffset += sampleLength;
            }
            else
            {
                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize);

                buffer = socketReader.ReadBuffer(AAC_ADTSParser.aac_adts_sampleSize);

                processor.byteOffset += AAC_ADTSParser.aac_adts_sampleSize;
                sampleLength = AAC_ADTSParser.aac_adts_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, processor.timeOffSet);
            sample.Duration = AAC_ADTSParser.aac_adts_sampleDuration;
            sample.KeyFrame = true;

            processor.timeOffSet = processor.timeOffSet.Add(AAC_ADTSParser.aac_adts_sampleDuration);


            return new Tuple<MediaStreamSample, uint>(sample, sampleLength);
        }

       
    }
}
