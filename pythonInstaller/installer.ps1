$pythonUrl = "https://www.python.org/ftp/python/3.10.11/python-3.10.11-embed-amd64.zip"
$pipUrl = "https://bootstrap.pypa.io/get-pip.py"

$webclient = New-Object System.Net.WebClient
$webclient.DownloadFile($pythonUrl,"$pwd/pythonTemp.zip")
$pythonPath = "$pwd/../python"
if (Test-Path $pythonPath) {Remove-Item -Recurse -Force $pythonPath}
Expand-Archive "$pwd/pythonTemp.zip" -DestinationPath $pythonPath
Remove-Item -Path "$pwd/pythonTemp.zip"
Copy-Item "$pwd/requirements.txt" -Destination "$pythonPath/requirements.txt"
Set-Location -Path $pythonPath
$webclient.DownloadFile($pipUrl,"$pwd/get-pip.py")
& "$pwd/python.exe" @('get-pip.py')
Remove-Item -Path "$pwd/get-pip.py"
Get-ChildItem "$pwd/python*._pth" | Rename-Item -NewName {$_.name -replace '._pth','._pth.bak' }
& "$pwd/python.exe" @('-m', 'pip', 'install', '-r', 'requirements.txt')
