using System;
using Xunit;
using YandexSpeech.services;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace YandexSpeech.Tests;

public sealed class YoutubeAudioStreamSelectionTests
{
    private static readonly AudioOnlyStreamInfo English = Track("en", "English", 128_000, 100);
    private static readonly AudioOnlyStreamInfo Russian = Track("ru", "Russian", 128_000, 100);

    [Fact]
    public void RussianAndEnglishWithIdenticalFormatsStayDistinct()
    {
        var tracks = new[] { English, Russian };

        Assert.Same(Russian, YoutubeAudioStreamSelection.Select(
            tracks, "ru", "Russian", "mp4", "aac", 128_000, 100));
        Assert.Same(English, YoutubeAudioStreamSelection.Select(
            tracks, "en", "English", "mp4", "aac", 128_000, 100));
    }

    [Fact]
    public void OldCachedLanguageNameStillSelectsCorrectTrack()
    {
        Assert.Same(Russian, YoutubeAudioStreamSelection.Select(
            new[] { English, Russian }, null, "Russian", "mp4", "aac", 128_000, 100));
    }

    [Fact]
    public void UnavailableOrUnspecifiedLanguageNeverFallsBackToFirstTrack()
    {
        var tracks = new[] { English, Russian };

        Assert.Throws<InvalidOperationException>(() => YoutubeAudioStreamSelection.Select(
            tracks, "de", "German", "mp4", "aac", 128_000, 100));
        Assert.Throws<InvalidOperationException>(() => YoutubeAudioStreamSelection.Select(
            tracks, null, null, "mp4", "aac", 128_000, 100));
        Assert.Throws<InvalidOperationException>(() => YoutubeAudioStreamSelection.Select(
            tracks, "ru", "Russian", "mp4", "aac", 192_000, 100));
    }

    private static AudioOnlyStreamInfo Track(string code, string name, long bitrate, long size) =>
        new(code, Container.Mp4, new FileSize(size), new Bitrate(bitrate), "aac",
            new Language(code, name), false);
}
