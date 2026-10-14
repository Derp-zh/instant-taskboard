# Run only after coordinating mouse use with the user. Always uses a fresh isolated data directory.
param([string]$DataDirectory = 'test-output/desktop-20261013-01', [string]$Executable = 'release/Taskboard/Taskboard.exe')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$testDirectory = [System.IO.Path]::GetFullPath((Join-Path $repository $DataDirectory))
if (-not $testDirectory.StartsWith((Join-Path $repository 'test-output') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw '必须使用项目的隔离测试目录。' }
if (Test-Path -LiteralPath $testDirectory) { throw '测试目录必须是新的空目录。' }
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$seed = Get-Content -LiteralPath (Join-Path $repository 'test-output/core-20261013-03/performance/board.json') -Raw | ConvertFrom-Json
$seed.Tasks = @($seed.Tasks[1], $seed.Tasks[2], $seed.Tasks[8])
$seed.Sessions = @()
$seed.Preferences.Positions = @{}
$seed | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $testDirectory 'board.json') -Encoding utf8
$movingId = $seed.Tasks[0].Id
$oldTitle = $seed.Tasks[0].Title
$ux05 = $Executable -like '*0.5*' -or $Executable -like '*0.6*' -or $Executable -like '*0.7*' -or $Executable -like '*0.8*'
$ux07 = $Executable -like '*0.7*' -or $Executable -like '*0.8*'
$titleInputOffset = if ($ux05) { 120 } else { 96 }
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class TaskboardMouseProbe {
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
  [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
  [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
  [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }
  [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
  public class Window { public IntPtr Handle; public string Title; public Rect Bounds; }
  delegate bool Callback(IntPtr window, IntPtr param);
  [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr param);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,Callback callback,IntPtr param);
  public static IntPtr[] Children(IntPtr parent) { var children=new List<IntPtr>(); EnumChildWindows(parent,(h,p)=>{ children.Add(h); return true; },IntPtr.Zero); return children.ToArray(); }
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
  [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
  [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region,int x,int y);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
  public static IntPtr Hit(int x,int y) { return GetAncestor(WindowFromPoint(new Point{X=x,Y=y}),2); }
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam,uint flags,uint timeout,out IntPtr result);
  public static bool Circle(IntPtr hwnd,int width,int height) { GetWindowRect(hwnd,out var bounds); long p=((long)(ushort)(bounds.Top+1)<<16)|(ushort)(bounds.Left+1); SendMessageTimeout(hwnd,0x84,IntPtr.Zero,new IntPtr(p),2,500,out var corner); return Math.Abs(width-height)<=1 && Hit(bounds.Left+width/2,bounds.Top+height/2)==hwnd && corner.ToInt64()==-1; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
  public static double Scale(IntPtr hwnd) { return GetDpiForWindow(hwnd)/96.0; }
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, Input[] input, int size);
  public static Window[] Windows(int pid) {
    var list = new List<Window>();
    EnumWindows((h,p)=> { uint owner; GetWindowThreadProcessId(h,out owner); DwmGetWindowAttribute(h,14,out var cloaked,4); if(owner==pid && IsWindowVisible(h) && cloaked==0) { var text=new StringBuilder(256); GetWindowText(h,text,256); Rect r; GetWindowRect(h,out r); list.Add(new Window{Handle=h,Title=text.ToString(),Bounds=r}); } return true; }, IntPtr.Zero);
    return list.ToArray();
  }
  static void Send(Input input) { if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1) throw new InvalidOperationException("鼠标键盘输入被系统拒绝"); }
  public static void MouseButton(bool right, bool up) { Send(new Input{Type=0,Data=new Union{Mouse=new Mouse{Flags=right ? (up ? 0x10u : 8u) : (up ? 4u : 2u)}}}); }
  public static void Wheel(int delta) { Send(new Input{Type=0,Data=new Union{Mouse=new Mouse{MouseData=unchecked((uint)delta),Flags=0x800u}}}); }
  public static void Key(ushort key,bool up) { Send(new Input{Type=1,Data=new Union{Keyboard=new Keyboard{Key=key,Flags=up ? 2u : 0u}}}); }
  public static void Press(ushort key) { Key(key,false); Key(key,true); }
  public static void Type(string text) { foreach(var c in text) { Send(new Input{Type=1,Data=new Union{Keyboard=new Keyboard{Scan=c,Flags=4}}}); Send(new Input{Type=1,Data=new Union{Keyboard=new Keyboard{Scan=c,Flags=6}}}); } }
  public static void Click(int x,int y,bool right=false) { SetCursorPos(x,y); Thread.Sleep(80); MouseButton(right,false); Thread.Sleep(65); MouseButton(right,true); Thread.Sleep(220); }
  public static void Drag(int x,int y,int tx,int ty,bool cancel=false,bool right=false) {
    SetCursorPos(x,y); Thread.Sleep(70); MouseButton(right,false); Thread.Sleep(right ? 420 : 100);
    for(var i=1;i<=16;i++) { SetCursorPos(x+(tx-x)*i/16,y+(ty-y)*i/16); Thread.Sleep(18); }
    if(cancel) { Press(27); Thread.Sleep(100); }
    MouseButton(right,true); Thread.Sleep(350);
  }
}
'@
$dpiContextBefore = [TaskboardMouseProbe]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$cursorBefore = [TaskboardMouseProbe+Point]::new()
[TaskboardMouseProbe]::GetCursorPos([ref]$cursorBefore) | Out-Null
$foregroundBefore = [TaskboardMouseProbe]::GetForegroundWindow()
$passed = [System.Collections.Generic.List[string]]::new()
$testProcess = $null
function Get-TestWindow([string]$title) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $window = @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq $title)[0]
        if ($window) { return $window }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 8)
    throw ('没有找到测试窗口：' + $title)
}
function Read-TestData { Get-Content -LiteralPath (Join-Path $testDirectory 'board.json') -Raw | ConvertFrom-Json }
function Assert-Test([bool]$condition,[string]$name) { if (-not $condition) { throw ('未通过：' + $name) }; $passed.Add($name) }
function Capture-TestWindow($window,[string]$name) {
    Add-Type -AssemblyName System.Drawing
    $bounds = $window.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Right-$bounds.Left,$bounds.Bottom-$bounds.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size); $bitmap.Save((Join-Path $testDirectory $name),[System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Get-Control($window,$type,[string]$name = '',[int]$matchIndex = 0) {
    foreach($attempt in 1..30) {
    $roots = @($window.Handle) + @([TaskboardMouseProbe]::Children($window.Handle))
    $diagnostic=[System.Collections.Generic.List[object]]::new()
    foreach ($handle in $roots) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        $match=0
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$type)
        foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)) {
            if (($name -eq '' -or $element.Current.Name -eq $name) -and $element.Current.BoundingRectangle.Width -gt 0) {
                if ($match++ -lt $matchIndex) { continue }
                [pscustomobject]@{ Window=$window.Title; Name=$element.Current.Name; Type=$element.Current.ControlType.ProgrammaticName; Bounds=$element.Current.BoundingRectangle.ToString(); Handle=$handle.ToString() } | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $testDirectory 'selected-controls.jsonl')
                return $element
            }
        }
        $diagnostic.Add([pscustomobject]@{ Handle=$handle.ToString(); Name=$root.Current.Name; Type=$root.Current.ControlType.ProgrammaticName })
    }
    Start-Sleep -Milliseconds 50
    }
    $diagnostic | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testDirectory 'uia-diagnostic.json')
    Capture-TestWindow $window 'uia-failure-window.png'
    throw "未找到隔离窗口控件：$name"
}
function Click-Control($element) { $r=$element.Current.BoundingRectangle; [TaskboardMouseProbe]::Click(($r.Left+$r.Right)/2,($r.Top+$r.Bottom)/2) }
function Assert-SearchPixels($element) {
    Add-Type -AssemblyName System.Drawing
    $r=$element.Current.BoundingRectangle; $glyphs=0; $caret=0
    foreach ($attempt in 1..8) {
        $bitmap=[System.Drawing.Bitmap]::new([int]$r.Width,[int]$r.Height); $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen([int]$r.Left,[int]$r.Top,0,0,$bitmap.Size)
            $glyphs=0; $caret=0
            for ($x=24;$x -lt $bitmap.Width-24;$x++) {
                $run=0
                for ($y=8;$y -lt $bitmap.Height-8;$y++) {
                    $color=$bitmap.GetPixel($x,$y)
                    if ($color.R -gt 210 -and $color.G -gt 210 -and $color.B -gt 210) { $glyphs++ }
                    if ($color.R -ge 100 -and $color.R -le 190 -and $color.G -gt 175 -and $color.B -gt 150 -and $color.G-$color.R -gt 30) { $run++ }
                }
                if ($run -gt $caret) { $caret=$run }
            }
            $bitmap.Save((Join-Path $testDirectory ('actual-search-'+$attempt+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
        if ($glyphs -gt 40 -and $caret -ge 10) { break }; Start-Sleep -Milliseconds 100
    }
    Assert-Test ($glyphs -gt 40) '搜索框真实键盘输入在桌面上有可见文字像素'
    Assert-Test ($caret -ge 10) '搜索框桌面上有可见输入光标，覆盖闪烁周期'
}
function Assert-Toggle($button,[string]$name) {
    Click-Control $button
    @([TaskboardMouseProbe]::Windows($testProcess.Id)) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testDirectory ('menu-windows-'+$name+'.json'))
    Capture-TestWindow (Get-TestWindow '编辑任务') ('menu-'+$name+'.png')
    Assert-Test ((Get-MenuCount) -eq 1) ($name+'第一次真实点击展开')
    Click-Control $button
    Assert-Test ((Get-MenuCount) -eq 0) ($name+'第二次真实点击收起')
    Click-Control $button
    Assert-Test ((Get-MenuCount) -eq 1) ($name+'第三次真实点击再次展开')
    [TaskboardMouseProbe]::Press(27); Start-Sleep -Milliseconds 150
}
function Get-MenuCount {
    $count=0
    $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::MenuItem)
    foreach ($popup in @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '')) {
        $root=[System.Windows.Automation.AutomationElement]::FromHandle($popup.Handle)
        if ($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition).Count -gt 0) { $count++ }
    }
    return $count
}
function Drag-WithPreview([int]$x,[int]$y,[int]$tx,[int]$ty,$destination) {
    [TaskboardMouseProbe]::SetCursorPos($x,$y) | Out-Null; [TaskboardMouseProbe]::MouseButton($false,$false); Start-Sleep -Milliseconds 100
    try {
        [TaskboardMouseProbe]::SetCursorPos($x+20,$y+10) | Out-Null; Start-Sleep -Milliseconds 180
        $first=Get-TestWindow '任务卡片拖动预览'
        [TaskboardMouseProbe]::SetCursorPos($tx,$ty) | Out-Null; Start-Sleep -Milliseconds 180
        $second=Get-TestWindow '任务卡片拖动预览'
        Assert-Test ([Math]::Abs(($second.Bounds.Left-$first.Bounds.Left)-($tx-$x-20)) -le 2 -and [Math]::Abs(($second.Bounds.Top-$first.Bounds.Top)-($ty-$y-10)) -le 2) '真实拖动卡片跟随鼠标位移'
        Assert-Test ([TaskboardMouseProbe]::Hit($tx,$ty) -eq $destination.Handle) '跟随卡片穿透鼠标，目标列可接收拖放'
        Capture-TestWindow $destination 'actual-drag-preview.png'
        [TaskboardMouseProbe]::SetCursorPos($tx+90,$ty+25) | Out-Null; Start-Sleep -Milliseconds 140
        Capture-TestWindow $destination 'actual-dashed-placeholder.png'
        [TaskboardMouseProbe]::SetCursorPos($tx,$ty) | Out-Null; Start-Sleep -Milliseconds 140
    } finally { [TaskboardMouseProbe]::MouseButton($false,$true); Start-Sleep -Milliseconds 350 }
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '任务卡片拖动预览').Count -eq 0) '松手后跟随窗口释放无残留'
}
try {
    if ($ux07) {
        $runtime=Split-Path -Parent (Join-Path $repository $Executable)
        Add-Type -Path (Join-Path $runtime 'UIAutomationClient.dll')
        Add-Type -Path (Join-Path $runtime 'UIAutomationTypes.dll')
    }
    $testProcess = Start-Process -FilePath (Join-Path $repository $Executable) -ArgumentList ('--data-dir ' + $DataDirectory) -WindowStyle Hidden -PassThru
    $ball = Get-TestWindow '即时任务看板入口'
    Start-Sleep -Milliseconds 350
    $ballScale = [TaskboardMouseProbe]::Scale($ball.Handle)
    [TaskboardMouseProbe]::Click($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale)
    $refining = Get-TestWindow '完善中'; $running = Get-TestWindow '进行中'; $paused = Get-TestWindow '暂搁置'; $done = Get-TestWindow '已完成'
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -in @('灵感库','完善中','进行中','待操作','暂搁置','已完成') }).Count -eq 6) '左键悬浮球实际展开六列'
    Add-Type -AssemblyName System.Windows.Forms
    $workArea = [System.Windows.Forms.Screen]::FromHandle($ball.Handle).WorkingArea
    $columns = @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -in @('灵感库','完善中','进行中','待操作','暂搁置','已完成') })
    $groupLeft = ($columns.Bounds.Left | Measure-Object -Minimum).Minimum
    $groupRight = ($columns.Bounds.Right | Measure-Object -Maximum).Maximum
    Assert-Test ([Math]::Abs(($groupLeft+$groupRight)/2-($workArea.Left+$workArea.Right)/2) -lt 3 -and [Math]::Abs(($refining.Bounds.Top+$refining.Bounds.Bottom)/2-($workArea.Top+$workArea.Bottom)/2) -lt 3) '新默认六列外框在工作区中央'
    $defaultScale = [TaskboardMouseProbe]::Scale($refining.Handle)
    if ($ux05) {
        $screenBounds = [System.Windows.Forms.Screen]::FromHandle($ball.Handle).Bounds
        Assert-Test ([Math]::Abs($groupRight-$groupLeft-$screenBounds.Width*7/8) -lt 4) '默认最大范围宽度为屏幕的八分之七（7/8）'
        $leftColumn = Get-TestWindow '灵感库'
        Assert-Test ([Math]::Abs(($refining.Bounds.Left-$leftColumn.Bounds.Right)/$defaultScale-24) -lt 2) '默认列表间距增大至24'
    } else {
        $expectedWidth = (($workArea.Width/$defaultScale-24-5*8)/6*.87)*$defaultScale
        Assert-Test ([Math]::Abs($refining.Bounds.Right-$refining.Bounds.Left-$expectedWidth) -lt 3) '默认列宽为旧版的87%'
    }
    Assert-Test ($ball.Bounds.Bottom -le $workArea.Bottom -and $ball.Bounds.Right -le $workArea.Right) '悬浮球和搜索条避开当前缩放下的任务栏'
    if ($ux07) {
        $searchControl=Get-Control $ball ([System.Windows.Automation.ControlType]::Edit)
        Click-Control $searchControl; [TaskboardMouseProbe]::Type('搜索输入可见'); Start-Sleep -Milliseconds 200
        Assert-Test ($searchControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '搜索输入可见') '搜索框真实键盘输入值正确'
        Assert-SearchPixels $searchControl
        Click-Control (Get-Control $ball ([System.Windows.Automation.ControlType]::Button) '×')
    }
    $sourceX = [int](($refining.Bounds.Left+$refining.Bounds.Right)/2)
    $columnScale = [TaskboardMouseProbe]::Scale($refining.Handle)
    [TaskboardMouseProbe]::Click($sourceX,$refining.Bounds.Top+82*$columnScale)
    $editor = Get-TestWindow '编辑任务'
    $editorScale = [TaskboardMouseProbe]::Scale($editor.Handle)
    if ($ux07) {
        $titleControl=Get-Control $editor ([System.Windows.Automation.ControlType]::Edit)
        $inputBounds=$titleControl.Current.BoundingRectangle
        [TaskboardMouseProbe]::Click($inputBounds.Left+2,($inputBounds.Top+$inputBounds.Bottom)/2)
        [TaskboardMouseProbe]::Type('左')
        $value=$titleControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        Assert-Test ($value.Current.Value.Contains('左')) '任务输入框左侧圆弧内部可点击输入'
        $inputBounds=$titleControl.Current.BoundingRectangle
        [TaskboardMouseProbe]::Click($inputBounds.Right-2,($inputBounds.Top+$inputBounds.Bottom)/2); [TaskboardMouseProbe]::Type('右')
        Capture-TestWindow (Get-TestWindow '编辑任务') 'rounded-right-input.png'
        [pscustomobject]@{ Value=$value.Current.Value; Bounds=$titleControl.Current.BoundingRectangle.ToString(); HasFocus=$titleControl.Current.HasKeyboardFocus; Hit=[TaskboardMouseProbe]::Hit($inputBounds.Right-2,($inputBounds.Top+$inputBounds.Bottom)/2).ToString() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDirectory 'rounded-right-input.json')
        Assert-Test ($value.Current.Value.Contains('右')) '任务输入框右侧圆弧内部可点击输入'
        [TaskboardMouseProbe]::SetCursorPos(($editor.Bounds.Left+$editor.Bounds.Right)/2,$editor.Bounds.Top+300*$editorScale) | Out-Null
        foreach ($scrollStep in 1..20) { [TaskboardMouseProbe]::Wheel(-120); Start-Sleep -Milliseconds 15 }; Start-Sleep -Milliseconds 220
        $aiName=@($seed.AiOptions | Where-Object Id -eq $seed.Tasks[0].AiIds[0])[0].Name
        $priorityName=@($seed.PriorityOptions | Where-Object Id -eq $seed.Tasks[0].PriorityId)[0].Name
        if (-not $aiName) { $aiName='未选择 ▾' }
        $priorityIndex=0
        if (-not $priorityName) { $priorityName='未选择 ▾'; if ($aiName -eq $priorityName) { $priorityIndex=1 } }
        Assert-Toggle (Get-Control $editor ([System.Windows.Automation.ControlType]::Button) $aiName) 'AI选择框'
        Assert-Toggle (Get-Control $editor ([System.Windows.Automation.ControlType]::Button) $priorityName $priorityIndex) '优先级选择框'
        [TaskboardMouseProbe]::SetCursorPos(($editor.Bounds.Left+$editor.Bounds.Right)/2,$editor.Bounds.Top+300*$editorScale) | Out-Null
        foreach ($scrollStep in 1..20) { [TaskboardMouseProbe]::Wheel(120); Start-Sleep -Milliseconds 15 }; Start-Sleep -Milliseconds 220
    }
    [TaskboardMouseProbe]::Click($editor.Bounds.Left+95*$editorScale,$editor.Bounds.Top+$titleInputOffset*$editorScale)
    [TaskboardMouseProbe]::Key(17,$false); [TaskboardMouseProbe]::Press(65); [TaskboardMouseProbe]::Key(17,$true)
    [TaskboardMouseProbe]::Type('桌面验证修改')
    Capture-TestWindow $editor 'actual-keyboard-input.png'
    [TaskboardMouseProbe]::Click($editor.Bounds.Right-68*$editorScale,$editor.Bounds.Bottom-35*$editorScale)
    Start-Sleep -Milliseconds 350
    Assert-Test (@((Read-TestData).Tasks | Where-Object Id -eq $movingId)[0].Title -eq '桌面验证修改') '实际鼠标和键盘修改并点击保存'
    [TaskboardMouseProbe]::Click($sourceX,$refining.Bounds.Top+82*$columnScale)
    $editor = Get-TestWindow '编辑任务'
    [TaskboardMouseProbe]::Click($editor.Bounds.Left+95*$editorScale,$editor.Bounds.Top+$titleInputOffset*$editorScale)
    [TaskboardMouseProbe]::Key(17,$false); [TaskboardMouseProbe]::Press(65); [TaskboardMouseProbe]::Key(17,$true); [TaskboardMouseProbe]::Type('不应提交')
    [TaskboardMouseProbe]::Click($editor.Bounds.Right-31*$editorScale,$editor.Bounds.Top+23*$editorScale)
    Start-Sleep -Milliseconds 250
    Assert-Test (@((Read-TestData).Tasks | Where-Object Id -eq $movingId)[0].Title -eq '桌面验证修改') '实际X点击放弃修改'
    $targetX = [int](($running.Bounds.Left+$running.Bounds.Right)/2)
    if ($ux07) { Drag-WithPreview $sourceX ($refining.Bounds.Top+82*$columnScale) $targetX ($running.Bounds.Top+70*$columnScale) $running }
    else { [TaskboardMouseProbe]::Drag($sourceX,$refining.Bounds.Top+82*$columnScale,$targetX,$running.Bounds.Top+70*$columnScale) }
    Start-Sleep -Milliseconds 250
    Assert-Test (@((Read-TestData).Tasks | Where-Object Id -eq $movingId)[0].Lane -eq 2) '跨窗口实际拖放改变列表并落盘'
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '编辑任务').Count -eq 0) '拖动没有误开编辑窗口'
    $beforeCancel = @((Read-TestData).Tasks | ForEach-Object Id)
    [TaskboardMouseProbe]::Drag($targetX,$running.Bounds.Top+82*$columnScale,$sourceX,$refining.Bounds.Top+100*$columnScale,$true)
    Assert-Test ((@((Read-TestData).Tasks | ForEach-Object Id) -join ',') -eq ($beforeCancel -join ',')) 'Esc取消拖放不改变顺序或状态'
    if ($ux07) {
        [TaskboardMouseProbe]::Drag($targetX,$running.Bounds.Top+82*$columnScale,$targetX,$running.Bounds.Bottom-30*$columnScale)
        Start-Sleep -Milliseconds 300
        $laneTasks=@((Read-TestData).Tasks | Where-Object Lane -eq 2)
        Assert-Test ($laneTasks[-1].Id -eq $movingId) '真实列内拖动到末尾改变顺序'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale,$true)
        $undoPopup=Get-TestWindow ''
        Click-Control (Get-Control $undoPopup ([System.Windows.Automation.ControlType]::MenuItem) '撤销'); Start-Sleep -Milliseconds 300
        Assert-Test (@((Read-TestData).Tasks | Where-Object Lane -eq 2)[0].Id -eq $movingId) '真实悬浮球撤销菜单恢复列内排序'
    }
    [TaskboardMouseProbe]::Click($paused.Bounds.Left+40*$columnScale,$paused.Bounds.Top+180*$columnScale,$true)
    Capture-TestWindow $paused 'resize-menu.png'
    [TaskboardMouseProbe]::Click($paused.Bounds.Left+40*$columnScale+100,$paused.Bounds.Top+180*$columnScale+20)
    Start-Sleep -Milliseconds 200
    Capture-TestWindow $paused 'resize-mode.png'
    [TaskboardMouseProbe]::Drag($paused.Bounds.Right-2,$paused.Bounds.Top+280*$columnScale,$paused.Bounds.Right+38,$paused.Bounds.Top+280*$columnScale)
    $afterResize = Get-TestWindow '暂搁置'; $afterNeighbor = Get-TestWindow '已完成'
    Assert-Test (($afterResize.Bounds.Right-$afterResize.Bounds.Left) -gt ($paused.Bounds.Right-$paused.Bounds.Left)+20) '菜单四边模式实际拉伸窗口'
    Assert-Test ($afterNeighbor.Bounds.Left -eq $done.Bounds.Left -and $afterNeighbor.Bounds.Right -eq $done.Bounds.Right) '四边拉伸没有挤压邻列'
    [TaskboardMouseProbe]::Press(27)
    if ($ux05) {
        $leftColumn = Get-TestWindow '灵感库'; $paused = Get-TestWindow '暂搁置'
        $beforeGroup = @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -in @('灵感库','完善中','进行中','待操作','暂搁置','已完成') })
        [TaskboardMouseProbe]::Drag($leftColumn.Bounds.Left+14*$columnScale,$leftColumn.Bounds.Bottom-14*$columnScale,$leftColumn.Bounds.Left+85*$columnScale,$leftColumn.Bounds.Bottom-45*$columnScale)
        $afterGroup = @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -in @('灵感库','完善中','进行中','待操作','暂搁置','已完成') })
        $ratios = @($beforeGroup | ForEach-Object { $before=$_; $after=@($afterGroup | Where-Object Title -eq $before.Title)[0]; ($after.Bounds.Right-$after.Bounds.Left)/($before.Bounds.Right-$before.Bounds.Left) })
        Assert-Test ($ratios.Count -eq 6 -and ($ratios | Measure-Object -Maximum).Maximum-($ratios | Measure-Object -Minimum).Minimum -lt .025 -and $ratios[0] -lt .99) '左下图案实际拖动等比例缩放六列'
        $paused = Get-TestWindow '暂搁置'
        [TaskboardMouseProbe]::Click(($paused.Bounds.Left+$paused.Bounds.Right)/2,$paused.Bounds.Top+80*$columnScale)
        $draft = Get-TestWindow '新建任务'; $paneWidth = ($draft.Bounds.Right-$draft.Bounds.Left)/1.2
        [TaskboardMouseProbe]::Click($draft.Bounds.Left+95*$editorScale,$draft.Bounds.Top+120*$editorScale); [TaskboardMouseProbe]::Type('同一窗口草稿一')
        [TaskboardMouseProbe]::Click($draft.Bounds.Right-$paneWidth/10,($draft.Bounds.Top+$draft.Bounds.Bottom)/2)
        $second = Get-TestWindow '新建任务'
        Assert-Test ($second.Handle -eq $draft.Handle -and @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 1) '新建第二草稿仍只有同一个真实窗口'
        [TaskboardMouseProbe]::Click($second.Bounds.Left+95*$editorScale,$second.Bounds.Top+120*$editorScale); [TaskboardMouseProbe]::Type('同一窗口草稿二')
        [TaskboardMouseProbe]::Click($second.Bounds.Left+$paneWidth+$paneWidth/10,$second.Bounds.Top+100*$editorScale)
        $switched = Get-TestWindow '新建任务'; Capture-TestWindow $switched 'one-window-stack.png'
        Assert-Test ($switched.Handle -eq $draft.Handle) '点击旧稿露出区在同一窗口内部切换'
        $sourceWindow = Get-TestWindow '完善中'
        [TaskboardMouseProbe]::Click($sourceWindow.Bounds.Left+10*$columnScale,$sourceWindow.Bounds.Top+18*$columnScale)
        $draft = Get-TestWindow '新建任务'
        Assert-Test ([TaskboardMouseProbe]::Hit($draft.Bounds.Left+25*$editorScale,$draft.Bounds.Top+90*$editorScale) -eq $draft.Handle) '点击列表后草稿编辑仍在列表上层'
        $ball = Get-TestWindow '即时任务看板入口'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-85*$ballScale,$ball.Bounds.Top+29*$ballScale)
        $settingWindow = Get-TestWindow '即时任务看板设置'; Capture-TestWindow $settingWindow 'sidebar-settings.png'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale)
        Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -eq '新建任务' -or $_.Title -eq '即时任务看板设置' }).Count -eq 0) '球实际收起共享草稿与设置'
        $ball = Get-TestWindow '即时任务看板入口'
        Assert-Test ([TaskboardMouseProbe]::Circle($ball.Handle,$ball.Bounds.Right-$ball.Bounds.Left,$ball.Bounds.Bottom-$ball.Bounds.Top)) '收起圆球内部可点击，圆外透明区域不遮挡'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale)
        Assert-Test ((Get-TestWindow '新建任务').Handle -eq $draft.Handle -and (Get-TestWindow '即时任务看板设置').Handle -eq $settingWindow.Handle) '重新打开恢复原共享宿主与设置'
        $settingWindow = Get-TestWindow '即时任务看板设置'
        [TaskboardMouseProbe]::Click($settingWindow.Bounds.Right-42*$editorScale,$settingWindow.Bounds.Top+32*$editorScale)
        foreach ($unused in 1..2) { $draft = Get-TestWindow '新建任务'; [TaskboardMouseProbe]::Click($draft.Bounds.Left+$paneWidth-31*$editorScale,$draft.Bounds.Top+35*$editorScale) }
        Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 0) '全部关闭共享草稿后没有残留'
    }
    if ($Executable -like '*0.4*') {
        $paused = Get-TestWindow '暂搁置'
        [TaskboardMouseProbe]::Click(($paused.Bounds.Left+$paused.Bounds.Right)/2,$paused.Bounds.Top+80*$columnScale)
        $firstDraft = Get-TestWindow '新建任务'
        [TaskboardMouseProbe]::Click($firstDraft.Bounds.Left+95*$editorScale,$firstDraft.Bounds.Top+96*$editorScale)
        [TaskboardMouseProbe]::Type('实际堆叠草稿一')
        $outline = Get-TestWindow '新建任务堆叠入口'
        [TaskboardMouseProbe]::Click($outline.Bounds.Right-40*$editorScale,($outline.Bounds.Top+$outline.Bounds.Bottom)/2)
        $secondDraft = Get-TestWindow '新建任务'
        [TaskboardMouseProbe]::Click($secondDraft.Bounds.Left+95*$editorScale,$secondDraft.Bounds.Top+96*$editorScale)
        [TaskboardMouseProbe]::Type('实际堆叠草稿二')
        $oldDraft = @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $firstDraft.Handle)[0]
        Assert-Test ([Math]::Abs($oldDraft.Bounds.Left-$secondDraft.Bounds.Left-($secondDraft.Bounds.Right-$secondDraft.Bounds.Left)/5) -lt 3) '真实新建堆叠旧窗口露出五分之一'
        [TaskboardMouseProbe]::Click($oldDraft.Bounds.Right-16*$editorScale,$oldDraft.Bounds.Top+80*$editorScale)
        $activeDraft = Get-TestWindow '新建任务'
        Assert-Test ($activeDraft.Handle -eq $firstDraft.Handle -and $activeDraft.Bounds.Left -eq $secondDraft.Bounds.Left) '实际点击旧窗口露出区切换至中央'
        Capture-TestWindow $activeDraft 'draft-front.png'
        $ball = Get-TestWindow '即时任务看板入口'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-85*$ballScale,$ball.Bounds.Top+29*$ballScale)
        $settingWindow = Get-TestWindow '即时任务看板设置'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale)
        Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -eq '即时任务看板设置' -or $_.Title -eq '新建任务' -or $_.Title -eq '新建任务堆叠入口' }).Count -eq 0) '设置及新建打开时实际点击球能收起全部窗口'
        $ball = Get-TestWindow '即时任务看板入口'
        [TaskboardMouseProbe]::Click($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale)
        Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 2 -and (Get-TestWindow '即时任务看板设置').Handle -eq $settingWindow.Handle) '实际重新点击球恢复设置和原新建堆叠'
        $settingWindow = Get-TestWindow '即时任务看板设置'
        [TaskboardMouseProbe]::Click($settingWindow.Bounds.Right-48*$editorScale,$settingWindow.Bounds.Top+40*$editorScale)
        foreach ($unused in 1..2) {
            $draft = Get-TestWindow '新建任务'
            [TaskboardMouseProbe]::Click($draft.Bounds.Right-31*$editorScale,$draft.Bounds.Top+23*$editorScale)
        }
        Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '新建任务堆叠入口').Count -eq 0) '实际全部关闭新建后没有草稿列表残留'
    }
    $ball = Get-TestWindow '即时任务看板入口'
    [TaskboardMouseProbe]::Drag($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale,$ball.Bounds.Right-29*$ballScale-40,$ball.Bounds.Top+29*$ballScale-20,$false,$true)
    $movedBall = Get-TestWindow '即时任务看板入口'
    Assert-Test ($movedBall.Bounds.Right -lt $ball.Bounds.Right-20) '右键长按实际移动悬浮球'
    [TaskboardMouseProbe]::Click($movedBall.Bounds.Right-29*$ballScale,$movedBall.Bounds.Top+29*$ballScale)
    Start-Sleep -Milliseconds 200
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object { $_.Title -in @('灵感库','完善中','进行中','待操作','暂搁置','已完成') }).Count -eq 0) '实际悬浮球收起无列残留'
    if ($Executable -like '*0.4*' -or $ux05) {
        $ball = Get-TestWindow '即时任务看板入口'
        [TaskboardMouseProbe]::SetCursorPos($ball.Bounds.Right+15,$ball.Bounds.Top+29*$ballScale) | Out-Null
        Start-Sleep -Milliseconds 220
        [TaskboardMouseProbe]::SetCursorPos($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top+29*$ballScale) | Out-Null
        Start-Sleep -Milliseconds 650
        $previewWindow = Get-TestWindow ''
        Assert-Test ([Math]::Abs(($previewWindow.Bounds.Right-$previewWindow.Bounds.Left)/$ballScale-198) -lt 2) '真实悬停预览宽度缩为198'
        Capture-TestWindow $previewWindow 'narrow-preview.png'
        if ($Executable -like '*0.8*') {
            [TaskboardMouseProbe]::SetCursorPos($ball.Bounds.Right-29*$ballScale,$ball.Bounds.Top-4*$ballScale) | Out-Null
            Start-Sleep -Milliseconds 330
            Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $previewWindow.Handle).Count -eq 1) '球向上进入间隙后预览仍保持'
            [TaskboardMouseProbe]::SetCursorPos(($previewWindow.Bounds.Left+$previewWindow.Bounds.Right)/2,$previewWindow.Bounds.Top+35*$ballScale) | Out-Null
            Start-Sleep -Milliseconds 330
            Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $previewWindow.Handle).Count -eq 1) '鼠标进入预览内部可持续操作'
            [TaskboardMouseProbe]::SetCursorPos($previewWindow.Bounds.Left-30,$previewWindow.Bounds.Top+35*$ballScale) | Out-Null
            Start-Sleep -Milliseconds 450
            Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $previewWindow.Handle).Count -eq 0) '离开完整悬浮范围后预览自动关闭'
        }
    }
    [pscustomobject]@{ Passed=$passed.Count; Checks=$passed; MouseMoved=$true; DataDirectory=$testDirectory } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $testDirectory 'desktop-report.json')
    $passed
} catch {
    $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $testDirectory 'desktop-failure.txt')
    throw
} finally {
    if ($testProcess -and -not $testProcess.HasExited) { Stop-Process -Id $testProcess.Id -Force }
    [TaskboardMouseProbe]::SetCursorPos($cursorBefore.X,$cursorBefore.Y) | Out-Null
    [TaskboardMouseProbe]::SetForegroundWindow($foregroundBefore) | Out-Null
    [TaskboardMouseProbe]::SetThreadDpiAwarenessContext($dpiContextBefore) | Out-Null
}
