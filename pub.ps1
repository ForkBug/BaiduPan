dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\win-x64        -r win-x64 
dotnet publish                                      -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\win-x86        -r win-x86
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\win-arm        -r win-arm 
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\win-arm64      -r win-arm64 
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\osx-x64        -r osx-x64   
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\osx-arm64      -r osx-arm64   
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\linux-arm      -r linux-arm 
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\linux-arm64    -r linux-arm64 
dotnet publish  --sc true /p:PublishSingleFile=true -c Release .\BaiduPanConsole\BaiduPanConsole.csproj -o .\pubs\linux-x64      -r linux-x64 

Get-ChildItem .\pubs\*.pdb -R | ForEach-Object {
    Remove-Item $_.FullName
}

Remove-Item .\pubs\*.zip
Compress-Archive -Path  .\pubs\win-x64\*      -DestinationPath  .\pubs\win-x64.zip
Compress-Archive -Path  .\pubs\win-x86\*      -DestinationPath  .\pubs\win-x86.zip
Compress-Archive -Path  .\pubs\win-arm\*      -DestinationPath  .\pubs\win-arm.zip
Compress-Archive -Path  .\pubs\osx-x64\*      -DestinationPath  .\pubs\osx-x64.zip
Compress-Archive -Path  .\pubs\win-arm64\*    -DestinationPath  .\pubs\win-arm64.zip
Compress-Archive -Path  .\pubs\osx-arm64\*    -DestinationPath  .\pubs\osx-arm64.zip
Compress-Archive -Path  .\pubs\linux-arm\*    -DestinationPath  .\pubs\linux-arm.zip
Compress-Archive -Path  .\pubs\linux-x64\*    -DestinationPath  .\pubs\linux-x64.zip
Compress-Archive -Path  .\pubs\linux-arm64\*  -DestinationPath  .\pubs\linux-arm64.zip


