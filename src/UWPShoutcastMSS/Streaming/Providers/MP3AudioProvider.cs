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

        public async Task<ServerAudioInfo> GrabFrameInfoAsync(ShoutcastStreamProcessor processor, ServerAudioInfo serverSentInfo)
        {
            ServerAudioInfo audioInfo = new ServerAudioInfo();
            audioInfo.AudioFormat = StreamAudioFormat.MP3;

            //load the first byte
            byte lastByte = await processor.ReadByteFromSocketAsync();

            while (true) //wait for frame sync
            {
                var curByte = await processor.ReadByteFromSocketAsync();

                if (MP3Parser.IsFrameSync(lastByte, curByte)) //check if we're at the frame sync. if we are, parse some of the audio data
                {
                    byte[] header = new byte[MP3Parser.HeaderLength];
                    header[0] = lastByte;
                    header[1] = curByte;

                    Array.Copy(await processor.ReadBytesFromSocketAsync(2), 0, header, 2, 2);

                    try
                    {
                        audioInfo.SampleRate = (uint)MP3Parser.GetSampleRate(header);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        //bad sample rate
                        audioInfo.SampleRate = 0;
                    }

                    try
                    {
                        audioInfo.ChannelCount = (uint)MP3Parser.GetChannelCount(header);
                    }
                    catch (Exception)
                    {
                        //bad channel count
                        audioInfo.ChannelCount = 0;
                    }

                    uint bitRate = (uint)MP3Parser.GetBitRate(header);

                    audioInfo.BitRate = bitRate;
                    break;

                }
                else
                {
                    lastByte = curByte;
                }
            }

            return audioInfo;
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

                buffer = socketReader.ReadBuffer(read < MP3Parser.mp3_sampleSize ? read : MP3Parser.mp3_sampleSize);

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
