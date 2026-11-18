$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('betterlyrics-integration-test-' + [Guid]::NewGuid().ToString('N'))
$sourceDirectory = Join-Path $testRoot 'source'
$constantsDir = Join-Path $sourceDirectory 'BetterLyrics.Core/Constants'
New-Item -ItemType Directory -Path $constantsDir -Force | Out-Null

function New-TestPackage {
    $payloadRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    $payload = Join-Path $payloadRoot 'BetterLyrics.WinUI3'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Add-Type -Path @((Join-Path $constantsDir 'Discord.cs'), (Join-Path $constantsDir 'LastFM.cs')) `
        -OutputAssembly (Join-Path $payload 'BetterLyrics.Core.dll')
    $package = "$payloadRoot.msix"
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot, $package)
    return $package
}

function Assert-Rejected([scriptblock]$Action, [string]$ExpectedMessage) {
    $rejected = $false
    try { & $Action }
    catch {
        if (-not $_.Exception.Message.Contains($ExpectedMessage)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Expected rejection: $ExpectedMessage" }
}

$generator = Join-Path $PSScriptRoot 'Set-IntegrationConstants.ps1'
$verifier = Join-Path $PSScriptRoot 'Test-PackageIntegrations.ps1'
$saved = @{}
foreach ($name in @('DISCORD_APP_ID', 'LASTFM_API_KEY', 'LASTFM_SHARED_SECRET')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:DISCORD_APP_ID = '123456789012345678'
    $env:LASTFM_API_KEY = 'Your api key here'
    $env:LASTFM_SHARED_SECRET = 'Your shared secret here'
    & $generator -SourceDirectory $sourceDirectory -WithoutLastFm
    $package = New-TestPackage
    & $verifier -PackagePath $package -WithoutLastFm
    Write-Output 'PASS: no-Last.fm mode clears template values from the generated package.'

    Assert-Rejected { & $verifier -PackagePath $package } 'empty or contains a template placeholder'
    Assert-Rejected { & $generator -SourceDirectory $sourceDirectory } 'template placeholder'
    Write-Output 'PASS: regular mode still rejects empty packages and template credentials.'

    $env:LASTFM_API_KEY = $null
    $env:LASTFM_SHARED_SECRET = $null
    & $generator -SourceDirectory $sourceDirectory -WithoutLastFm
    Assert-Rejected { & $generator -SourceDirectory $sourceDirectory } 'Missing integration secret'
    Write-Output 'PASS: missing Last.fm secrets are allowed only in explicit no-Last.fm mode.'

    $env:LASTFM_API_KEY = 'configured-test-key'
    $env:LASTFM_SHARED_SECRET = 'configured-test-secret'
    & $generator -SourceDirectory $sourceDirectory
    $configuredPackage = New-TestPackage
    & $verifier -PackagePath $configuredPackage
    Assert-Rejected { & $verifier -PackagePath $configuredPackage -WithoutLastFm } 'unexpectedly contains'
    Write-Output 'PASS: configured packages match credentials and cannot masquerade as no-Last.fm packages.'

    $env:LASTFM_API_KEY = 'different-test-key'
    Assert-Rejected { & $verifier -PackagePath $configuredPackage } 'does not match'
    Write-Output 'PASS: regular mode rejects a packaged credential mismatch.'
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name])
    }
}
