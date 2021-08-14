setlocal
set XENKO_PATH=%~dp0..\..\..
set NUGET=%XENKO_PATH%\build\.nuget\Nuget.exe
set LAUNCHER_PATH=%~dp0bin\Debug\publish
pushd %LAUNCHER_PATH%
%NUGET% pack %~dp0Xenko.Launcher.nuspec -BasePath %LAUNCHER_PATH%
popd