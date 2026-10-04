param(
    [string]$GameDirectory='C:\Program Files (x86)\Steam\steamapps\common\Lethal Company',
    [string]$JavaDirectory=$env:JAVA_HOME
)
$ErrorActionPreference='Stop'
if(!$JavaDirectory -or !(Test-Path -LiteralPath (Join-Path $JavaDirectory 'bin/javac.exe'))){throw 'Supply -JavaDirectory for a Java 25 JDK.'}
$output=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $output,(Join-Path $output 'mods') | Out-Null
foreach($folder in @('prism','licenses')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $output -Recurse -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $output 'licenses') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $output 'licenses/LethalCraft-LICENSE.txt') -Force
$oldJava=$env:JAVA_HOME;$oldPath=$env:PATH;$oldOptions=$env:JAVA_TOOL_OPTIONS
try {
    $env:JAVA_HOME=$JavaDirectory;$env:PATH="$JavaDirectory\bin;$env:PATH"
    $env:JAVA_TOOL_OPTIONS='-Djavax.net.ssl.trustStoreType=Windows-ROOT -Djavax.net.ssl.trustStore=NONE'
    dotnet build (Join-Path $PSScriptRoot 'lethal/LethalCraft.csproj') -c Release "-p:GameDir=$GameDirectory" --nologo
    if($LASTEXITCODE -ne 0){throw 'LC adapter build failed.'}
    dotnet run --project (Join-Path $PSScriptRoot 'tests/BridgeChecks.csproj') -c Release
    if($LASTEXITCODE -ne 0){throw 'Bridge/relay checks failed.'}
    Push-Location (Join-Path $PSScriptRoot 'fabric')
    try{& ./gradlew.bat build --no-daemon --console=plain;if($LASTEXITCODE -ne 0){throw 'Minecraft build failed.'}}finally{Pop-Location}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'lethal/bin/Release/netstandard2.1/LethalCraft.dll') -Destination (Join-Path $output 'mods') -Force
    Get-ChildItem -LiteralPath (Join-Path $output 'mods') -Filter 'skycraft-*-lethalcraft.jar' -File |
        Where-Object Name -ne 'skycraft-0.2.4-lethalcraft.jar' | Remove-Item
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fabric/build/libs/skycraft-0.2.4-lethalcraft.jar') -Destination (Join-Path $output 'mods') -Force
    $api=Join-Path $output 'mods/fabric-api-0.161.0+26.3.jar'
    $expected='86f16178a3cecc887a85a4cfe9a79d92fa7341d8f39b5951a4d6ad800ab657a6'
    if(!(Test-Path -LiteralPath $api) -or (Get-FileHash -LiteralPath $api -Algorithm SHA256).Hash -ne $expected){
        Invoke-WebRequest -Uri 'https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/0.161.0+26.3/fabric-api-0.161.0+26.3.jar' -OutFile ($api+'.partial')
        if((Get-FileHash -LiteralPath ($api+'.partial') -Algorithm SHA256).Hash -ne $expected){throw 'Fabric API checksum mismatch.'}
        Move-Item -LiteralPath ($api+'.partial') -Destination $api -Force
    }
    & (Join-Path $PSScriptRoot 'launcher/BuildInstaller.ps1') -PackageDirectory $output
    if($LASTEXITCODE -ne 0){throw 'Installer build failed.'}
    Write-Output "Built $output\LethalCraft.exe. Close both games before installing."
} finally {$env:JAVA_HOME=$oldJava;$env:PATH=$oldPath;$env:JAVA_TOOL_OPTIONS=$oldOptions}
