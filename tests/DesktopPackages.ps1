function Check-DesktopPackages {
    function Choose-Dialog($title, $choice, $expected) {
        $byPid = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
        $byTitle = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $title)
        $condition = [Windows.Automation.AndCondition]::new($byPid, $byTitle)
        for ($i = 0; $i -lt 60; $i++) {
            $dialog = [Windows.Automation.AutomationElement]::RootElement.FindFirst(2,$condition)
            if (!$dialog) { $dialog = $window.FindFirst(4,$condition) }
            if ($dialog) { break }
            Start-Sleep -Milliseconds 100
        }
        if (!$dialog) { throw "Missing modal $title" }
        $window = $dialog
        $body = $window.FindAll(4,[Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
        if (($body -join ' ') -notmatch $expected) { throw "Unexpected preview: $body" }
        Invoke-Control $choice
    }
    function Select-Package($path) {
        Invoke-Control '加载插件包…'
        Complete-NativeFileDialog $path
    }
    function Verify-Revision($revision) {
        Open-Workbench
        Check-Action '第三方 A' "A v$revision · 请求 A"
        Open-Manager
    }
    $packages = foreach ($revision in 1,2) {
        $output = Join-Path $work.FullName "package-$revision.zip"
        [IO.Compression.ZipFile]::CreateFromDirectory((Get-SampleBuild $revision), $output)
        $output
    }
    $hash = (Get-FileHash $packages[0]).Hash
    Open-Manager
    Invoke-Control '启用 工作台'
    [void](Wait-Element '工作台 · 已启用' $label)
    Select-Package $packages[0]
    Choose-Dialog '插件包预览' '加载并启用' 'thirdparty.tools'
    [void](Wait-Element '已启用 · 第三方工具' $label)
    Verify-Revision 1
    Select-Package $packages[0]
    [void](Wait-Element '此包已安装。' $label)
    Select-Package $packages[1]
    Choose-Dialog '插件包预览' '取消' '外部 ZIP'
    Verify-Revision 1
    Select-Package $packages[1]
    Choose-Dialog '插件包预览' '替换并启用' '外部 ZIP'
    [void](Wait-Element '已启用 · 第三方工具' $label)
    Verify-Revision 2
    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$appProcess.WaitForExit(5000)) { throw 'Package restart timed out' }
    Start-Desktop
    Verify-Revision 2
    Invoke-Control '卸载插件…'
    Choose-Dialog '卸载插件' '取消' '清理受管副本'
    Verify-Revision 2
    Invoke-Control '卸载插件…'
    Choose-Dialog '卸载插件' '确认卸载插件' 'thirdparty.tools'
    [void](Wait-Element '已卸载插件 · 第三方工具' $label)
    if (Find-Element '第三方 A' $menuItem) { throw 'Removed plugin menu remains' }
    if ((Get-FileHash $packages[0]).Hash -ne $hash) { throw 'Original ZIP changed' }
    'PASS actual ZIP picker, duplicate detection, cancelled replacement, v2 action after restart, cancelled and confirmed removal'
}
