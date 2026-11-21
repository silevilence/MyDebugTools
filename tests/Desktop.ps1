# Run from the repository root with PowerShell 7 after publishing src/Desktop.
[CmdletBinding()]
param([string]$Publish = 'artifacts/publish')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$exe = (Resolve-Path (Join-Path $Publish 'MyDebugTools.exe')).Path
$work = [IO.Directory]::CreateTempSubdirectory('MyDebugTools-desktop-')
$companyDir = Join-Path $work.FullName 'CompanyTools'
$providerDir = Join-Path $work.FullName 'Workbench'

function Build-Sample($sample, $dir, $property) {
    dotnet build "samples/$sample" --no-restore -o $dir $property | Out-Host
    if ($LASTEXITCODE) { throw "Build failed for $sample" }
}

function Find-Element($name, $type) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    if ($name) {
        $byName = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name)
        $condition = [Windows.Automation.AndCondition]::new($condition, $byName)
    }
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-Element($name, $type) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $element = Find-Element $name $type
        if ($element -and $element.Current.IsEnabled) { return $element }
        Start-Sleep -Milliseconds 100
    }
    $names = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { "$($_.Current.ControlType.ProgrammaticName) $($_.Current.Name)" }
    throw "Missing [$name]. Visible controls: $($names -join ', ')"
}

$button = [Windows.Automation.ControlType]::Button
$menuItem = [Windows.Automation.ControlType]::MenuItem
$edit = [Windows.Automation.ControlType]::Edit
$label = [Windows.Automation.ControlType]::Text

function Invoke-Control($name, $type = $button) {
    $element = Wait-Element $name $type
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-Text($value) {
    (Wait-Element '' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Add-Directory($dir) {
    Set-Text $dir
    Invoke-Control '添加目录'
}

function Open-Actions {
    (Wait-Element '页面操作' $menuItem).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
}

function Check-Action($name, $expected) {
    Open-Actions
    $group = Wait-Element '页面操作' $menuItem
    $byName = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name)
    $action = $group.FindFirst([Windows.Automation.TreeScope]::Descendants, $byName)
    if (!$action) { throw "Missing action $name" }
    $action.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-Element $expected $label)
}

function Open-Manager {
    Invoke-Control '插件管理' $menuItem
    [void](Wait-Element '添加目录' $button)
}
function Open-Workbench {
    Invoke-Control '工作台' $menuItem
    [void](Wait-Element '草稿' $label)
}

try {
    Build-Sample CompanyTools $companyDir '-p:SampleRevision=1'
    Build-Sample Workbench $providerDir '-p:SummaryVersion=1'
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['LOCALAPPDATA'] = $work.FullName
    $appProcess = [Diagnostics.Process]::Start($start)
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($appProcess.HasExited) { throw "App exited: $($appProcess.ExitCode)" }
        $appProcess.Refresh()
        $hwnd = $appProcess.MainWindowHandle
        if ($hwnd -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    $window = [Windows.Automation.AutomationElement]::FromHandle($hwnd)
    for ($round = 1; $round -le 2; $round++) {
        Invoke-Control '启用 工作台'
        Open-Workbench
        Open-Manager
        Invoke-Control '停用 工作台'
        [void](Wait-Element '启用 工作台' $button)
        if (Find-Element '工作台' $menuItem) { throw 'Disabled page still in menu' }
    }
    'PASS two desktop activation cycles'
    Invoke-Control '启用 工作台扩展'
    Add-Directory $providerDir
    Invoke-Control '启用 本地工作台'
    [void](Wait-Element '停用 本地工作台' $button)
    Add-Directory $companyDir
    Invoke-Control '启用 公司工具'
    [void](Wait-Element '停用 公司工具' $button)
    Open-Workbench
    Set-Text '尚未发送的草稿'
    $combo = Wait-Element '' ([Windows.Automation.ControlType]::ComboBox)
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Wait-Element '请求 B' ([Windows.Automation.ControlType]::ListItem)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Check-Action '公司 A' 'A v1 · 请求 B'
    Open-Manager
    Build-Sample CompanyTools $companyDir '-p:SampleRevision=2'
    Invoke-Control '重新加载 公司工具'
    [void](Wait-Element '停用 公司工具' $button)
    Open-Workbench
    $draft = (Wait-Element '' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($draft -ne '尚未发送的草稿') { throw 'Draft lost' }
    Check-Action '公司 A' 'A v2 · 请求 B'
    'PASS private dependency update, current selection and preserved draft'
    Open-Manager
    Invoke-Control '检查卸载'
    [void](Wait-Element '已回收 1 个旧版本。仍待回收：无' $label)
    Build-Sample Workbench $providerDir '-p:SummaryVersion=2'
    Invoke-Control '重新加载 本地工作台'
    [void](Wait-Element '停用 本地工作台' $button)
    [void](Wait-Element '显示摘要 · sample.workbench/summary：要求版本 1，实际版本 2' $label)
    Open-Workbench
    Open-Actions
    if (Find-Element '显示摘要' $menuItem) { throw 'Incompatible action still mounted' }
    Invoke-Control '查看选中项' $menuItem
    [void](Wait-Element '当前选中：请求 A' $label)
    Open-Manager
    Build-Sample Workbench $providerDir '-p:SummaryVersion=1'
    Invoke-Control '重新加载 本地工作台'
    [void](Wait-Element '停用 本地工作台' $button)
    Open-Workbench
    Check-Action '显示摘要' '摘要：请求 A'
    Open-Manager
    Invoke-Control '检查卸载'
    [void](Wait-Element '已回收 2 个旧版本。仍待回收：无' $label)
    'PASS version mismatch, compatible action, restoration and unload diagnostics'
    Invoke-Control '调试目标' $menuItem
    Set-Text ([string]$appProcess.Id)
    Invoke-Control '选择目标进程'
    [void](Wait-Element "目标 x64 · $env:ProgramFiles\dotnet\dotnet.exe" $label)
    'PASS x64 process routing'
}
finally {
    if ($appProcess -and !$appProcess.HasExited) {
        [void]$appProcess.CloseMainWindow()
        if (!$appProcess.WaitForExit(4000)) { $appProcess.Kill(); $appProcess.WaitForExit() }
    }
    $work.Delete($true)
}
