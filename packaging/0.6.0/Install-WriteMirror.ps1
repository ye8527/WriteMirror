$ErrorActionPreference = "Stop"

$packagePath = Join-Path $PSScriptRoot "WriteMirror_0.6.0_ARM64.msix"
$certificatePath = Join-Path $PSScriptRoot "WriteMirror_公開証明書.cer"
$expectedThumbprint = "ECEEABF981A97A2E0CDB7AD7DA18B535D73B9976"
$expectedVersion = [Version]"0.6.0.4"

Write-Host "WriteMirror 0.6.0 ARM64を確認しています…"
if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "MSIXが見つかりません: $packagePath"
}
if (-not (Test-Path -LiteralPath $certificatePath)) {
    throw "公開証明書が見つかりません: $certificatePath"
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
if ($certificate.Thumbprint -ne $expectedThumbprint) {
    throw "公開証明書の指紋が配布情報と一致しません。インストールを中止します。"
}

$signature = Get-AuthenticodeSignature -LiteralPath $packagePath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "MSIXの署名を確認できません: $($signature.StatusMessage)"
}
if ($signature.SignerCertificate.Thumbprint -ne $expectedThumbprint) {
    throw "MSIX署名者の指紋が配布情報と一致しません。インストールを中止します。"
}

$trustedPeople = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    "TrustedPeople",
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
try {
    $trustedPeople.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $alreadyTrusted = $trustedPeople.Certificates |
        Where-Object Thumbprint -eq $expectedThumbprint
    if (-not $alreadyTrusted) {
        $trustedPeople.Add($certificate)
        Write-Host "現在のユーザーのTrustedPeopleへ公開証明書を登録しました。"
    }
    else {
        Write-Host "公開証明書は登録済みです。"
    }
}
finally {
    $trustedPeople.Close()
}

$installed = Get-AppxPackage -Name "WriteMirror" -ErrorAction SilentlyContinue
if ($installed -and [Version]$installed.Version -eq $expectedVersion) {
    Write-Host "WriteMirror $expectedVersion はインストール済みです。"
}
else {
    Add-AppxPackage -Path $packagePath -ForceUpdateFromAnyVersion
    $installed = Get-AppxPackage -Name "WriteMirror"
    if (-not $installed -or [Version]$installed.Version -ne $expectedVersion) {
        throw "インストール後のバージョンを確認できませんでした。"
    }
    Write-Host "WriteMirror $($installed.Version) をインストールしました。"
}

Write-Host "WriteMirrorを起動します。初回はWindows MLの準備に時間がかかる場合があります。"
Start-Process "explorer.exe" "shell:AppsFolder\WriteMirror_h5hdpgey430jp!App"
