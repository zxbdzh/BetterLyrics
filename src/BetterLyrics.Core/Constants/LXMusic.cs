namespace BetterLyrics.Core.Constants
{
    public static class LXMusic
    {
        public const string DefaultServer = "http://127.0.0.1:23330";
        public const string QuerySuffix = "/subscribe-player-status?filter=progress,duration,picUrl,mediaKind,contentId,transcript,longFormContent";

        public static string ResolveServer(string? configuredServer)
        {
            var server = string.IsNullOrWhiteSpace(configuredServer)
                ? DefaultServer
                : configuredServer.Trim();

            return server.TrimEnd('/');
        }

        public static Uri GetSubscriptionUri(string? configuredServer)
        {
            return new Uri($"{ResolveServer(configuredServer)}{QuerySuffix}", UriKind.Absolute);
        }
    }
}
