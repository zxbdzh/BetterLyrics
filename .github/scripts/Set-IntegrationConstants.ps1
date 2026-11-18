param(
    [Parameter(Mandatory)]
    [string]$SourceDirectory,
    [switch]$WithoutLastFm
)

$ErrorActionPreference = 'Stop'
$integrationSecrets = @{
    DISCORD_APP_ID = $env:DISCORD_APP_ID
    LASTFM_API_KEY = $env:LASTFM_API_KEY
    LASTFM_SHARED_SECRET = $env:LASTFM_SHARED_SECRET
}
$required = @('DISCORD_APP_ID')
if (-not $WithoutLastFm) {
    $required += @('LASTFM_API_KEY', 'LASTFM_SHARED_SECRET')
}
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace($integrationSecrets[$name])) {
        throw "Missing integration secret: $name"
    }
}
if ($WithoutLastFm) {
    # 显式无密钥发行不使用仓库里的 Last.fm 值，包括仍未更正的模板值。
    $integrationSecrets.LASTFM_API_KEY = ''
    $integrationSecrets.LASTFM_SHARED_SECRET = ''
}
else {
    $placeholders = @{
        LASTFM_API_KEY = 'Your api key here'
        LASTFM_SHARED_SECRET = 'Your shared secret here'
    }
    foreach ($name in $placeholders.Keys) {
        if ($integrationSecrets[$name].Trim() -eq $placeholders[$name]) {
            throw "Repository secret $name contains a template placeholder."
        }
    }
}
function ConvertTo-CSharpStringLiteral([string]$value) {
    return '"' + $value.Replace('\', '\\').Replace('"', '\"') + '"'
}
$constantsDir = Join-Path $SourceDirectory 'BetterLyrics.Core/Constants'
$discordAppId = ConvertTo-CSharpStringLiteral $integrationSecrets.DISCORD_APP_ID
$lastFmApiKey = ConvertTo-CSharpStringLiteral $integrationSecrets.LASTFM_API_KEY
$lastFmSharedSecret = ConvertTo-CSharpStringLiteral $integrationSecrets.LASTFM_SHARED_SECRET
@"
namespace BetterLyrics.Core.Constants;
public static class Discord
{
    public const string AppID = $discordAppId;
}
"@ | Set-Content -LiteralPath (Join-Path $constantsDir 'Discord.cs') -Encoding utf8
@"
namespace BetterLyrics.Core.Constants;
public static class LastFM
{
    public const string ApiKey = $lastFmApiKey;
    public const string SharedSecret = $lastFmSharedSecret;
    public const string UnAuthUrl = "https://www.last.fm/settings/applications";
    public const string SessionKeyCredentialKey = "LastFMSessionKey";
}
"@ | Set-Content -LiteralPath (Join-Path $constantsDir 'LastFM.cs') -Encoding utf8
