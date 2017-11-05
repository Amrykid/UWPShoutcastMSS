using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using UWPShoutcastMSS.Streaming;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace TestApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            stationComboBox.ItemsSource = new StationItem[]
            {
                new StationItem
                {
                    Name = "Hitradio OE3 (MP3, 128kb)",
                    Url = new Uri("http://194.232.200.156:8000/")
                },
                new StationItem
                {
                    Name = "yggdrasilradio.net (AAC, 56kb)",
                    Url = new Uri("http://95.211.241.92:9100/")
                },
                new StationItem
                {
                    Name = "OZFM (AAC, 96kb)",
                    Url = new Uri("http://174.37.159.206:8262/stream")
                },
                new StationItem
                {
                    Name = "R/a/dio",
                    Url = new Uri("http://relay0.r-a-d.io/main.mp3"),
                    RelativePath = ""
                }
            };

            stationComboBox.SelectedIndex = 0;
        }

        UWPShoutcastMSS.Streaming.ShoutcastStream shoutcastStream = null;

        private async void StreamManager_MetadataChanged(object sender, ShoutcastMediaSourceStreamMetadataChangedEventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler(() =>
            {
                SongBox.Text = "Song: " + e.Title;
                ArtistBox.Text = "Artist: " + e.Artist;
            }));
        }

        private async void playButton_Click(object sender, RoutedEventArgs e)
        {
            playButton.IsEnabled = false;
            stationComboBox.IsEnabled = false;
            stopButton.IsEnabled = true;

            var selectedStation = stationComboBox.SelectedItem as StationItem;

            if (selectedStation != null)
            {

                try
                {
                    shoutcastStream = await ShoutcastStreamFactory.ConnectAsync(selectedStation.Url,
                        new ShoutcastStreamFactoryConnectionSettings()
                        {
                            RelativePath = selectedStation.RelativePath
                        });
                    shoutcastStream.MetadataChanged += StreamManager_MetadataChanged;
                    MediaPlayer.SetMediaStreamSource(shoutcastStream.MediaStreamSource);
                    MediaPlayer.Play();

                    SampleRateBox.Text = "Sample Rate: " + shoutcastStream.AudioInfo.SampleRate;
                    BitRateBox.Text = "Bit Rate: " + shoutcastStream.AudioInfo.BitRate;
                    AudioFormatBox.Text = "Audio Format: " + Enum.GetName(typeof(UWPShoutcastMSS.Streaming.StreamAudioFormat), shoutcastStream.AudioInfo.AudioFormat);
                }
                catch (Exception ex)
                {
                    playButton.IsEnabled = true;
                    stationComboBox.IsEnabled = true;
                    stopButton.IsEnabled = false;

                    if (shoutcastStream != null)
                    {
                        try
                        {
                            shoutcastStream.Disconnect();
                        }
                        catch (Exception)
                        {

                        }
                        finally
                        {
                            shoutcastStream.Dispose();
                        }
                    }

                    MessageDialog dialog = new MessageDialog("Unable to connect!");
                    await dialog.ShowAsync();
                }
            }
        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            playButton.IsEnabled = true;
            stationComboBox.IsEnabled = true;
            stopButton.IsEnabled = false;

            if (shoutcastStream != null)
            {
                shoutcastStream.MetadataChanged -= StreamManager_MetadataChanged;
                MediaPlayer.Stop();
                MediaPlayer.Source = null;

                shoutcastStream.Disconnect();
                shoutcastStream = null;
            }
        }
    }
}
