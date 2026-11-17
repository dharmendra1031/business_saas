param(
    [Parameter(Mandatory = $true)]
    [string]$Email
)

$ErrorActionPreference = 'Stop'
$securePassword = Read-Host 'Dedicated Super Admin password (16+ characters)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)

try {
    $env:PLATFORM_BOOTSTRAP_EMAIL = $Email
    $env:PLATFORM_BOOTSTRAP_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    dotnet run --project backend/BusinessSaas.Api/BusinessSaas.Api.csproj --configuration Release -- --bootstrap-platform-owner
    if ($LASTEXITCODE -ne 0) {
        throw 'Platform owner bootstrap failed.'
    }
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    Remove-Item Env:PLATFORM_BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:PLATFORM_BOOTSTRAP_EMAIL -ErrorAction SilentlyContinue
}
