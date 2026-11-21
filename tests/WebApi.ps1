# Uses Desktop.ps1's isolated settings directory and Windows automation helpers.
function Check-WebApi {
    $tabItem = [Windows.Automation.ControlType]::TabItem
    $combo = [Windows.Automation.ControlType]::ComboBox
    $listItem = [Windows.Automation.ControlType]::ListItem
    $select = [Windows.Automation.SelectionItemPattern]::Pattern
    $expand = [Windows.Automation.ExpandCollapsePattern]::Pattern
    $valuePattern = [Windows.Automation.ValuePattern]::Pattern
    function Use-Tab($name) { (Wait-Element $name $tabItem).GetCurrentPattern($select).Select() }
    function Write-Field($name, $text) { (Wait-Element $name $edit).GetCurrentPattern($valuePattern).SetValue($text) }
    function Read-Field($name) { (Wait-Element $name $edit).GetCurrentPattern($valuePattern).Current.Value }
    function Choose($name, $option) {
        $control = Wait-Element $name $combo
        $control.GetCurrentPattern($expand).Expand()
        (Wait-Element $option $listItem).GetCurrentPattern($select).Select()
        $control.GetCurrentPattern($expand).Collapse()
    }
    function Assert-Same($expected, $actual) {
        if ($expected -cne $actual) { throw "Expected [$expected], received [$actual]" }
    }
    function Status-Matches($pattern) {
        $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $label)
        for ($attempt = 0; $attempt -lt 50; $attempt++) {
            $visible = $window.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) | ForEach-Object { $_.Current.Name }
            if ($visible -match $pattern) { return }
            Start-Sleep -Milliseconds 80
        }
        throw "Missing status [$pattern] among $visible"
    }
    function Send-Request {
        Invoke-Control '发送'
        [void](Wait-Element '响应原文' $edit)
        [void](Wait-Element '发送' $button)
    }
    function Enable-Web {
        Open-Manager
        Invoke-Control '启用 Web API'
        Invoke-Control 'Web API' $menuItem
    }
    function New-Draft {
        Use-Tab '接口定义'
        Invoke-Control '新建临时请求'
        [void](Wait-Element '添加参数' $button)
    }
    function Open-Saved {
        Use-Tab '接口定义'
        (Wait-Element '验收目录' $listItem).GetCurrentPattern($select).Select()
        (Wait-Element '查询订单' $listItem).GetCurrentPattern($select).Select()
        Invoke-Control '打开接口'
        [void](Wait-Element '添加参数' $button)
    }
    function Read-Json { (Read-Field '响应原文') | ConvertFrom-Json }
    $portFile = Join-Path $work.FullName 'echo.port'
    $probe = Join-Path $work.FullName 'unexpected-request'
    $pythonExe = python -c "import sys; print(sys.executable)"
    $start = [Diagnostics.ProcessStartInfo]::new($pythonExe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $arrived = Join-Path $work.FullName 'in-flight'
    foreach ($arg in @("$PSScriptRoot/WebApiEcho.py", $portFile, $probe, $arrived)) { $start.ArgumentList.Add($arg) }
    $server = [Diagnostics.Process]::Start($start)
    try {
        for ($i = 0; $i -lt 50 -and !(Test-Path $portFile); $i++) { Start-Sleep -Milliseconds 100 }
        $base = "http://127.0.0.1:$(Get-Content -Raw $portFile)"
        Enable-Web
        Write-Field 'URL' "$base/lookup"
        Write-Field 'HTTP 方法' 'CHECK'
        Invoke-Control '添加参数'
        Write-Field '参数名' 'id'
        $expander = Wait-Element '参数定义' $button
        $expander.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
        Write-Field '默认值' '1001'
        Write-Field '说明' '订单编号'
        Write-Field '类型' 'integer'
        Use-Tab '接口定义'
        Write-Field '目录' '验收目录'
        Write-Field '接口名称' '查询订单'
        Invoke-Control '保存定义'
        [void](Wait-Element '已保存 · 查询订单' $label)
        Open-Saved
        Assert-Same '1001' (Read-Field '实参')
        Send-Request
        $seen = Read-Json
        Assert-Same 'CHECK' $seen.method
        Assert-Same '1001' $seen.query.id[0]
        Use-Tab '参数'
        Write-Field '实参' '1002'
        Send-Request
        Assert-Same '1002' (Read-Json).query.id[0]
        New-Draft
        Write-Field 'URL' "$base/temp"
        Send-Request
        Assert-Same 'GET' (Read-Json).method
        Use-Tab '请求日志'
        $detail = Read-Field '日志详情'
        if ($detail -notmatch '临时请求' -or $detail -notmatch 'HTTP 200') { throw $detail }
        'PASS independent 1001, 1002 and temporary requests verified by service'

        [void]$appProcess.CloseMainWindow()
        if (!$appProcess.WaitForExit(5000)) { throw 'Desktop did not exit' }
        Start-Desktop
        Enable-Web
        Use-Tab '请求日志'
        $history = Wait-Element '历史记录' ([Windows.Automation.ControlType]::List)
        $children = $history.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)
        $entries = @($children | Where-Object { $_.Current.ControlType -eq $listItem })
        Assert-Same 3 $entries.Count
        foreach ($pair in @(@(1,'1002'), @(2,'1001'))) {
            $entries[$pair[0]].GetCurrentPattern($select).Select()
            if ((Read-Field '日志详情') -notmatch "查询 · id = $($pair[1])") { throw 'Missing persisted actual value' }
        }
        Invoke-Control '清空请求日志'
        Assert-Same '' (Read-Field '日志详情')
        Open-Saved
        Assert-Same '1001' (Read-Field '实参')
        Send-Request
        Assert-Same '1001' (Read-Json).query.id[0]
        'PASS restart, independent history clear and saved default reuse'
        Use-Tab '参数'
        (Wait-Element '参数定义' $button).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
        Write-Field '默认值' '1003'
        Use-Tab '接口定义'
        Invoke-Control '保存定义'
        Open-Saved
        Assert-Same '1003' (Read-Field '实参')
        New-Draft
        Write-Field 'URL' "$base/path/{part}?keep=1"
        Invoke-Control '添加参数'
        Choose '位置' '路径'
        Write-Field '参数名' 'part'
        Write-Field '实参' '中 +'
        Send-Request
        Assert-Same '/path/中 +' (Read-Json).path
        Use-Tab '参数'
        Choose '位置' '请求头'
        Write-Field '参数名' 'X-Test'
        Write-Field '实参' 'sample'
        Write-Field 'URL' "$base/header"
        Send-Request
        Assert-Same 'sample' (Read-Json).header
        'PASS explicit default update, encoded path and custom header'

        New-Draft
        Write-Field 'HTTP 方法' 'POST'
        Write-Field 'URL' "$base/echo"
        Use-Tab '正文'
        Choose '正文类型' '原始文本'
        Write-Field '内容类型' 'application/json'
        Write-Field '请求正文' '{"值":?}'
        Invoke-Control '格式化 JSON'
        Status-Matches 'JSON 第 1 行，第 6 列'
        Assert-Same '{"值":?}' (Read-Field '请求正文')
        Write-Field '请求正文' '{"值":42}'
        Invoke-Control '格式化 JSON'
        $formatted = Read-Field '请求正文'
        if (!$formatted.Contains([char]10)) { throw 'JSON was not formatted' }
        Send-Request
        Assert-Same ([Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($formatted)).ToLowerInvariant()) (Read-Json).data
        $raw = Read-Field '响应原文'
        Invoke-Control '格式化视图'
        if (!(Read-Field '格式化响应').Contains([char]10)) { throw 'Missing formatted JSON' }
        Invoke-Control '原文'
        Assert-Same $raw (Read-Field '响应原文')
        Use-Tab '正文'
        Write-Field '内容类型' 'application/xml'
        Write-Field '请求正文' '<doc/>'
        Send-Request
        Assert-Same '3c646f632f3e' (Read-Json).data

        Use-Tab '参数'
        Invoke-Control '添加参数'
        Write-Field '参数名' 'msg'
        Write-Field '实参' 'a +&'
        Choose '位置' '正文'
        Use-Tab '正文'
        Choose '正文类型' 'URL 编码表单'
        Send-Request
        Assert-Same 'msg=a+%2B%26' ([Text.Encoding]::UTF8.GetString([Convert]::FromHexString((Read-Json).data)))
        $file = Join-Path $work.FullName 'upload.bin'
        [IO.File]::WriteAllBytes($file, [byte[]](0,1,127,255))
        Use-Tab '参数'
        Invoke-Control '添加参数'
        $named = [Windows.Automation.AutomationElement]::NameProperty
        function Last-Input($name) {
            $condition = [Windows.Automation.PropertyCondition]::new($named, $name)
            $controls = $window.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
            $controls[$controls.Count-1]
        }
        (Last-Input '参数名').GetCurrentPattern($valuePattern).SetValue('upload')
        (Last-Input '实参').GetCurrentPattern($valuePattern).SetValue($file)
        $position = Last-Input '位置'
        $position.GetCurrentPattern($expand).Expand()
        (Wait-Element '文件' $listItem).GetCurrentPattern($select).Select()
        $position.GetCurrentPattern($expand).Collapse()
        Use-Tab '正文'
        Choose '正文类型' 'multipart'
        Send-Request
        $parts = (Read-Json).fields
        Assert-Same '61202b26' $parts[0][1]
        Assert-Same '00017fff' $parts[1][1]
        New-Draft
        Write-Field 'URL' "$base/echo"
        Write-Field 'HTTP 方法' 'POST'
        Use-Tab '正文'
        Choose '正文类型' '二进制文件'
        Write-Field '二进制文件路径' $file
        Send-Request
        Assert-Same '00017fff' (Read-Json).data
        'PASS JSON editor, raw XML, URL-encoded fields, multipart and binary byte equality'

        New-Draft
        foreach ($route in 'xml','broken','html') {
            Write-Field 'URL' "$base/$route"
            Send-Request
            $raw = Read-Field '响应原文'
            if ($route -eq 'html') {
                Status-Matches 'HTTP 503.*ms'
                if ((Read-Field '响应头') -notmatch 'X-Echo: local') { throw 'Missing response header' }
                Invoke-Control '静态预览'
                [void](Wait-Element '服务维护中' $label)
                if (Test-Path $probe) { throw 'HTML preview executed script or loaded resources' }
            } else {
                Invoke-Control '格式化视图'
                if ($route -eq 'xml') {
                    if (!(Read-Field '格式化响应').Contains([char]10)) { throw 'Missing formatted XML' }
                } else { Status-Matches 'JSON 第 1 行' }
            }
            Invoke-Control '原文'
            Assert-Same $raw (Read-Field '响应原文')
        }
        Write-Field 'URL' "$base/slow"
        Write-Field '超时秒数' '1'
        Invoke-Control '发送'
        Status-Matches '请求超时.*ms'
        Write-Field '超时秒数' '30'
        Invoke-Control '发送'
        Invoke-Control '取消请求'
        Status-Matches '已取消.*ms'
        $reserved = [Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork, [Net.Sockets.SocketType]::Stream, [Net.Sockets.ProtocolType]::Tcp)
        try {
            $reserved.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
            Write-Field 'URL' "http://$($reserved.LocalEndPoint)/refused"
            Invoke-Control '发送'
            Status-Matches '连接或传输失败.*ms'
        } finally { $reserved.Dispose() }
        'PASS HTTP errors, static HTML without outbound probes, timeout, cancellation and refusal'
        Open-Manager
        Invoke-Control '重新加载 Web API'
        Invoke-Control 'Web API' $menuItem
        Open-Saved
        Assert-Same '1003' (Read-Field '实参')
        Open-Manager
        Invoke-Control '检查卸载'
        [void](Wait-Element '已回收 1 个旧版本。仍待回收：无' $label)
        'PASS reload after editor, preview and HTTP use; retired plugin collected'
        Invoke-Control 'Web API' $menuItem
        New-Draft
        Write-Field 'URL' "$base/shutdown"
        Invoke-Control '发送'
        for ($i = 0; $i -lt 50 -and !(Test-Path $arrived); $i++) { Start-Sleep -Milliseconds 50 }
        if (!(Test-Path $arrived)) { throw 'Service did not receive in-flight request' }
        [void]$appProcess.CloseMainWindow()
        if (!$appProcess.WaitForExit(5000)) { throw 'Desktop did not exit with active request' }
        Start-Desktop
        Enable-Web
        Use-Tab '请求日志'
        $latest = Read-Field '日志详情'
        if ($latest -notmatch '/shutdown' -or $latest -notmatch '已取消') { throw $latest }
        'PASS in-flight cancellation persisted before host exit'
    } finally {
        if (!$server.HasExited) { $server.Kill(); $server.WaitForExit() }
        $server.Dispose()
    }
}
