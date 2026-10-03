param(
    [string]$GameDirectory='C:\Program Files (x86)\Steam\steamapps\common\Lethal Company',
    [string]$PrismExe=(Join-Path $env:LOCALAPPDATA 'Programs\PrismLauncher\prismlauncher.exe'),
    [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
$profileDirectory=Join-Path $env:APPDATA 'PrismLauncher\instances\LethalCraft'
$required=@(
    (Join-Path $GameDirectory 'Lethal Company.exe'),
    (Join-Path $GameDirectory 'BepInEx\plugins\LethalCraft\LethalCraft.dll'),
    (Join-Path $GameDirectory 'winhttp.dll'),
    (Join-Path $profileDirectory '.minecraft\mods\skycraft-0.2.1-lethalcraft.jar'),
    (Join-Path $profileDirectory '.minecraft\mods\fabric-api-0.161.0+26.3.jar'),
    $PrismExe
)
foreach($path in $required){if(!(Test-Path -LiteralPath $path)){throw "Missing: $path. Run Install.ps1 or adjust the paths in Launch.ps1."}}
$bepConfig=Get-Content -LiteralPath (Join-Path $GameDirectory 'BepInEx\config\BepInEx.cfg') -Raw
if($bepConfig -notmatch '(?m)^HideManagerGameObject\s*=\s*true\s*$'){throw 'BepInEx.cfg must have HideManagerGameObject = true.'}
if($CheckOnly){Write-Output 'LethalCraft files, launcher and required loader setting are present.';exit 0}
$minecraftRunning=$false
try{$marker=[Threading.Mutex]::OpenExisting('Local\LethalCraft_v1_minecraft');$marker.Dispose();$minecraftRunning=$true}catch [Threading.WaitHandleCannotBeOpenedException]{}
if(!$minecraftRunning){
    Start-Process -FilePath $PrismExe -ArgumentList '--launch','LethalCraft' -WindowStyle Hidden
}
$gameProcess=Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue
if(!$gameProcess){
    # The game is interactive; its normal launch-options and host/join screens remain available.
    Start-Process -FilePath (Join-Path $GameDirectory 'Lethal Company.exe') -WorkingDirectory $GameDirectory
}
Write-Output 'LethalCraft launched. E: interact; I: inventory; F7: helmet mask; Alt: native controls; F8: bridge off/on.'
