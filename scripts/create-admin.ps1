param(
    [string]$ConnectionString
)

if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $env:DMS_MONGO_CONNECTION_STRING = $ConnectionString
}

if ([string]::IsNullOrWhiteSpace($env:DMS_MONGO_CONNECTION_STRING)) {
    Write-Error "Set DMS_MONGO_CONNECTION_STRING or pass -ConnectionString before running this script."
    exit 1
}

dotnet run --project ".\Tools\CreateAdmin\CreateAdmin.csproj"
exit $LASTEXITCODE
