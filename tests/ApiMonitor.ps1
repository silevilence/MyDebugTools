# Loaded by Desktop.ps1 -MonitorOnly. Uses UI Automation patterns throughout.
function Set-MonitorText($name, $value) {
    (Wait-Element $name $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Wait-CaptureText($id, $pattern) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    for ($i = 0; $i -lt 60; $i++) {
        $item = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if ($item) {
            $value = $item.Current.Name
            if ($value -match $pattern) { return $value }
        }
        Start-Sleep -Milliseconds 100
    }
    $visible = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        Select-Object -First 70 | ForEach-Object { $_.Current.Name }
    throw "Missing $id / $pattern. Last value [$value]. Visible controls: $($visible -join ', ')"
}

function Wait-MonitorDialog {
    $byName = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, '停用监控插件')
    $byPid = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
    $condition = [Windows.Automation.AndCondition]::new($byPid, $byName)
    for ($i = 0; $i -lt 50; $i++) {
        $dialog = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if (!$dialog) { $dialog = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition) }
        if ($dialog) { return $dialog }
        Start-Sleep -Milliseconds 100
    }
    throw 'Stop confirmation missing'
}

function Choose-MonitorDialog($dialog, $choice, $targetId) {
    $window = $dialog
    $texts = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    if (!($texts | Where-Object { $_.Current.Name -like "*interactive*PID $targetId*" })) { throw 'Wrong target in confirmation' }
    Invoke-Control $choice
}

function Check-ApiMonitor {
    Open-Manager
    Invoke-Control '启用 API 监控'
    Invoke-Control 'API 监控' $menuItem
    foreach ($arch in 'x86','x64') {
        $dir = (Resolve-Path (Join-Path $DllFixtures $arch)).Path
        Set-MonitorText '监控 DLL 路径' "$dir/fixture.dll"
        Invoke-Control '读取 DLL 导出'
        Set-MonitorText '搜索 SDK 函数' 'fixture.dll!add'
        Invoke-Control '加入监控'
        Set-MonitorText '目标 EXE 路径' "$dir/monitor-target.exe"
        Invoke-Control '启动并监控'
        [void](Wait-CaptureText 'capture-status' "$arch.*已结束.*10 次调用.*丢失 0")
        $first = Wait-CaptureText 'call-detail' 'a=0，b=100'
        if ($first -notmatch '返回：100' -or $first -notmatch 'monitor_target\+0x') { throw "Wrong detail $first" }
        if ($arch -eq 'x86') {
            $list = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'chosen-functions'))
            $item = $list.FindFirst([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
            $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Invoke-Control '移除选中函数'
        }
        "PASS desktop launch $arch"
    }
    $start = [Diagnostics.ProcessStartInfo]::new("$dir/interactive.exe")
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $target = [Diagnostics.Process]::Start($start)
    function Call-Add($n) {
        $target.StandardInput.WriteLine($n)
        if ($target.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult() -ne [string](100 + $n)) {
            throw 'Unexpected target response'
        }
    }
    try {
        Call-Add 1
        Set-MonitorText '目标进程 ID' ([string]$target.Id)
        Invoke-Control '附加并监控'
        [void](Wait-CaptureText 'capture-status' "PID $($target.Id).*采集中")
        Call-Add 2
        $saved = Wait-CaptureText 'call-detail' '返回：102'
        Open-Manager
        Invoke-Control '停用 API 监控'
        $dialog = Wait-MonitorDialog
        Call-Add 3
        Choose-MonitorDialog $dialog '继续监控' $target.Id
        Call-Add 4
        Invoke-Control 'API 监控' $menuItem
        [void](Wait-CaptureText 'capture-status' '3 次调用')
        Open-Manager
        Invoke-Control '停用 API 监控'
        Choose-MonitorDialog (Wait-MonitorDialog) '结束监控并停用' $target.Id
        [void](Wait-Element '启用 API 监控' $button)
        Call-Add 5
        Invoke-Control '启用 API 监控'
        [void](Wait-Element '重新加载 API 监控' $button)
        if ($PreviousMonitorPlugin) { Copy-Item -LiteralPath $newMonitor -Destination $liveMonitor }
        Invoke-Control '重新加载 API 监控'
        [void](Wait-Element '重新加载 API 监控' $button)
        Invoke-Control 'API 监控' $menuItem
        [void](Wait-CaptureText 'capture-status' "PID $($target.Id).*已结束.*3 次调用")
        if ((Wait-CaptureText 'call-detail' '返回：102') -ne $saved) { throw 'Reload changed captured fields' }
        [void](Wait-CaptureText 'function-preview' 'Windows SDK')
        [void]$appProcess.CloseMainWindow()
        if (!$appProcess.WaitForExit(8000)) { throw 'Desktop did not close' }
        Start-Desktop
        Invoke-Control '启用 API 监控'
        Invoke-Control 'API 监控' $menuItem
        [void](Wait-Element '附加并监控' $button)
        [void](Wait-CaptureText 'capture-status' '^$')
        'PASS continue, detach, reload detail and empty history after restart'
        Set-MonitorText '搜索 SDK 函数' 'kernel32.dll!ExitProcess'
        [void](Wait-CaptureText 'function-preview' '此函数不会返回')
        if ((Find-Element '加入监控' $button).Current.IsEnabled) { throw 'Unsupported function is selectable' }
        Set-MonitorText '搜索 SDK 函数' 'kernel32.dll!GetSystemTime'
        [void](Wait-CaptureText 'function-preview' 'LPSYSTEMTIME lpSystemTime')
        Invoke-Control '加入监控'
        Set-MonitorText '目标进程 ID' ([string]$target.Id)
        Invoke-Control '附加并监控'
        [void](Wait-CaptureText 'capture-status' '采集中')
        $target.StandardInput.WriteLine('sdk')
        if ($target.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult() -ne 'done') {
            throw 'SDK sample failed'
        }
        Invoke-Control '停止所选会话'
        [void](Wait-CaptureText 'capture-status' '已结束')
        [void](Wait-CaptureText 'call-detail' 'lpSystemTime=0x[0-9A-F]+（内容未知）')
        'PASS SDK search, restrictions and partial capture without choosing a DLL'
    } finally {
        $target.StandardInput.Close()
        if (!$target.WaitForExit(5000)) { $target.Kill(); $target.WaitForExit() }
        $target.Dispose()
    }
}
