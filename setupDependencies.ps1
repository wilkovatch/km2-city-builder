$pluginUrls = @(
    "https://github.com/TokisanGames/Terrain3D/releases/download/v1.0.1/Terrain3D_v1.0.1.zip"
)

$addonsDir = "addons"
$tempExtractDir = "temp_plugin_extract"

foreach ($pluginUrl in $pluginUrls) {
    $zipFile = [System.IO.Path]::GetFileName($pluginUrl)

    # Clean up old temp directory
    if (Test-Path $tempExtractDir) {
        Remove-Item -Recurse -Force $tempExtractDir
    }
    New-Item -ItemType Directory -Path $tempExtractDir | Out-Null

    Write-Host "`nDownloading $zipFile from $pluginUrl..."
    Invoke-WebRequest -Uri $pluginUrl -OutFile $zipFile

    Write-Host "Extracting $zipFile to temporary directory..."
    Expand-Archive -Path $zipFile -DestinationPath $tempExtractDir -Force

    $foundAddons = Get-ChildItem -Path $tempExtractDir -Recurse -Directory -Filter "addons" | Select-Object -First 1

    if ($null -eq $foundAddons) {
        Write-Host "No 'addons' folder found in $zipFile. Skipping."
        Remove-Item $zipFile
        Remove-Item -Recurse -Force $tempExtractDir
        continue
    }

    if (-Not (Test-Path $addonsDir)) {
        New-Item -ItemType Directory -Path $addonsDir | Out-Null
    }

    Write-Host "Copying plugin from '$($zipFile)' to '$addonsDir'..."
    Copy-Item -Path "$($foundAddons.FullName)\*" -Destination $addonsDir -Recurse -Force

    Remove-Item $zipFile
    Remove-Item -Recurse -Force $tempExtractDir

    Write-Host "$zipFile installed successfully in '$addonsDir'."
}
