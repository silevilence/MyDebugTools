function Check-DockPointer {
    if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This pointer test runs in a private Windows Sandbox desktop.' }
    . "$PSScriptRoot/DesktopUx.ps1"
    Initialize-UxCapture
    if (!('MyDebugTools.Tests.GuestMouse' -as [type])) {
        Add-Type -Namespace MyDebugTools.Tests -Name GuestMouse -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32")] public static extern bool SetPhysicalCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32")] public static extern void mouse_event(uint flags,int dx,int dy,uint data,UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
'@
    }
    $tabItem = [Windows.Automation.ControlType]::TabItem
    function At($name) { (Wait-Element $name $tabItem).Current.BoundingRectangle }
    function Point($x,$y) {
        if (![MyDebugTools.Tests.GuestMouse]::SetPhysicalCursorPos([int]$x,[int]$y)) { throw 'Guest cursor unavailable' }
        Start-Sleep -Milliseconds 80
    }
    function Drag($x,$y,$tx,$ty,$cancel) {
        Point $x $y
        [MyDebugTools.Tests.GuestMouse]::mouse_event(2,0,0,0,0)
        try {
            foreach ($i in 1..12) { Point ($x+($tx-$x)*$i/12) ($y+($ty-$y)*$i/12) }
            if ($cancel) {
                Snapshot 'dock-drag-preview'
                [MyDebugTools.Tests.GuestMouse]::keybd_event(27,0,0,0)
                [MyDebugTools.Tests.GuestMouse]::keybd_event(27,0,2,0)
                Start-Sleep -Milliseconds 250
            }
        } finally { [MyDebugTools.Tests.GuestMouse]::mouse_event(4,0,0,0,0) }
        Start-Sleep -Milliseconds 350
    }
    [void][MyDebugTools.Tests.UxWindow]::SetForegroundWindow($appProcess.MainWindowHandle)
    Invoke-Control '启用并打开请求'
    $draft = 'http://localhost/drag-draft'
    (Wait-Element 'URL' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($draft)
    Invoke-Control 'HTTP 响应' $menuItem
    $response = At 'HTTP 响应'
    $request = At 'Web API'
    $client = $window.Current.BoundingRectangle
    $x = $response.Left + $response.Width/3
    $y = $response.Top + $response.Height/2
    Drag $x $y ($client.Left+$client.Width/2) ($client.Top+$client.Height/2) $true
    $after = At 'HTTP 响应'
    if ([Math]::Abs($after.Left-$response.Left) -gt 4) { throw 'Escape did not restore the original tab position' }
    Drag $x $y ($request.Left+$request.Width/4) ($request.Top+$request.Height/2) $false
    if ((At 'HTTP 响应').Left -ge (At 'Web API').Left) { throw 'Tab order did not change after dragging' }
    Invoke-Control 'Web API' $menuItem
    if ((Wait-Element 'URL' $edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $draft) { throw 'Tab drag lost draft' }
    Invoke-Control 'HTTP 响应' $menuItem
    (Wait-Element '视图' $menuItem).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Invoke-Control '向右停靠' $menuItem
    $right = At 'HTTP 响应'
    $left = At 'Web API'
    # The divider sits immediately before the right tab strip.
    $sx = $right.Left-4
    $sy = $right.Bottom+90
    Drag $sx $sy ($sx+110) $sy $false
    if ([Math]::Abs((At 'HTTP 响应').Left-$right.Left) -lt 20) { throw 'Divider did not move' }
    'PASS guest pointer drag, Escape, tab reorder, splitter resize and draft continuity'
}
