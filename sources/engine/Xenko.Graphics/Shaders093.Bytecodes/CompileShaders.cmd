@echo off
setlocal
set XenkoSdkDir=%~dp0..\..\..\..\
set XenkoAssetCompiler=%XenkoSdkDir%sources\assets\Xenko.Core.Assets.CompilerApp\bin\Debug\net5.0-windows7.0\Xenko.Core.Assets.CompilerApp.exe
%XenkoAssetCompiler% --platform=Windows --property:RuntimeIdentifier=win --output-path=%~dp0obj\app_data --build-path=%~dp0obj\build_app_data --package-file=Graphics.xkpkg
%XenkoAssetCompiler% --platform=Windows --property:RuntimeIdentifier=win-opengl --output-path=%~dp0obj\app_data --build-path=%~dp0obj\build_app_data --package-file=Graphics.xkpkg
%XenkoAssetCompiler% --platform=Windows --property:RuntimeIdentifier=win-opengles --output-path=%~dp0obj\app_data --build-path=%~dp0obj\build_app_data --package-file=Graphics.xkpkg
%XenkoAssetCompiler% --platform=Windows --property:RuntimeIdentifier=win-vulkan --output-path=%~dp0obj\app_data --build-path=%~dp0obj\build_app_data --package-file=Graphics.xkpkg
