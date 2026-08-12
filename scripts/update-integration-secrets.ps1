$ErrorActionPreference = 'Stop'

function Get-ConstantValue {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $source = Get-Content -Raw -LiteralPath $Path
    $match = [regex]::Match(
        $source,
        "(?m)\b$([regex]::Escape($Name))\s*=\s*`"([^`"]+)`""
    )
    if (-not $match.Success) {
        throw "Constant $Name was not found in $Path."
    }

    return $match.Groups[1].Value
}

$secretValues = [ordered]@{
    DISCORD_APP_ID = Get-ConstantValue `
        -Path 'src\BetterLyrics.Core\Constants\Discord.cs' `
        -Name 'AppID'
    LASTFM_API_KEY = Get-ConstantValue `
        -Path 'src\BetterLyrics.Core\Constants\LastFM.cs' `
        -Name 'ApiKey'
    LASTFM_SHARED_SECRET = Get-ConstantValue `
        -Path 'src\BetterLyrics.Core\Constants\LastFM.cs' `
        -Name 'SharedSecret'
}

try {
    foreach ($secret in $secretValues.GetEnumerator()) {
        & gh secret set $secret.Key --body $secret.Value
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to update GitHub secret $($secret.Key)."
        }
    }

    Write-Output 'Updated BetterLyrics integration secrets.'
}
finally {
    foreach ($key in @($secretValues.Keys)) {
        $secretValues[$key] = $null
    }
}
