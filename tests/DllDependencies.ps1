# Loaded by Desktop.ps1. Reuse its window, test settings and local PE fixtures.
function Check-DllDependencies {
    $dir = (Resolve-Path (Join-Path $DllFixtures 'dependencies')).Path
    $treeItem = [Windows.Automation.ControlType]::TreeItem
    $listItem = [Windows.Automation.ControlType]::ListItem
    $tabItem = [Windows.Automation.ControlType]::TabItem
    $select = [Windows.Automation.SelectionItemPattern]::Pattern
    $expand = [Windows.Automation.ExpandCollapsePattern]::Pattern
    $value = [Windows.Automation.ValuePattern]::Pattern
    function Set-File($file) {
        (Wait-Element 'DLL 文件路径' $edit).GetCurrentPattern($value).SetValue($file)
        Invoke-Control '打开 DLL'
    }
    function Use-View($mode) {
        $combo = Wait-Element '依赖视图' ([Windows.Automation.ControlType]::ComboBox)
        $combo.GetCurrentPattern($expand).Expand()
        (Wait-Element $mode $listItem).GetCurrentPattern($select).Select()
        $combo.GetCurrentPattern($expand).Collapse()
    }
    function Add-Search($folder) {
        (Wait-Element '搜索目录路径' $edit).GetCurrentPattern($value).SetValue($folder)
        Invoke-Control '添加搜索目录'
    }
    function Branch($name) { (Wait-Element $name $treeItem).GetCurrentPattern($expand).Expand() }
    function Use-Dependencies {
        (Wait-Element '依赖' $tabItem).GetCurrentPattern($select).Select()
    }
    function Check-ChoiceTree($variant) {
        Branch "Choice.dll · 普通导入 · $dir\$variant\Choice.dll · x64"
        [void](Wait-Element "Leaf$variant.dll · 普通导入 · $dir\$variant\Leaf$variant.dll · x64" $treeItem)
    }
    function Check-ChoiceGraph($variant) {
        Invoke-Control 'Choice.dll'
        [void](Wait-Element "$dir\$variant\Choice.dll · x64" $label)
        [void](Wait-Element "导入 → Leaf$variant.dll · 普通导入 · $dir\$variant\Leaf$variant.dll · x64" $label)
    }

    Open-Manager
    if (Find-Element '启用 DLL 工作台' $button) { Invoke-Control '启用 DLL 工作台' }
    Invoke-Control 'DLL 工作台' $menuItem
    Set-File "$dir\Root.dll"
    Open-Manager
    Invoke-Control '启用 DLL 依赖'
    Invoke-Control 'DLL 工作台' $menuItem
    Use-Dependencies
    [void](Wait-Element "$dir\Root.dll · x64" $treeItem)
    [void](Wait-Element 'Choice.dll · 普通导入 · 缺失 · 搜索目录中没有此文件' $treeItem)
    Branch "LeftBranch.dll · 普通导入 · $dir\LeftBranch.dll · x64"
    Branch "SharedLeaf.dll · 普通导入 · $dir\SharedLeaf.dll · x64"
    $cycle = "Root.dll · 普通导入 · 循环引用 · $dir\Root.dll · x64"
    $shared = "SharedLeaf.dll · 普通导入 · 共享引用 · $dir\SharedLeaf.dll · x64"
    if ((Wait-Element $cycle $treeItem).GetCurrentPattern($expand).Current.ExpandCollapseState -ne 'LeafNode') {
        throw 'Cycle must terminate the branch'
    }
    Branch "Later.dll · 延迟导入 · $dir\Later.dll · x64"
    $sharedBranch = Wait-Element $shared $treeItem
    $sharedBranch.GetCurrentPattern($expand).Expand()
    $byCycle = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $cycle)
    if (!$sharedBranch.FindFirst([Windows.Automation.TreeScope]::Descendants, $byCycle)) { throw 'Missing cycle below shared branch' }
    $sharedBranch = $null
    Use-View '依赖图'
    [void](Wait-Element 'Choice.dll · 未解析' $button)
    Invoke-Control 'SharedLeaf.dll'
    [void](Wait-Element "导入 → $cycle" $label)
    [void](Wait-Element "来自 $dir\Later.dll · x64 → $shared" $label)
    'PASS tree and graph share ordinary, delayed, missing, shared and cyclic edges'

    $wrong = [IO.Directory]::CreateDirectory((Join-Path $work.FullName 'wrong')).FullName
    Copy-Item -LiteralPath (Join-Path $DllFixtures 'x86/fixture.dll') -Destination "$wrong/Choice.dll"
    Use-View '依赖树'
    Add-Search $wrong
    [void](Wait-Element "Choice.dll · 普通导入 · 架构不匹配 · $wrong\Choice.dll 为 x86，目标为 x64" $treeItem)
    Add-Search "$dir\A"
    Check-ChoiceTree 'A'
    Use-View '依赖图'
    Check-ChoiceGraph 'A'
    Add-Search "$dir\B"
    Invoke-Control '上移'
    # The replacement graph and its node can arrive after the previous graph.
    [void](Wait-Element 'LeafB.dll' $button)
    Check-ChoiceGraph 'B'
    if (Find-Element 'LeafA.dll' $button) { throw 'Graph retained old child' }
    Use-View '依赖树'
    Check-ChoiceTree 'B'
    (Wait-Element '导出' $tabItem).GetCurrentPattern($select).Select()
    [void](Wait-Element '函数入口' $edit)
    Use-Dependencies
    Check-ChoiceTree 'B'
    'PASS ordered paths and descendants in both views, export tab round trip'

    Set-File "$dir\Contracts.dll"
    $contract = "api-ms-win-core-synch-l1-2-0.dll · 普通导入 · API Set 宿主 kernelbase.dll · $([Environment]::GetFolderPath('System'))\kernelbase.dll · x64"
    [void](Wait-Element $contract $treeItem)
    Use-View '依赖图'
    [void](Wait-Element "导入 → $contract" $label)
    if (Find-Element 'Choice.dll' $button) { throw 'Graph retained previous file' }
    Open-Manager
    Invoke-Control '停用 DLL 依赖'
    Invoke-Control 'DLL 工作台' $menuItem
    if (Find-Element '依赖' $tabItem) { throw 'Disabled tab still mounted' }
    [void](Wait-Element '函数入口' $edit)
    Open-Manager
    Invoke-Control '检查卸载'
    [void](Wait-Element '已回收 1 个旧版本。仍待回收：无' $label)
    Invoke-Control '启用 DLL 依赖'
    Invoke-Control 'DLL 工作台' $menuItem
    Use-Dependencies
    [void](Wait-Element $contract $treeItem)
    $byName = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, '依赖')
    $tabs = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, $byName) |
        Where-Object { $_.Current.ControlType -eq $tabItem })
    if ($tabs.Count -ne 1) { throw "Expected one dependency tab, got $($tabs.Count)" }
    'PASS current file replacement, local API Set host, independent tab removal and restoration'
    for ($round = 0; $round -lt 2; $round++) {
        Open-Manager
        Invoke-Control '重新加载 DLL 依赖'
        Invoke-Control 'DLL 工作台' $menuItem
        Use-Dependencies
        [void](Wait-Element $contract $treeItem)
        Open-Manager
        Invoke-Control '检查卸载'
        [void](Wait-Element '已回收 1 个旧版本。仍待回收：无' $label)
    }
    'PASS two workbench tab reloads with the current DLL and collected prior assemblies'
}
