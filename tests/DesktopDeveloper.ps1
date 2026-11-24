function Check-DeveloperMcp {
    foreach ($dll in 'Microsoft.Extensions.DependencyInjection.Abstractions','Microsoft.Extensions.Logging.Abstractions','Microsoft.Extensions.AI.Abstractions','ModelContextProtocol.Core') {
        [void][Reflection.Assembly]::LoadFrom((Resolve-Path "$Publish/$dll.dll").Path)
    }
    $ct = [Threading.CancellationToken]::None
    $checkbox = [Windows.Automation.ControlType]::CheckBox
    function Connect-Client($config) {
        $entry = ($config | ConvertFrom-Json).mcpServers.MyDebugTools
        $options = [ModelContextProtocol.Client.HttpClientTransportOptions]::new()
        $options.Endpoint = [Uri]$entry.url
        $options.TransportMode = 'StreamableHttp'
        $headers = [Collections.Generic.Dictionary[string,string]]::new()
        $headers.Add('Authorization', $entry.headers.Authorization)
        $options.AdditionalHeaders = $headers
        $transport = [ModelContextProtocol.Client.HttpClientTransport]::new($options, [Net.Http.HttpClient]::new(), $null, $true)
        $protocol = [ModelContextProtocol.Client.McpClientOptions]::new()
        $protocol.ProtocolVersion = '2025-11-25'
        [ModelContextProtocol.Client.McpClient]::CreateAsync($transport,$protocol,$null,$ct).GetAwaiter().GetResult()
    }
    function Send-Tool($name,$arguments) {
        $values = [Collections.Generic.Dictionary[string,object]]::new()
        foreach ($key in $arguments.Keys) { $values.Add($key,$arguments[$key]) }
        $client.CallToolAsync($name,$values,$null,$null,$ct).AsTask()
    }
    function Result($pending) {
        $answer = $pending.WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult()
        if ($answer.IsError) { throw $answer.Content[0].Text }
        $answer.Content[0].Text
    }
    function Choose($title,$expected,$choice) {
        $byTitle = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$title)
        $owner = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$appProcess.Id)
        for ($i=0; $i -lt 80; $i++) {
            $dialog = [Windows.Automation.AutomationElement]::RootElement.FindFirst(2,[Windows.Automation.AndCondition]::new($owner,$byTitle))
            if (!$dialog) { $dialog = $window.FindFirst(4,$byTitle) }
            if ($dialog) { break }
            Start-Sleep -Milliseconds 100
        }
        if (!$dialog) { throw "Missing dialog $title" }
        $window = $dialog
        $body = ($dialog.FindAll(4,[Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name}) -join ' '
        if (!$body.Contains($expected)) { throw "Missing $expected in confirmation" }
        Invoke-Control $choice
    }
    function Install($choice = '确认加载此快照') {
        $pending = Send-Tool 'install_temporary' @{directory=$thirdPartyDir}
        Choose '确认加载临时插件' $thirdPartyDir $choice
        Result $pending
    }
    function Check-ActionAndDraft($revision) {
        Open-Workbench
        $draft = (Wait-Element '' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($draft -ne '保留这份草稿') { throw 'Workbench draft lost' }
        Open-Actions
        $name = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'第三方 A')
        $actions = (Wait-Element '页面操作' $menuItem).FindAll(4,$name)
        if ($actions.Count -ne 1) { throw 'Expected one action' }
        $actions[0].GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        [void](Wait-Element "A v$revision · 请求 A" $label)
    }
    function Restart {
        $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
        if (!$appProcess.WaitForExit(5000)) { throw 'Desktop did not close' }
        Start-Desktop
        Open-Manager
    }
    function Check-State($state) {
        $json = Result (Send-Tool 'list_plugins' @{})
        $rows = @($json | ConvertFrom-Json | Where-Object id -eq 'thirdparty.tools')
        if ($rows.Count -ne 1 -or $rows[0].source -ne 'formal' -or $rows[0].state -ne $state) { throw "Unexpected restored plugin state $json" }
    }
    Open-Manager
    $toggle = (Wait-Element '插件开发模式' $checkbox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne 'Off') { throw 'Expected default off' }
    $toggle.Toggle()
    Invoke-Control '复制 MCP 连接配置'
    [void](Wait-Element '已复制本机 MCP 连接配置' $label)
    $config = Get-Clipboard -Raw
    $client = Connect-Client $config
    try {
        $doc = Result (Send-Tool 'read_documentation' @{})
        if (!$doc.Contains('type Registration')) { throw 'Missing public contract' }
        Invoke-Control '启用 工作台'
        [void](Wait-Element '工作台 · 已启用' $label)
        Open-Workbench
        Set-Text '保留这份草稿'
        Open-Manager
        $formal = Get-SampleBuild 1
        Add-Directory $formal
        Invoke-Control '启用 第三方工具'
        [void](Wait-Element '第三方工具 · 已启用' $label)
        [void][IO.Directory]::CreateDirectory($thirdPartyDir)
        foreach ($revision in 2,1,2) {
            Copy-Item -Path "$(Get-SampleBuild $revision)/*" -Destination $thirdPartyDir -Recurse -Force
            if ($revision -eq 2 -and !$once) {
                $once = $true
                if ((Install '取消') -notmatch '取消') { throw 'Consent cancellation lost' }
            }
            [void](Install)
            Check-ActionAndDraft $revision
        }
        Open-Manager
        Invoke-Control '检查卸载'
        [void](Wait-Element '已回收 3 个旧版本。仍待回收：无' $label)
        $rows = Result (Send-Tool 'list_plugins' @{}) | ConvertFrom-Json
        if (@($rows | Where-Object id -eq 'thirdparty.tools').Count -ne 2) { throw 'Missing formal or temporary source' }
        Restart
        Check-State active
        Open-Workbench
        Check-Action '第三方 A' 'A v1 · 请求 A'
        Open-Manager
        Invoke-Control '停用 第三方工具'
        [void](Wait-Element '第三方工具 · 已停用' $label)
        [void](Install)
        # Abnormal exit must restore the formal preference saved before takeover.
        $appProcess.Kill()
        $appProcess.WaitForExit()
        Start-Desktop
        Open-Manager
        Check-State disabled
        $snapshots = Join-Path $work.FullName 'MyDebugTools/developer-snapshots'
        if (@(Get-ChildItem -LiteralPath $snapshots).Count) { throw 'Abandoned temporary snapshot survived restart' }
        [void](Install)
        [void](Result (Send-Tool 'uninstall_temporary' @{id='thirdparty.tools'}))
        Check-State disabled
        # Reuse the real F# test plugin binary built by the Release suite.
        $capture = Join-Path $work.FullName 'capture'
        [void][IO.Directory]::CreateDirectory($capture)
        Copy-Item -LiteralPath "$PSScriptRoot/Host.Tests/bin/Release/net10.0/Host.Tests.dll" -Destination $capture
        $manifest = @{Id='capture.ticks'; Title='计时采样'; Assembly='Host.Tests.dll'; EntryType='MyDebugTools.Tests.Temporary+TickCapture'}
        $manifest | ConvertTo-Json | Set-Content -LiteralPath "$capture/plugin.json"
        $pending = Send-Tool 'install_temporary' @{directory=$capture}
        Choose '确认加载临时插件' $capture '确认加载此快照'
        [void](Result $pending)
        foreach ($choice in '继续监控','结束采集并切换') {
            $pending = Send-Tool 'uninstall_temporary' @{id='capture.ticks'}
            Choose '切换插件' '采样计时器' $choice
            $receipt = Result $pending
            $registered = @(Result (Send-Tool 'list_plugins' @{}) | ConvertFrom-Json | Where-Object id -eq 'capture.ticks')
            if ($choice -eq '继续监控' -and ($receipt -notmatch '取消' -or $registered.Count -ne 1)) { throw 'Capture did not continue' }
            if ($choice -ne '继续监控' -and ($receipt -notmatch '已卸载' -or $registered.Count -ne 0)) { throw 'Capture did not stop' }
        }
        Invoke-Control '启用 DLL 工作台'
        [void](Wait-Element 'DLL 工作台 · 已启用' $label)
        $dllPage = Result (Send-Tool 'get_interface' @{id='dll.debugger'}) | ConvertFrom-Json
        if ($dllPage.points.Count -ne 1 -or $dllPage.points[0].kind -ne 'dllTab') { throw 'DLL tab declaration missing' }
        $points = Result (Send-Tool 'get_interface' @{id='sample.workbench'}) | ConvertFrom-Json
        if ($points.points.Count -ne 2) { throw 'Workbench menu declarations missing' }
        (Wait-Element '插件开发模式' $checkbox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
        [void](Wait-Element '本机 MCP 服务已关闭' $label)
        'PASS copied configuration, SDK connection, consent, two replacements, menu, draft, collection, graceful and abnormal restart, capture decisions and DLL tab declaration'
    } finally {
        $client.DisposeAsync().AsTask().GetAwaiter().GetResult()
    }
}
