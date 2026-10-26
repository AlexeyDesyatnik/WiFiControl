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
    # Сколько ждать шлюз по умолчанию после подключения со статическим адресом, секунд.
    [int]$GatewayTimeout = 20,
    # Что проверять:
    #   Full         — оба случая возврата к DHCP, шаги 0–10;
    #   FastRoute    — статика до подключения, маршрут по умолчанию возвращается сразу после подключения;
    #   AfterConnect — подключение по DHCP, статика записывается уже на подключённом адаптере;
    #   OtherNetwork — статика до подключения к сети -Network, когда до этого был DHCP в сети -DhcpNetwork.
    [ValidateSet('Full', 'FastRoute', 'AfterConnect', 'OtherNetwork')]
    [string]$Variant = 'Full',
    # Сеть, в которой адаптер работает по DHCP перед проверкой; по умолчанию та же, что -Network.
    [string]$DhcpNetwork = '',
    # Сколько следить за соединением после записи статики, секунд.
    [int]$WatchSeconds = 45,
    # Только вернуть DHCP и подключиться к сети, без проверки.
    [switch]$Restore
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if (-not $DhcpNetwork) { $DhcpNetwork = $Network }

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
    $all = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -PolicyStore ActiveStore -ErrorAction SilentlyContinue |
        ForEach-Object { '{0} через {1} (метрика {2} + {3})' -f $_.InterfaceAlias, $_.NextHop, $_.RouteMetric, $_.InterfaceMetric })
    if ($all.Count -eq 0) { $all = @('(нет)') }
    $lines += "Маршруты по умолчанию, все адаптеры: {0}" -f ($all -join '; ')
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
function Connect-Wifi([string]$To = $Network) {
    $start = Get-Date
    Invoke-Native 'netsh.exe' @('wlan', 'connect', "name=$To", "interface=$Adapter") | Out-Null
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
    # Пингуем шлюз действующего маршрута, если он есть, иначе заданный.
    $route = Get-DefaultRoute
    $target = if ($route) { $route.NextHop } else { $Gateway }
    $gw = Test-Connection -ComputerName $target -Count 2 -Quiet -ErrorAction SilentlyContinue
    Write-Check $Step "шлюз $target отвечает на ping" $gw ''
    $net = $false
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $net = $tcp.ConnectAsync('1.1.1.1', 443).Wait(3000)
        $tcp.Close()
    } catch { $net = $false }
    Write-Check $Step 'интернет (TCP 1.1.1.1:443)' $net ''
}

function Get-DefaultRoute {
    Get-NetRoute -InterfaceAlias $Adapter -DestinationPrefix '0.0.0.0/0' -PolicyStore ActiveStore -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

# Ждёт маршрут по умолчанию через Wi-Fi-адаптер в действующих настройках.
function Wait-Gateway([string]$Step, [int]$Seconds) {
    $start = Get-Date
    $deadline = $start.AddSeconds($Seconds)
    $route = Get-DefaultRoute
    while (-not $route -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $route = Get-DefaultRoute
    }
    if ($route) {
        Write-Check $Step 'шлюз по умолчанию в действующих настройках' $true ('{0}, через {1:N1} с' -f $route.NextHop, ((Get-Date) - $start).TotalSeconds)
    } else {
        Write-Check $Step 'шлюз по умолчанию в действующих настройках' $false "не появился за $Seconds с"
    }
    return [bool]$route
}

# После подключения со статическим адресом: ждать шлюз, а если его нет —
# пробовать вернуть: сначала повторной записью настроек IPv4, затем маршрутом напрямую.
function Test-StaticGateway([string]$Step) {
    $ok = Wait-Gateway $Step $GatewayTimeout
    Write-Snapshot 'после ожидания шлюза'
    Test-Reachability $Step
    if ($ok) { return }

    Wait-Step "${Step}а" 'Повторно записать настройки IPv4 на подключённом адаптере' 'Тот же netsh set address … gateway=… и set dnsservers, что и до подключения. Так инструмент исправил бы настройки при проверке после переключения.'
    Set-Static
    $ok = Wait-Gateway "${Step}а" 10
    Write-Snapshot 'после повторной записи'
    Test-Reachability "${Step}а"
    if ($ok) { return }

    Wait-Step "${Step}б" 'Добавить маршрут по умолчанию в действующие настройки' 'netsh interface ipv4 add route 0.0.0.0/0 … store=active — только в действующие, постоянные не трогаем.'
    Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'add', 'route', 'prefix=0.0.0.0/0', "interface=$Adapter", "nexthop=$Gateway", 'metric=1', 'store=active') | Out-Null
    $ok = Wait-Gateway "${Step}б" 10
    Write-Snapshot 'после добавления маршрута'
    Test-Reachability "${Step}б"
}

function Add-DefaultRoute {
    Invoke-Native 'netsh.exe' @('interface', 'ipv4', 'add', 'route', 'prefix=0.0.0.0/0', "interface=$Adapter", "nexthop=$Gateway", 'metric=1', 'store=active') | Out-Null
}

# Следит за соединением: пишет каждую смену состояния адаптера и маршрута по умолчанию,
# в конце проверяет, что соединение не разорвано и маршрут на месте.
function Watch-Connection([string]$Step, [int]$Seconds) {
    Write-Log ("Слежу за соединением {0} с: состояние адаптера и маршрут по умолчанию." -f $Seconds)
    $start = Get-Date
    $deadline = $start.AddSeconds($Seconds)
    $last = ''
    $dropped = $false
    while ((Get-Date) -lt $deadline) {
        $status = [string](Get-AdapterInfo).Status
        $route = Get-DefaultRoute
        $now = if ($route) { "$status, маршрут через $($route.NextHop)" } else { "$status, маршрута нет" }
        if ($now -ne $last) {
            Write-Log ("  +{0:N1} с: {1}" -f ((Get-Date) - $start).TotalSeconds, $now)
            $last = $now
        }
        if ($status -ne 'Up') { $dropped = $true }
        Start-Sleep -Milliseconds 1000
    }
    Write-Check $Step "соединение не разрывалось $Seconds с" (-not $dropped) $last
    Write-Check $Step 'маршрут по умолчанию на месте в конце' ([bool](Get-DefaultRoute)) ''
}

# Возврат к исходному состоянию после вариантов: отключение, DHCP, подключение к сети DHCP.
function Restore-AfterVariant([string]$Step) {
    Wait-Step $Step 'Вернуть DHCP' "Отключение, обычный netsh source=dhcp (при необходимости запасной путь), подключение к «$DhcpNetwork»."
    Disconnect-Wifi | Out-Null
    Restore-Dhcp
    Write-Snapshot 'после возврата к DHCP'
    $ip = Connect-Wifi $DhcpNetwork
    $got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
    Write-Check $Step 'адрес получен по DHCP' ($ip -and $ip.Origin -eq 'Dhcp') $got
    Test-Reachability $Step
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
    Write-Log ("Если Wi-Fi остался без адреса: .\write-check.ps1 -Restore -Adapter '{0}' -Network '{1}'" -f $Adapter, $DhcpNetwork)
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
    $ip = Connect-Wifi $DhcpNetwork
    Write-Snapshot 'после подключения'
    Write-Log ("Отчёт: {0}" -f $report) 'Yellow'
    exit
}

# Шаг 0: исходное состояние и предварительные проверки.
Write-Log ("Вариант: {0}; сеть DHCP перед проверкой «{1}»" -f $Variant, $DhcpNetwork)
Wait-Step '0' 'Исходное состояние' "Wi-Fi-адаптер должен быть подключён к сети «$DhcpNetwork» по DHCP. Ничего не меняется."
Write-Snapshot 'исходное'
$ok = $true
if ((Get-AdapterInfo).Status -ne 'Up' -or (Get-Ssid) -ne $DhcpNetwork) {
    Write-Log ("Wi-Fi-адаптер не подключён к «{0}». Подключитесь и запустите заново." -f $DhcpNetwork) 'Red'; $ok = $false
}
if ($Variant -eq 'OtherNetwork' -and $DhcpNetwork -eq $Network) {
    Write-Log 'Для OtherNetwork нужны разные сети: -DhcpNetwork (где сейчас DHCP) и -Network (куда подключаемся со статикой).' 'Red'; $ok = $false
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
Write-Check '0' 'предварительные проверки' $true "подключён к «$DhcpNetwork» по DHCP, адрес $Address свободен"

# Варианты для первого подключения со статическим адресом. В полном прогоне маршрут
# по умолчанию после такого подключения пропадал, и через ~30 с драйвер рвал соединение.

if ($Variant -eq 'FastRoute' -or $Variant -eq 'OtherNetwork') {
    Wait-Step '1' 'Отключить Wi-Fi-адаптер от сети' ''
    if (-not (Disconnect-Wifi)) { Finish }

    Wait-Step '2' 'Статический адрес на отключённом адаптере' "Адрес $Address, шлюз $Gateway, DNS $Dns."
    Set-Static
    Write-Snapshot 'после записи статического адреса без соединения'

    if ($Variant -eq 'FastRoute') {
        $explain = "Подключение, до 3 с ждём маршрут; если его нет — сразу, без вопроса, добавляем его в действующие настройки и $WatchSeconds с следим за соединением."
    } else {
        $explain = "Подключение к другой сети, без исправлений: $WatchSeconds с следим, появится ли маршрут и не разорвётся ли соединение."
    }
    Wait-Step '3' "Подключиться к «$Network»" $explain
    $ip = Connect-Wifi
    $got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
    Write-Check '3' "действует статический адрес $Address" ($ip -and $ip.Address -eq $Address) $got
    if ($Variant -eq 'FastRoute') {
        if (-not (Wait-Gateway '3' 3)) {
            Add-DefaultRoute
            Wait-Gateway '3' 2 | Out-Null
        }
    }
    Write-Snapshot 'сразу после подключения'
    Test-Reachability '3 сразу'
    Watch-Connection '3' $WatchSeconds
    Test-Reachability '3 в конце'
    Write-Snapshot 'в конце наблюдения'

    Restore-AfterVariant '4'
    Finish
}

if ($Variant -eq 'AfterConnect') {
    Wait-Step '1' 'Отключить Wi-Fi-адаптер от сети' ''
    if (-not (Disconnect-Wifi)) { Finish }

    Wait-Step '2' "Подключиться к «$Network» с DHCP" 'Настройки IPv4 не меняются: адаптер подключается с тем, что было.'
    $ip = Connect-Wifi
    $got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
    Write-Log ("Адрес до записи статики: {0}" -f $got)

    Wait-Step '3' 'Статический адрес на подключённом адаптере' "Как в NetSwitch: netsh set address source=static … на подключённом адаптере, затем $WatchSeconds с следим за соединением."
    $start = Get-Date
    Set-Static
    $deadline = $start.AddSeconds(10)
    $cur = $null
    while ((Get-Date) -lt $deadline) {
        $cur = Get-NetIPAddress -InterfaceAlias $Adapter -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -eq $Address -and $_.AddressState -eq 'Preferred' } | Select-Object -First 1
        if ($cur) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($cur) {
        Write-Check '3' "действует статический адрес $Address" $true ('через {0:N1} с после записи' -f ((Get-Date) - $start).TotalSeconds)
    } else {
        Write-Check '3' "действует статический адрес $Address" $false 'не стал действующим за 10 с'
    }
    Wait-Gateway '3' 5 | Out-Null
    Write-Snapshot 'сразу после записи'
    Test-Reachability '3 сразу'
    Watch-Connection '3' $WatchSeconds
    Test-Reachability '3 в конце'
    Write-Snapshot 'в конце наблюдения'

    Restore-AfterVariant '4'
    Finish
}

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

Wait-Step '3' "Подключиться к «$Network»" "Ожидается статический адрес, затем до $GatewayTimeout с ждём шлюз по умолчанию (ADR 0004, шаг 3). Если шлюза не будет — шаги 3а и 3б пробуют его вернуть."
$ip = Connect-Wifi
Write-Snapshot 'сразу после подключения со статическим адресом'
$got = if ($ip) { "$($ip.Address) ($($ip.Origin))" } else { 'адреса нет' }
Write-Check '3' "действует статический адрес $Address" ($ip -and $ip.Address -eq $Address) $got
Test-StaticGateway '3'

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
