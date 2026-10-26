<#
.SYNOPSIS
    Проверка записи настроек IPv4 на Wi-Fi-адаптере без соединения (этап 1 плана).

.DESCRIPTION
    Выполняет по шагам те же команды, что перенесены из NetSwitch (ADR 0003):
    статический адрес через netsh на отключённом Wi-Fi-адаптере, возврат к DHCP
    обычным netsh и запасным путём (netsh delete address + MSFT_NetIPInterface
    в постоянном хранилище). После каждого шага снимает состояние адаптера
    и пишет всё в отчёт. Инструкция — docs/write-check.md.

    Сценарий меняет настройки IPv4 Wi-Fi-адаптера. Вернуть DHCP: параметр -Restore.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    # Имя Wi-Fi-адаптера в «Сетевых подключениях».
    [string]$Adapter = 'Wi-Fi',
    # Профиль сети Windows, к которой подключаемся между шагами.
    [string]$Network = 'MichLycee',
    # Статический адрес: свободный адрес в этой сети.
    [string]$Address = '192.168.1.200',
    [string]$Mask = '255.255.255.0',
    [string]$Gateway = '192.168.1.1',
    [string]$Dns = '192.168.1.1',
    # Сколько ждать подключения и адреса, секунд.
    [int]$ConnectTimeout = 30,
    # Сколько следить, не подключилась ли Windows сама после отключения, секунд.
    [int]$WatchAfterDisconnect = 15,
    # Только вернуть DHCP и подключиться к сети, без проверки.
    [switch]$Restore
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$reportDir = Join-Path $PSScriptRoot 'Отчёты'
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$report = Join-Path $reportDir ('write-check_{0}_{1}.txt' -f $env:COMPUTERNAME, (Get-Date -Format 'yyyy-MM-dd_HH-mm'))
$results = New-Object System.Collections.Generic.List[string]

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

function Write-Check([string]$Step, [string]$What, [bool]$Ok, [string]$Fact) {
    if ($Ok) { $mark = 'ДА '; $color = 'Green' } else { $mark = 'НЕТ'; $color = 'Red' }
    $text = if ($Fact) { "[$mark] $What — $Fact" } else { "[$mark] $What" }
    Write-Log $text $color
    $results.Add(("{0}: {1}" -f $Step, $text))
}

# Запуск внешней команды с записью команды, кода возврата и вывода.
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

# --- Состояние адаптера ----------------------------------------------------

function Get-AdapterInfo { Get-NetAdapter -Name $Adapter }

function Get-TcpipKey {
    $guid = (Get-AdapterInfo).InterfaceGuid
    "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$guid"
}

function Get-RegValue([string]$Name) {
    $item = Get-ItemProperty -LiteralPath (Get-TcpipKey) -ErrorAction SilentlyContinue
    if ($item -and ($item.PSObject.Properties.Name -contains $Name)) { return $item.$Name }
    return $null
}

function Get-DhcpState([string]$Store) {
    $i = Get-NetIPInterface -InterfaceAlias $Adapter -AddressFamily IPv4 -PolicyStore $Store -ErrorAction SilentlyContinue
    if (-not $i) { return '(нет записи)' }
    if (-not [string]$i.Dhcp) { return '(не задано)' }
    return [string]$i.Dhcp
}

function Get-Addresses([string]$Store) {
    Get-NetIPAddress -InterfaceAlias $Adapter -AddressFamily IPv4 -PolicyStore $Store -ErrorAction SilentlyContinue
}

function Get-Ssid {
    $ErrorActionPreference = 'Continue'
    $text = & netsh.exe wlan show interfaces 2>&1 | Out-String
    $block = $false
    foreach ($l in ($text -split "`r?`n")) {
        if ($l -match '^\s*\S[^:]*:\s*(.+)$' -and $l -match [regex]::Escape($Adapter)) { $block = $true }
        if ($block -and $l -match '^\s*SSID\s*:\s*(.+)$') { return $Matches[1].Trim() }
    }
    return $null
}

# Признак ручного адреса, по которому NetSwitch включает запасной путь.
function Test-ManualAddress {
    $flag = Get-RegValue 'DisableDhcpOnConnect'
    $persistentDhcp = Get-DhcpState 'PersistentStore'
    $manual = @(Get-Addresses 'PersistentStore' | Where-Object { $_.PrefixOrigin -eq 'Manual' })
    return ($flag -eq 1) -or ($persistentDhcp -eq 'Disabled') -or ($manual.Count -gt 0)
}

function Write-Snapshot([string]$Title) {
    Write-Log ("--- Снимок: {0}" -f $Title) 'Yellow'
    $a = Get-AdapterInfo
    $lines = @()
    $lines += "Адаптер: {0} ({1}), состояние {2}, GUID {3}" -f $a.Name, $a.InterfaceDescription, $a.Status, $a.InterfaceGuid
    $lines += "Сеть (SSID): {0}" -f (Get-Ssid)
    $lines += "DHCP: действующие настройки {0}, постоянные {1}" -f (Get-DhcpState 'ActiveStore'), (Get-DhcpState 'PersistentStore')
    foreach ($store in 'ActiveStore', 'PersistentStore') {
        $addr = @(Get-Addresses $store | ForEach-Object { '{0}/{1} ({2}, {3})' -f $_.IPAddress, $_.PrefixLength, $_.PrefixOrigin, $_.AddressState })
        if ($addr.Count -eq 0) { $addr = @('(нет)') }
        $lines += "Адреса, {0}: {1}" -f $store, ($addr -join '; ')
        $routes = @(Get-NetRoute -InterfaceAlias $Adapter -DestinationPrefix '0.0.0.0/0' -PolicyStore $store -ErrorAction SilentlyContinue |
            ForEach-Object { '{0} (метрика {1})' -f $_.NextHop, $_.RouteMetric })
        if ($routes.Count -eq 0) { $routes = @('(нет)') }
        $lines += "Шлюзы, {0}: {1}" -f $store, ($routes -join '; ')
    }
    $dns = @(Get-DnsClientServerAddress -InterfaceAlias $Adapter -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        ForEach-Object { $_.ServerAddresses })
    $lines += "DNS действующие: {0}" -f ($dns -join ', ')
    $lines += "Реестр {0}:" -f (Get-TcpipKey)
    foreach ($name in 'EnableDHCP', 'DisableDhcpOnConnect', 'IPAddress', 'SubnetMask', 'DefaultGateway', 'NameServer', 'DhcpIPAddress', 'DhcpDefaultGateway') {
        $v = Get-RegValue $name
        if ($null -eq $v) { $v = '(нет значения)' } elseif ($v -is [array]) { $v = ($v -join ', ') }
        $lines += "  {0} = {1}" -f $name, $v
    }
    $lines += "Ручной адрес по признаку NetSwitch: {0}" -f $(if (Test-ManualAddress) { 'да' } else { 'нет' })
    Write-Block ($lines -join "`n")
    $ErrorActionPreference = 'Continue'
    $cfg = & netsh.exe interface ipv4 show config name="$Adapter" 2>&1 | Out-String
    Write-Log 'netsh interface ipv4 show config:'
    Write-Block $cfg
}

# --- Действия --------------------------------------------------------------

function Wait-Step([string]$Number, [string]$Title, [string]$Explain) {
    Write-Host ''
    Write-Log ("===== Шаг {0}. {1}" -f $Number, $Title) 'Magenta'
    if ($Explain) { Write-Block $Explain }
    $answer = Read-Host 'Enter — выполнить, q — прервать'
    if ($answer -eq 'q') { Write-Log 'Прервано человеком.' 'Red'; Finish }
}

function Disconnect-Wifi {
    Invoke-Native 'netsh.exe' @('wlan', 'disconnect', "interface=$Adapter") | Out-Null
    $deadline = (Get-Date).AddSeconds($WatchAfterDisconnect)
    $reconnected = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ((Get-AdapterInfo).Status -eq 'Up') { $reconnected = $true; break }
    }
    if ($reconnected) {
        Write-Log ("Windows сама подключилась к сети «{0}». Выключите автоподключение этой сети или её точку доступа и повторите шаг." -f (Get-Ssid)) 'Red'
    } else {
        Write-Log ("За {0} с Windows сама не подключилась." -f $WatchAfterDisconnect)
    }
    return -not $reconnected
}

# Подключение и ожидание адреса. Возвращает адрес (или $null) и время.
function Connect-Wifi {
    $start = Get-Date
    Invoke-Native 'netsh.exe' @('wlan', 'connect', "name=$Network", "interface=$Adapter") | Out-Null
    $deadline = $start.AddSeconds($ConnectTimeout)
    $upAt = $null; $ip = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if (-not $upAt -and (Get-AdapterInfo).Status -eq 'Up') { $upAt = Get-Date }
        if ($upAt) {
            $ip = Get-NetIPAddress -InterfaceAlias $Adapter -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.AddressState -eq 'Preferred' } | Select-Object -First 1
            if ($ip -and -not $ip.IPAddress.StartsWith('169.254.')) { break }
        }
    }
    $now = Get-Date
    if ($upAt) {
        Write-Log ("Подключение: {0:N1} с после команды" -f ($upAt - $start).TotalSeconds)
    } else {
        Write-Log ("Подключения нет за {0} с" -f $ConnectTimeout) 'Red'
    }
    if ($ip) {
        Write-Log ("Адрес {0}: {1:N1} с после подключения" -f $ip.IPAddress, ($now - $upAt).TotalSeconds)
        return [pscustomobject]@{ Address = $ip.IPAddress; Origin = [string]$ip.PrefixOrigin }
    }
    return $null
}

function Test-Reachability([string]$Step) {
    $gw = Test-Connection -ComputerName $Gateway -Count 2 -Quiet -ErrorAction SilentlyContinue
    Write-Check $Step "шлюз $Gateway отвечает на ping" $gw ''
    $net = $false
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $net = $tcp.ConnectAsync('1.1.1.1', 443).Wait(3000)
        $tcp.Close()
    } catch { $net = $false }
    Write-Check $Step 'интернет (TCP 1.1.1.1:443)' $net ''
}

function Set-Static {
    Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'set', 'address', "name=$Adapter", 'source=static', "address=$Address", "mask=$Mask", "gateway=$Gateway") | Out-Null
    Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'set', 'dnsservers', "name=$Adapter", 'source=static', "address=$Dns", 'register=primary', 'validate=no') | Out-Null
}

# Обычный netsh. Возвращает вывод команды адреса, чтобы видеть отказ.
function Set-DhcpPlain {
    $r = Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'set', 'address', "name=$Adapter", 'source=dhcp')
    Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'set', 'dnsservers', "name=$Adapter", 'source=dhcp') | Out-Null
    return $r
}

# Запасной путь NetSwitch (ADR 0003): удалить ручные адреса и шлюзы,
# затем включить DHCP в постоянных настройках через MSFT_NetIPInterface.
function Set-DhcpFallback {
    $manual = @(Get-Addresses 'PersistentStore' | Where-Object { $_.PrefixOrigin -eq 'Manual' } | ForEach-Object { $_.IPAddress })
    if ($manual.Count -eq 0) { $manual = @($Address) }
    foreach ($a in $manual) {
        Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'delete', 'address', "name=$Adapter", "address=$a", 'gateway=all') | Out-Null
    }
    Write-Log ("> Set-NetIPInterface -InterfaceAlias '{0}' -AddressFamily IPv4 -PolicyStore PersistentStore -Dhcp Enabled  (это MSFT_NetIPInterface)" -f $Adapter) 'Cyan'
    try {
        Set-NetIPInterface -InterfaceAlias $Adapter -AddressFamily IPv4 -PolicyStore PersistentStore -Dhcp Enabled
        Write-Log '  выполнено без ошибки'
    } catch {
        Write-Log ("  ОШИБКА: {0}" -f $_.Exception.Message) 'Red'
    }
}

function Restore-Dhcp {
    Set-DhcpPlain | Out-Null
    if (Test-ManualAddress) {
        Write-Log 'Адрес всё ещё ручной — запасной путь.'
        Set-DhcpFallback
    }
}

function Finish {
    Write-Host ''
    Write-Log '===== Итог' 'Magenta'
    Write-Block ($results -join "`n")
    Write-Log ("Отчёт: {0}" -f $report) 'Yellow'
    Write-Log ("Если Wi-Fi остался без адреса: .\write-check.ps1 -Restore -Adapter '{0}' -Network '{1}'" -f $Adapter, $Network)
    exit
}

# --- Начало ----------------------------------------------------------------

Add-Content -LiteralPath $report -Value 'WiFiControl — проверка записи настроек IPv4 (этап 1)' -Encoding UTF8
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
Write-Log ("Компьютер {0}, Windows {1}, сборка {2}.{3}" -f $env:COMPUTERNAME, $os.DisplayVersion, $os.CurrentBuildNumber, $os.UBR)
Write-Log ("Параметры: адаптер «{0}», сеть «{1}», адрес {2} {3}, шлюз {4}, DNS {5}" -f $Adapter, $Network, $Address, $Mask, $Gateway, $Dns)

if ($Restore) {
    Write-Log '===== Возврат к DHCP' 'Magenta'
    Restore-Dhcp
    Write-Snapshot 'после возврата к DHCP'
    $ip = Connect-Wifi
    Write-Snapshot 'после подключения'
    Write-Log ("Отчёт: {0}" -f $report) 'Yellow'
    exit
}

# Шаг 0: исходное состояние и предварительные проверки.
Wait-Step '0' 'Исходное состояние' "Wi-Fi-адаптер должен быть подключён к сети «$Network» по DHCP. Ничего не меняется."
Write-Snapshot 'исходное'
$ok = $true
if ((Get-AdapterInfo).Status -ne 'Up' -or (Get-Ssid) -ne $Network) {
    Write-Log ("Wi-Fi-адаптер не подключён к «{0}». Подключитесь и запустите заново." -f $Network) 'Red'; $ok = $false
}
if ((Get-DhcpState 'ActiveStore') -ne 'Enabled' -or (Test-ManualAddress)) {
    Write-Log 'На адаптере сейчас не DHCP. Сценарий в конце вернёт DHCP, и прежние ручные настройки пропадут — перепишите их, прежде чем продолжать.' 'Red'; $ok = $false
}
$current = Get-NetIPAddress -InterfaceAlias $Adapter -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
if ($current -and $current.IPAddress -eq $Address) {
    Write-Log "Адрес $Address уже выдан этому адаптеру по DHCP — выберите другой (-Address)." 'Red'; $ok = $false
} elseif (Test-Connection -ComputerName $Address -Count 2 -Quiet -ErrorAction SilentlyContinue) {
    Write-Log "Адрес $Address отвечает на ping — он занят, выберите другой (-Address)." 'Red'; $ok = $false
}
if (-not $ok) { Finish }
Write-Check '0' 'предварительные проверки' $true "подключён к «$Network» по DHCP, адрес $Address свободен"

# Случай А: статический адрес был действующим, затем возврат к DHCP.
Wait-Step '1' 'Отключить Wi-Fi-адаптер от сети' "Отключение и $WatchAfterDisconnect с наблюдения: не подключится ли Windows сама (ADR 0004, шаг 1)."
$stayed = Disconnect-Wifi
Write-Check '1' 'адаптер остался отключённым' $stayed ''
Write-Snapshot 'после отключения'
if (-not $stayed) { Finish }

Wait-Step '2' 'Статический адрес на отключённом адаптере' "netsh set address source=static и set dnsservers source=static (ADR 0004, шаг 2)."
Set-Static
Write-Snapshot 'после записи статического адреса без соединения'
Write-Check '2' 'адрес записан как ручной' (Test-ManualAddress) ("DisableDhcpOnConnect = {0}, DHCP постоянные = {1}" -f (Get-RegValue 'DisableDhcpOnConnect'), (Get-DhcpState 'PersistentStore'))
Write-Check '2' 'адаптер остался отключённым' ((Get-AdapterInfo).Status -ne 'Up') ''

Wait-Step '3' "Подключиться к «$Network»" 'Ожидается статический адрес, шлюз и интернет (ADR 0004, шаг 3).'
$ip = Connect-Wifi
Write-Snapshot 'после подключения со статическим адресом'
$got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
Write-Check '3' "действует статический адрес $Address" ($ip -and $ip.Address -eq $Address) $got
Test-Reachability '3'

Wait-Step '4' 'Отключить от сети' ''
$stayed = Disconnect-Wifi
Write-Check '4' 'адаптер остался отключённым' $stayed ''
Write-Snapshot 'после отключения'
if (-not $stayed) { Finish }

Wait-Step '5' 'Вернуть DHCP обычным netsh (статика была действующей)' 'netsh set address source=dhcp и set dnsservers source=dhcp. Если адрес останется ручным — запасной путь.'
$r = Set-DhcpPlain
Write-Snapshot 'после обычного netsh'
$plainOk = -not (Test-ManualAddress)
Write-Check '5' 'обычный netsh вернул DHCP' $plainOk ("код {0}" -f $r.Code)
if (-not $plainOk) {
    Wait-Step '5б' 'Запасной путь' 'netsh delete address … gateway=all, затем MSFT_NetIPInterface в постоянном хранилище.'
    Set-DhcpFallback
    Write-Snapshot 'после запасного пути'
    Write-Check '5б' 'запасной путь вернул DHCP' (-not (Test-ManualAddress)) ''
}

Wait-Step '6' "Подключиться к «$Network»" 'Ожидается адрес по DHCP.'
$ip = Connect-Wifi
Write-Snapshot 'после подключения по DHCP'
$got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
Write-Check '6' 'адрес получен по DHCP' ($ip -and $ip.Origin -eq 'Dhcp') $got
Test-Reachability '6'

# Случай Б: статический адрес записан без соединения и не применялся (откат, неудачное переключение).
Wait-Step '7' 'Отключить от сети' ''
$stayed = Disconnect-Wifi
Write-Check '7' 'адаптер остался отключённым' $stayed ''
if (-not $stayed) { Finish }

Wait-Step '8' 'Статический адрес на отключённом адаптере, без подключения' ''
Set-Static
Write-Snapshot 'после записи статического адреса без соединения'
Write-Check '8' 'адрес записан как ручной' (Test-ManualAddress) ("DisableDhcpOnConnect = {0}" -f (Get-RegValue 'DisableDhcpOnConnect'))

Wait-Step '9' 'Вернуть DHCP обычным netsh, не подключаясь' 'По опыту NetSwitch на Windows 10 ожидается отказ «DHCP is already enabled».'
$r = Set-DhcpPlain
Write-Snapshot 'после обычного netsh'
$plainOk = -not (Test-ManualAddress)
Write-Check '9' 'обычный netsh вернул DHCP' $plainOk ("код {0}" -f $r.Code)
if (-not $plainOk) {
    Wait-Step '9б' 'Запасной путь' 'netsh delete address … gateway=all, затем MSFT_NetIPInterface в постоянном хранилище.'
    Set-DhcpFallback
    Write-Snapshot 'после запасного пути'
    Write-Check '9б' 'запасной путь вернул DHCP' (-not (Test-ManualAddress)) ''
}

Wait-Step '10' "Подключиться к «$Network»" 'Ожидается адрес по DHCP, не 169.254.x.x.'
$ip = Connect-Wifi
Write-Snapshot 'итоговое'
$got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
Write-Check '10' 'адрес получен по DHCP' ($ip -and $ip.Origin -eq 'Dhcp') $got
Test-Reachability '10'

Finish
