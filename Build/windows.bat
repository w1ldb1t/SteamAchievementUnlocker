@echo off
setlocal

set OUTPUT=%~dp0..\Release

:: Download steam_api64.dll from NuGet if not present
if not exist "%OUTPUT%\steam_api64.dll" (
    echo Downloading steam_api64.dll ...
    if not exist "%OUTPUT%" mkdir "%OUTPUT%"
    powershell -NoProfile -Command ^
        "$tmp = Join-Path $env:TEMP 'facepunch.nupkg';" ^
        "Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Facepunch.Steamworks.Dll/1.53.0' -OutFile $tmp;" ^
        "Add-Type -AssemblyName System.IO.Compression.FileSystem;" ^
        "$zip = [IO.Compression.ZipFile]::OpenRead($tmp);" ^
        "$entry = $zip.GetEntry('runtimes/win-x64/native/steam_api64.dll');" ^
        "$s = $entry.Open();" ^
        "$f = [IO.File]::Create('%OUTPUT%\steam_api64.dll');" ^
        "$s.CopyTo($f); $f.Close(); $s.Close(); $zip.Dispose();" ^
        "Remove-Item $tmp"
)

:: Clean only the executables
del /q "%OUTPUT%\SteamAchievementUnlocker.exe" 2>nul
del /q "%OUTPUT%\Client.exe" 2>nul

dotnet publish "%~dp0..\Server\Server.csproj" -c Release -r win-x64 -o "%OUTPUT%" --nologo -v quiet || exit /b 1
dotnet publish "%~dp0..\Client\Client.csproj" -c Release -r win-x64 -o "%OUTPUT%" --nologo -v quiet || exit /b 1

del /q "%OUTPUT%\*.pdb" 2>nul

echo Build located at: %OUTPUT%
