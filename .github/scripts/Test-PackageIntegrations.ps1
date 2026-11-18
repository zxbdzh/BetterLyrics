param(
    [Parameter(Mandatory)]
    [string]$PackagePath,
    [switch]$WithoutLastFm
)

$ErrorActionPreference = 'Stop'

function Get-IntegrationConstant($metadata, [string]$typeName, [string]$fieldName) {
    foreach ($typeHandle in $metadata.TypeDefinitions) {
        $definition = $metadata.GetTypeDefinition($typeHandle)
        if ($metadata.GetString($definition.Namespace) -ne 'BetterLyrics.Core.Constants' -or
            $metadata.GetString($definition.Name) -ne $typeName) {
            continue
        }
        foreach ($fieldHandle in $definition.GetFields()) {
            $field = $metadata.GetFieldDefinition($fieldHandle)
            if ($metadata.GetString($field.Name) -ne $fieldName) { continue }
            $constantHandle = $field.GetDefaultValue()
            if ($constantHandle.IsNil) { break }
            $constant = $metadata.GetConstant($constantHandle)
            if ($constant.TypeCode -ne [System.Reflection.Metadata.ConstantTypeCode]::String) { break }
            return [Text.Encoding]::Unicode.GetString($metadata.GetBlobBytes($constant.Value))
        }
        break
    }
    throw "The package is missing the integration constant $typeName.$fieldName."
}

$expectedConstants = @(
    @{ Secret = 'LASTFM_API_KEY'; Type = 'LastFM'; Field = 'ApiKey'; Placeholder = 'Your api key here' },
    @{ Secret = 'LASTFM_SHARED_SECRET'; Type = 'LastFM'; Field = 'SharedSecret'; Placeholder = 'Your shared secret here' },
    @{ Secret = 'DISCORD_APP_ID'; Type = 'Discord'; Field = 'AppID'; Placeholder = $null }
)

$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath))
$assemblyStream = [IO.MemoryStream]::new()
try {
    $entry = $archive.GetEntry('BetterLyrics.WinUI3/BetterLyrics.Core.dll')
    if ($null -eq $entry) { throw 'BetterLyrics.Core.dll is missing from the package.' }
    $entryStream = $entry.Open()
    try { $entryStream.CopyTo($assemblyStream) }
    finally { $entryStream.Dispose() }
    $assemblyStream.Position = 0

    # 只解析元数据，避免 CI 的 PowerShell 运行时与应用目标框架不同而无法加载程序集。
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
    try {
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        foreach ($item in $expectedConstants) {
            $actual = Get-IntegrationConstant $metadata $item.Type $item.Field
            if ($WithoutLastFm -and $item.Type -eq 'LastFM') {
                # 无密钥发行必须真正清空凭据，不能把模板值打包后伪装成可用。
                if ($actual -ne '') {
                    throw "The no-Last.fm package unexpectedly contains $($item.Secret)."
                }
                Write-Output "$($item.Secret): Last.fm is explicitly disabled."
                continue
            }
            $expected = [Environment]::GetEnvironmentVariable($item.Secret)
            if ([string]::IsNullOrWhiteSpace($expected)) {
                throw "Repository secret $($item.Secret) is unavailable during package verification."
            }
            if ([string]::IsNullOrWhiteSpace($actual) -or
                ($item.Placeholder -and $actual.Trim() -eq $item.Placeholder)) {
                throw "The packaged value for $($item.Secret) is empty or contains a template placeholder."
            }
            if (-not [string]::Equals($actual, $expected, [StringComparison]::Ordinal)) {
                throw "The packaged value for $($item.Secret) does not match the repository secret."
            }
            # 验收日志只报告字段是否一致，不打印值、哈希或长度。
            Write-Output "$($item.Secret): packaged value matches the repository secret."
        }
    }
    finally { $peReader.Dispose() }
}
finally {
    $assemblyStream.Dispose()
    $archive.Dispose()
}
