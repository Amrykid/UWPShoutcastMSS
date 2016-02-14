# UWPShoutcastMSS

A library for connecting to Shoutcast in Windows 10 UWP applications. It's more of a hack than a proper library, but it gets the job done.

## How to use it

Easy, peasy. 

### Foreground Audio
Assuming you set up an invisible MediaElement named 'MediaPlayer', the following should work:
```c#
ShoutcastMediaSourceManager streamManager = new ShoutcastMediaSourceManager(new Uri("http://fakeshoutcaststream.com/"));
streamManager.MetadataChanged += StreamManager_MetadataChanged;
await streamManager.ConnectAsync();

MediaPlayer.SetMediaStreamSource(streamManager.MediaStreamSource);
MediaPlayer.Play();
```

### Background Audio
Use the following in your audio background task.

```c#
//Initialize the stream manager
ShoutcastMediaSourceManager streamManager = new ShoutcastMediaSourceManager(new Uri("http://fakeshoutcaststream.com/"));
//Hook up an event handler for grabbing metadata when it changes. This means you can update your "Now Playing" display.
streamManager.MetadataChanged += Metadata_EventHandler;
//Connect the manager to the stream.
await streamManager.ConnectAsync();
//Set the manager's underlying stream as the BackgroundMediaPlayer's MediaSource.
BackgroundMediaPlayer.Current.SetMediaSource(streamManager.MediaStreamSource);
//Wait half a second to allow the stream to buffer.
await Task.Delay(500);
//Play!
BackgroundMediaPlayer.Current.Play();
```

## Want to contribute?
I'm pretty new to the open source scene so if you have any contributions, feel free to send a pull request. I will be re-evaluating the license of the code in the future if it is a problem. I will also be looking into setting up a Nuget package if this library takes off.

## Known Issues
- AAC streams aren't supported at this time. See [#1](https://github.com/Amrykid/UWPShoutcastMSS/issues/1)
