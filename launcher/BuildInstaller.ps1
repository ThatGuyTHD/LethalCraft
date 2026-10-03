param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$package=[IO.Path]::GetFullPath($PackageDirectory)
$stage=Join-Path $PSScriptRoot ('obj/payload-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
foreach($folder in @('mods','prism','licenses')){
    $target=Join-Path $stage $folder
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $package $folder) -File | Copy-Item -Destination $target
}
$hashes=[ordered]@{}
Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    $relative=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/')
    $hashes[$relative]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$hashes|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $stage 'hashes.json') -Encoding utf8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $PSScriptRoot 'payload.zip') -Force
$publish=Join-Path $PSScriptRoot 'bin/installer'
dotnet publish (Join-Path $PSScriptRoot 'LethalCraft.Launcher.csproj') -c Release -o $publish --nologo
if($LASTEXITCODE -ne 0){throw 'Installer build failed.'}
Copy-Item -LiteralPath (Join-Path $publish 'LethalCraft.exe') -Destination (Join-Path $package 'LethalCraft.exe') -Force
Write-Output 'Built standalone LethalCraft.exe. Its mods are embedded; friends only need this one file.'
