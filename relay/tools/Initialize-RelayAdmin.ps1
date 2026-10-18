# Windows PowerShell 5.1 / Windows 11. Only explicit -Bootstrap/-Status use the network.
[CmdletBinding(DefaultParameterSetName = 'Help')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Initialize')][switch]$Initialize,
    [Parameter(Mandatory = $true, ParameterSetName = 'Initialize')]
    [ValidatePattern('^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$')]
    [string]$DatabaseId,
    [Parameter(Mandatory = $true, ParameterSetName = 'Show')][switch]$ShowConfiguration,
    [Parameter(Mandatory = $true, ParameterSetName = 'Bootstrap')][switch]$Bootstrap,
    [Parameter(Mandatory = $true, ParameterSetName = 'Status')][switch]$Status,
    [Parameter(Mandatory = $true, ParameterSetName = 'SelfTest')][switch]$SelfTest,
    [Parameter(ParameterSetName = 'Initialize')][Parameter(ParameterSetName = 'Show')]
    [Parameter(ParameterSetName = 'Bootstrap')][Parameter(ParameterSetName = 'Status')]
    [string]$IdentityDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MaVka\Relay'),
    [Parameter(ParameterSetName = 'SelfTest')][string]$OutputFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$provider = [Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function Get-PublicJwk([Security.Cryptography.CngKey]$Key) {
    $blob = $Key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    if ($blob.Length -ne 72 -or [BitConverter]::ToInt32($blob, 4) -ne 32) { throw 'Expected a P-256 public key.' }
    return [ordered]@{ kty = 'EC'; crv = 'P-256'; x = (ConvertTo-Base64Url $blob[8..39]); y = (ConvertTo-Base64Url $blob[40..71]) }
}
function New-StoredKey([string]$Name, [Security.Cryptography.CngAlgorithm]$Algorithm, [Security.Cryptography.CngKeyUsages]$Usage) {
    $parameters = New-Object Security.Cryptography.CngKeyCreationParameters
    $parameters.Provider = $provider
    $parameters.ExportPolicy = [Security.Cryptography.CngExportPolicies]::None
    $parameters.KeyUsage = $Usage
    $parameters.KeyCreationOptions = [Security.Cryptography.CngKeyCreationOptions]::None
    return [Security.Cryptography.CngKey]::Create($Algorithm, $Name, $parameters)
}
function Write-NewPublicFile([string]$Path, [string]$Text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $stream = New-Object IO.FileStream($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Read-Identity([string]$Folder) {
    $path = Join-Path $Folder 'admin-public.json'
    $state = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($state.format -ne 'MAVKA_RELAY_ADMIN_WINDOWS_V1' -or $state.deviceId -notmatch '^[a-f0-9-]{36}$') { throw 'Invalid identity metadata.' }
    $signing = $null; $encryption = $null
    try {
        $signing = [Security.Cryptography.CngKey]::Open($state.signingKeyName, $provider, [Security.Cryptography.CngKeyOpenOptions]::None)
        $encryption = [Security.Cryptography.CngKey]::Open($state.encryptionKeyName, $provider, [Security.Cryptography.CngKeyOpenOptions]::None)
        if ($signing.IsMachineKey -or $encryption.IsMachineKey -or
            $signing.ExportPolicy -ne [Security.Cryptography.CngExportPolicies]::None -or
            $encryption.ExportPolicy -ne [Security.Cryptography.CngExportPolicies]::None) { throw 'Unexpected key policy.' }
        $actualSigning = Get-PublicJwk $signing | ConvertTo-Json -Compress
        $actualEncryption = Get-PublicJwk $encryption | ConvertTo-Json -Compress
        if ($actualSigning -cne ($state.publicSigningKey | ConvertTo-Json -Compress) -or
            $actualEncryption -cne ($state.publicEncryptionKey | ConvertTo-Json -Compress)) { throw 'Metadata does not match the stored keys.' }
        return $state
    } finally {
        if ($signing) { $signing.Dispose() }; if ($encryption) { $encryption.Dispose() }
    }
}
function Initialize-Identity([string]$Folder, [string]$D1Id) {
    if ($D1Id -eq '00000000-0000-0000-0000-000000000000') { throw 'The existing D1 UUID is required.' }
    $path = Join-Path $Folder 'admin-public.json'
    if ([IO.File]::Exists($path)) {
        $existing = Read-Identity $Folder
        if ($existing.databaseId -ine $D1Id) { throw 'This identity is already associated with a different database.' }
        return $existing
    }
    [void][IO.Directory]::CreateDirectory($Folder)
    $deviceId = [Guid]::NewGuid().ToString()
    $signingName = 'MaVka.Relay.Admin.Signing.' + $deviceId
    $encryptionName = 'MaVka.Relay.Admin.Encryption.' + $deviceId
    $signing = $null; $encryption = $null
    try {
        $signing = New-StoredKey $signingName ([Security.Cryptography.CngAlgorithm]::ECDsaP256) ([Security.Cryptography.CngKeyUsages]::Signing)
        $encryption = New-StoredKey $encryptionName ([Security.Cryptography.CngAlgorithm]::ECDiffieHellmanP256) ([Security.Cryptography.CngKeyUsages]::KeyAgreement)
        $state = [ordered]@{
            format = 'MAVKA_RELAY_ADMIN_WINDOWS_V1'; databaseId = $D1Id; deviceId = $deviceId
            signingKeyName = $signingName; encryptionKeyName = $encryptionName
            publicSigningKey = (Get-PublicJwk $signing); publicEncryptionKey = (Get-PublicJwk $encryption)
        }
        Write-NewPublicFile $path ($state | ConvertTo-Json -Depth 8)
        return [pscustomobject]$state
    } catch {
        # These key handles belong only to this attempted creation, never to a pre-existing identity.
        if ($encryption) { $encryption.Delete() }; if ($signing) { $signing.Delete() }
        throw
    } finally {
        if ($encryption) { $encryption.Dispose() }; if ($signing) { $signing.Dispose() }
    }
}
function Get-Sha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ,$sha.ComputeHash($Bytes) } finally { $sha.Dispose() }
}
function New-RelaySignature([Security.Cryptography.CngKey]$Key, [string]$DeviceId, [string]$Method, [string]$Path, [string]$Body) {
    $time = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString([Globalization.CultureInfo]::InvariantCulture)
    $nonceBytes = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($nonceBytes) } finally { $rng.Dispose() }
    $nonce = ConvertTo-Base64Url $nonceBytes
    $bodyHash = [BitConverter]::ToString((Get-Sha256 ([Text.Encoding]::UTF8.GetBytes($Body)))).Replace('-', '').ToLowerInvariant()
    $inputText = @('MAVKA_RELAY_REQUEST_V1', 'https://mavka-relay.arudichik.workers.dev', $Method,
        $Path, $DeviceId, $time, $nonce, $bodyHash) -join "`n"
    $ecdsa = New-Object Security.Cryptography.ECDsaCng($Key)
    try { $signature = $ecdsa.SignHash((Get-Sha256 ([Text.Encoding]::UTF8.GetBytes($inputText)))) }
    finally { $ecdsa.Dispose() }
    if ($signature.Length -ne 64) { throw 'Expected IEEE P1363 signature.' }
    return [pscustomobject]@{ time = $time; nonce = $nonce; signature = (ConvertTo-Base64Url $signature) }
}
function Invoke-RelayCheck([object]$State, [bool]$CreateMainDevice) {
    $key = [Security.Cryptography.CngKey]::Open($State.signingKeyName, $provider)
    try {
        if ($CreateMainDevice) {
            $method = 'POST'; $path = '/v1/bootstrap'
            $body = [ordered]@{ publicEncryptionKey = $State.publicEncryptionKey } | ConvertTo-Json -Depth 8 -Compress
        } else { $method = 'GET'; $path = '/v1/status'; $body = '' }
        $proof = New-RelaySignature $key $State.deviceId $method $path $body
        $parameters = @{
            Uri = ('https://mavka-relay.arudichik.workers.dev' + $path); Method = $method
            UseBasicParsing = $true; MaximumRedirection = 0; TimeoutSec = 30; ErrorAction = 'Stop'
            Headers = @{ 'X-Mavka-Device' = $State.deviceId; 'X-Mavka-Time' = $proof.time
                'X-Mavka-Nonce' = $proof.nonce; 'X-Mavka-Signature' = $proof.signature }
        }
        if ($CreateMainDevice) { $parameters.ContentType = 'application/json'; $parameters.Body = [Text.Encoding]::UTF8.GetBytes($body) }
        $response = Invoke-WebRequest @parameters
        return ($response.Content | ConvertFrom-Json)
    } finally { $key.Dispose() }
}
function Test-PrivateExportDenied([Security.Cryptography.CngKey]$Key) {
    try {
        $unexpected = $Key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)
        [Array]::Clear($unexpected, 0, $unexpected.Length)
        return $false
    } catch [Security.Cryptography.CryptographicException] { return $true }
}
function Invoke-SyntheticSelfTest {
    $folder = Join-Path ([IO.Path]::GetTempPath()) ('mavka-relay-cng-test-' + [Guid]::NewGuid().ToString())
    $state = $null; $signing = $null; $encryption = $null
    try {
        $fakeDatabaseId = [Guid]::NewGuid().ToString()
        $state = Initialize-Identity $folder $fakeDatabaseId
        $again = Initialize-Identity $folder $fakeDatabaseId
        if ($state.deviceId -ne $again.deviceId) { throw 'Initialization replaced the identity.' }
        $wrongDatabaseRejected = $false
        try { $null = Initialize-Identity $folder ([Guid]::NewGuid().ToString()) }
        catch { $wrongDatabaseRejected = $true }
        if (-not $wrongDatabaseRejected) { throw 'Database mismatch was accepted.' }
        $signing = [Security.Cryptography.CngKey]::Open($state.signingKeyName, $provider)
        $encryption = [Security.Cryptography.CngKey]::Open($state.encryptionKeyName, $provider)
        $denied = (Test-PrivateExportDenied $signing) -and (Test-PrivateExportDenied $encryption)
        if (-not $denied) { throw 'Private export unexpectedly succeeded.' }
        $body = [ordered]@{ publicEncryptionKey = $state.publicEncryptionKey } | ConvertTo-Json -Depth 8 -Compress
        $proof = New-RelaySignature $signing $state.deviceId 'POST' '/v1/bootstrap' $body
        $fixture = [ordered]@{
            format = 'MAVKA_RELAY_WINDOWS_QA_V1'; deviceId = $state.deviceId
            publicSigningKey = $state.publicSigningKey; publicEncryptionKey = $state.publicEncryptionKey
            time = $proof.time; nonce = $proof.nonce; rawBody = $body; signature = $proof.signature
            privateExportDenied = $denied; userScoped = (-not $signing.IsMachineKey)
            identityReused = ($state.deviceId -eq $again.deviceId); wrongDatabaseRejected = $wrongDatabaseRejected
        }
    } finally {
        if ($encryption) { $encryption.Delete(); $encryption.Dispose() }
        if ($signing) { $signing.Delete(); $signing.Dispose() }
        if ($state) {
            foreach ($name in @($state.signingKeyName, $state.encryptionKeyName)) {
                if ([Security.Cryptography.CngKey]::Exists($name, $provider)) { throw 'Synthetic key cleanup failed.' }
            }
        }
        if ([IO.Directory]::Exists($folder)) { [IO.Directory]::Delete($folder, $true) }
    }
    return $fixture
}

switch ($PSCmdlet.ParameterSetName) {
    'Initialize' {
        $state = Initialize-Identity ([IO.Path]::GetFullPath($IdentityDirectory)) $DatabaseId
        [ordered]@{
            MAVKA_D1_DATABASE_ID = $state.databaseId
            MAVKA_ADMIN_DEVICE_ID = $state.deviceId
            MAVKA_ADMIN_SIGNING_JWK = ($state.publicSigningKey | ConvertTo-Json -Compress)
        } | ConvertTo-Json -Depth 4
        Write-Host ('Public metadata saved: ' + (Join-Path $IdentityDirectory 'admin-public.json'))
        Write-Host 'Private keys remain in the current Windows user CNG store. No network requests made.'
    }
    'Show' {
        $state = Read-Identity ([IO.Path]::GetFullPath($IdentityDirectory))
        [ordered]@{ MAVKA_D1_DATABASE_ID = $state.databaseId; MAVKA_ADMIN_DEVICE_ID = $state.deviceId
            MAVKA_ADMIN_SIGNING_JWK = ($state.publicSigningKey | ConvertTo-Json -Compress) } | ConvertTo-Json -Depth 4
    }
    'SelfTest' {
        $result = Invoke-SyntheticSelfTest | ConvertTo-Json -Depth 8 -Compress
        if ($OutputFile) { Write-NewPublicFile ([IO.Path]::GetFullPath($OutputFile)) $result } else { $result }
    }
    'Bootstrap' { Invoke-RelayCheck (Read-Identity ([IO.Path]::GetFullPath($IdentityDirectory))) $true | ConvertTo-Json -Depth 4 }
    'Status' { Invoke-RelayCheck (Read-Identity ([IO.Path]::GetFullPath($IdentityDirectory))) $false | ConvertTo-Json -Depth 4 }
    default { Write-Host 'Use -Initialize -DatabaseId <existing-D1-UUID>, -ShowConfiguration, -SelfTest, -Bootstrap, or -Status.' }
}
