# Runs inside the isolated desktop fixture with its own writable system clipboard.
function Check-ToolReports {
    $value = [Windows.Automation.ValuePattern]::Pattern
    $select = [Windows.Automation.SelectionItemPattern]::Pattern
    $expand = [Windows.Automation.ExpandCollapsePattern]::Pattern
    $combo = [Windows.Automation.ControlType]::ComboBox
    $tab = [Windows.Automation.ControlType]::TabItem
    function Fill($name,$text) { (Wait-Element $name $edit).GetCurrentPattern($value).SetValue($text) }
    function Choose($name,$option) {
        $box = (Wait-Element $name $combo).GetCurrentPattern($expand)
        $box.Expand()
        (Wait-Element $option ([Windows.Automation.ControlType]::ListItem)).GetCurrentPattern($select).Select()
        $box.Collapse()
    }
    function Report($page,$action,$reason) {
        Invoke-Control '复制诊断信息'
        [void](Wait-Element '已复制诊断信息' $label)
        $text = Get-Clipboard -Raw
        foreach ($term in ('MyDebugTools','时间',$page,$action,$reason)) {
            if (!$text.Contains($term)) { throw "Missing $term in $text" }
        }
        if ($text -match 'SYNTHETIC-SECRET|MY-PRIVATE-FOLDER') { throw 'Leaked diagnostic input' }
        "PASS report $page / $action"
    }
    Open-Manager
    Add-Directory (Join-Path $work.FullName 'MY-PRIVATE-FOLDER/absent')
    Report '插件管理' '关联开发目录' 'plugin.json'
    Invoke-Control '调试目标' $menuItem
    Fill '目标 DLL 路径' (Join-Path $work.FullName 'MY-PRIVATE-FOLDER/absent.dll')
    Invoke-Control '选择目标 DLL'
    Report '调试目标' '选择文件' 'absent.dll'
    Fill '进程 ID' '2147483647'
    Invoke-Control '选择目标进程'
    Report '调试目标' '选择进程' '2147483647'
    if ($env:USERNAME -eq 'WDAGUtilityAccount') {
        $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry32)
        $key = $registry.CreateSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x86')
        $previous = $key.GetValue('InstallLocation')
        try {
            $key.SetValue('InstallLocation',(Join-Path $work.FullName 'missing-runtime'))
            Fill '目标 DLL 路径' (Resolve-Path "$DllFixtures/x86/fixture.dll").Path
            Invoke-Control '选择目标 DLL'
            [void](Wait-Element '执行需要 .NET 10 X86 运行时。可以继续静态浏览。' $label)
            Invoke-Control '在 DLL 工作台打开'
            [void](Wait-Element '函数入口' $edit)
            'PASS missing x86 runtime leaves static inspection available'
        } finally {
            if ($null -eq $previous) {$key.DeleteValue('InstallLocation')} else {$key.SetValue('InstallLocation',$previous)}
            $key.Dispose(); $registry.Dispose()
        }
    }
    $native = (Resolve-Path "$DllFixtures/x64/fixture.dll").Path
    Invoke-Control '首页' $menuItem
    if (Find-Element '打开导出' $button) { Invoke-Control '打开导出' } else { Invoke-Control '启用并打开导出' }
    Fill 'DLL 文件路径' $native
    Invoke-Control '打开 DLL'
    Fill '函数入口' 'add'
    Invoke-Control '准备函数调用'
    $convention = '调用约定（cdecl / stdcall / winapi）'
    Fill $convention 'modified-draft'
    Fill '函数入口' 'adjust'
    Fill '函数入口' 'add'
    if ((Wait-Element $convention $edit).GetCurrentPattern($value).Current.Value -ne 'modified-draft') {throw 'Lost signature draft'}
    Invoke-Control '确认并保存签名'
    Report 'DLL 工作台' '核对签名' '调用约定'
    Invoke-Control '首页' $menuItem
    Invoke-Control '启用并打开依赖'
    Fill 'DLL 文件路径' (Resolve-Path "$DllFixtures/dependencies/Root.dll").Path
    Invoke-Control '打开 DLL'
    $missing = Wait-Element 'Choice.dll · 普通导入 · 缺失 · 搜索目录中没有此文件' ([Windows.Automation.ControlType]::TreeItem)
    $missing.GetCurrentPattern($select).Select()
    Report 'DLL 依赖' '解析导入' 'Choice.dll'
    Invoke-Control '复制引用链'
    if ((Get-Clipboard -Raw) -notmatch 'Root.dll → Choice.dll') { throw 'Copied reference chain differs' }
    Choose '依赖视图' '依赖图'
    [void](Wait-Element '缺失 · 搜索目录中没有此文件' $label)
    Invoke-Control '首页' $menuItem
    Invoke-Control '启用并打开监控'
    Fill '监控 DLL 路径' $native
    Invoke-Control '读取 DLL 导出'
    Fill '搜索 SDK 函数' 'fixture.dll!add'
    Invoke-Control '加入监控'
    Choose '监控目标模式' '附加进程'
    Fill '目标进程 ID' 'abc'
    Invoke-Control '附加并监控'
    Report 'API 监控' '附加采集' '进程 ID'
    Invoke-Control '首页' $menuItem
    Invoke-Control '启用并打开请求'
    (Wait-Element '正文' $tab).GetCurrentPattern($select).Select()
    Choose '正文类型' '原始文本'
    Fill '内容类型' 'application/json'
    Fill '请求正文' '{"SYNTHETIC-SECRET":?}'
    Invoke-Control '格式化 JSON'
    Report 'Web API 正文' '格式化 JSON' 'JSON 语法错误'
    Fill 'URL' 'http://127.0.0.1:1/check?token=SYNTHETIC-SECRET'
    (Wait-Element '参数' $tab).GetCurrentPattern($select).Select()
    Invoke-Control '发送'
    Report 'Web API' '发送请求' '127.0.0.1:1'
    $port = Get-Content -LiteralPath $EchoPortFile
    Fill 'URL' "http://127.0.0.1:$port/slow"
    Invoke-Control '发送'
    (Wait-Element '请求日志' $tab).GetCurrentPattern($select).Select()
    $items = Wait-Element '历史记录' ([Windows.Automation.ControlType]::List)
    $details = Wait-Element '日志详情' $edit
    $details.SetFocus()
    $oldRecord = $details.GetCurrentPattern($value).Current.Value
    [void](Wait-Element '发送' $button)
    if (!(Wait-Element '请求日志' $tab).GetCurrentPattern($select).Current.IsSelected) {throw 'Delayed response interrupted reading'}
    if (!$details.Current.HasKeyboardFocus -or $oldRecord -cne $details.GetCurrentPattern($value).Current.Value) {throw 'Delayed response changed the focused record'}
    'PASS delayed response preserved the selected history, detail and keyboard focus'
}
