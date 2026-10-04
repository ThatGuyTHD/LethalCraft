param(
    [string]$GameDirectory='C:\Program Files (x86)\Steam\steamapps\common\Lethal Company',
    [string]$PrismRoot=(Join-Path $env:APPDATA 'PrismLauncher'),
    [string]$JavaPath=(Join-Path $env:APPDATA 'PrismLauncher\java\java-runtime-epsilon\bin\javaw.exe')
)
$ErrorActionPreference='Stop'
if(Get-Process -Name 'Lethal Company','prismlauncher','javaw' -ErrorAction SilentlyContinue){throw 'Close Minecraft, Lethal Company and Prism Launcher before installing so their files can be updated.'}
if(!(Test-Path -LiteralPath (Join-Path $GameDirectory 'Lethal Company.exe'))){throw 'Lethal Company.exe was not found. Supply -GameDirectory.'}
if(!(Test-Path -LiteralPath $PrismRoot)){throw 'Prism Launcher data was not found. Supply -PrismRoot.'}
if(!(Test-Path -LiteralPath $JavaPath)){throw 'Java 25 was not found. Supply -JavaPath pointing to Java 25 javaw.exe.'}
$profileDirectory=Join-Path $PrismRoot 'instances\LethalCraft'
if((Test-Path -LiteralPath $profileDirectory) -and !(Test-Path -LiteralPath (Join-Path $profileDirectory 'lethalcraft-managed.json'))){throw 'A LethalCraft instance already exists and is not marked as managed by this package; it was left unchanged.'}
$bep=Join-Path $GameDirectory 'BepInEx'
if(!(Test-Path -LiteralPath (Join-Path $bep 'core\BepInEx.dll'))){
    if(Test-Path -LiteralPath (Join-Path $GameDirectory 'winhttp.dll')){throw 'A different winhttp.dll loader is present. Inspect it before installing BepInEx.'}
    $temporary=Join-Path $env:TEMP ('LethalCraft-setup-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary | Out-Null
    $archive=Join-Path $temporary 'BepInEx.zip'
    Invoke-WebRequest -Uri 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' -OutFile $archive
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'){throw 'BepInEx download checksum did not match.'}
    $expanded=Join-Path $temporary 'expanded'
    Expand-Archive -LiteralPath $archive -DestinationPath $expanded
    Get-ChildItem -LiteralPath $expanded -Force | Copy-Item -Destination $GameDirectory -Recurse
}
$configuration=Join-Path $bep 'config\BepInEx.cfg'
New-Item -ItemType Directory -Path (Split-Path -Parent $configuration) -Force | Out-Null
if(Test-Path -LiteralPath $configuration){
    if(!(Test-Path -LiteralPath ($configuration+'.pre-lethalcraft.bak'))){Copy-Item -LiteralPath $configuration -Destination ($configuration+'.pre-lethalcraft.bak')}
    $text=Get-Content -LiteralPath $configuration -Raw
    if($text -match '(?m)^HideManagerGameObject\s*='){$text=$text -replace '(?m)^HideManagerGameObject\s*=.*$','HideManagerGameObject = true'}
    elseif($text -match '(?m)^\[Chainloader\]'){$text=$text -replace '(?m)^\[Chainloader\]',('[Chainloader]'+"`r`nHideManagerGameObject = true")}
    else{$text+="`r`n[Chainloader]`r`nHideManagerGameObject = true`r`n"}
}else{$text="[Chainloader]`r`nHideManagerGameObject = true`r`n"}
Set-Content -LiteralPath $configuration -Value $text -Encoding UTF8
$pluginDirectory=Join-Path $bep 'plugins\LethalCraft'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
$installedDll=Join-Path $pluginDirectory 'LethalCraft.dll'
if(Test-Path -LiteralPath $installedDll){
    $backupDirectory=Join-Path $bep ('LethalCraftBackups\'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    Copy-Item -LiteralPath $installedDll -Destination $backupDirectory
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mods\LethalCraft.dll') -Destination $pluginDirectory -Force
New-Item -ItemType Directory -Path (Join-Path $profileDirectory '.minecraft\mods') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'prism\mmc-pack.json') -Destination $profileDirectory -Force
# Keep one bridge jar loaded. Archive older managed versions outside the mods directory.
$oldJars=Get-ChildItem -LiteralPath (Join-Path $profileDirectory '.minecraft\mods') -Filter 'skycraft-*-lethalcraft.jar' |
    Where-Object Name -ne 'skycraft-0.2.3-lethalcraft.jar'
if($oldJars){
    $jarBackup=Join-Path $profileDirectory ('lethalcraft-backups\'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
    New-Item -ItemType Directory -Path $jarBackup -Force | Out-Null
    foreach($oldJar in $oldJars){Move-Item -LiteralPath $oldJar.FullName -Destination $jarBackup}
}
foreach($jar in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'mods') -Filter '*.jar'){
    Copy-Item -LiteralPath $jar.FullName -Destination (Join-Path $profileDirectory '.minecraft\mods') -Force
}
$cfg=Join-Path $profileDirectory 'instance.cfg'
if(!(Test-Path -LiteralPath $cfg)){
    $instanceText=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'prism\instance.cfg') -Raw
    $instanceText=$instanceText.Replace('JAVA_PATH_HERE',$JavaPath.Replace('\','/'))
    Set-Content -LiteralPath $cfg -Value $instanceText -Encoding UTF8
}
'{"package":"LethalCraft","version":"0.2.3"}' | Set-Content -LiteralPath (Join-Path $profileDirectory 'lethalcraft-managed.json') -Encoding UTF8
Write-Output 'Installed. Run Launch LethalCraft.cmd. Sign into your Minecraft account in Prism if requested.'
