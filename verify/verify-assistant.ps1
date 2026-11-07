# Exercise the compiled mod's real assistant serializer with isolated snapshots.
param([Parameter(Mandatory = $true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'MelonLoader/net35/MelonLoader.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin/Release/MaiPlannerMod.dll'))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
function New-ModObject($name) {
    [Activator]::CreateInstance($assembly.GetType("MaiPlanner.$name", $true), $true)
}
function Set-Field($object, $name, $value) {
    $object.GetType().GetField($name, $instance).SetValue($object, $value)
}
function Assert($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('MaiPlanner-assistant-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $sandbox 'Sinmai_Data/StreamingAssets/Test/AssetBundleImages/jacket') -Force | Out-Null
$previous = [Environment]::CurrentDirectory
try {
    [Environment]::CurrentDirectory = $sandbox
    $assembly.GetType('MaiPlanner.UserFiles').GetField('_root', $static).SetValue($null, (Join-Path $sandbox 'data'))
    $song = New-ModObject 'GameSnapshot+Song'
    Set-Field $song 'InternalId' 10146
    Set-Field $song 'DataId' 146
    Set-Field $song 'Title' 'Regression song'
    Set-Field $song 'Version' 'CiRCLE'
    $song.Enabled[2] = $true
    $song.Enabled[3] = $true
    $song.LevelValue[3] = 137
    $song.LevelText[3] = '13.7'
    $songs = $assembly.GetType('MaiPlanner.GameSnapshot').GetField('_songs', $static).GetValue($null)
    $songs.Add($song)
    $players = $assembly.GetType('MaiPlanner.PlayerStore').GetField('_players', $static).GetValue($null)
    foreach ($index in 0, 1) {
        $player = New-ModObject 'PlayerStore+Player'
        Set-Field $player 'Index' $index
        Set-Field $player 'IsEntry' $true
        $players.Add($player)
    }
    $override = $assembly.GetType('MaiPlanner.PlayerStore').GetMethods($static) | Where-Object { $_.Name -eq 'Override' -and $_.GetParameters().Count -eq 8 }
    $override.Invoke($null, @([int]0, [int]10146, [int]3, [uint32]900000, [int]5, [int]1, [int]0, [uint32]1)) | Out-Null
    $override.Invoke($null, @([int]1, [int]10146, [int]3, [uint32]1005000, [int]13, [int]3, [int]0, [uint32]2)) | Out-Null
    $parse = $assembly.GetType('MaiPlanner.JsonMini').GetMethod('ParseObj', $static)
    $state = $parse.Invoke($null, @('{"cards":[],"preview":[{"internalId":10146,"chartid":3,"pool":"unplayed"}]}'))
    $serialize = $assembly.GetType('MaiPlanner.Assistant').GetMethod('PreviewJson', $static)
    $data = $serialize.Invoke($null, @('1', $state)) | ConvertFrom-Json
    Assert ($data.preview[0].song.internalId -eq 10146) 'Preview includes the exact selected song'
    Assert ($data.preview[0].song.sheets.Count -eq 1 -and $data.preview[0].song.sheets[0].chartid -eq 3) 'Preview contains only the drawn difficulty'
    Assert ($data.preview[0].song.sheets[0].achievement -eq 1005000) '2P preview uses 2P scores instead of 1P'
    Assert ($data.preview[0].song.sheets[0].level -eq 13.7) 'Preview retains chart level'
    $state = $parse.Invoke($null, @('{"cards":[],"preview":[{"internalId":146,"chartid":3},{"internalId":10146,"chartid":4}]}'))
    $data = $serialize.Invoke($null, @('1', $state)) | ConvertFrom-Json
    Assert ($null -eq $data.preview[0].song) 'Missing STD chart does not resolve to its DX counterpart'
    Assert ($null -eq $data.preview[1].song) 'Disabled difficulty returns no chart'
} finally {
    [Environment]::CurrentDirectory = $previous
    # The sandbox is left in the OS temp directory for inspecting failures.
}
