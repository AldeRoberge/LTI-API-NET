# Import TLS certificate into Trusted Root on Windows (Local Machine) with logging

# --- CONFIG ---
$certPath = "C:\Users\rotmg\Documents\GitHub\AutomaticDialogGenerator\src\ADG\ADG.LTI\Container\certs\tls.crt"
$logFile = Join-Path -Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) -ChildPath "ImportTlsCert.log"

# --- LOGGING FUNCTION ---
function Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "$timestamp [$Level] $Message"
    Write-Output $logMessage
    Add-Content -Path $logFile -Value $logMessage
}

# --- CHECK ADMIN RIGHTS ---
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Log "You must run this script as Administrator!" "ERROR"
    exit 1
}
Log "Running with Administrator privileges."

# --- SET EXECUTION POLICY FOR THIS SESSION ---
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process -Force
Log "Execution policy set to RemoteSigned for this session."

# --- IMPORT CERTIFICATE ---
try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
    $cert.Import($certPath)
    Log "Certificate loaded from $certPath."

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store "Root", "LocalMachine"
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $store.Add($cert)
    $store.Close()

    Log "Certificate imported successfully into Trusted Root Certification Authorities." "SUCCESS"
} catch {
    Log "Failed to import certificate: $_" "ERROR"
}
