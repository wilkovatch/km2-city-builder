$antlrUrl = "https://www.antlr.org/download/antlr-4.11.1-complete.jar"
$antlrFile = "antlr-4.11.1-complete.jar"
$libgit2Url = "https://www.nuget.org/api/v2/package/LibGit2Sharp.NativeBinaries/2.0.321"
$libgit2Folder = "LibGit2Sharp.NativeBinaries.2.0.321"

$webclient = New-Object System.Net.WebClient
$webclient.DownloadFile($antlrUrl,"$pwd/ExpressionParser/$antlrFile")
$webclient.DownloadFile($libgit2Url,"$pwd/libgit2.zip")
Expand-Archive "$pwd/libgit2.zip" -DestinationPath "$pwd/libgit2_temp"
New-Item -ItemType Directory -Path "$pwd/Assets/Packages/$libgit2Folder/runtimes"
Copy-Item -Path "$pwd/libgit2_temp/runtimes/win-x64" -Destination "$pwd/Assets/Packages/$libgit2Folder/runtimes" -Recurse
Remove-Item -Path "$pwd/libgit2.zip"
Remove-Item -Recurse -Force "$pwd/libgit2_temp"
Set-Location -Path "$pwd/ExpressionParser"
$p = Start-Process compile.bat -Wait -Passthru
$p.WaitForExit()