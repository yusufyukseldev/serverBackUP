<#
.SYNOPSIS
    Installs ServerBackup.Service as a Windows Service running under a
    dedicated, low-privilege service account.

.DESCRIPTION
    Must be run elevated. Publishes the service (self-contained, so the
    target machine doesn't need the .NET runtime installed), creates a
    local service account if one doesn't already exist, grants it
    "Log on as a service", and registers the Windows Service via sc.exe.

    The service account intentionally does NOT get admin rights. It only
    needs:
      - Read/write on the repository path(s) configured in appsettings.json
      - "Back up files and directories" + "Restore files and directories"
        user rights (for VSS-based backups) — grant these via secpol.msc
        or an equivalent Group Policy if the service needs VSS.

.PARAMETER ServiceAccountName
    Local account to run the service as. Created if missing.

.PARAMETER PublishDir
    Where to publish the self-contained build. Defaults to
    C:\ServerBackup\service.

.EXAMPLE
    .\install-service.ps1 -ServiceAccountName "svc-serverbackup" -ServiceAccountPassword (Read-Host -AsSecureString "Service account password")
#>
[CmdletBinding()]
param(
    [string]$ServiceAccountName = "svc-serverbackup",

    [Parameter(Mandatory = $true)]
    [SecureString]$ServiceAccountPassword,

    [string]$PublishDir = "C:\ServerBackup\service",

    [string]$ServiceName = "ServerBackup",

    [string]$ServiceDisplayName = "ServerBackup"
)

$ErrorActionPreference = "Stop"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated (Administrator) PowerShell session."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$serviceProject = Join-Path $repoRoot "src\ServerBackup.Service\ServerBackup.Service.csproj"

Write-Host "Publishing self-contained build to '$PublishDir'..."
dotnet publish $serviceProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $PublishDir "ServerBackup.Service.exe"
if (-not (Test-Path $exePath)) {
    throw "Published executable not found at '$exePath'."
}

$localAccount = Get-LocalUser -Name $ServiceAccountName -ErrorAction SilentlyContinue
if ($null -eq $localAccount) {
    Write-Host "Creating local service account '$ServiceAccountName'..."
    New-LocalUser -Name $ServiceAccountName -Password $ServiceAccountPassword `
        -PasswordNeverExpires -UserMayNotChangePassword `
        -Description "ServerBackup Windows Service account (low privilege)." | Out-Null
} else {
    Write-Host "Service account '$ServiceAccountName' already exists — reusing it."
}

# Grant "Log on as a service" (SeServiceLogonRight) without needing secedit's
# full policy round-trip complexity: ntrights-equivalent via LSA API is not
# built into PowerShell, so this uses the standard secedit approach.
$tempDir = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDir | Out-Null
$cfgPath = Join-Path $tempDir "secpol.cfg"
$dbPath = Join-Path $tempDir "secpol.sdb"

secedit /export /cfg $cfgPath | Out-Null
$sid = (Get-LocalUser -Name $ServiceAccountName).SID.Value
$content = Get-Content $cfgPath
$logonRightLine = ($content | Select-String "SeServiceLogonRight").ToString()
if ($logonRightLine -and $logonRightLine -notmatch [regex]::Escape($sid)) {
    $newLine = "$logonRightLine,*$sid"
    $content = $content -replace [regex]::Escape($logonRightLine), $newLine
} elseif (-not $logonRightLine) {
    $content += "SeServiceLogonRight = *$sid"
}
$content | Set-Content $cfgPath

secedit /configure /db $dbPath /cfg $cfgPath /areas USER_RIGHTS | Out-Null
Remove-Item $tempDir -Recurse -Force

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists — stopping and removing it first."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ServiceAccountPassword))

Write-Host "Registering Windows Service '$ServiceName'..."
sc.exe create $ServiceName `
    binPath= "`"$exePath`"" `
    DisplayName= "$ServiceDisplayName" `
    start= auto `
    obj= ".\$ServiceAccountName" `
    password= "$plainPassword" | Out-Null

sc.exe description $ServiceName "Yerel yedekleme servisi — zamanlanmış yedekleme planlarını çalıştırır." | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Write-Host ""
Write-Host "Done. Before starting the service:" -ForegroundColor Yellow
Write-Host "  1. Edit '$PublishDir\appsettings.json' — set ServerBackup:Repositories."
Write-Host "  2. For each repository, run (as an interactive admin):"
Write-Host "       serverbackup repo enable-unattended <repoPath>"
Write-Host "     This wraps the repository key with DPAPI (LocalMachine scope) so the"
Write-Host "     service can unlock it without a password prompt."
Write-Host "  3. Grant '$ServiceAccountName' NTFS read/write access to each repository path."
Write-Host "  4. If backups need VSS, grant '$ServiceAccountName' the 'Back up files and"
Write-Host "     directories' and 'Restore files and directories' user rights (secpol.msc)."
Write-Host "  5. Start-Service -Name $ServiceName"
