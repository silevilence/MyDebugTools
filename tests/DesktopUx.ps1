function Initialize-UxCapture {
    $destination = if ($env:MYDEBUGTOOLS_TEST_EVIDENCE) { $env:MYDEBUGTOOLS_TEST_EVIDENCE } else { Join-Path $Publish '../evidence' }
    $script:evidence = [IO.Directory]::CreateDirectory($destination).FullName
    Add-Type -AssemblyName System.Drawing
    if (!('MyDebugTools.Tests.UxWindow' -as [type])) {
        Add-Type -Namespace MyDebugTools.Tests -Name UxWindow -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr wnd,uint message,IntPtr key,IntPtr flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr owner);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr owner);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool CloseClipboard();
'@
    }
    function script:Snapshot($name) {
        $rect = $window.Current.BoundingRectangle
        $bitmap = [Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try {
            if (![MyDebugTools.Tests.UxWindow]::PrintWindow($appProcess.MainWindowHandle,$dc,2)) {throw 'Cannot capture test window'}
        } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        try { $bitmap.Save((Join-Path $evidence "$name.png"),[Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
    }
}
function Check-DesktopUx {
    $value = [Windows.Automation.ValuePattern]::Pattern
    $toggle = [Windows.Automation.TogglePattern]::Pattern
    $expand = [Windows.Automation.ExpandCollapsePattern]::Pattern
    Initialize-UxCapture
    [void](Wait-Element '启用并打开请求' $button)
    Snapshot 'home'
    $dpi = [MyDebugTools.Tests.UxWindow]::GetDpiForWindow($appProcess.MainWindowHandle)
    $transform = $window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
    $transform.Resize(580*$dpi/96,400*$dpi/96)
    [void](Wait-Element '启用并打开依赖' $button)
    Snapshot 'home-narrow'
    Invoke-Control '调试目标' $menuItem
    Invoke-Control '选择目标进程'
    [void](Wait-Element '请输入大于 0 的进程 ID。' $label)
    (Wait-Element '诊断详情' $button).GetCurrentPattern($toggle).Toggle()
    $first = (Wait-Element '诊断摘要' $edit).GetCurrentPattern($value).Current.Value
    Invoke-Control '复制诊断信息'
    $actual = Get-Clipboard -Raw
    if ($actual -cne $first) {throw "Clipboard mismatch. UI feedback: $(($window.FindAll(4,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {$_.Current.Name}) -join ' / ')"}
    (Wait-Element '进程 ID' $edit).GetCurrentPattern($value).SetValue('abc')
    Invoke-Control '选择目标进程'
    $second = (Wait-Element '诊断摘要' $edit).GetCurrentPattern($value).Current.Value
    if ($first -eq $second -or !$second.Contains('PID abc')) {throw 'Stale diagnostic'}
    if (![MyDebugTools.Tests.UxWindow]::OpenClipboard([IntPtr]::Zero)) {throw 'Could not reserve clipboard for failure check'}
    try {
        Invoke-Control '复制诊断信息'
        $fallback = (Wait-Element '诊断摘要' $edit).GetCurrentPattern($value).Current.Value
        if ($fallback -cne $second) {throw 'Fallback text differs'}
    } finally { [void][MyDebugTools.Tests.UxWindow]::CloseClipboard() }
    Invoke-Control '复制诊断信息'
    if ((Get-Clipboard -Raw) -cne $second) {throw 'Clipboard did not recover'}
    Snapshot 'target-error'
    'PASS target correction, clipboard equality, failure fallback and recovery'
    $transform.Resize(860*$dpi/96,580*$dpi/96)
    foreach ($arch in 'x86','x64') {
        $file = (Resolve-Path "$DllFixtures/$arch/fixture.dll").Path
        (Wait-Element '目标 DLL 路径' $edit).GetCurrentPattern($value).SetValue($file)
        Invoke-Control '选择目标 DLL'
        [void](Wait-Element "当前目标 · fixture.dll · 目标 $($arch.ToUpper()) · 宿主 X64" $label)
        Invoke-Control '在 DLL 工作台打开'
        $opened = (Wait-Element 'DLL 文件路径' $edit).GetCurrentPattern($value).Current.Value
        if ($opened -ne $file) {throw 'DLL context was not transferred'}
        Invoke-Control '调试目标' $menuItem
    }
    Open-Manager
    Snapshot 'plugin-table'
    (Wait-Element '查找插件' $edit).GetCurrentPattern($value).SetValue('dll.dependencies')
    for ($i = 0; $i -lt 30; $i++) {
        $reference = $window.FindAll(4,[Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq $button -and $_.Current.Name -like 'DLL 依赖 · *' } | Select-Object -First 1
        if ($reference) { break }
        Start-Sleep -Milliseconds 100
    }
    if (!$reference) { throw 'Filtered extension does not show its provider and page' }
    $reference.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-Element '共 9 个插件' $label)
    Invoke-Control '首页' $menuItem
    Invoke-Control '启用并打开请求'
    (Wait-Element 'URL' $edit).GetCurrentPattern($value).SetValue('http://localhost/draft')
    Invoke-Control 'HTTP 响应' $menuItem
    function Layout-Command($title) {
        (Wait-Element '视图' $menuItem).GetCurrentPattern($expand).Expand()
        Invoke-Control $title $menuItem
    }
    foreach ($side in '向左停靠','向上停靠','向下停靠') {
        Layout-Command $side
        [void](Wait-Element '尚未发送请求。' $label)
        [void](Wait-Element 'URL' $edit)
        Layout-Command '合并标签'
    }
    Layout-Command '向右停靠'
    [void](Wait-Element '尚未发送请求。' $label)
    [void](Wait-Element 'URL' $edit)
    Snapshot 'request-and-response'
    Layout-Command '隐藏'
    Invoke-Control 'HTTP 响应' $menuItem
    [void](Wait-Element '尚未发送请求。' $label)
    Layout-Command '合并标签'
    Invoke-Control 'Web API' $menuItem
    if ((Wait-Element 'URL' $edit).GetCurrentPattern($value).Current.Value -ne 'http://localhost/draft') {throw 'Lost draft after docking'}
    Layout-Command '恢复默认布局'
    Invoke-Control 'Web API' $menuItem
    if ((Wait-Element 'URL' $edit).GetCurrentPattern($value).Current.Value -ne 'http://localhost/draft') {throw 'Default layout cleared an open draft'}
    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$appProcess.WaitForExit(5000)) {throw 'Layout save timed out'}
    $reason = $appProcess.StandardError.ReadToEnd()
    if ($reason) { Write-Output $reason }
    $saved = Get-Content -Raw (Join-Path $work.FullName 'MyDebugTools/layout.json') | ConvertFrom-Json
    $layoutText = $saved.Layout | ConvertTo-Json -Depth 40
    if ($layoutText.Contains('http://localhost/draft') -or !$layoutText.Contains('web.api')) { throw 'Invalid persisted layout' }
    Start-Desktop
    [void](Wait-Element 'Web API' $menuItem)
    Check-FloatingUx
    'PASS DPI ' + $dpi + ', target handoff, docking, draft continuity and layout restart'
}

function Check-FloatingUx {
    function Layout-Command($title) {
        (Wait-Element '视图' $menuItem).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Invoke-Control $title $menuItem
    }
    # Move the supplied external F# page to a real floating window and replace its files twice.
    $sample = Get-SampleBuild 1
    $secondBuild = Get-SampleBuild 2
    Open-Manager
    if (Find-Element '启用 Web API' $button) { Invoke-Control '启用 Web API' }
    Invoke-Control 'Web API' $menuItem
    $draft = 'http://localhost/keep-during-reload'
    (Wait-Element 'URL' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($draft)
    Invoke-Control 'HTTP 响应' $menuItem
    Layout-Command '向右停靠'
    Invoke-Control '调试目标' $menuItem
    Layout-Command '隐藏'
    $tab = [Windows.Automation.ControlType]::TabItem
    $development = Join-Path $work.FullName 'floating-sample'
    Copy-Item -LiteralPath $sample -Destination $development -Recurse
    Open-Manager
    Add-Directory $development
    Invoke-Control '启用 第三方工具'
    Invoke-Control '第三方 A' $menuItem
    Layout-Command '浮动'
    function Floating-Content($text) {
        $byOwner = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$appProcess.Id)
        $byText = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$text)
        for ($i = 0; $i -lt 40; $i++) {
            foreach ($top in [Windows.Automation.AutomationElement]::RootElement.FindAll(2,$byOwner)) {
                if ($top.Current.NativeWindowHandle -ne $window.Current.NativeWindowHandle -and $top.FindFirst(4,$byText)) { return $top }
            }
            Start-Sleep -Milliseconds 100
        }
        $visible = foreach ($top in [Windows.Automation.AutomationElement]::RootElement.FindAll(2,$byOwner)) {
            "$($top.Current.Name): " + (($top.FindAll(4,[Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name}) -join '|')
        }
        throw "Missing floating page: $text. $visible"
    }
    [void](Floating-Content '第三方 A v1')
    foreach ($pair in @(@($secondBuild,'2'),@($sample,'1'))) {
        Open-Manager
        Copy-Item -Path "$($pair[0])/*.dll" -Destination $development
        Invoke-Control '重新加载 第三方工具'
        [void](Floating-Content "第三方 A v$($pair[1])")
        Invoke-Control '检查卸载'
        [void](Wait-Element '已回收 1 个旧版本。仍待回收：无' $label)
    }
    Invoke-Control 'Web API' $menuItem
    if ((Wait-Element 'URL' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $draft) { throw 'Floating plugin reload cleared the independent draft' }
    $boundary = (Wait-Element 'HTTP 响应' $tab).Current.BoundingRectangle.Left - $window.Current.BoundingRectangle.Left
    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$appProcess.WaitForExit(5000)) {throw 'Floating window kept the desktop running'}
    $file = Join-Path $work.FullName 'MyDebugTools/layout.json'
    $saved = Get-Content -Raw $file | ConvertFrom-Json
    if ($saved.Windows.Count -ne 1) {throw 'Expected one persisted floating window'}
    $saved.Windows[0].X = -90000
    $saved.Windows[0].Y = -90000
    $saved | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $file
    Start-Desktop
    $returned = Floating-Content '第三方 A v1'
    $restoredBoundary = (Wait-Element 'HTTP 响应' $tab).Current.BoundingRectangle.Left - $window.Current.BoundingRectangle.Left
    if ([Math]::Abs($restoredBoundary-$boundary) -gt 6 -or (Find-Element '调试目标' $tab)) { throw 'Split proportion or hidden state changed on restart' }
    if ($returned.Current.BoundingRectangle.Right -le 0 -or $returned.Current.BoundingRectangle.Bottom -le 0) {throw 'Floating window outside the display'}
    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$appProcess.WaitForExit(5000)) {throw 'Restored layout did not close'}
    $entry = Join-Path $development 'ThirdPartyTools.dll'
    $unavailable = Join-Path $development 'ThirdPartyTools.unavailable'
    Move-Item -LiteralPath $entry -Destination $unavailable -ErrorAction Stop
    try {
        Start-Desktop
        Open-Manager
        [void](Wait-Element '启用 第三方工具' $button)
    } finally { Move-Item -LiteralPath $unavailable -Destination $entry -ErrorAction Stop }
    Invoke-Control '启用 第三方工具'
    [void](Floating-Content '第三方 A v1')
    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$appProcess.WaitForExit(5000)) {throw 'Reactivated floating page did not close'}
    Set-Content -LiteralPath $file -Value 'broken'
    Start-Desktop
    [void](Wait-Element '启用并打开请求' $button)
    'PASS two floating plugin replacements, collectible assemblies, off-screen recovery and damaged layout fallback'
}

function Check-Appearance {
    Initialize-UxCapture
    [void](Wait-Element '启用并打开请求' $button)
    $dpi = [MyDebugTools.Tests.UxWindow]::GetDpiForWindow($appProcess.MainWindowHandle)
    "Published window DPI $dpi"
    Snapshot 'home'
    $transform = $window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
    $transform.Resize(580*$dpi/96,400*$dpi/96)
    [void](Wait-Element '启用并打开监控' $button)
    Snapshot 'narrow'
    Invoke-Control '调试目标' $menuItem
    $inputBox = Wait-Element '进程 ID' $edit
    [void][MyDebugTools.Tests.UxWindow]::SetForegroundWindow($appProcess.MainWindowHandle)
    $inputBox.SetFocus()
    # Keyboard input is confined to the sandbox session.
    if ($env:USERNAME -eq 'WDAGUtilityAccount') {
        Add-Type -AssemblyName System.Windows.Forms
        $handle = $appProcess.MainWindowHandle
        foreach ($ch in 'abc'.ToCharArray()) { [void][MyDebugTools.Tests.UxWindow]::SendMessageW($handle,0x102,[int]$ch,1) }
        [void][MyDebugTools.Tests.UxWindow]::SendMessageW($handle,0x100,13,1)
        [void][MyDebugTools.Tests.UxWindow]::SendMessageW($handle,0x101,13,1)
        "Focused field text: " + $inputBox.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        [void](Wait-Element '进程 ID 须为大于 0 的整数。请修改后重试。' $label)
        Snapshot 'keyboard-error'
        [void][MyDebugTools.Tests.UxWindow]::SendMessageW($handle,0x100,9,1)
        [void][MyDebugTools.Tests.UxWindow]::SendMessageW($handle,0x101,9,1)
        Invoke-Control '首页' $menuItem
        [void](Wait-Element '启用并打开依赖' $button)
    }
    $transform.Resize(860*$dpi/96,580*$dpi/96)
    Open-Manager
    Snapshot 'table'
}
