#! /usr/bin/env pwsh
remove-item -Recurse "./dist" -ErrorAction SilentlyContinue
dotnet publish -r linux-x64 --self-contained true /p:PublishReadyToRun=true /p:PublishSingleFile=true -o dist
if (-Not $?) {
    write-output "Build failed..."
}
$root = $pwd
set-location "./dist"
write-host "Copying file to /usr/local/bin/BgPictureChanger"
sudo cp BgPictureChanger /usr/local/bin
if (-Not $?) {
    write-output "The deploy failed..."
} else {
    write-host "Build and deploy successful"
}
set-location $root