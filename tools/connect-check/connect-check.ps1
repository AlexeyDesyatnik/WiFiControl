<#
.SYNOPSIS
    Ручная проверка подключения к Wi-Fi-сети на Windows 11 (#30, до кода отката).

.DESCRIPTION
    Подключается к сети через netsh wlan так, как это будет делать переключение
    (ADR 0004): отключиться, при необходимости создать профиль сети Windows (add profile
    с XML), подключиться и ждать успеха или неудачи. Пишет со временем состояние адаптера
    и события журнала WLAN-AutoConfig (8001 — подключено, 8002 — не удалось, с кодом
    причины). Уведомления WLAN рядом пишет разведочная сборка: время в обоих отчётах
    местное, их можно сопоставить. Инструкция — docs/connect-check.md.

    Настройки IPv4 и прокси сценарий не трогает. Меняется только подключение
    и профиль сети Windows, который сценарий сам создал: в конце он удаляется.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    # Что проверять:
    #   Known         — знакомая сеть: профиль сети Windows уже есть;
    #   New           — новая WPA2-сеть: профиль сети Windows создаётся с паролем -Password;
    #   WrongPassword — то же с неверным паролем;
    #   Hidden        — скрытая сеть: профиль сети Windows создаётся с nonBroadcast;
    #   OutOfRange    — сети нет в зоне: берётся её профиль сети Windows, а если его нет — создаётся.
    [Parameter(Mandatory = $true)]
    [ValidateSet('Known', 'New', 'WrongPassword', 'Hidden', 'OutOfRange')]
    [string]$Case,
    # Сеть, к которой подключаемся. Создаваемый профиль сети Windows называется так же.
    [string]$Network = 'HONOR 400',
    # Пароль для New, Hidden и OutOfRange; для WrongPassword — заведомо неверный.
    [string]$Password = '',
    # Прежняя сеть: к ней адаптер подключён в начале, к ней же возвращаемся, как при откате.
    [string]$Previous = 'MichLycee',
    # Имя Wi-Fi-адаптера в «Сетевых подключениях».
    [string]$Adapter = 'Wi-Fi',
    # Для OutOfRange: создавать профиль сети Windows с nonBroadcast (Windows ищет её активно).
    [switch]$NonBroadcast,
    # Прокси для проверки порта после подключения, вида host:port; пусто — не проверять.
    [string]$Proxy = '',
    # Сколько ждать успеха или неудачи подключения, секунд. Больше 30 с из плана — чтобы увидеть, чем кончится.
    [int]$ConnectTimeout = 60,
    # Сколько следить после отключения, не подключится ли Windows сама, секунд.
    [int]$WatchAfterDisconnect = 15,
    # Сколько следить после неудачи, до возврата к прежней сети, секунд.
    [int]$WatchAfterFail = 30,
    # Сколько следить за соединением после удачного подключения, секунд.
    [int]$WatchSeconds = 45
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$reportDir = Join-Path $PSScriptRoot 'Отчёты'
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$report = Join-Path $reportDir ('connect-check_{0}_{1}_{2}.txt' -f $Case, $env:COMPUTERNAME, (Get-Date -Format 'yyyy-MM-dd_HH-mm'))
$results = New-Object System.Collections.Generic.List[string]
$wlanLog = 'Microsoft-Windows-WLAN-AutoConfig/Operational'
# Профиль сети Windows, созданный этим запуском; удаляется в конце и при прерывании.
$script:createdProfile = $null
$script:lastRecordId = 0
# Последнее записанное состояние адаптера и сеть; сеть перечитывается, пока адаптер Up.
$script:lastStatus = ''
$script:lastSsid = ''
$script:ssidReadAt = [datetime]::MinValue

# --- Отчёт -----------------------------------------------------------------

function Write-Log([string]$Text = '', [string]$Color = '') {
    $line = '{0}  {1}' -f (Get-Date -Format 'HH:mm:ss.fff'), $Text
    if ($Color) { Write-Host $line -ForegroundColor $Color } else { Write-Host $line }
    Add-Content -LiteralPath $report -Value $line -Encoding UTF8
}

function Write-Block([string]$Text) {
    foreach ($l in ($Text -split "`r?`n")) {
        if ($l.Trim()) { Write-Host "    $l"; Add-Content -LiteralPath $report -Value "    $l" -Encoding UTF8 }
    }
}

# Строка итога: попадает и в ленту, и в сводку в конце.
function Write-Result([string]$Text, [string]$Color = 'Green') {
    Write-Log $Text $Color
    $results.Add($Text)
}

function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    Write-Log ("> {0} {1}" -f $Exe, ($Arguments -join ' ')) 'Cyan'
    # В Windows PowerShell 5.1 stderr внешней команды при Stop стал бы исключением.
    $ErrorActionPreference = 'Continue'
    $out = & $Exe @Arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Block $out
    Write-Log ("  код возврата: {0}" -f $code)
    return [pscustomobject]@{ Code = $code; Output = $out }
}

function Format-Seconds([datetime]$From, [datetime]$To) { '{0:N1} с' -f ($To - $From).TotalSeconds }

# --- Адаптер и профили сетей Windows --------------------------------------

function Get-AdapterStatus { [string](Get-NetAdapter -Name $Adapter).Status }

function Get-Ssid {
    $ErrorActionPreference = 'Continue'
    $text = & netsh.exe wlan show interfaces 2>&1 | Out-String
    # Адаптеры в выводе разделены пустыми строками; берём блок, где одно из значений — имя адаптера.
    foreach ($block in ($text -split '\r?\n[ \t]*\r?\n')) {
        $lines = $block -split "`r?`n"
        if (-not ($lines | Where-Object { $_ -match '^\s*\S[^:]*:\s*(.+)$' -and $Matches[1].Trim() -eq $Adapter })) { continue }
        foreach ($l in $lines) {
            if ($l -match '^\s*SSID\s*:\s*(.+)$') { return $Matches[1].Trim() }
        }
        return $null
    }
    return $null
}

function Test-ConnectedTo([string]$Ssid) {
    (Get-AdapterStatus) -eq 'Up' -and (Get-Ssid) -eq $Ssid
}

function Get-XmlText([xml]$Doc, [string]$Path) {
    $xpath = '/' + (($Path -split '/' | ForEach-Object { "*[local-name()='$_']" }) -join '/')
    $node = $Doc.SelectSingleNode($xpath)
    if ($node) { return $node.InnerText }
    return $null
}

# Профили сетей Windows этого адаптера из XML (netsh export без пароля):
# XML не переводится, в отличие от вывода netsh wlan show profiles.
function Get-WifiProfiles {
    $dir = Join-Path $env:TEMP ('connect-check_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $ErrorActionPreference = 'Continue'
        & netsh.exe wlan export profile "folder=$dir" "interface=$Adapter" 2>&1 | Out-Null
        foreach ($f in Get-ChildItem -LiteralPath $dir -Filter '*.xml') {
            $doc = New-Object xml
            $doc.Load($f.FullName)
            [pscustomobject]@{
                Name   = Get-XmlText $doc 'WLANProfile/name'
                Ssid   = Get-XmlText $doc 'WLANProfile/SSIDConfig/SSID/name'
                Auto   = (Get-XmlText $doc 'WLANProfile/connectionMode') -eq 'auto'
                Hidden = (Get-XmlText $doc 'WLANProfile/SSIDConfig/nonBroadcast') -eq 'true'
                Auth   = Get-XmlText $doc 'WLANProfile/MSM/security/authEncryption/authentication'
            }
        }
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Find-WifiProfile([string]$Name) {
    Get-WifiProfiles | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
}

function Write-Profiles {
    Write-Log 'Профили сетей Windows (имя — SSID, автоподключение, скрытая, защита):'
    $lines = @(Get-WifiProfiles | ForEach-Object {
        '{0} — {1}, {2}{3}, {4}' -f $_.Name, $_.Ssid, $(if ($_.Auto) { 'автоматически' } else { 'вручную' }), $(if ($_.Hidden) { ', скрытая' } else { '' }), $_.Auth
    })
    if ($lines.Count -eq 0) { $lines = @('(нет)') }
    Write-Block ($lines -join "`n")
}

# Профиль сети Windows WPA2-PSK для новой сети. SSID — и в hex (UTF-8, ADR 0007), и именем.
# Автоподключение выключено на время проверки, чтобы Windows не уходила в эту сеть сама.
function New-ProfileXml([string]$Ssid, [string]$Key, [bool]$Hidden) {
    $hex = -join ([Text.Encoding]::UTF8.GetBytes($Ssid) | ForEach-Object { $_.ToString('X2') })
    $name = [Security.SecurityElement]::Escape($Ssid)
    $keyXml = [Security.SecurityElement]::Escape($Key)
    $nonBroadcast = if ($Hidden) { 'true' } else { 'false' }
    @"
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>$name</name>
  <SSIDConfig>
    <SSID>
      <hex>$hex</hex>
      <name>$name</name>
    </SSID>
    <nonBroadcast>$nonBroadcast</nonBroadcast>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>WPA2PSK</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
      <sharedKey>
        <keyType>passPhrase</keyType>
        <protected>false</protected>
        <keyMaterial>$keyXml</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
</WLANProfile>
"@
}

# Пароль лежит в файле только на время команды netsh add profile; в отчёт не попадает.
function Add-WifiProfile([bool]$Hidden) {
    $file = Join-Path $env:TEMP ('connect-check_' + [guid]::NewGuid().ToString('N') + '.xml')
    try {
        [IO.File]::WriteAllText($file, (New-ProfileXml $Network $Password $Hidden), (New-Object Text.UTF8Encoding $false))
        $cmd = Invoke-Native 'netsh.exe' @('wlan', 'add', 'profile', "filename=$file", "interface=$Adapter", 'user=all')
    } finally {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
    if (Find-WifiProfile $Network) {
        $script:createdProfile = $Network
        Write-Log ("Профиль сети Windows «{0}» создан{1}." -f $Network, $(if ($Hidden) { ', скрытая сеть' } else { '' }))
        return $true
    }
    Write-Log ("Профиль сети Windows «{0}» не создан (код {1})." -f $Network, $cmd.Code) 'Red'
    return $false
}

function Remove-CreatedProfile {
    if (-not $script:createdProfile) { return }
    Invoke-Native 'netsh.exe' @('wlan', 'delete', 'profile', "name=$($script:createdProfile)", "interface=$Adapter") | Out-Null
    $script:createdProfile = $null
}

# --- Наблюдение ------------------------------------------------------------

# Новые события журнала WLAN-AutoConfig с прошлого вызова: пишет их в ленту и возвращает.
# 8000 — начало подключения, 8001 — подключено, 8002 — не удалось, 8003 — отключено.
function Read-WlanEvents {
    $filter = "*[System[EventRecordID > $($script:lastRecordId)]]"
    $events = @(Get-WinEvent -LogName $wlanLog -FilterXPath $filter -ErrorAction SilentlyContinue | Sort-Object RecordId)
    foreach ($e in $events) {
        $script:lastRecordId = $e.RecordId
        $doc = [xml]$e.ToXml()
        $fields = @{}
        $pairs = @()
        foreach ($d in $doc.GetElementsByTagName('Data')) {
            $n = $d.GetAttribute('Name'); $v = $d.InnerText.Trim()
            if (-not $n -or -not $v) { continue }
            $fields[$n] = $v
            if ($n -notin 'InterfaceGuid', 'InterfaceDescription', 'SSIDLength') { $pairs += "$n=$v" }
        }
        $text = if ($e.Id -ge 8000 -and $e.Id -le 8003) { $pairs -join ', ' } else { ([string]$e.Message -split "`r?`n")[0] }
        Write-Log ("  журнал WLAN {0:HH:mm:ss.fff}  {1}  {2}" -f $e.TimeCreated, $e.Id, $text) 'DarkCyan'
        [pscustomobject]@{ Id = $e.Id; Time = $e.TimeCreated; Fields = $fields }
    }
}

# Состояние адаптера и сеть; пишет только смену.
function Update-AdapterState {
    $status = Get-AdapterStatus
    # netsh — при смене состояния и раз в 2 с, пока Up: переход из сети в сеть может пройти между опросами.
    if ($status -eq $script:lastStatus -and ($status -ne 'Up' -or ((Get-Date) - $script:ssidReadAt).TotalSeconds -lt 2)) { return $status }
    $ssid = ''
    if ($status -eq 'Up') { $ssid = [string](Get-Ssid); $script:ssidReadAt = Get-Date }
    if ($status -ne $script:lastStatus -or $ssid -ne $script:lastSsid) {
        $script:lastStatus = $status
        $script:lastSsid = $ssid
        Write-Log ("  адаптер: {0}{1}" -f $status, $(if ($ssid) { ", сеть «$ssid»" } else { '' })) 'DarkYellow'
    }
    return $status
}

# Причина неудачи из события 8002: код и текст. В 8003 текст лежит в Reason; в 8002, по документации, —
# в FailureReason, но на машине это ещё не видели, поэтому запасной вариант оставлен.
function Get-FailureText($WlanEvent) {
    $f = $WlanEvent.Fields
    $text = if ($f['FailureReason']) { $f['FailureReason'] } else { $f['Reason'] }
    'ReasonCode={0}; {1}' -f $f['ReasonCode'], $text
}

# Следит заданное время. Возвращает сети, к которым адаптер оказывался подключён.
function Watch-Adapter([int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $seen = New-Object System.Collections.Generic.List[string]
    while ((Get-Date) -lt $deadline) {
        Read-WlanEvents | Out-Null
        if ((Update-AdapterState) -eq 'Up') {
            if (-not $seen.Contains($script:lastSsid)) { $seen.Add($script:lastSsid) }
        }
        Start-Sleep -Milliseconds 300
    }
    return , $seen
}

# --- Действия --------------------------------------------------------------

function Wait-Step([string]$Number, [string]$Title, [string]$Explain) {
    Write-Host ''
    Write-Log ("===== Шаг {0}. {1}" -f $Number, $Title) 'Magenta'
    if ($Explain) { Write-Block $Explain }
    $answer = Read-Host 'Enter — выполнить, q — прервать'
    if ($answer -eq 'q') { Write-Log 'Прервано человеком.' 'Red'; Finish }
}

function Disconnect-Wifi([int]$Watch) {
    Read-WlanEvents | Out-Null
    Invoke-Native 'netsh.exe' @('wlan', 'disconnect', "interface=$Adapter") | Out-Null
    Watch-Adapter $Watch | Out-Null
    # Первые доли секунды адаптер ещё Up в прежней сети — это не автоподключение.
    $status = Get-AdapterStatus
    if ($status -eq 'Up') {
        Write-Result ("Отключение: через {0} с адаптер подключён к «{1}» — Windows подключилась сама. Проверка идёт дальше: подключение к сети проверки уведёт адаптер оттуда." -f $Watch, (Get-Ssid)) 'Red'
        return $false
    }
    Write-Result ("Отключение: за {0} с Windows сама не подключилась." -f $Watch)
    return $true
}

# Подключение и ожидание исхода. Исход — первое из: 8001 или 8002 для этой сети, срок.
function Connect-AndWait([string]$To, [int]$Timeout) {
    Read-WlanEvents | Out-Null
    $start = Get-Date
    $cmd = Invoke-Native 'netsh.exe' @('wlan', 'connect', "name=$To", "interface=$Adapter")
    $deadline = $start.AddSeconds($Timeout)
    $outcome = $null
    $upAt = $null
    while (-not $outcome -and (Get-Date) -lt $deadline) {
        foreach ($e in @(Read-WlanEvents)) {
            if (-not $outcome -and ($e.Id -eq 8001 -or $e.Id -eq 8002) -and
                ($e.Fields['ProfileName'] -eq $To -or $e.Fields['SSID'] -eq $To)) { $outcome = $e }
        }
        if ((Update-AdapterState) -eq 'Up' -and -not $upAt) { $upAt = Get-Date }
        if (-not $outcome) { Start-Sleep -Milliseconds 200 }
    }
    return [pscustomobject]@{ Start = $start; Command = $cmd; Event = $outcome; UpAt = $upAt }
}

function Get-DefaultRoute {
    Get-NetRoute -InterfaceAlias $Adapter -DestinationPrefix '0.0.0.0/0' -PolicyStore ActiveStore -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Get-PreferredAddress {
    Get-NetIPAddress -InterfaceAlias $Adapter -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.AddressState -eq 'Preferred' } | Select-Object -First 1
}

# Сроки проверки после подключения (план: маршрут ~3 с, адрес 10 с, ping 3×1 с, порт 3 с):
# здесь ждём дольше, чтобы увидеть фактическое время.
function Measure-AfterConnect([datetime]$ConnectedAt) {
    $deadline = $ConnectedAt.AddSeconds(20)
    $routeAt = $null; $addrAt = $null; $route = $null; $ip = $null
    while ((Get-Date) -lt $deadline -and -not ($routeAt -and $addrAt)) {
        if (-not $routeAt) { $route = Get-DefaultRoute; if ($route) { $routeAt = Get-Date } }
        if (-not $addrAt) {
            $ip = Get-PreferredAddress
            if ($ip -and -not $ip.IPAddress.StartsWith('169.254.')) { $addrAt = Get-Date }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($routeAt) { Write-Result ("Маршрут по умолчанию через {0}: {1} после подключения" -f $route.NextHop, (Format-Seconds $ConnectedAt $routeAt)) }
    else { Write-Result 'Маршрута по умолчанию нет 20 с после подключения' 'Red' }
    if ($addrAt) { Write-Result ("Адрес {0} ({1}): {2} после подключения" -f $ip.IPAddress, $ip.PrefixOrigin, (Format-Seconds $ConnectedAt $addrAt)) }
    else {
        $now = Get-PreferredAddress
        Write-Result ("Адреса нет 20 с после подключения{0}" -f $(if ($now) { ", действует $($now.IPAddress)" } else { '' })) 'Red'
    }

    if ($route) {
        $ping = New-Object System.Net.NetworkInformation.Ping
        $answers = @()
        for ($i = 1; $i -le 3; $i++) {
            try {
                $reply = $ping.Send($route.NextHop, 1000)
                $answers += if ($reply.Status -eq 'Success') { "$($reply.RoundtripTime) мс" } else { [string]$reply.Status }
            } catch { $answers += 'ошибка' }
        }
        Write-Result ("Ping шлюза {0}, 3 попытки по 1 с: {1}" -f $route.NextHop, ($answers -join ', '))
    }

    if ($Proxy) {
        $hostName, $port = $Proxy -split ':', 2
        $tcp = New-Object System.Net.Sockets.TcpClient
        $t = Get-Date
        try {
            $ok = $tcp.ConnectAsync($hostName, [int]$port).Wait(3000)
            $text = if ($ok) { 'соединение за {0:N0} мс' -f ((Get-Date) - $t).TotalMilliseconds } else { 'нет соединения за 3 с' }
        } catch { $text = 'ошибка: ' + $_.Exception.GetBaseException().Message }
        finally { $tcp.Close() }
        Write-Result ("Порт прокси {0}: {1}" -f $Proxy, $text)
    }
}

# Возврат к прежней сети, как при откате: удалить созданный профиль сети Windows и подключиться.
function Restore-Previous([string]$Step) {
    Wait-Step $Step "Вернуться к «$Previous»" 'Как при откате: отключиться, удалить созданный этим запуском профиль сети Windows, подключиться к прежней сети.'
    if ((Get-AdapterStatus) -eq 'Up' -and -not (Test-ConnectedTo $Previous)) {
        Invoke-Native 'netsh.exe' @('wlan', 'disconnect', "interface=$Adapter") | Out-Null
        Watch-Adapter 2 | Out-Null
    }
    Remove-CreatedProfile
    if (Test-ConnectedTo $Previous) {
        Write-Result "Возврат: адаптер уже подключён к «$Previous»."
        return
    }
    $attempt = Connect-AndWait $Previous 30
    if ($attempt.Event -and $attempt.Event.Id -eq 8001) {
        Write-Result ("Возврат к «{0}»: подключено (8001) через {1} после команды" -f $Previous, (Format-Seconds $attempt.Start $attempt.Event.Time))
    } else {
        Write-Result ("Возврат к «{0}» не удался: {1}" -f $Previous, $(if ($attempt.Event) { 'событие 8002, ' + (Get-FailureText $attempt.Event) } else { 'нет события за 30 с' })) 'Red'
    }
}

function Finish {
    Remove-CreatedProfile
    Write-Host ''
    Write-Log '===== Итог' 'Magenta'
    Write-Block ($results -join "`n")
    Write-Log ("Отчёт: {0}" -f $report) 'Yellow'
    Write-Log 'Сохраните отчёт разведочной сборки: в нём уведомления WLAN (ACM и MSM) за то же время.' 'Yellow'
    exit
}

# --- Начало ----------------------------------------------------------------

try {
    $succeeded = $false
    Add-Content -LiteralPath $report -Value 'WiFiControl — ручная проверка подключения (#30)' -Encoding UTF8
    $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    Write-Log ("Компьютер {0}, Windows {1}, сборка {2}.{3}" -f $env:COMPUTERNAME, $os.DisplayVersion, $os.CurrentBuildNumber, $os.UBR)
    Write-Log ("Случай {0}: сеть «{1}», прежняя сеть «{2}», адаптер «{3}»{4}" -f $Case, $Network, $Previous, $Adapter, $(if ($Proxy) { ", прокси $Proxy" } else { '' }))
    # Отсчёт событий журнала — с этого момента; прежние не нужны.
    $last = Get-WinEvent -LogName $wlanLog -MaxEvents 1 -ErrorAction SilentlyContinue
    if ($last) { $script:lastRecordId = $last.RecordId }

    Wait-Step '0' 'Исходное состояние' "Разведочная сборка должна быть запущена (она пишет уведомления WLAN). Адаптер — подключён к «$Previous». Ничего не меняется."
    Update-AdapterState | Out-Null
    Write-Profiles
    $ok = $true
    if ($Network -eq $Previous) { Write-Log 'Сеть проверки и прежняя сеть должны различаться (-Network, -Previous).' 'Red'; $ok = $false }
    if (-not (Find-WifiProfile $Previous)) { Write-Log "Нет профиля сети Windows «$Previous»: к ней не вернуться." 'Red'; $ok = $false }
    if (-not (Test-ConnectedTo $Previous)) {
        Write-Log "Адаптер не подключён к «$Previous». Подключитесь и запустите заново." 'Red'; $ok = $false
    }
    if (-not (Get-Process -Name 'WiFiControl.Recon' -ErrorAction SilentlyContinue)) {
        Write-Log 'Разведочная сборка не запущена: уведомлений WLAN не будет, только журнал WLAN-AutoConfig. Запустите её, если это не нарочно.' 'Red'
    }
    $existing = Find-WifiProfile $Network
    $create = $false
    switch ($Case) {
        'Known' {
            if (-not $existing) { Write-Log "Для Known нужен профиль сети Windows «$Network»: подключитесь к ней раньше из системного меню." 'Red'; $ok = $false }
        }
        'OutOfRange' {
            $create = -not $existing
        }
        default {
            # Чужой профиль сети Windows сценарий не перезаписывает: в нём рабочий пароль.
            if ($existing) {
                Write-Log ("Профиль сети Windows «{0}» уже есть. Для случая {1} его нужно удалить: netsh wlan delete profile name=""{0}""" -f $Network, $Case) 'Red'; $ok = $false
            }
            $create = $true
        }
    }
    if ($NonBroadcast -and -not ($Case -eq 'OutOfRange' -and $create)) {
        Write-Log '-NonBroadcast — только для OutOfRange, когда профиля сети Windows для этой сети нет: иначе он ничего не меняет.' 'Red'; $ok = $false
    }
    if ($Proxy -and $Proxy -notmatch '^[^:]+:\d+$') { Write-Log "-Proxy нужен вида host:port, а не «$Proxy»." 'Red'; $ok = $false }
    if ($create -and -not $Password) {
        if ($Case -eq 'WrongPassword' -or $Case -eq 'OutOfRange') { $Password = 'WiFiControl-wrong-1' }
        else { Write-Log "Для случая $Case нужен пароль сети: -Password." 'Red'; $ok = $false }
    }
    if (-not $ok) { Finish }

    Wait-Step '1' "Отключиться от «$Previous»" "netsh wlan disconnect и $WatchAfterDisconnect с наблюдения: не подключится ли Windows сама (ADR 0004, шаг 1)."
    Disconnect-Wifi $WatchAfterDisconnect | Out-Null

    if ($create) {
        $hidden = $Case -eq 'Hidden' -or $NonBroadcast
        Wait-Step '2' 'Создать профиль сети Windows' ("netsh wlan add profile: WPA2-PSK, автоподключение выключено{0}." -f $(if ($hidden) { ', nonBroadcast' } else { '' }))
        if (-not (Add-WifiProfile $hidden)) { Restore-Previous '5'; Finish }
    }

    Wait-Step '3' "Подключиться к «$Network»" "netsh wlan connect, до $ConnectTimeout с ждём событие 8001 (подключено) или 8002 (не удалось)."
    $attempt = Connect-AndWait $Network $ConnectTimeout
    if ($attempt.Command.Code -ne 0) { Write-Result ("netsh wlan connect отказал сразу, код {0}: {1}" -f $attempt.Command.Code, $attempt.Command.Output.Trim()) 'Red' }
    if ($attempt.Event -and $attempt.Event.Id -eq 8001) {
        $succeeded = $true
        Write-Result ("Подключено (8001): {0} после команды" -f (Format-Seconds $attempt.Start $attempt.Event.Time))
        if ($attempt.UpAt) { Write-Result ("Адаптер Up: {0} после команды" -f (Format-Seconds $attempt.Start $attempt.UpAt)) }
        Measure-AfterConnect $attempt.Event.Time

        Wait-Step '4' 'Следить за соединением' "$WatchSeconds с: не разорвёт ли Windows соединение (ограниченное подключение)."
        $seen = Watch-Adapter $WatchSeconds
        $status = Get-AdapterStatus
        Write-Result ("Через {0} с: адаптер {1}, сети за это время: {2}" -f $WatchSeconds, $status, $(if ($seen.Count) { $seen -join ', ' } else { '(нет)' })) $(if ($status -eq 'Up') { 'Green' } else { 'Red' })
    } elseif ($attempt.Event) {
        Write-Result ("Не удалось (8002): {0} после команды; {1}" -f (Format-Seconds $attempt.Start $attempt.Event.Time), (Get-FailureText $attempt.Event)) 'Yellow'
    } else {
        Write-Result ("За {0} с нет ни 8001, ни 8002 для «{1}»" -f $ConnectTimeout, $Network) 'Yellow'
    }

    if (-not $succeeded) {
        # Без вопроса: окно сразу после неудачи и есть время отката.
        Write-Log ("===== Шаг 4. Следить после неудачи: {0} с, как будто идёт откат. Профиль сети Windows пока не удаляется." -f $WatchAfterFail) 'Magenta'
        $seen = Watch-Adapter $WatchAfterFail
        if ($seen.Count) { Write-Result ("После неудачи Windows сама подключилась к: {0}" -f ($seen -join ', ')) 'Red' }
        else { Write-Result ("После неудачи за {0} с Windows сама ни к чему не подключилась" -f $WatchAfterFail) }
    }

    Restore-Previous '5'
    Finish
} finally {
    # Прерывание по Ctrl+C: созданный профиль сети Windows не оставляем.
    Remove-CreatedProfile
}
