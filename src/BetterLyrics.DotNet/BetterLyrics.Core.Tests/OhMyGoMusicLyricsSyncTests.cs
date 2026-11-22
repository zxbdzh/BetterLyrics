using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Extensions;
using BetterLyrics.Core.Helpers.Lyrics.ContentParser;
using BetterLyrics.Core.Implementations.Services;
using BetterLyrics.Core.Implementations.Services.GsmtcService;
using BetterLyrics.Core.Interfaces.Providers;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Domain;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using BetterLyrics.Core.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class OhMyGoMusicLyricsSyncTests
{
    private static readonly LiteDatabase Database = new(new MemoryStream());
    private static readonly ISongSearchMapService Mappings = new SongSearchMapService(
        ServiceStub.Create<IDatabaseService>((method, _) => method.Name == "get_SongSearchMapDb" ? Database : throw new NotSupportedException()),
        NullLogger<SongSearchMapService>.Instance);
    private static AppSettings Settings = null!;
    private static readonly ISettingsService SettingsService = ServiceStub.Create<ISettingsService>(
        (method, _) => method.Name == "get_AppSettings" ? Settings : throw new NotSupportedException());
    private static readonly IAppUIThreadProvider UiThread = ServiceStub.Create<IAppUIThreadProvider>((method, arguments) =>
    {
        ((Action)arguments![0]!)();
        return method.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    });
    private static readonly IGlobalToastProvider Toasts = ServiceStub.Create<IGlobalToastProvider>((_, _) => null);
    private static readonly ITranslationService Translation = ServiceStub.Create<ITranslationService>((_, _) =>
        throw new InvalidOperationException("Synchronization must not generate translations."));
    private static readonly ITransliterationService Transliteration = ServiceStub.Create<ITransliterationService>((_, _) =>
        throw new InvalidOperationException("Synchronization must not generate transliterations."));
    private static readonly ILocalizationService Localization = ServiceStub.Create<ILocalizationService>((_, arguments) => arguments![0]);

    static OhMyGoMusicLyricsSyncTests()
    {
        Ioc.Default.ConfigureServices(new ServiceCollection()
            .AddSingleton(SettingsService)
            .AddSingleton(UiThread)
            .AddSingleton(Toasts)
            .AddSingleton(Translation)
            .AddSingleton(Transliteration)
            .AddSingleton(Localization)
            .AddSingleton(Mappings)
            .AddSingleton<Microsoft.Extensions.Logging.ILogger<LyricsContentParser>>(NullLogger<LyricsContentParser>.Instance)
            .AddSingleton(ServiceStub.Create<IStringConverterProvider>((_, arguments) => arguments![0]))
            .AddSingleton(ServiceStub.Create<IMediaManagerProvider>((_, _) => throw new NotSupportedException()))
            .AddSingleton(ServiceStub.Create<IMonitorProvider>((method, _) => method.Name == "GetPrimaryMonitorInfo"
                ? ("test-monitor", new AppRect(0, 0, 1920, 1080)) : throw new NotSupportedException(method.Name)))
            .BuildServiceProvider());
    }

    public OhMyGoMusicLyricsSyncTests()
    {
        Database.GetCollection<MappedSongSearchQuery>("songSearchMap").DeleteAll();
        Settings = new AppSettings();
        Settings.TranslationSettings.IsTranslationEnabled = false;
        Settings.TranslationSettings.IsFilterEnabled = true;
    }

    // 同步导出不能受显示开关影响，也不能生成原来源不存在的翻译或逐字时间。
    [Fact]
    public async Task SynchronizationPreservesHiddenTranslationAndUnfilteredLines()
    {
        var source = new LyricsCacheItem
        {
            Provider = LyricsProvider.Netease,
            Raw = "[00:00.000]作词：测试\n[00:01.234]hello world",
            Translation = "[00:01.234]你好世界"
        };
        var tracks = await new LyricsContentParser().ParseForSynchronizationAsync(source, CancellationToken.None);

        var output = OhMyGoMusicLyricsAdapter.ToWriteModel(source, tracks);

        Assert.Contains("作词：测试", output.Lrc);
        Assert.Equal("[00:01.234]你好世界\n", output.Translation);
        Assert.Equal(2, output.Lines.Count);
        Assert.All(output.Lines, line => Assert.Empty(line.Words));
    }

    // 无论远端成功、拒绝还是离线，本地必须先存实际歌词，不能丢掉用户选中的候选。
    [Theory]
    [InlineData(200)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(0)]
    public async Task SavePersistsSelectedLyricsBeforeAttemptingRemoteWrite(int statusCode)
    {
        var song = CurrentSong();
        var source = new LyricsCacheItem { Provider = LyricsProvider.Netease, Raw = "[00:01.000]manual lyrics" };
        var written = false;
        var refreshed = false;
        var media = ServiceStub.Create<IGsmtcService>((method, arguments) => method.Name switch
        {
            "get_CurrentSongInfo" => song,
            "get_CurrentOhMyGoMusicStatus" => CurrentStatus(),
            "WriteCurrentOhMyGoMusicLyricsAsync" => WriteAsync(),
            "UpdateLyrics" => Refresh(),
            _ => throw new NotSupportedException(method.Name)
        });
        var search = ServiceStub.Create<ILyricsSearchService>((method, _) => method.Name switch
        {
            "GetActiveProviders" => new List<LyricsProvider> { source.Provider },
            "SearchAllAsync" => Results(),
            _ => throw new NotSupportedException(method.Name)
        });
        var viewModel = new LyricsSearchControlViewModel(search, media, SettingsService, Mappings, UiThread, Toasts,
            NullLogger<LyricsSearchControlViewModel>.Instance, Localization);
        try
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(viewModel.IsSearching) && !viewModel.IsSearching)
                    completed.TrySetResult();
            };
            viewModel.SearchCommand.Execute(null);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(written);
            viewModel.SelectedLyricsSearchResult = Assert.Single(viewModel.LyricsSearchResults);

            await viewModel.SaveCommand.ExecuteAsync(null);

            var saved = Assert.IsType<MappedSongSearchQuery>(await Mappings.TryGetMappingAsync(song));
            Assert.Equal("[00:01.000]manual lyrics", saved.ManualLyrics?.Raw?.TrimEnd());
            Assert.Equal(statusCode == 200, saved.MyGoOverrideSaved);
            Assert.True(written);
            Assert.True(refreshed);
        }
        finally
        {
            viewModel.IsActive = false;
        }

        async Task WriteAsync()
        {
            var local = await Mappings.TryGetMappingAsync(song);
            Assert.Equal("[00:01.000]manual lyrics", local?.ManualLyrics?.Raw?.TrimEnd());
            written = true;
            if (statusCode == 0) throw new HttpRequestException("MyGo is offline.");
            if (statusCode != 200) throw new HttpRequestException("Rejected.", null, (HttpStatusCode)statusCode);
        }

        object? Refresh()
        {
            refreshed = true;
            return null;
        }

        async IAsyncEnumerable<LyricsCacheItem> Results()
        {
            await Task.Yield();
            yield return source;
        }
    }

    // 清除覆盖的确认必须落盘；随后断线并重新创建播放器服务也不能复活旧歌词。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearedOverrideStaysClearedAfterOfflineReopen(bool hasSourceLyrics)
    {
        using var endpoint = new MyGoEndpoint(hasSourceLyrics ? "[00:01.000]source lyrics" : null);
        Settings.GeneralSettings.OhMyGoMusicServer = endpoint.Address;
        await SaveManualMappingAsync();
        var externalSearches = 0;
        var search = ServiceStub.Create<ILyricsSearchService>((_, _) =>
        {
            externalSearches++;
            return Task.FromResult<LyricsCacheItem?>(new() { Raw = "[00:01.000]old result" });
        });

        var online = new TestGsmtcService(search);
        await online.RefreshAsync();
        var saved = Assert.IsType<MappedSongSearchQuery>(await Mappings.TryGetMappingAsync(CurrentSong()));
        Assert.Null(saved.ManualLyrics);
        Assert.True(saved.MyGoOverrideSaved);
        if (hasSourceLyrics)
            Assert.Equal("source lyrics", online.CurrentLyricsData!.LyricsLines.Single().PrimaryText);

        endpoint.Dispose();
        var reopened = new TestGsmtcService(search);
        await reopened.RefreshAsync();

        Assert.True(reopened.CurrentLyricsData!.IsPlaceholder);
        Assert.Equal(0, externalSearches);
    }

    // 尚未被清除的已同步歌词，在 MyGo 离线时仍可从本地持久备份恢复。
    [Fact]
    public async Task SyncedLyricsRemainAvailableWhenMyGoIsOffline()
    {
        using var endpoint = new MyGoEndpoint(null);
        Settings.GeneralSettings.OhMyGoMusicServer = endpoint.Address;
        endpoint.Dispose();
        await SaveManualMappingAsync();
        var service = new TestGsmtcService(ServiceStub.Create<ILyricsSearchService>((_, _) => throw new NotSupportedException()));

        await service.RefreshAsync();

        Assert.Equal("manual lyrics", service.CurrentLyricsData!.LyricsLines.Single().PrimaryText);
    }

    // 保存期间播放轨道、实例或稳定身份变化时，不能把搜索结果写给当前的另一首歌。
    [Theory]
    [InlineData("track")]
    [InlineData("instance")]
    [InlineData("identity")]
    [InlineData("metadata")]
    public async Task WriteRejectsChangedCurrentSongBeforeSendingLyrics(string changed)
    {
        using var endpoint = new MyGoEndpoint(null);
        Settings.GeneralSettings.OhMyGoMusicServer = endpoint.Address;
        var song = CurrentSong();
        if (changed == "metadata") song.Title = "Another song";
        var expected = new OhMyGoMusicStatus
        {
            TrackId = changed == "track" ? 6UL : 7UL,
            InstanceId = changed == "instance" ? "previous-instance" : "instance",
            SongKey = new() { Source = "test", StableId = changed == "identity" ? "other-song" : "song" }
        };
        var service = new TestGsmtcService(null!);
        service.SetSong(song);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.WriteCurrentOhMyGoMusicLyricsAsync(expected, song, new() { Lrc = "[00:01.000]manual lyrics" }));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal(0, endpoint.WriteRequests);
    }

    private static Task SaveManualMappingAsync() => Mappings.SaveMappingAsync(new()
    {
        OriginalTitle = "Song", OriginalArtist = "Artist", OriginalAlbum = "Album",
        ManualLyrics = OhMyGoMusicLyricsAdapter.ToCacheItem(new() { Lrc = "[00:01.000]manual lyrics" }),
        MyGoOverrideSaved = true
    });

    private static SongInfo CurrentSong() => new()
    {
        Title = "Song", Artist = "Artist", Album = "Album", PlayerId = "Oh MyGo Music.exe", StartedAt = 1
    };

    private static OhMyGoMusicStatus CurrentStatus() => new()
    {
        Title = "Song", Artist = "Artist", Album = "Album", TrackId = 7, InstanceId = "instance",
        SongKey = new() { Source = "test", StableId = "song" }
    };

    public sealed class TestGsmtcService(ILyricsSearchService search) : GsmtcServiceBase(
        SettingsService, null!, search, Translation, Transliteration, UiThread, NullLogger.Instance, Toasts, null!)
    {
        protected override IMediaSessionProvider? GetCurrentDesiredSession() => null;

        public void SetSong(SongInfo song) =>
            typeof(GsmtcServiceBase).GetProperty(nameof(CurrentSongInfo))!.SetValue(this, song);

        public async Task RefreshAsync()
        {
            // 只初始化歌曲快照，不启动 Windows 媒体会话、窗口或状态订阅。
            SetSong(CurrentSong());
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(CurrentLyricsData) && CurrentLyricsData is { } lyrics &&
                    (!lyrics.IsPlaceholder || ReferenceEquals(lyrics, LyricsDataExtensions.NotFoundPlaceholder)))
                    completed.TrySetResult();
            };
            UpdateLyrics();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class MyGoEndpoint : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _requests;
        public string Address { get; }
        public int WriteRequests { get; private set; }

        public MyGoEndpoint(string? lyrics)
        {
            _listener.Start();
            Address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _requests = ServeAsync(lyrics);
        }

        private async Task ServeAsync(string? lyrics)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token);
                    if (request?.StartsWith("PUT ") == true) WriteRequests++;
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    var statusRequest = request?.StartsWith("GET /status ") == true;
                    var found = statusRequest || lyrics != null;
                    var body = Encoding.UTF8.GetBytes(statusRequest
                        ? JsonSerializer.Serialize(CurrentStatus())
                        : JsonSerializer.Serialize(new OhMyGoMusicLyrics
                        {
                            TrackId = 7, SongKey = CurrentStatus().SongKey, Lrc = lyrics ?? ""
                        }));
                    var header = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token);
                    await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _requests.GetAwaiter().GetResult();
        }
    }

    public class ServiceStub : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => InvokeMethod(method!, arguments);

        public static TService Create<TService>(Func<MethodInfo, object?[]?, object?> invoke) where TService : class
        {
            var result = Create<TService, ServiceStub>();
            ((ServiceStub)(object)result).InvokeMethod = invoke;
            return result;
        }
    }
}
