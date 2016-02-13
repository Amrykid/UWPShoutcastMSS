# UWPShoutcastMSS

A library for connecting to Shoutcast in Windows 10 UWP applications. It's more of a hack than a proper library, but it gets the job done.

## How to use it

Easy, peasy.

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

## Known Issues
- AAC streams aren't support at this time.
