# Run from the repository root with PowerShell 7 after publishing src/Desktop.
[CmdletBinding()]
param([string]$Publish = 'artifacts/publish', [string]$DllFixtures = 'tests/DllDebugger.Tests/bin/Release/net10.0',
      [switch]$DllOnly, [switch]$MonitorOnly, [string]$PreviousMonitorPlugin, [switch]$DependenciesOnly, [switch]$WebApiOnly)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (!('MyDebugTools.Tests.SaveDialog' -as [type])) {
    Add-Type -Namespace MyDebugTools.Tests -Name SaveDialog -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr window, uint message, System.IntPtr wParam, string text);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr window, uint message, System.IntPtr wParam, System.IntPtr lParam);
'@
}
$exe = (Resolve-Path (Join-Path $Publish 'MyDebugTools.exe')).Path
$work = [IO.Directory]::CreateTempSubdirectory('MyDebugTools-desktop-')
$thirdPartyDir = Join-Path $work.FullName 'ThirdPartyTools'
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
        try {
            $element = Find-Element $name $type
            if ($element -and $element.Current.IsEnabled) { return $element }
        } catch [System.Runtime.InteropServices.COMException], [Windows.Automation.ElementNotAvailableException] {
            if ($attempt -eq 29) { throw }
        }
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

function Check-DllWorkbench {
    Open-Manager
    Invoke-Control '启用 DLL 工作台'
    Invoke-Control 'DLL 工作台' $menuItem
    $target = Join-Path $work.FullName 'fixture.dll'
    $values = '{"a":5,"b":7}'
    foreach ($arch in 'x86', 'x64') {
        Copy-Item -LiteralPath (Join-Path $DllFixtures "$arch/fixture.dll") -Destination $target
        if ($arch -eq 'x64') {
            Copy-Item -LiteralPath (Join-Path $DllFixtures "$arch/fixture.pdb") -Destination $work.FullName
        }
        (Wait-Element 'DLL 文件路径' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($target)
        Invoke-Control '打开 DLL'
        (Wait-Element '函数入口' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('add')
        if ($arch -eq 'x64') {
            [void](Wait-Element 'DLL 已换版 · 旧签名待重新确认' $label)
        } else {
            (Wait-Element 'C 声明' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('int __cdecl add(int a, int b);')
            Invoke-Control '识别 C 声明'
        }
        Invoke-Control '确认并保存签名'
        [void](Wait-Element '已确认 · 同一份 DLL' $label)
        (Wait-Element '调用参数' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($values)
        Invoke-Control '调用函数'
        [void](Wait-Element '{"Result":12,"Outputs":{}}' $label)
        Invoke-Control '预览代码'
        $preview = (Wait-Element '生成代码' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        if (!$preview.Contains('DllImport(') -or !$preview.Contains('Example()')) { throw 'Expected runnable DllImport example' }
        if ($arch -eq 'x64') {
            $format = Wait-Element '代码格式' ([Windows.Automation.ControlType]::ComboBox)
            $format.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            (Wait-Element 'C# LibraryImport' ([Windows.Automation.ControlType]::ListItem)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $format.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
            Invoke-Control '预览代码'
            $expected = (Wait-Element '生成代码' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if (!$expected.Contains('LibraryImport(')) { throw 'Expected LibraryImport preview' }
            Invoke-Control '导出文件…'
            $filter = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')
            $owner = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
            $condition = [Windows.Automation.AndCondition]::new($filter, $owner)
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                $dialog = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
                if (!$dialog) { $dialog = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition) }
                if ($dialog) { break }
                Start-Sleep -Milliseconds 100
            }
            if (!$dialog) { throw 'Export file picker was not shown' }
            $byId = [Windows.Automation.AutomationElement]::AutomationIdProperty
            $scope = [Windows.Automation.TreeScope]::Descendants
            # The native common dialog exposes these HWNDs as panes without UIA Value/Invoke patterns.
            $byClass = [Windows.Automation.AutomationElement]::ClassNameProperty
            function Native-Child($class, $id) {
                $properties = [Windows.Automation.AndCondition]::new(
                    [Windows.Automation.PropertyCondition]::new($byId, $id),
                    [Windows.Automation.PropertyCondition]::new($byClass, $class))
                $dialog.FindFirst($scope, $properties)
            }
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                $nameBox = Native-Child 'Edit' '1001'
                $save = Native-Child 'Button' '1'
                if ($nameBox.Current.NativeWindowHandle -and $save.Current.NativeWindowHandle) { break }
                Start-Sleep -Milliseconds 100
            }
            $outputFile = Join-Path $work.FullName 'Bindings.cs'
            if (!$nameBox.Current.NativeWindowHandle -or !$save.Current.NativeWindowHandle) { throw 'Missing native save controls' }
            [void][MyDebugTools.Tests.SaveDialog]::SendMessage($nameBox.Current.NativeWindowHandle, 0xC, 0, $outputFile)
            [void][MyDebugTools.Tests.SaveDialog]::PostMessage($save.Current.NativeWindowHandle, 0xF5, 0, 0)
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                if ([IO.File]::Exists($outputFile) -and [IO.File]::ReadAllText($outputFile) -eq $expected) { break }
                Start-Sleep -Milliseconds 100
            }
            if ([IO.File]::ReadAllText($outputFile) -ne $expected) { throw 'Export differs from preview' }
            'PASS exported LibraryImport file matches the preview'
        }
        Invoke-Control '重新开始会话'
        [void](Wait-Element '已重新开始会话。' $label)
        "PASS DLL workbench $arch · signature confirmation, call and generated example"
    }
}

function Start-Desktop {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['LOCALAPPDATA'] = $work.FullName
    $script:appProcess = [Diagnostics.Process]::Start($start)
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($appProcess.HasExited) { throw "App exited: $($appProcess.ExitCode)" }
        $appProcess.Refresh()
        $hwnd = $appProcess.MainWindowHandle
        if ($hwnd -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    $script:window = [Windows.Automation.AutomationElement]::FromHandle($hwnd)
}

try {
    if ($MonitorOnly -and $PreviousMonitorPlugin) {
        $newMonitor = (Resolve-Path "$Publish/plugins/ApiMonitor/ApiMonitor.dll").Path
        if ((Get-FileHash $PreviousMonitorPlugin).Hash -eq (Get-FileHash $newMonitor).Hash) { throw 'Need two distinct monitor builds' }
        $desktopCopy = Join-Path $work.FullName 'desktop'
        Copy-Item -LiteralPath (Resolve-Path $Publish).Path -Destination $desktopCopy -Recurse
        $liveMonitor = Join-Path $desktopCopy 'plugins/ApiMonitor/ApiMonitor.dll'
        Copy-Item -LiteralPath $PreviousMonitorPlugin -Destination $liveMonitor
        $exe = Join-Path $desktopCopy 'MyDebugTools.exe'
    }
    if (!$DllOnly -and !$MonitorOnly -and !$DependenciesOnly -and !$WebApiOnly) {
        Build-Sample ThirdPartyTools $thirdPartyDir '-p:SampleRevision=1'
        Build-Sample Workbench $providerDir '-p:SummaryVersion=1'
    }
    Start-Desktop
    if ($WebApiOnly) {
        . "$PSScriptRoot/WebApi.ps1"
        Check-WebApi
        return
    }
    . "$PSScriptRoot/DllDependencies.ps1"
    if ($DependenciesOnly) {
        Check-DllDependencies
        return
    }
    if ($MonitorOnly) {
        . "$PSScriptRoot/ApiMonitor.ps1"
        Check-ApiMonitor
        return
    }
    Check-DllWorkbench
    if ($DllOnly) { return }
    Open-Manager
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
    Add-Directory $thirdPartyDir
    Invoke-Control '启用 第三方工具'
    [void](Wait-Element '停用 第三方工具' $button)
    Open-Workbench
    Set-Text '尚未发送的草稿'
    $combo = Wait-Element '' ([Windows.Automation.ControlType]::ComboBox)
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Wait-Element '请求 B' ([Windows.Automation.ControlType]::ListItem)).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Check-Action '第三方 A' 'A v1 · 请求 B'
    Open-Manager
    Build-Sample ThirdPartyTools $thirdPartyDir '-p:SampleRevision=2'
    Invoke-Control '重新加载 第三方工具'
    [void](Wait-Element '停用 第三方工具' $button)
    Open-Workbench
    $draft = (Wait-Element '' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($draft -ne '尚未发送的草稿') { throw 'Draft lost' }
    Check-Action '第三方 A' 'A v2 · 请求 B'
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
    Check-DllDependencies
}
finally {
    if ($appProcess -and !$appProcess.HasExited) {
        [void]$appProcess.CloseMainWindow()
        if (!$appProcess.WaitForExit(4000)) { $appProcess.Kill(); $appProcess.WaitForExit() }
    }
    $root = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ($work.Parent.FullName.TrimEnd('\') -ne $root -or !$work.Name.StartsWith('MyDebugTools-desktop-')) {
        throw "Unexpected test directory $($work.FullName)"
    }
    Remove-Item -LiteralPath $work.FullName -Recurse -ErrorAction Stop
}
