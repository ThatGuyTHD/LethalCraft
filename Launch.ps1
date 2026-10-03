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
    (Join-Path $profileDirectory '.minecraft\mods\skycraft-0.2.2-lethalcraft.jar'),
    (Join-Path $profileDirectory '.minecraft\mods\fabric-api-0.161.0+26.3.jar'),
    $PrismExe
)
foreach($path in $required){if(!(Test-Path -LiteralPath $path)){throw "Missing: $path. Run Install.ps1 or adjust the paths in Launch.ps1."}}
$bepConfig=Get-Content -LiteralPath (Join-Path $GameDirectory 'BepInEx\config\BepInEx.cfg') -Raw
if($bepConfig -notmatch '(?m)^HideManagerGameObject\s*=\s*true\s*$'){throw 'BepInEx.cfg must have HideManagerGameObject = true.'}
$steamRoot=Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue
if(!$steamRoot -or !(Test-Path -LiteralPath (Join-Path $steamRoot 'steam.exe'))){throw 'Install Steam and sign in to the account that owns Lethal Company.'}
$libraries=@($steamRoot)
$folders=Join-Path $steamRoot 'steamapps/libraryfolders.vdf'
if(Test-Path -LiteralPath $folders){foreach($match in [regex]::Matches([IO.File]::ReadAllText($folders),'"path"\s+"([^"]+)"')){$libraries+=$match.Groups[1].Value.Replace('\\','\')}}
$registered=''
foreach($library in ($libraries|Select-Object -Unique)){
    $manifest=Join-Path $library 'steamapps/appmanifest_1966720.acf'
    if(!(Test-Path -LiteralPath $manifest)){continue}
    $data=[IO.File]::ReadAllText($manifest)
    $folder=[regex]::Match($data,'"installdir"\s+"([^"]+)"')
    if($data -notmatch '"appid"\s+"1966720"' -or !$folder.Success -or $folder.Groups[1].Value -match '[/\\:]|^\.{1,2}$'){continue}
    $candidate=Join-Path $library ('steamapps/common/'+$folder.Groups[1].Value)
    if(Test-Path -LiteralPath (Join-Path $candidate 'Lethal Company.exe')){$registered=[IO.Path]::GetFullPath($candidate);break}
}
if(!$registered){throw 'Install Lethal Company through your Steam library first.'}
if([IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/') -ne $registered.TrimEnd('\','/')){throw "Steam launches a different folder. Install LethalCraft here: $registered"}
if($CheckOnly){Write-Output 'LethalCraft files, launcher and required loader setting are present.';exit 0}
$minecraftRunning=$false
try{$marker=[Threading.Mutex]::OpenExisting('Local\LethalCraft_v1_minecraft');$marker.Dispose();$minecraftRunning=$true}catch [Threading.WaitHandleCannotBeOpenedException]{}
if(!$minecraftRunning){
    Start-Process -FilePath $PrismExe -ArgumentList '--launch','LethalCraft' -WindowStyle Hidden
}
$gameProcess=Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue
if(!$gameProcess){
    # The game is interactive; its normal launch-options and host/join screens remain available.
    Start-Process -FilePath 'steam://run/1966720'
    $deadline=[DateTime]::UtcNow.AddMinutes(2)
    while(!(Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 500}
    if(!(Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue)){throw 'Steam has not started the game. Finish signing in or updating in Steam, then launch again.'}
}
Write-Output 'LethalCraft launched. E: interact; I: inventory; F7: helmet mask; Alt: native controls; F8: bridge off/on.'
