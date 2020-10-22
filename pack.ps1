#! /usr/bin/env pwsh

dotnet publish -r linux-x64 --self-contained true /p:PublishReadyToRun=true /p:PublishSingleFile=true -o dist
dotnet pack
dotnet nuget push dist/BgPictureChanger.0.1.0.nupkg -k "$env:NUGET_API_KEY" --source https://api.nuget.org/v3/index.json