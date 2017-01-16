using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWPShoutcastMSS.Parsers.Audio
{
    public static class AAC_ADTSParser
    {
        //Reference: https://wiki.multimedia.cx/index.php/ADTS

        public static readonly byte[] FrameSync = new byte[] { (byte)255, (byte)240 }; //12 bits - ADTS sync bits.
        public const int HeaderLength = 7;
        public const int HeaderLengthWithCRC = 9;

        public static bool IsFrameSync(byte firstByte, byte secondByte)
        {
            return (firstByte == FrameSync[0]) && (secondByte & FrameSync[1]) == (byte)240;
        }

        public static bool IsProtectionAbsent(byte[] header)
        {
            byte data = header[1];

            //we only need the right most bit. TODO better way of doing this.

            //Console.WriteLine(ByteToBinaryString(data));
            data = (byte)(data << 7);
            data = (byte)(data >> 7);

            //Console.WriteLine(ByteToBinaryString(data));

            bool value = ((int)data == 1);

            return value;
        }

        public static int GetAudioObjectProfileType(byte[] header)
        {
            byte data = header[2];

            data = (byte)(data >> 6);

            int value = (int)data + 1; //-1?

            return value;
        }

        public static int GetFrameLength(byte[] header)
        {
            //FrameLength = (ProtectionAbsent == 1 ? 7 : 9) + size(AACFrame)

            //size of AAC audio frame is equal to 1024 samples per channel (2048 bytes), size of MP3 frame - 1152 samples per channel (2304 bytes).
            //from: https://software.intel.com/en-us/forums/intel-integrated-performance-primitives/topic/297268
            int aacFrameSize = 1024;

            return (IsProtectionAbsent(header) ? 7 : 9) + aacFrameSize;

        }

        public static int GetSampleRate(byte[] header)
        {
            int[] sampleRateTable = new int[] {
                96000, 88200, 64000,
                48000, 44100, 32000,
                24000, 22050, 16000,
                12000, 11025, 8000,
                7350};

            byte data = header[2];

            data = (byte)(data << 2);

            data = (byte)(data >> 4);

            int value = (int)data;

            int sampleRate = sampleRateTable[value];

            int audioObjectType = GetAudioObjectProfileType(header);
            if (audioObjectType == 5) //SBR (Spectral Band Replication) := means we need to double the sample rate. https://wiki.multimedia.cx/index.php/Spectral_Band_Replication
                sampleRate *= 2;
            else if (audioObjectType == 2) //HE-AAC/AAC LC which also uses SBR
                sampleRate *= 2;

            return sampleRate;
        }

        public static int GetChannelConfig(byte[] header)
        {
            byte firstByte = header[2];

            byte secondByte = header[3];

            //deal with the first byte first
            //shift the bit we care about all the way to the left and then back. there may be a better way to do this.
            //Console.WriteLine(ByteToBinaryString(firstByte));
            firstByte = (byte)(firstByte << 8);
            firstByte = (byte)(firstByte >> 8);
            //Console.WriteLine(ByteToBinaryString(firstByte));

            firstByte = (byte)(firstByte << 2); //shift it back to the third spot so that we can add it later.


            //next, shift the second byte to the right 7 times
            //Console.WriteLine(ByteToBinaryString(secondByte));
            secondByte = (byte)(secondByte >> 6);
            //Console.WriteLine(ByteToBinaryString(secondByte));

            int value = ((int)firstByte) + ((int)secondByte);

            return value;
        }

        public static int GetBitRate(byte[] header)
        {
            //fast estimate: 
            //"the bitdepth is 16 bits per sample. generally the formula for bitrate (bits/sec) calculation is sampling rate (samples/sec) * bit_depth (bits/sample) * number_of_channels"
            //from: https://hydrogenaud.io/index.php?PHPSESSID=h88o88rsgo5goc26g3tbu95br2&topic=71414.msg629346#msg629346

            int sampleRate = GetSampleRate(header);
            int channelCount = GetChannelCount(header);

            return sampleRate * 16 * channelCount;
        }

        public static int GetChannelCount(byte[] header)
        {
            int channelMode = AAC_ADTSParser.GetChannelConfig(header);
            switch (channelMode)
            {
                case 7:
                    return 8;
                default:
                    {
                        if (channelMode >= 8)
                        {
                            throw new Exception("Unknown channel config."); //reserved
                        }
                        else if (channelMode <= 6)
                        {
                            return channelMode;
                        }
                    }
                    break;
            }

            return 0;
        }

        private static string ByteToBinaryString(byte bte)
        {
            return Convert.ToString(bte, 2).PadLeft(8, '0');
        }
    }
}
