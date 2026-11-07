# Read a live machine and verify the compiled DLL's profile using isolated in-memory snapshots.
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'MelonLoader/net35/MelonLoader.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin/Release/MaiPlannerMod.dll'))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
function New-ModObject($name) { [Activator]::CreateInstance($assembly.GetType("MaiPlanner.$name", $true), $true) }
function Set-Field($object, $name, $value) { $object.GetType().GetField($name, $instance).SetValue($object, $value) }
function Assert($condition, $message) { if (-not $condition) { throw $message }; Write-Output "PASS $message" }
$users = (Invoke-RestMethod "$BaseUrl/api/users" -TimeoutSec 15 -NoProxy).users | Where-Object isEntry
$user = @($users)[0]
if ($null -eq $user) { throw 'A logged-in player is required' }
$catalog = (Invoke-RestMethod "$BaseUrl/api/songs?user=$($user.id)" -TimeoutSec 30 -NoProxy).songs
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('MaiPlanner-live-profile-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox | Out-Null
$previous = [Environment]::CurrentDirectory
try {
    [Environment]::CurrentDirectory = $sandbox
    $assembly.GetType('MaiPlanner.UserFiles').GetField('_root', $static).SetValue($null, (Join-Path $sandbox 'data'))
    $player = New-ModObject 'PlayerStore+Player'
    Set-Field $player 'Index' ([int]$user.id)
    Set-Field $player 'IsEntry' $true
    Set-Field $player 'Rating' ([int]$user.rating)
    $assembly.GetType('MaiPlanner.PlayerStore').GetField('_players', $static).GetValue($null).Add($player)
    $override = $assembly.GetType('MaiPlanner.PlayerStore').GetMethods($static) | Where-Object { $_.Name -eq 'Override' -and $_.GetParameters().Count -eq 8 }
    $songs = $assembly.GetType('MaiPlanner.GameSnapshot').GetField('_songs', $static).GetValue($null)
    foreach ($source in $catalog) {
        $song = New-ModObject 'GameSnapshot+Song'
        foreach ($key in 'InternalId','DataId') { Set-Field $song $key ([int]$source.$key) }
        foreach ($key in 'Title','Version') { Set-Field $song $key ([string]$source.$key) }
        foreach ($sheet in $source.sheets) {
            $chart = [int]$sheet.chartid
            if ($chart -lt 0 -or $chart -gt 4) { continue }
            $song.Enabled[$chart] = $true
            $song.LevelValue[$chart] = [int][Math]::Round($sheet.level * 10)
            $song.LevelText[$chart] = [string]$sheet.levelText
            if ($sheet.achievement -gt 0) {
                $record = $override.Invoke($null, @([int]$user.id, [int]$source.internalId, $chart, [uint32]$sheet.achievement,
                    [int]$sheet.rank, [int]$sheet.combo, [int]$sheet.sync, [uint32]$sheet.playCount))
                $id = [int]$source.internalId
                if (-not $player.Scores.ContainsKey($id)) {
                    $player.Scores.Add($id, [Activator]::CreateInstance($player.Scores.GetType().GenericTypeArguments[1]))
                }
                $player.Scores[$id].Add($chart, $record)
            }
        }
        $songs.Add($song)
    }
    $analysisType = $assembly.GetType('MaiPlanner.Analysis')
    $tagField = $analysisType.GetField('TagProvider', $static)
    $tagMethod = $assembly.GetType('MaiPlanner.CommunityTags').GetMethod('ForChart', $static)
    $tagField.SetValue($null, [Delegate]::CreateDelegate($tagField.FieldType, $tagMethod))
    $analysis = $analysisType.GetMethod('Build', $static).Invoke($null, @([string]$user.id, $true))
    Assert ($null -ne $analysis) 'Real game snapshot builds an analysis'
    $state = $assembly.GetType('MaiPlanner.JsonMini').GetMethod('ParseObj', $static).Invoke($null, @('{"cards":[],"preview":[]}'))
    $response = $assembly.GetType('MaiPlanner.Assistant').GetMethod('PreviewJson', $static).Invoke($null, @([string]$user.id, $state)) | ConvertFrom-Json
    Assert ($response.profile.rating -eq $user.rating) 'Profile Rating matches the logged-in machine player'
    Assert ($response.profile.config.Count -ge 3) 'Real scores generate configuration radar axes'
    Assert ($response.profile.eval.Count -ge 3) 'Real scores generate evaluation radar axes'
    Assert (@($response.profile.config | Where-Object { [Math]::Abs($_.value) -gt 0.0001 }).Count -gt 0) 'Ability graph is calculated from real scores, not placeholder zeros'
    $response.profile | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
    Write-Output "Profile: rating=$($response.profile.rating), config=$($response.profile.config.Count), eval=$($response.profile.eval.Count)"
} finally { [Environment]::CurrentDirectory = $previous }
