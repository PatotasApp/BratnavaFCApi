param(
    [string]$GroupId = '3f401edf-d309-4bae-97d8-28eae0da7a8a',
    [string]$SourceSecretKey = 'ConnectionStrings:DefaultConnection',
    [string]$TargetSecretKey = 'DatabaseCopy:DevelopmentConnection',
    [string]$UserSecretsId = '64fa5fc7-1145-4ee8-8674-e978908e11b0',
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin',
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

function ConvertFrom-ConnectionString([string]$connectionString) {
    $result = @{}
    foreach ($piece in ($connectionString -split ';')) {
        $pair = $piece -split '=', 2
        if ($pair.Count -eq 2) {
            $result[$pair[0].Trim().ToLowerInvariant()] = $pair[1].Trim()
        }
    }
    return $result
}

function New-PsqlStartInfo(
    [hashtable]$connection,
    [bool]$readOnly,
    [string[]]$arguments
) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $PostgresBin 'psql.exe'
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $info.Environment['PGPASSWORD'] = $connection['password']
    if ($readOnly) {
        $info.Environment['PGOPTIONS'] = '-c default_transaction_read_only=on'
        $info.Environment['PGSSLMODE'] = 'require'
    }
    $info.ArgumentList.Add('--host')
    $info.ArgumentList.Add($connection['host'])
    $info.ArgumentList.Add('--port')
    $info.ArgumentList.Add(($connection['port'] ?? '5432'))
    $info.ArgumentList.Add('--username')
    $info.ArgumentList.Add($connection['username'])
    $info.ArgumentList.Add('--dbname')
    $info.ArgumentList.Add($connection['database'])
    $info.ArgumentList.Add('--no-password')
    $info.ArgumentList.Add('--no-psqlrc')
    $info.ArgumentList.Add('--set')
    $info.ArgumentList.Add('ON_ERROR_STOP=1')
    foreach ($argument in $arguments) {
        $info.ArgumentList.Add($argument)
    }
    return $info
}

function Invoke-PsqlText(
    [hashtable]$connection,
    [bool]$readOnly,
    [string]$sql
) {
    $info = New-PsqlStartInfo $connection $readOnly @(
        '--tuples-only', '--no-align', '--command', $sql
    )
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        throw "psql falhou: $stderr"
    }
    return $stdout.Trim()
}

function Get-TableColumns(
    [hashtable]$connection,
    [bool]$readOnly,
    [string]$table
) {
    $escaped = $table.Replace("'", "''")
    $sql = "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='$escaped' ORDER BY ordinal_position;"
    $text = Invoke-PsqlText $connection $readOnly $sql
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }
    return @($text -split "`r?`n" | Where-Object { $_ })
}

function Copy-QueryToTable(
    [hashtable]$source,
    [hashtable]$target,
    [string]$table,
    [string]$where
) {
    $sourceColumns = Get-TableColumns $source $true $table
    if ($sourceColumns.Count -eq 0) {
        return [pscustomobject]@{ Table = $table; Source = 0; Target = $null; Status = 'ausente na origem' }
    }
    $targetColumns = Get-TableColumns $target $false $table
    $columns = @($sourceColumns | Where-Object { $targetColumns -contains $_ })
    if ($columns.Count -eq 0) {
        throw "Tabela $table não possui colunas compatíveis."
    }

    $quotedColumns = @($columns | ForEach-Object { '"' + $_.Replace('"', '""') + '"' })
    $columnSql = $quotedColumns -join ', '
    $sourceQuery = "SELECT $columnSql FROM `"$table`" WHERE $where"
    $sourceCount = [int](Invoke-PsqlText $source $true "SELECT COUNT(*) FROM ($sourceQuery) AS selected_rows;")
    if ($sourceCount -eq 0) {
        return [pscustomobject]@{ Table = $table; Source = 0; Target = $null; Status = 'sem linhas' }
    }

    $sourceCopy = "\copy ($sourceQuery) TO STDOUT WITH (FORMAT csv, NULL '\N')"
    $targetCopy = "\copy `"$table`" ($columnSql) FROM STDIN WITH (FORMAT csv, NULL '\N')"

    $sourceInfo = New-PsqlStartInfo $source $true @('--quiet', '--command', $sourceCopy)
    $targetInfo = New-PsqlStartInfo $target $false @(
        '--quiet',
        '--command', 'SET session_replication_role = replica;',
        '--command', $targetCopy
    )
    $targetInfo.RedirectStandardInput = $true
    $sourceProcess = [System.Diagnostics.Process]::new()
    $targetProcess = [System.Diagnostics.Process]::new()
    $sourceProcess.StartInfo = $sourceInfo
    $targetProcess.StartInfo = $targetInfo

    [void]$targetProcess.Start()
    $targetOutTask = $targetProcess.StandardOutput.ReadToEndAsync()
    $targetErrTask = $targetProcess.StandardError.ReadToEndAsync()
    [void]$sourceProcess.Start()
    $sourceErrTask = $sourceProcess.StandardError.ReadToEndAsync()

    $sourceProcess.StandardOutput.BaseStream.CopyTo($targetProcess.StandardInput.BaseStream)
    $targetProcess.StandardInput.Close()
    $sourceProcess.WaitForExit()
    $targetProcess.WaitForExit()

    $sourceError = $sourceErrTask.GetAwaiter().GetResult()
    $targetError = $targetErrTask.GetAwaiter().GetResult()
    [void]$targetOutTask.GetAwaiter().GetResult()
    if ($sourceProcess.ExitCode -ne 0) { throw "Leitura de $table falhou: $sourceError" }
    if ($targetProcess.ExitCode -ne 0) { throw "Escrita de $table falhou: $targetError" }

    return [pscustomobject]@{
        Table = $table
        Source = $sourceCount
        Target = $null
        Status = 'copiada'
    }
}

$parsedGroupId = [Guid]::Empty
if (-not [Guid]::TryParse($GroupId, [ref]$parsedGroupId)) {
    throw 'GroupId inválido.'
}

$secretPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
$secrets = Get-Content -Raw -LiteralPath $secretPath | ConvertFrom-Json
$sourceConnection = [string]$secrets.$SourceSecretKey
$targetConnection = [string]$secrets.$TargetSecretKey
if ([string]::IsNullOrWhiteSpace($sourceConnection) -or [string]::IsNullOrWhiteSpace($targetConnection)) {
    throw 'Uma das conexões não foi encontrada nos user-secrets.'
}
$source = ConvertFrom-ConnectionString $sourceConnection
$target = ConvertFrom-ConnectionString $targetConnection

if ($target['host'] -notin @('localhost', '127.0.0.1') -or $target['database'] -notlike '*dev*') {
    throw 'Proteção acionada: o destino precisa ser localhost e ter "dev" no nome do banco.'
}
if ((Invoke-PsqlText $source $true 'SHOW transaction_read_only;') -ne 'on') {
    throw 'Proteção acionada: a origem não está em modo read-only.'
}

$groupLiteral = "'$GroupId'::uuid"
$existing = [int](Invoke-PsqlText $target $false "SELECT COUNT(*) FROM `"Groups`" WHERE `"Id`"=$groupLiteral;")
if ($existing -ne 0 -and -not $ValidateOnly) {
    throw 'A patota já existe no destino. Restaure o backup ou use um banco limpo antes de repetir.'
}

$players = "SELECT `"Id`" FROM `"Players`" WHERE `"GroupId`"=$groupLiteral"
$matches = "SELECT `"Id`" FROM `"Matches`" WHERE `"GroupId`"=$groupLiteral"
$matchPlayers = "SELECT `"Id`" FROM `"MatchPlayers`" WHERE `"GroupId`"=$groupLiteral"
$polls = "SELECT `"Id`" FROM `"Polls`" WHERE `"GroupId`"=$groupLiteral"
$pollOptions = "SELECT o.`"Id`" FROM `"PollOptions`" o WHERE o.`"PollId`" IN ($polls)"
$bets = "SELECT `"Id`" FROM `"MatchBets`" WHERE `"GroupId`"=$groupLiteral"
$clips = "SELECT `"Id`" FROM `"ReplayClips`" WHERE `"GroupId`"=$groupLiteral"
$users = @"
SELECT "CreatedByUserId" FROM "Groups" WHERE "Id"=$groupLiteral
UNION SELECT "UserId" FROM "Players" WHERE "GroupId"=$groupLiteral
UNION SELECT "UserId" FROM "GroupAdmins" WHERE "GroupId"=$groupLiteral
UNION SELECT "UserId" FROM "GroupFinanceiros" WHERE "GroupId"=$groupLiteral
UNION SELECT "TargetUserId" FROM "GroupInvites" WHERE "GroupId"=$groupLiteral
UNION SELECT "CreatedByUserId" FROM "CalendarEvents" WHERE "GroupId"=$groupLiteral
UNION SELECT "CreatedByUserId" FROM "Polls" WHERE "GroupId"=$groupLiteral
UNION SELECT "CreatedByAdminId" FROM "ExtraCharges" WHERE "GroupId"=$groupLiteral
UNION SELECT "MarkedByAdminId" FROM "MonthlyPayments" WHERE "GroupId"=$groupLiteral
UNION SELECT "MarkedByAdminId" FROM "ExtraChargePayments" WHERE "GroupId"=$groupLiteral
UNION SELECT "CreatedByUserId" FROM "GroupTransactions" WHERE "GroupId"=$groupLiteral
UNION SELECT "ResolvedByUserId" FROM "ExitDebtAlerts" WHERE "GroupId"=$groupLiteral
UNION SELECT "UserId" FROM "MatchBets" WHERE "GroupId"=$groupLiteral
UNION SELECT "UserId" FROM "UserBetBalances" WHERE "GroupId"=$groupLiteral
UNION SELECT "UserId" FROM "ReplayLikes" WHERE "ClipId" IN ($clips)
UNION SELECT "UserId" FROM "ReplayFavorites" WHERE "ClipId" IN ($clips)
"@

$definitions = @(
    @{ Table='Users'; Where="`"Id`" IN ($users)" },
    @{ Table='Groups'; Where="`"Id`"=$groupLiteral" },
    @{ Table='GroupSettings'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='TeamColors'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='Players'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='GroupAdmins'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='GroupFinanceiros'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='GroupInvites'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='UserAbsences'; Where="`"UserId`" IN ($users)" },
    @{ Table='CalendarCategories'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='CalendarEvents'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='Polls'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='PollOptions'; Where="`"PollId`" IN ($polls)" },
    @{ Table='PollOptionImages'; Where="`"OptionId`" IN ($pollOptions)" },
    @{ Table='PollVotes'; Where="`"PollId`" IN ($polls)" },
    @{ Table='PollGuests'; Where="`"PollId`" IN ($polls)" },
    @{ Table='Matches'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='MatchPlayers'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='Goals'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='Votes'; Where="`"MatchId`" IN ($matches)" },
    @{ Table='MonthlyPayments'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ExtraCharges'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ExtraChargePayments'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='GroupTransactions'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ExitDebtAlerts'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='MatchBets'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='MatchBetSelections'; Where="`"BetId`" IN ($bets)" },
    @{ Table='UserBetBalances'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='PlayerMatchStatContributions'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='PlayerStatProjections'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='SeasonTitles'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ReplayClips'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ReplayEventOutbox'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ReplayLikes'; Where="`"ClipId`" IN ($clips)" },
    @{ Table='ReplayFavorites'; Where="`"ClipId`" IN ($clips)" },
    @{ Table='Championships'; Where="`"GroupId`"=$groupLiteral" },
    @{ Table='ChampionshipTeams'; Where="`"ChampionshipId`" IN (SELECT `"Id`" FROM `"Championships`" WHERE `"GroupId`"=$groupLiteral)" },
    @{ Table='ChampionshipPlayers'; Where="`"ChampionshipId`" IN (SELECT `"Id`" FROM `"Championships`" WHERE `"GroupId`"=$groupLiteral)" },
    @{ Table='ChampionshipVotes'; Where="`"ChampionshipId`" IN (SELECT `"Id`" FROM `"Championships`" WHERE `"GroupId`"=$groupLiteral)" }
)

$copied = if (-not $ValidateOnly) {
    foreach ($definition in $definitions) {
        Copy-QueryToTable $source $target $definition.Table $definition.Where
    }
}

$results = for ($index = 0; $index -lt $definitions.Count; $index++) {
    $definition = $definitions[$index]
    if ($ValidateOnly) {
        $sourceColumns = Get-TableColumns $source $true $definition.Table
        $sourceCount = if ($sourceColumns.Count -eq 0) {
            0
        } else {
            [int](Invoke-PsqlText $source $true "SELECT COUNT(*) FROM `"$($definition.Table)`" WHERE $($definition.Where);")
        }
    } else {
        $sourceCount = $copied[$index].Source
    }
    $targetCount = [int](Invoke-PsqlText $target $false "SELECT COUNT(*) FROM `"$($definition.Table)`" WHERE $($definition.Where);")
    [pscustomobject]@{
        Table = $definition.Table
        Source = $sourceCount
        Target = $targetCount
        Status = if ($sourceCount -eq $targetCount) { 'ok' } else { 'divergente' }
    }
}

$mismatches = @($results | Where-Object { $_.Status -eq 'divergente' })
$results | Format-Table -AutoSize
if ($mismatches.Count -gt 0) {
    throw 'A cópia terminou com contagens divergentes.'
}

Write-Host 'Cópia concluída. PushTokens, UserNotifications e ScheduledNotificationJobs não foram copiados.'
