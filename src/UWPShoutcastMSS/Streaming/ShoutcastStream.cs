using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
using Windows.Media.MediaProperties;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Media.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UWPShoutcastMSS.Streaming.Sockets;
using System.Net.Sockets;

namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastStream : IDisposable
    {
        public ServerAudioInfo AudioInfo { get; internal set; }
        public ServerStationInfo StationInfo { get; internal set; }
        public MediaStreamSource MediaStreamSource { get; private set; }

        public event EventHandler<ShoutcastMediaSourceStreamMetadataChangedEventArgs> MetadataChanged;
        public event EventHandler Reconnected;

        private ShoutcastStreamProcessor streamProcessor = null;
        internal uint metadataInt = 100;
        private Uri serverUrl;
        private SocketWrapper socket;
        internal ShoutcastStreamFactoryConnectionSettings serverSettings;
        private ShoutcastServerType serverType;
        private AudioEncodingProperties audioProperties = null;
        private DateTime? lastPauseTime = null;
        private volatile bool isDisposed = false;
        private CancellationTokenSource cancelTokenSource = null;

        internal ShoutcastStream(Uri serverUrl, ShoutcastStreamFactoryConnectionSettings settings, SocketWrapper socketWrapper)
        {
            this.socket = socketWrapper;
            this.serverUrl = serverUrl;
            this.serverSettings = settings;

            StationInfo = new ServerStationInfo();
            AudioInfo = new ServerAudioInfo();
            cancelTokenSource = new CancellationTokenSource();
            //MediaStreamSource = new MediaStreamSource(null);
            streamProcessor = new ShoutcastStreamProcessor(this, socket);
        }

        private async Task DetectAudioEncodingPropertiesAsync(KeyValuePair<string, string>[] headers)
        {
            //if this happens to be an Icecast 2 server, it'll send the audio information for us.
            if (headers.Any(x => x.Key.ToLower() == "ice-audio-info"))
            {
                //looks like it is indeed an Icecast 2 server. lets strip out the data and be on our way.

                serverType = ShoutcastServerType.Icecast;

                /* example: ice-audio-info: ice-bitrate=32;ice-samplerate=32000;ice-channels=2
                 * "Note that unlike SHOUTcast, it is not necessary to parse ADTS audio frames to obtain the Audio Sample Rate."
                 * from: http://www.indexcom.com/streaming/player/Icecast2.html
                 */

                string headerValue = headers.First(x => x.Key.ToLower() == "ice-audio-info").Value;

                //split the properties and values and parsed them into a usable object.
                KeyValuePair<string, string>[] propertiesAndValues = headerValue.Split(';')
                    .Select(x => new KeyValuePair<string, string>(
                        x.Substring(0, x.IndexOf("=")).ToLower().Trim(),
                        x.Substring(x.IndexOf("=") + 1))).ToArray();

                //grab each value that we need.

                if (AudioInfo.BitRate == 0) //usually this is sent in the regular headers. grab it if it isn't.
                    AudioInfo.BitRate = uint.Parse(propertiesAndValues.First(x => x.Key == "ice-bitrate" || x.Key == "bitrate").Value);


                if (propertiesAndValues.Any(x => x.Key == "ice-channels" || x.Key == "channels") && propertiesAndValues.Any(x => x.Key == "ice-samplerate" || x.Key == "samplerate"))
                {
                    AudioInfo.ChannelCount = uint.Parse(propertiesAndValues.First(x => x.Key == "ice-channels" || x.Key == "channels").Value);
                    AudioInfo.SampleRate = uint.Parse(propertiesAndValues.First(x => x.Key == "ice-samplerate" || x.Key == "samplerate").Value);

                    //now just create the appropriate AudioEncodingProperties object.
                    switch (AudioInfo.AudioFormat)
                    {
                        case StreamAudioFormat.MP3:
                            audioProperties = AudioEncodingProperties.CreateMp3(AudioInfo.SampleRate, AudioInfo.ChannelCount, AudioInfo.BitRate);
                            break;
                        case StreamAudioFormat.AAC:
                        case StreamAudioFormat.AAC_ADTS:
                            audioProperties = AudioEncodingProperties.CreateAacAdts(AudioInfo.SampleRate, AudioInfo.ChannelCount, AudioInfo.BitRate);
                            break;
                    }
                }
                else
                {
                    //something is missing from audio-info so we need to fallback.

                    audioProperties = await ParseEncodingFromMediaAsync().ConfigureAwait(false);
                }
            }
            else
            {
                audioProperties = await ParseEncodingFromMediaAsync().ConfigureAwait(false);
            }
        }

        private static string ByteToBinaryString(byte bte)
        {
            return Convert.ToString(bte, 2).PadLeft(8, '0');
        }

        private async Task<AudioEncodingProperties> ParseEncodingFromMediaAsync()
        {
            //grab the first frame and strip it for information

            AudioEncodingProperties obtainedProperties = null;
            IBuffer buffer = null;

            if (AudioInfo.AudioFormat == StreamAudioFormat.AAC)
            {
                //obtainedProperties = AudioEncodingProperties.CreateAac(0, 2, 0);
                throw new Exception("Not supported.");
            }

            var provider = AudioProviderFactory.GetAudioProvider(AudioInfo.AudioFormat);

            ServerAudioInfo firstFrame = await provider.GrabFrameInfoAsync(streamProcessor, AudioInfo).ConfigureAwait(false);

            //loop until we receive a few "frames" with identical information.
            while (true)
            {
                cancelTokenSource.Token.ThrowIfCancellationRequested();

                ServerAudioInfo secondFrame = await provider.GrabFrameInfoAsync(streamProcessor, AudioInfo).ConfigureAwait(false);

                if (firstFrame.BitRate == secondFrame.BitRate
                    && firstFrame.SampleRate == secondFrame.SampleRate)
                {
                    //both frames are identical, use one of them and escape the loop.
                    AudioInfo = firstFrame;
                    break;
                }
                else
                {
                    //frames aren't identical, get rid of the first one using the second frame and loop back.
                    firstFrame = secondFrame;
                    continue;
                }
            }

            cancelTokenSource.Token.ThrowIfCancellationRequested();

            if (AudioInfo.AudioFormat == StreamAudioFormat.MP3)
            {
                //skip the entire first frame/sample to get back on track
                await socket.LoadAsync(MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength);
                buffer = await socket.ReadBufferAsync(MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength);
                //streamProcessor.byteOffset += MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength;


                obtainedProperties = AudioEncodingProperties.CreateMp3((uint)AudioInfo.SampleRate, (uint)AudioInfo.ChannelCount, AudioInfo.BitRate);
            }
            else if (AudioInfo.AudioFormat == StreamAudioFormat.AAC_ADTS)
            {
                //skip the entire first frame/sample to get back on track
                await socket.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength);
                buffer = await socket.ReadBufferAsync(AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength);
                //streamProcessor.byteOffset += AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength;

                obtainedProperties = AudioEncodingProperties.CreateAacAdts((uint)AudioInfo.SampleRate, (uint)AudioInfo.ChannelCount, AudioInfo.BitRate);
            }
            else
            {
                throw new Exception("Unsupported format.");
            }

            if (serverSettings.RequestSongMetdata)
                streamProcessor.metadataPos += buffer.Length; //very important or it will throw everything off!

            return obtainedProperties;
        }

        internal void RaiseMetadataChangedEvent(ShoutcastMediaSourceStreamMetadataChangedEventArgs shoutcastMediaSourceStreamMetadataChangedEventArgs)
        {
            if (MetadataChanged != null)
            {
                MetadataChanged(this, shoutcastMediaSourceStreamMetadataChangedEventArgs);
            }
        }

        internal async Task HandleHeadersAsync(KeyValuePair<string, string>[] headers)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers), "Headers are null");
            if (headers.Length == 0) throw new ArgumentException(paramName: "headers", message: "Header count is 0");

            await DetectAudioEncodingPropertiesAsync(headers);

            if (cancelTokenSource.IsCancellationRequested) return;

            if (audioProperties == null) throw new InvalidOperationException("Unable to detect audio encoding properties.");

            var audioStreamDescriptor = new AudioStreamDescriptor(audioProperties);
            MediaStreamSource = new MediaStreamSource(audioStreamDescriptor);
            MediaStreamSource.Paused += MediaStreamSource_Paused;
            MediaStreamSource.Starting += MediaStreamSource_Starting;
            MediaStreamSource.Closed += MediaStreamSource_Closed;
            MediaStreamSource.SampleRequested += MediaStreamSource_SampleRequested;
        }

        private void MediaStreamSource_Closed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
        {
            //todo needs to be handled.
        }

        private void MediaStreamSource_Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            lastPauseTime = null;
        }

        private void MediaStreamSource_Paused(MediaStreamSource sender, object args)
        {
            lastPauseTime = DateTime.Now;
        }

        private async Task ReconnectSocketsAsync()
        {
            cancelTokenSource.Token.ThrowIfCancellationRequested();

            var result = await ShoutcastStreamFactory.ConnectInternalAsync(serverUrl,
                serverSettings);

            this.socket = SocketWrapperFactory.CreateSocketWrapper(result);

            cancelTokenSource = new CancellationTokenSource();

            streamProcessor = new ShoutcastStreamProcessor(this, socket);
        }

        public void Disconnect()
        {
            if (isDisposed) throw new System.ObjectDisposedException(typeof(ShoutcastStream).Name);

            try
            {
                MediaStreamSource.SampleRequested -= MediaStreamSource_SampleRequested;
            }
            catch (ArgumentException) { }

            try
            {
                MediaStreamSource.Starting -= MediaStreamSource_Starting;
                MediaStreamSource.Closed -= MediaStreamSource_Closed;
                MediaStreamSource.Paused -= MediaStreamSource_Paused;
            }
            catch (ArgumentException)
            {
                //Event handlers must have been disconnected already. Continue anyway.
            }

            cancelTokenSource.Cancel();

            DisconnectSockets();

            cancelTokenSource.Dispose();
        }

        private void DisconnectSockets()
        {
            streamProcessor = null;

            if (socket != null)
            {
                try
                {
                    socket.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    
                }
                socket = null;
            }
        }

        private async void MediaStreamSource_SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            bool connected = true;

            try
            {
                cancelTokenSource.Token.ThrowIfCancellationRequested();

                try
                {
                    await ReadSampleAsync(request).ConfigureAwait(false);
                    cancelTokenSource.Token.ThrowIfCancellationRequested();
                }
                catch (ShoutcastDisconnectionException)
                {
                    //Reset and reconnect.
                    DisconnectSockets();
                    connected = false;
                }
                catch (COMException ex)
                {
                    //Usually this is thrown when we get disconnected because of inactivity.
                    //Reset and reconnect.
                    DisconnectSockets();
                    connected = false;
                }

                if (!cancelTokenSource.IsCancellationRequested)
                {
                    cancelTokenSource.Token.ThrowIfCancellationRequested();
                    if (!connected)
                    {
                        try
                        {
                            await ReconnectSocketsAsync().ConfigureAwait(false);
                            Reconnected?.Invoke(this, EventArgs.Empty);

                            await ReadSampleAsync(request).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.ConnectionToServerLost);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception)
            {
                MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.Other);
            }
            finally
            {
                deferral.Complete();
            }
        }

        public bool PollConnection()
        {
            if (isDisposed) return false;

            return !(DateTime.Now.Subtract(socket.LastReadTime) > TimeSpan.FromMinutes(5)); //if the last time read time was 5 minutes, assume we're disconnected.
        }

        private async Task ReadSampleAsync(MediaStreamSourceSampleRequest request)
        {
            var sample = await streamProcessor.GetNextSampleAsync(cancelTokenSource.Token);

            if (cancelTokenSource.IsCancellationRequested) return;

            if (sample != null)
                request.Sample = sample;
        }

        public void Dispose()
        {
            if (isDisposed) return;

            try 
            {
                Disconnect();
            } 
            catch (Exception)
            {

            }
            finally
            {
                isDisposed = true;
            }
        }
    }
}